using System.Security.Claims;
using BillKro.Data;
using BillKro.Server.Auth;
using BillKro.Server.Infrastructure;
using BillKro.Shared;
using Microsoft.EntityFrameworkCore;

namespace BillKro.Server.Endpoints;

/// <summary>Goods returned to suppliers. Each return is a debit note that lowers what we owe the supplier.</summary>
public static class PurchaseReturnEndpoints
{
    public const string ReturnKind = "PRT";
    public const string DebitNotePrefix = "D";
    public const int MaxLines = 300;

    public static void MapPurchaseReturns(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/purchase-returns").RequireAuthorization(Policies.Manager);
        g.MapPost("/", Create);
        g.MapGet("/", List);
        g.MapGet("/{id:int}", Get);
        g.MapGet("/candidates", Candidates);

        app.MapGet("/api/purchases/{id:int}/returnable", Returnable).RequireAuthorization(Policies.Manager);
    }

    private static async Task<IResult> Create(PurchaseReturnRequest req, ClaimsPrincipal principal, PosDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var e = new Errors().MaxLength(nameof(req.Reason), req.Reason, 200);
        var lines = req.Lines?.Where(l => l.Qty != 0).ToList() ?? [];
        if (lines.Count == 0) e.Add(nameof(req.Lines), "Enter a quantity for at least one item.");
        else if (lines.Count > MaxLines) e.Add(nameof(req.Lines), $"A return can have at most {MaxLines} lines.");
        else if (lines.Any(l => l.Qty < 0 || decimal.Round(l.Qty, 3) != l.Qty))
            e.Add(nameof(req.Lines), "Return quantities must be above zero with at most 3 decimals.");
        else if (lines.Any(l => l.Rate < 0 || l.GstRate is < 0 or > 40))
            e.Add(nameof(req.Lines), "Rates cannot be negative and GST must be 0–40%.");
        if (e.Any) return e.ToResult();

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SupplierId, ct);
        if (supplier is null) return e.Add(nameof(req.SupplierId), "Choose the supplier.").ToResult();

