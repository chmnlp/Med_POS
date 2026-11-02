using System.Security.Claims;
using BillKro.Data;
using BillKro.Server.Auth;
using BillKro.Server.Infrastructure;
using BillKro.Shared;
using Microsoft.EntityFrameworkCore;

namespace BillKro.Server.Endpoints;

public static class StockEndpoints
{
    public const string AdjustmentKind = "ADJ";
    public const string AdjustmentPrefix = "A";

    public static void MapStock(this IEndpointRouteBuilder app)
    {
        var adj = app.MapGroup("/api/stock/adjustments").RequireAuthorization(Policies.Manager);
        adj.MapPost("/", Adjust);
        adj.MapGet("/", ListAdjustments);

        app.MapGet("/api/items/{id:int}/movements", Movements);

        var reports = app.MapGroup("/api/reports").RequireAuthorization(Policies.Manager);
        reports.MapGet("/expiry", Expiry);
        reports.MapGet("/reorder", Reorder);
        reports.MapGet("/stock-valuation", Valuation);
        reports.MapGet("/profit", Profit);
    }

    // ---------------------------------------------------------------- adjustments

    private static async Task<IResult> Adjust(StockAdjustmentRequest req, ClaimsPrincipal principal, PosDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var e = new Errors().MaxLength(nameof(req.Note), req.Note, 200);
        if (!Enum.IsDefined(req.Reason)) e.Add(nameof(req.Reason), "Unknown reason.");
        if (req.QtyChange == 0) e.Add(nameof(req.QtyChange), "Enter the quantity to add or remove.");
        else if (decimal.Round(req.QtyChange, 3) != req.QtyChange) e.Add(nameof(req.QtyChange), "Quantity can have at most 3 decimals.");
        // Only a physical count (or a noted special case) can add stock; everything else removes it.
        if (req.QtyChange > 0 && req.Reason is not (AdjustmentReason.CountCorrection or AdjustmentReason.Other))
            e.Add(nameof(req.QtyChange), $"{req.Reason} can only remove stock. Use Count correction to add.");
        if ((req.Reason is AdjustmentReason.Other or AdjustmentReason.CountCorrection) && string.IsNullOrWhiteSpace(req.Note))
            e.Add(nameof(req.Note), "Add a note explaining the adjustment.");
        // Returns to a supplier are debit notes (they change what we owe), not stock corrections.
        if (req.Reason == AdjustmentReason.ReturnToSupplier)
            e.Add(nameof(req.Reason), "Use Supplier returns, so a debit note is raised and the supplier balance is reduced.");
        if (e.Any) return e.ToResult();

        var batch = await db.Batches.AsNoTracking().Include(b => b.Item).FirstOrDefaultAsync(b => b.Id == req.BatchId, ct);
        if (batch is null) return DbHelpers.NotFound("Batch");
        if (req.SupplierId is { } sid && !await db.Suppliers.AnyAsync(s => s.Id == sid, ct))
            return e.Add(nameof(req.SupplierId), "Supplier not found.").ToResult();

        var userId = principal.GetUserId();
        var now = clock.GetLocalNow().DateTime;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var change = req.QtyChange;
            var updated = await db.Batches
                .Where(b => b.Id == batch.Id && b.QtyOnHand + change >= 0)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.QtyOnHand, b => b.QtyOnHand + change)
                    .SetProperty(b => b.UpdatedAt, DateTime.UtcNow), ct);
            if (updated == 0)
            {
                await tx.RollbackAsync(ct);
                var available = await db.Batches.Where(b => b.Id == batch.Id).Select(b => b.QtyOnHand).FirstAsync(ct);
                return DbHelpers.Conflict($"Only {available:0.###} of {batch.Item.Name} batch {batch.BatchNo} is in stock.");
            }

            var fy = Documents.FinancialYear(now);
            var number = await Documents.NextNumberAsync(db, AdjustmentKind, fy, ct);
            var adjustment = new StockAdjustment
            {
                AdjustmentNo = $"{AdjustmentPrefix}{fy}-{number:D6}",
                Date = now, ItemId = batch.ItemId, BatchId = batch.Id, QtyChange = change, Reason = req.Reason,
                Note = Errors.Clean(req.Note), SupplierId = req.SupplierId, UserId = userId,
            };
            db.StockAdjustments.Add(adjustment);
            db.StockMovements.Add(new StockMovement
            {
                ItemId = batch.ItemId, BatchId = batch.Id, QtyChange = change, Type = StockMovementType.Adjustment,
                RefNo = adjustment.AdjustmentNo, UserId = userId,
                Note = Errors.Truncate(ReasonText(req.Reason) + (adjustment.Note is null ? "" : $": {adjustment.Note}"), 200),
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Created($"/api/stock/adjustments/{adjustment.Id}", new StockAdjustmentDto(
                adjustment.Id, adjustment.AdjustmentNo, adjustment.Date, batch.Item.Name, batch.BatchNo, change,
                adjustment.Reason, adjustment.Note, null, principal.Identity?.Name ?? ""));
        });
    }

    private static async Task<PagedResult<StockAdjustmentDto>> ListAdjustments(PosDbContext db, DateOnly? from, DateOnly? to,
        int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var query = db.StockAdjustments.AsNoTracking();
        if (from is { } f) query = query.Where(a => a.Date >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) query = query.Where(a => a.Date < t.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.Date).ThenByDescending(a => a.Id).Skip((p - 1) * size).Take(size)
            .Select(a => new StockAdjustmentDto(a.Id, a.AdjustmentNo, a.Date, a.Item.Name, a.Batch.BatchNo, a.QtyChange,
                a.Reason, a.Note, a.Supplier != null ? a.Supplier.Name : null, a.User.FullName))
            .ToListAsync(ct);
        return new PagedResult<StockAdjustmentDto>(rows, total, p, size);
    }

    private static string ReasonText(AdjustmentReason r) => r switch
    {
        AdjustmentReason.CountCorrection => "Count correction",
        AdjustmentReason.ReturnToSupplier => "Returned to supplier",
        _ => r.ToString(),
    };

    // ---------------------------------------------------------------- stock history

    /// <summary>Newest first, with the item's stock balance after each movement.</summary>
    private static async Task<IResult> Movements(int id, int? page, int? pageSize, PosDbContext db, CancellationToken ct)
    {
        if (!await db.Items.AnyAsync(i => i.Id == id, ct)) return DbHelpers.NotFound("Item");
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var all = db.StockMovements.AsNoTracking().Where(m => m.ItemId == id);
        var ordered = all.OrderByDescending(m => m.Id);

        var total = await all.CountAsync(ct);
        var stock = await all.SumAsync(m => (decimal?)m.QtyChange, ct) ?? 0;
        var newer = await ordered.Take((p - 1) * size).SumAsync(m => (decimal?)m.QtyChange, ct) ?? 0;

        var rows = await ordered.Skip((p - 1) * size).Take(size)
            .Select(m => new { m.Id, m.CreatedAt, m.Type, m.Batch.BatchNo, m.QtyChange, m.RefNo, m.Note, User = m.User != null ? m.User.FullName : null })
            .ToListAsync(ct);

        var balance = stock - newer;
        var result = new List<StockMovementDto>(rows.Count);
        foreach (var r in rows)
        {
            result.Add(new StockMovementDto(r.Id, DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc).ToLocalTime(),
                MovementText(r.Type), r.BatchNo, r.QtyChange, balance, r.RefNo, r.Note, r.User));
            balance -= r.QtyChange;
        }
        return Results.Ok(new PagedResult<StockMovementDto>(result, total, p, size));
    }

    private static string MovementText(StockMovementType t) => t switch
    {
        StockMovementType.SaleReturn => "Sale return",
        StockMovementType.SaleCancel => "Sale cancelled",
        StockMovementType.PurchaseCancel => "Purchase cancelled",
        _ => t.ToString(),
    };

    // ---------------------------------------------------------------- reports

    /// <summary>Batches in stock expiring within the window (and, optionally, already expired).</summary>
    private static async Task<List<ExpiryReportRow>> Expiry(int? days, bool? includeExpired, PosDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var cutoff = today.AddDays(Math.Clamp(days ?? 90, 0, 3650));
        var query = db.Batches.AsNoTracking()
            .Where(b => b.QtyOnHand > 0 && b.ExpiryDate != null && b.ExpiryDate <= cutoff && b.Item.IsActive);
        if (includeExpired == false) query = query.Where(b => b.ExpiryDate >= today);

        var rows = await query.OrderBy(b => b.ExpiryDate).ThenBy(b => b.Item.Name)
            .Select(b => new { b.ItemId, b.Item.Code, b.Item.Name, b.Item.Manufacturer, b.Id, b.BatchNo, Expiry = b.ExpiryDate!.Value,
                b.QtyOnHand, b.PurchaseRate, b.Mrp })
            .ToListAsync(ct);
        return rows.Select(r => new ExpiryReportRow(r.ItemId, r.Code, r.Name, r.Manufacturer, r.Id, r.BatchNo, r.Expiry,
            r.Expiry.DayNumber - today.DayNumber, r.QtyOnHand, r.PurchaseRate, r.Mrp,
            BillCalculator.Round2(r.QtyOnHand * r.PurchaseRate))).ToList();
    }

    /// <summary>Items at or below reorder level, with a suggested order up to twice the reorder level.</summary>
    private static async Task<List<ReorderReportRow>> Reorder(PosDbContext db, CancellationToken ct)
    {
        var rows = await db.Items.AsNoTracking()
            .Where(i => i.IsActive && i.ReorderLevel > 0)
            .Select(i => new
            {
                i.Id, i.Code, i.Name, i.Manufacturer, i.ReorderLevel,
                Stock = i.Batches.Sum(b => b.QtyOnHand),
                Last = db.PurchaseLines.Where(l => l.ItemId == i.Id && l.Purchase.Status == PurchaseStatus.Completed)
                    .OrderByDescending(l => l.Purchase.ReceivedAt)
                    .Select(l => new { Supplier = l.Purchase.Supplier.Name, l.Rate })
                    .FirstOrDefault(),
            })
            .Where(x => x.Stock <= x.ReorderLevel)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

        return rows.Select(r => new ReorderReportRow(r.Id, r.Code, r.Name, r.Manufacturer, r.Stock, r.ReorderLevel,
            Math.Max(0, Math.Ceiling(r.ReorderLevel * 2 - r.Stock)), r.Last?.Supplier, r.Last?.Rate)).ToList();
    }

    /// <summary>
    /// Gross profit = taxable sales value − lot cost recorded at the time of sale, for completed bills in the period;
    /// returns are taken off on the day they come back.
    /// </summary>
    private static Task<ProfitReport> Profit(DateOnly from, DateOnly to, ProfitGrouping? groupBy, PosDbContext db,
        CancellationToken ct) => ProfitAsync(db, from, to, groupBy, ct);

    public static async Task<ProfitReport> ProfitAsync(PosDbContext db, DateOnly from, DateOnly to, ProfitGrouping? groupBy,
        CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var sold = await db.SaleInvoiceLines.AsNoTracking()
            .Where(l => l.Invoice.Status == InvoiceStatus.Completed && l.Invoice.InvoiceDate >= start && l.Invoice.InvoiceDate < end)
            .Select(l => new { l.Invoice.InvoiceDate, l.ItemId, l.ItemName, l.Qty, Revenue = l.Taxable, Cost = l.Qty * l.CostRate })
            .ToListAsync(ct);
        var returned = await db.SaleReturns.AsNoTracking()
            .Where(r => r.Invoice.Status == InvoiceStatus.Completed && r.ReturnDate >= start && r.ReturnDate < end)
            .SelectMany(r => r.Lines.Select(l => new
            {
                InvoiceDate = r.ReturnDate, l.InvoiceLine.ItemId, l.InvoiceLine.ItemName,
                Qty = -l.Qty, Revenue = -l.Taxable, Cost = -l.Qty * l.InvoiceLine.CostRate,
            }))
            .ToListAsync(ct);

        var all = sold.Concat(returned).ToList();
        var rows = (groupBy == ProfitGrouping.Item
                ? all.GroupBy(x => (Key: x.ItemId.ToString(), Label: x.ItemName))
                : all.GroupBy(x => (Key: x.InvoiceDate.ToString("yyyy-MM-dd"), Label: x.InvoiceDate.ToString("dd-MM-yyyy ddd"))))
            .Select(g => new ProfitRow(g.Key.Key, g.Key.Label, g.Sum(x => x.Qty), g.Sum(x => x.Revenue),
                BillCalculator.Round2(g.Sum(x => x.Cost))))
            .OrderBy(r => groupBy == ProfitGrouping.Item ? r.Label : r.Key)
            .ToList();

        return new ProfitReport(rows, rows.Sum(r => r.Revenue), rows.Sum(r => r.Cost));
    }

    /// <summary>Stock on hand valued at landed purchase cost and at MRP.</summary>
    private static async Task<StockValuationReport> Valuation(string? q, int? page, int? pageSize, PosDbContext db, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var batches = db.Batches.AsNoTracking().Where(b => b.QtyOnHand != 0 && b.Item.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = DbHelpers.Contains(q);
            batches = batches.Where(b => EF.Functions.Like(b.Item.Name, like) || EF.Functions.Like(b.Item.Code, like)
                || (b.Item.Manufacturer != null && EF.Functions.Like(b.Item.Manufacturer, like)));
        }

        var grouped = batches.GroupBy(b => new { b.ItemId, b.Item.Code, b.Item.Name, b.Item.Manufacturer })
            .Select(g => new
            {
                g.Key.ItemId, g.Key.Code, g.Key.Name, g.Key.Manufacturer,
                Qty = g.Sum(b => b.QtyOnHand),
                PurchaseValue = g.Sum(b => b.QtyOnHand * b.PurchaseRate),
                MrpValue = g.Sum(b => b.QtyOnHand * b.Mrp),
            });

        var total = await grouped.CountAsync(ct);
        var totals = await batches.GroupBy(_ => 1).Select(g => new
        {
            Qty = g.Sum(b => b.QtyOnHand),
            Purchase = g.Sum(b => b.QtyOnHand * b.PurchaseRate),
            Mrp = g.Sum(b => b.QtyOnHand * b.Mrp),
        }).FirstOrDefaultAsync(ct);
        var rows = await grouped.OrderBy(x => x.Name).Skip((p - 1) * size).Take(size).ToListAsync(ct);

        return new StockValuationReport(
            rows.Select(r => new StockValuationRow(r.ItemId, r.Code, r.Name, r.Manufacturer, r.Qty,
                BillCalculator.Round2(r.PurchaseValue), BillCalculator.Round2(r.MrpValue))).ToList(),
            total, p, size,
            totals?.Qty ?? 0, BillCalculator.Round2(totals?.Purchase ?? 0), BillCalculator.Round2(totals?.Mrp ?? 0));
    }
}