        Purchase? purchase = null;
        if (req.PurchaseId is { } pid)
        {
            purchase = await db.Purchases.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == pid, ct);
            if (purchase is null || purchase.SupplierId != supplier.Id)
                return e.Add(nameof(req.PurchaseId), "That purchase is not from this supplier.").ToResult();
            if (purchase.Status != PurchaseStatus.Completed)
                return DbHelpers.Conflict("A cancelled purchase cannot be returned against.");
        }

        var batchIds = lines.Select(l => l.BatchId).Distinct().ToList();
        var batches = await db.Batches.AsNoTracking().Include(b => b.Item).Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);

        // Against a purchase: never more than received on that line, minus what went back already.
        var purchaseLineIds = lines.Where(l => l.PurchaseLineId is not null).Select(l => l.PurchaseLineId!.Value).ToList();
        var alreadyReturned = await db.PurchaseReturnLines.Where(r => r.PurchaseLineId != null && purchaseLineIds.Contains(r.PurchaseLineId.Value))
            .GroupBy(r => r.PurchaseLineId!.Value).Select(g => new { g.Key, Qty = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        var built = new List<PurchaseReturnLine>();
        foreach (var group in lines.GroupBy(l => (l.BatchId, l.PurchaseLineId)))
        {
            var first = group.First();
            if (!batches.TryGetValue(first.BatchId, out var batch))
                return e.Add(nameof(req.Lines), "Lot not found.").ToResult();
            var qty = group.Sum(l => l.Qty);

            PurchaseLine? pl = null;
            if (first.PurchaseLineId is { } plId)
            {
                pl = purchase?.Lines.FirstOrDefault(x => x.Id == plId);
                if (pl is null || pl.BatchId != batch.Id)
                    return e.Add(nameof(req.Lines), $"{batch.Item.Name} batch {batch.BatchNo} is not on that purchase.").ToResult();
                var left = pl.Qty + pl.FreeQty - alreadyReturned.GetValueOrDefault(plId);
                if (qty > left)
                    return e.Add(nameof(req.Lines), $"Only {left:0.###} of {batch.Item.Name} from this purchase can still be returned.").ToResult();
            }

            var rate = first.Rate ?? (pl is { LandingCost: > 0 } ? pl.LandingCost : batch.PurchaseRate);
            var gst = first.GstRate ?? pl?.GstRate ?? batch.Item.GstRate;
            var calc = PurchaseCalculator.CalculateLine(qty, rate, 0, gst);
            built.Add(new PurchaseReturnLine
            {
                ItemId = batch.ItemId, BatchId = batch.Id, PurchaseLineId = pl?.Id, ItemName = batch.Item.Name,
                BatchNo = batch.BatchNo, ExpiryDate = batch.ExpiryDate, Qty = qty, Rate = rate, GstRate = gst,
                Taxable = calc.Taxable, Tax = calc.Tax, Amount = calc.Amount,
            });
        }

        var totals = PurchaseCalculator.Totals(built.Select(b => new PurchaseCalculator.Line(b.Taxable, b.Tax, b.Amount)));
        var userId = principal.GetUserId();
        var now = clock.GetLocalNow().DateTime;

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Expired lots can go back too: returning expiry is the usual reason.
            foreach (var group in built.GroupBy(b => b.BatchId))
            {
                var qty = group.Sum(b => b.Qty);
                var updated = await db.Batches.Where(b => b.Id == group.Key && b.QtyOnHand >= qty)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.QtyOnHand, b => b.QtyOnHand - qty)
                        .SetProperty(b => b.UpdatedAt, DateTime.UtcNow), ct);
                if (updated == 0)
                {
                    await tx.RollbackAsync(ct);
                    var available = await db.Batches.Where(b => b.Id == group.Key).Select(b => b.QtyOnHand).FirstAsync(ct);
                    var line = group.First();
                    return DbHelpers.Conflict($"Only {available:0.###} of {line.ItemName} batch {line.BatchNo} is in stock.");
                }
            }

            var fy = Documents.FinancialYear(now);
            var number = await Documents.NextNumberAsync(db, ReturnKind, fy, ct);
            var ret = new PurchaseReturn
            {
                ReturnNo = $"{DebitNotePrefix}{fy}-{number:D6}",
                FinancialYear = fy,
                ReturnDate = now,
                SupplierId = supplier.Id,
                PurchaseId = purchase?.Id,
                Reason = Errors.Clean(req.Reason),
                Taxable = totals.Taxable, Tax = totals.Tax, RoundOff = totals.RoundOff, Total = totals.Total,
                CreatedByUserId = userId,
                Lines = built,
            };
            db.PurchaseReturns.Add(ret);
            db.StockMovements.AddRange(built.Select(b => new StockMovement
            {
                ItemId = b.ItemId, BatchId = b.BatchId, QtyChange = -b.Qty, Type = StockMovementType.PurchaseReturn,
                RefNo = ret.ReturnNo, UserId = userId,
                Note = Errors.Truncate($"To {supplier.Name}" + (ret.Reason is null ? "" : $": {ret.Reason}"), 200),
            }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/purchase-returns/{ret.Id}", await LoadDto(db, ret.Id, ct));
        });
    }

    private static async Task<PagedResult<PurchaseReturnSummaryDto>> List(PosDbContext db, DateOnly? from, DateOnly? to,
        int? supplierId, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var query = db.PurchaseReturns.AsNoTracking();
        if (from is { } f) query = query.Where(r => r.ReturnDate >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) query = query.Where(r => r.ReturnDate < t.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (supplierId is { } sid) query = query.Where(r => r.SupplierId == sid);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.ReturnDate).ThenByDescending(r => r.Id).Skip((p - 1) * size).Take(size)
            .Select(r => new PurchaseReturnSummaryDto(r.Id, r.ReturnNo, r.ReturnDate, r.Supplier.Name,
                r.Purchase != null ? r.Purchase.PurchaseNo : null, r.Lines.Count, r.Total))
            .ToListAsync(ct);
        return new PagedResult<PurchaseReturnSummaryDto>(rows, total, p, size);
    }

    private static async Task<IResult> Get(int id, PosDbContext db, CancellationToken ct) =>
        await LoadDto(db, id, ct) is { } dto ? Results.Ok(dto) : DbHelpers.NotFound("Purchase return");

    private static Task<PurchaseReturnDto?> LoadDto(PosDbContext db, int id, CancellationToken ct) =>
        db.PurchaseReturns.AsNoTracking().Where(r => r.Id == id).Select(r => new PurchaseReturnDto
        {
            Id = r.Id, ReturnNo = r.ReturnNo, ReturnDate = r.ReturnDate, SupplierId = r.SupplierId,
            SupplierName = r.Supplier.Name, SupplierGstin = r.Supplier.Gstin,
            PurchaseNo = r.Purchase != null ? r.Purchase.PurchaseNo : null,
            SupplierInvoiceNo = r.Purchase != null ? r.Purchase.SupplierInvoiceNo : null,
            Reason = r.Reason, Taxable = r.Taxable, Tax = r.Tax, RoundOff = r.RoundOff, Total = r.Total,
            CreatedBy = r.CreatedBy.FullName,
            Lines = r.Lines.OrderBy(l => l.Id).Select(l => new PurchaseReturnLineDto(l.Id, l.ItemId, l.ItemName, l.BatchId,
                l.BatchNo, l.ExpiryDate, l.Qty, l.Rate, l.GstRate, l.Taxable, l.Tax, l.Amount)).ToList(),
        }).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Lots in stock that were bought from this supplier and are expired or expire within <paramref name="days"/>:
    /// the usual expiry-return list a distributor's rep collects against.
    /// </summary>
    private static async Task<List<ReturnCandidateDto>> Candidates(int supplierId, int? days, PosDbContext db, TimeProvider clock,
        CancellationToken ct)
    {
        var cutoff = DateOnly.FromDateTime(clock.GetLocalNow().DateTime).AddDays(Math.Clamp(days ?? 0, 0, 365));
        return await db.Batches.AsNoTracking()
            .Where(b => b.QtyOnHand > 0 && b.ExpiryDate != null && b.ExpiryDate <= cutoff
                && db.PurchaseLines.Any(l => l.BatchId == b.Id && l.Purchase.SupplierId == supplierId
                    && l.Purchase.Status == PurchaseStatus.Completed))
            .OrderBy(b => b.ExpiryDate).ThenBy(b => b.Item.Name)
            .Select(b => new ReturnCandidateDto(b.ItemId, b.Item.Name, b.Item.Pack, b.Id, b.BatchNo, b.ExpiryDate,
                b.QtyOnHand, b.PurchaseRate, b.Item.GstRate))
            .ToListAsync(ct);
    }

    private static async Task<IResult> Returnable(int id, PosDbContext db, CancellationToken ct)
    {
        var purchase = await db.Purchases.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (purchase is null) return DbHelpers.NotFound("Purchase");
        var rows = await db.PurchaseLines.AsNoTracking().Where(l => l.PurchaseId == id).OrderBy(l => l.Id)
            .Select(l => new ReturnablePurchaseLineDto(l.Id, l.ItemId, l.ItemName, l.BatchId, l.BatchNo, l.ExpiryDate,
                l.Qty + l.FreeQty,
                db.PurchaseReturnLines.Where(r => r.PurchaseLineId == l.Id).Sum(r => (decimal?)r.Qty) ?? 0,
                l.Batch.QtyOnHand, l.LandingCost, l.GstRate))
            .ToListAsync(ct);
        return Results.Ok(rows);
    }
}
