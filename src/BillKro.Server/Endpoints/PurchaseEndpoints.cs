using System.Security.Claims;
using BillKro.Data;
using BillKro.Server.Auth;
using BillKro.Server.Infrastructure;
using BillKro.Shared;
using BillKro.Sync.Parser;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillKro.Server.Endpoints;

public static class PurchaseEndpoints
{
    public const int MaxLines = 300;
    public const long MaxEdeFileBytes = 2 * 1024 * 1024;
    public const string PurchaseKind = "PUR";
    public const string PurchasePrefix = "P";

    public static void MapPurchases(this IEndpointRouteBuilder app)
    {
        // Receiving goods changes stock and costs, so it is a manager task.
        var g = app.MapGroup("/api/purchases").RequireAuthorization(Policies.Manager);
        g.MapGet("/", List);
        g.MapGet("/{id:int}", Get);
        g.MapPost("/", Create);
        g.MapPost("/{id:int}/cancel", Cancel);
        g.MapPost("/import-ede", ImportEde).DisableAntiforgery();

        app.MapGet("/api/suppliers/{id:int}/balance", SupplierBalance).RequireAuthorization(Policies.Manager);
    }

    // ---------------------------------------------------------------- receive

    private record PricedLine(PurchaseLineRequest Req, Item Item, string BatchNo, DateOnly? Expiry, decimal GstRate,
        decimal Mrp, decimal SaleRate, decimal WholesaleRate, decimal HospitalRate, decimal? MarginPercent,
        PurchaseCalculator.Line Calc, decimal LandingCost);

    /// <summary>
    /// Picks the lot for a received batch: the existing lot with the same MRP <b>and</b> the same purchase cost
    /// (to the paisa), else the batch number itself if unused, else a new lot "BATCH/n". So every box keeps the
    /// price it was bought at, and its selling rate/margin stays tied to that cost.
    /// </summary>
    internal static string ResolveLot(string batchNo, decimal mrp, decimal cost,
        List<(string BatchNo, decimal Mrp, decimal Cost)> family)
    {
        if (family.FirstOrDefault(l => l.Mrp == mrp && SameCost(l.Cost, cost)) is { BatchNo: { } same }) return same;
        if (family.Count == 0) return batchNo;
        var highest = family.Select(l => l.BatchNo == batchNo ? 1
            : int.TryParse(l.BatchNo[(batchNo.Length + 1)..], out var n) ? n : 1).Max();
        return $"{batchNo}/{highest + 1}";
    }

    private static bool SameCost(decimal a, decimal b) =>
        Math.Round(a, 2, MidpointRounding.AwayFromZero) == Math.Round(b, 2, MidpointRounding.AwayFromZero);

    private static bool InFamily(string lot, string batchNo) =>
        lot == batchNo || (lot.StartsWith(batchNo + "/") && int.TryParse(lot[(batchNo.Length + 1)..], out _));

    private static async Task<IResult> Create(PurchaseRequest req, ClaimsPrincipal principal, PosDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var e = new Errors().Required(nameof(req.SupplierInvoiceNo), req.SupplierInvoiceNo, 30)
            .MaxLength(nameof(req.Notes), req.Notes, 500);
        if (req.Lines is not { Count: > 0 }) e.Add(nameof(req.Lines), "Add at least one item.");
        else if (req.Lines.Count > MaxLines) e.Add(nameof(req.Lines), $"A purchase can have at most {MaxLines} lines.");
        if (e.Any) return e.ToResult();

        var now = clock.GetLocalNow().DateTime;
        var today = DateOnly.FromDateTime(now);
        if (req.SupplierInvoiceDate > today) e.Add(nameof(req.SupplierInvoiceDate), "Supplier invoice date cannot be in the future.");

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SupplierId && s.IsActive, ct);
        if (supplier is null) return e.Add(nameof(req.SupplierId), "Choose an active supplier.").ToResult();

        var invoiceNo = req.SupplierInvoiceNo.Trim().ToUpperInvariant();
        if (await db.Purchases.AnyAsync(p => p.SupplierId == supplier.Id && p.SupplierInvoiceNo == invoiceNo
                && p.Status == PurchaseStatus.Completed, ct))
            return DbHelpers.Conflict($"Invoice {invoiceNo} from {supplier.Name} has already been entered.");

        var itemIds = req.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var lots = await db.Batches.AsNoTracking().Where(b => itemIds.Contains(b.ItemId))
            .Select(b => new { b.ItemId, b.BatchNo, b.Mrp, b.PurchaseRate, b.ExpiryDate }).ToListAsync(ct);
        // Lots planned by earlier lines of this same purchase, so lines with different MRPs or costs split correctly.
        var planned = new List<(int ItemId, string BatchNo, decimal Mrp, decimal Cost)>();

        var priced = new List<PricedLine>();
        for (var i = 0; i < req.Lines.Count; i++)
        {
            var l = req.Lines[i];
            var key = $"Lines[{i}]";
            if (!items.TryGetValue(l.ItemId, out var item) || !item.IsActive)
            {
                e.Add(key, "Item not found or inactive.");
                continue;
            }

            if (l.Qty <= 0) e.Add(key, $"{item.Name}: quantity must be more than zero.");
            if (l.FreeQty < 0) e.Add(key, $"{item.Name}: free quantity cannot be negative.");
            if (decimal.Round(l.Qty, 3) != l.Qty || decimal.Round(l.FreeQty, 3) != l.FreeQty)
                e.Add(key, $"{item.Name}: quantities can have at most 3 decimals.");
            if (l.Rate < 0) e.Add(key, $"{item.Name}: rate cannot be negative.");
            if (l.DiscountPercent is < 0 or > 100) e.Add(key, $"{item.Name}: discount must be 0–100%.");
            if (l.MarginPercent is < 0 or > 1000) e.Add(key, $"{item.Name}: margin must be 0–1000%.");
            var gst = l.GstRate ?? item.GstRate;
            if (gst is < 0 or > 40) e.Add(key, $"{item.Name}: GST must be 0–40%.");

            var mrp = l.Mrp > 0 ? l.Mrp : item.Mrp;
            var calc = PurchaseCalculator.CalculateLine(l.Qty, l.Rate, l.DiscountPercent, gst);
            var landing = PurchaseCalculator.LandingCost(calc.Taxable, l.Qty, l.FreeQty);

            string batchNo;
            DateOnly? expiry = null;
            if (item.IsBatchTracked)
            {
                var entered = l.BatchNo?.Trim().ToUpperInvariant() ?? "";
                expiry = l.ExpiryDate;
                if (entered.Length == 0 || entered == Batch.DefaultBatchNo) e.Add(key, $"{item.Name}: enter the batch number.");
                else if (entered.Length > 26) e.Add(key, $"{item.Name}: batch number is too long.");
                if (expiry is null) e.Add(key, $"{item.Name}: enter the expiry date.");
                else if (expiry < today) e.Add(key, $"{item.Name}: batch {entered} expired on {expiry:dd-MM-yyyy}; do not receive expired stock.");
                if (l.MfgDate is { } mfg && expiry is { } exp && mfg >= exp) e.Add(key, $"{item.Name}: mfg. date must be before expiry.");

                var family = lots.Where(b => b.ItemId == item.Id && InFamily(b.BatchNo, entered)).ToList();
                if (family.FirstOrDefault(b => b.ExpiryDate is { } had && expiry is { } got && had != got) is { } clash)
                    e.Add(key, $"{item.Name}: batch {entered} is already in stock with expiry {clash.ExpiryDate:MM/yyyy}, not {expiry:MM/yyyy}.");

                batchNo = ResolveLot(entered, mrp, landing, family.Select(b => (b.BatchNo, b.Mrp, Cost: b.PurchaseRate))
                    .Concat(planned.Where(p => p.ItemId == item.Id && InFamily(p.BatchNo, entered)).Select(p => (p.BatchNo, p.Mrp, p.Cost)))
                    .DistinctBy(x => x.BatchNo).ToList());
                if (!planned.Any(p => p.ItemId == item.Id && p.BatchNo == batchNo)) planned.Add((item.Id, batchNo, mrp, landing));
            }
            else batchNo = Batch.DefaultBatchNo;

            // Selling rates: as entered, else from margin on landed cost (selling GST is the item's), else the item's.
            var margin = l.MarginPercent ?? (item.MarginPercent > 0 ? item.MarginPercent : null);
            var sale = l.SaleRate > 0 ? l.SaleRate
                : margin is { } m ? Pricing.FromMargin(landing, item.GstRate, m, mrp)
                : item.SaleRate;
            var wholesale = l.WholesaleRate > 0 ? l.WholesaleRate
                : item.WholesaleMarginPercent > 0 ? Pricing.FromMargin(landing, item.GstRate, item.WholesaleMarginPercent, mrp)
                : item.WholesaleRate;
            var hospital = l.HospitalRate > 0 ? l.HospitalRate : item.HospitalRate;
            if (mrp > 0 && sale > mrp) e.Add(key, $"{item.Name}: selling rate {sale:0.00} is above MRP {mrp:0.00}.");
            if (mrp > 0 && (wholesale > mrp || hospital > mrp)) e.Add(key, $"{item.Name}: wholesale/hospital rate is above MRP {mrp:0.00}.");

            priced.Add(new PricedLine(l, item, batchNo, expiry, gst, mrp, sale, wholesale, hospital,
                l.SaleRate > 0 ? null : margin, calc, landing));
        }
        if (e.Any) return e.ToResult();

        var totals = PurchaseCalculator.Totals(priced.Select(p => p.Calc));
        var userId = principal.GetUserId();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // 1. Create lots that are new to us (quantity and cost are added atomically below).
            var batchIds = await db.Batches.Where(b => itemIds.Contains(b.ItemId))
                .Select(b => new { b.ItemId, b.BatchNo, b.Id }).ToListAsync(ct);
            var ids = batchIds.ToDictionary(b => (b.ItemId, b.BatchNo), b => b.Id);
            var created = new List<Batch>();
            foreach (var p in priced.Where(p => !ids.ContainsKey((p.Item.Id, p.BatchNo))).DistinctBy(p => (p.Item.Id, p.BatchNo)))
                created.Add(db.Batches.Add(new Batch
                {
                    ItemId = p.Item.Id, BatchNo = p.BatchNo, ExpiryDate = p.Expiry, MfgDate = p.Req.MfgDate,
                    Mrp = p.Mrp, SaleRate = p.SaleRate, WholesaleRate = p.WholesaleRate, HospitalRate = p.HospitalRate,
                    PurchaseRate = p.LandingCost, QtyOnHand = 0,
                }).Entity);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return DbHelpers.Conflict("Another counter added the same batch just now. Please save again.");
            }
            foreach (var b in created) ids[(b.ItemId, b.BatchNo)] = b.Id;

            // 2. Add stock. Batch-tracked lots only receive stock at their own cost (see ResolveLot), so the average
            //    below only moves for untracked items, which share the single "-" lot. Both sides use the row's values
            //    from before this UPDATE, so concurrent sales cannot skew it.
            foreach (var p in priced)
            {
                var received = p.Req.Qty + p.Req.FreeQty;
                var landing = p.LandingCost;
                var batchId = ids[(p.Item.Id, p.BatchNo)];
                await db.Batches.Where(b => b.Id == batchId).ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.PurchaseRate, b => (b.QtyOnHand * b.PurchaseRate + received * landing) / (b.QtyOnHand + received))
                    .SetProperty(b => b.QtyOnHand, b => b.QtyOnHand + received)
                    .SetProperty(b => b.Mrp, p.Mrp)
                    .SetProperty(b => b.SaleRate, p.SaleRate)
                    .SetProperty(b => b.WholesaleRate, p.WholesaleRate)
                    .SetProperty(b => b.HospitalRate, p.HospitalRate)
                    .SetProperty(b => b.MfgDate, b => p.Req.MfgDate ?? b.MfgDate)
                    .SetProperty(b => b.UpdatedAt, DateTime.UtcNow), ct);
                await db.Items.Where(i => i.Id == p.Item.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.PurchaseRate, landing)
                    .SetProperty(i => i.Mrp, p.Mrp)
                    .SetProperty(i => i.SaleRate, p.SaleRate)
                    .SetProperty(i => i.WholesaleRate, i => p.WholesaleRate > 0 ? p.WholesaleRate : i.WholesaleRate)
                    .SetProperty(i => i.HospitalRate, i => p.HospitalRate > 0 ? p.HospitalRate : i.HospitalRate)
                    .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);
            }
            // 3. The purchase document and its stock movements.
            var fy = Documents.FinancialYear(now);
            var number = await Documents.NextNumberAsync(db, PurchaseKind, fy, ct);
            var purchase = new Purchase
            {
                PurchaseNo = $"{PurchasePrefix}{fy}-{number:D6}",
                FinancialYear = fy,
                Status = PurchaseStatus.Completed,
                SupplierId = supplier.Id,
                SupplierInvoiceNo = invoiceNo,
                SupplierInvoiceDate = req.SupplierInvoiceDate,
                ReceivedAt = now,
                Notes = Errors.Clean(req.Notes),
                Taxable = totals.Taxable, Tax = totals.Tax, RoundOff = totals.RoundOff, Total = totals.Total,
                CreatedByUserId = userId,
                Lines = priced.Select(p => new PurchaseLine
                {
                    ItemId = p.Item.Id, BatchId = ids[(p.Item.Id, p.BatchNo)], ItemCode = p.Item.Code, ItemName = p.Item.Name,
                    BatchNo = p.BatchNo, ExpiryDate = p.Expiry, Qty = p.Req.Qty, FreeQty = p.Req.FreeQty, Rate = p.Req.Rate,
                    DiscountPercent = p.Req.DiscountPercent, GstRate = p.GstRate, Mrp = p.Mrp, SaleRate = p.SaleRate,
                    WholesaleRate = p.WholesaleRate, HospitalRate = p.HospitalRate, LandingCost = p.LandingCost,
                    MarginPercent = p.MarginPercent, Taxable = p.Calc.Taxable, Tax = p.Calc.Tax, Amount = p.Calc.Amount,
                }).ToList(),
            };
            db.Purchases.Add(purchase);
            db.StockMovements.AddRange(priced.Select(p => new StockMovement
            {
                ItemId = p.Item.Id, BatchId = ids[(p.Item.Id, p.BatchNo)], QtyChange = p.Req.Qty + p.Req.FreeQty,
                Type = StockMovementType.Purchase, RefNo = purchase.PurchaseNo, UserId = userId,
                Note = Errors.Truncate($"{supplier.Name} inv. {invoiceNo}", 200),
            }));

            await RememberAliasesAsync(db, supplier.Id, priced, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/purchases/{purchase.Id}", await LoadDto(db, purchase.Id, ct));
        });
    }

    private static async Task RememberAliasesAsync(PosDbContext db, int supplierId, List<PricedLine> lines, CancellationToken ct)
    {
        var mapped = lines.Where(p => !string.IsNullOrWhiteSpace(p.Req.SupplierItemCode))
            .GroupBy(p => p.Req.SupplierItemCode!.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().Item.Id);
        if (mapped.Count == 0) return;

        var codes = mapped.Keys.ToList();
        var existing = await db.ItemAliases.Where(a => a.SupplierId == supplierId && codes.Contains(a.SupplierItemCode))
            .ToDictionaryAsync(a => a.SupplierItemCode, ct);
        foreach (var (code, itemId) in mapped)
        {
            if (existing.TryGetValue(code, out var alias)) alias.ItemId = itemId;
            else if (code.Length <= 50) db.ItemAliases.Add(new ItemAlias { SupplierId = supplierId, SupplierItemCode = code, ItemId = itemId });
        }
    }

    // ---------------------------------------------------------------- read

    private static async Task<PagedResult<PurchaseSummaryDto>> List(PosDbContext db, DateOnly? from, DateOnly? to,
        string? q, int? supplierId, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var query = db.Purchases.AsNoTracking();
        if (from is { } f) query = query.Where(x => x.ReceivedAt >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) query = query.Where(x => x.ReceivedAt < t.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (supplierId is { } sid) query = query.Where(x => x.SupplierId == sid);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = DbHelpers.Contains(q);
            query = query.Where(x => EF.Functions.Like(x.PurchaseNo, like) || EF.Functions.Like(x.SupplierInvoiceNo, like)
                || EF.Functions.Like(x.Supplier.Name, like));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(x => new PurchaseSummaryDto(x.Id, x.PurchaseNo, x.Supplier.Name, x.SupplierInvoiceNo, x.SupplierInvoiceDate,
                x.ReceivedAt, x.Lines.Count, x.Total, x.Status))
            .ToListAsync(ct);
        return new PagedResult<PurchaseSummaryDto>(rows, total, p, size);
    }

    private static async Task<IResult> Get(int id, PosDbContext db, CancellationToken ct) =>
        await LoadDto(db, id, ct) is { } dto ? Results.Ok(dto) : DbHelpers.NotFound("Purchase");

    private static async Task<PurchaseDto?> LoadDto(PosDbContext db, int id, CancellationToken ct) =>
        await db.Purchases.AsNoTracking().Where(x => x.Id == id).Select(x => new PurchaseDto
        {
            Id = x.Id, PurchaseNo = x.PurchaseNo, Status = x.Status, SupplierId = x.SupplierId, SupplierName = x.Supplier.Name,
            SupplierInvoiceNo = x.SupplierInvoiceNo, SupplierInvoiceDate = x.SupplierInvoiceDate, ReceivedAt = x.ReceivedAt,
            Notes = x.Notes, Taxable = x.Taxable, Tax = x.Tax, RoundOff = x.RoundOff, Total = x.Total,
            ReceivedBy = x.CreatedBy.FullName, CancelReason = x.CancelReason,
            Lines = x.Lines.OrderBy(l => l.Id).Select(l => new PurchaseLineDto
            {
                Id = l.Id, ItemId = l.ItemId, ItemCode = l.ItemCode, ItemName = l.ItemName, BatchId = l.BatchId, BatchNo = l.BatchNo,
                ExpiryDate = l.ExpiryDate, Qty = l.Qty, FreeQty = l.FreeQty, Rate = l.Rate, DiscountPercent = l.DiscountPercent,
                GstRate = l.GstRate, Mrp = l.Mrp, SaleRate = l.SaleRate, WholesaleRate = l.WholesaleRate, HospitalRate = l.HospitalRate,
                LandingCost = l.LandingCost, MarginPercent = l.MarginPercent, Taxable = l.Taxable, Tax = l.Tax, Amount = l.Amount,
            }).ToList(),
        }).FirstOrDefaultAsync(ct);

    private static async Task<IResult> SupplierBalance(int id, PosDbContext db, CancellationToken ct)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == id, ct)) return DbHelpers.NotFound("Supplier");
        return Results.Ok(new SupplierBalanceDto(id, await Documents.SupplierOutstandingAsync(db, id, ct)));
    }

    // ---------------------------------------------------------------- cancel

    private static async Task<IResult> Cancel(int id, CancelPurchaseRequest req, ClaimsPrincipal principal, PosDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return new Errors().Add(nameof(req.Reason), "Give a reason for cancelling.").ToResult();
        var userId = principal.GetUserId();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var purchase = await db.Purchases.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (purchase is null) return DbHelpers.NotFound("Purchase");
            if (purchase.Status == PurchaseStatus.Cancelled) return DbHelpers.Conflict("This purchase is already cancelled.");
            if (await db.PurchaseReturns.AnyAsync(r => r.PurchaseId == id, ct))
                return DbHelpers.Conflict("Goods from this purchase have been returned to the supplier; it cannot be cancelled.");

            // Take the received stock back out; refuse if some of it has already been sold.
            foreach (var group in purchase.Lines.GroupBy(l => l.BatchId))
            {
                var qty = group.Sum(l => l.Qty + l.FreeQty);
                var updated = await db.Batches.Where(b => b.Id == group.Key && b.QtyOnHand >= qty)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.QtyOnHand, b => b.QtyOnHand - qty)
                        .SetProperty(b => b.UpdatedAt, DateTime.UtcNow), ct);
                if (updated == 0)
                {
                    await tx.RollbackAsync(ct);
                    var available = await db.Batches.Where(b => b.Id == group.Key).Select(b => b.QtyOnHand).FirstAsync(ct);
                    var line = group.First();
                    return DbHelpers.Conflict(
                        $"Cannot cancel: only {available:0.###} of {line.ItemName} batch {line.BatchNo} is left (received {qty:0.###}). " +
                        "Use a stock adjustment (return to supplier) for what remains.");
                }
                db.StockMovements.Add(new StockMovement
                {
                    ItemId = group.First().ItemId, BatchId = group.Key, QtyChange = -qty,
                    Type = StockMovementType.PurchaseCancel, RefNo = purchase.PurchaseNo, UserId = userId,
                    Note = Errors.Truncate(req.Reason.Trim(), 200),
                });
            }

            purchase.Status = PurchaseStatus.Cancelled;
            purchase.CancelReason = Errors.Truncate(req.Reason.Trim(), 200);
            purchase.CancelledAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(await LoadDto(db, id, ct));
        });
    }

    // ---------------------------------------------------------------- EDE import (preview only)

    /// <summary>
    /// Reads a distributor's EDE bill file and returns a draft purchase with each line matched to our items where
    /// possible. Nothing is saved; the draft is reviewed and posted through <see cref="Create"/>.
    /// </summary>
    private static async Task<IResult> ImportEde(IFormFile file, int? supplierId, PosDbContext db, TimeProvider clock,
        CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > MaxEdeFileBytes)
            return new Errors().Add("file", "Choose an EDE file under 2 MB.").ToResult();

        var path = Path.Combine(Path.GetTempPath(), $"billkro-ede-{Guid.NewGuid():N}.csv");
        IReadOnlyList<BillKro.Sync.Core.Models.PurchaseBill> bills;
        try
        {
            await using (var stream = File.Create(path))
                await file.CopyToAsync(stream, ct);
            var parser = new EdeParser();
            if (!parser.CanParse(path))
                return new Errors().Add("file", "This is not an EDE purchase file (it should start with an H line).").ToResult();
            bills = await parser.ParseAsync(path, ct);
        }
        finally
        {
            File.Delete(path);
        }

        if (bills.Count == 0 || bills[0].Lines.Count == 0)
            return new Errors().Add("file", "No bill lines were found in this file.").ToResult();

        var bill = bills[0];
        var warnings = new List<string>();
        if (bills.Count > 1) warnings.Add($"The file has {bills.Count} bills; only the first ({bill.BillNumber}) was imported.");

        var codes = bill.Lines.Select(l => l.ItemCode.Trim().ToUpperInvariant()).Distinct().ToList();
        var names = bill.Lines.Select(l => l.ItemName.Trim()).Distinct().ToList();

        var aliasItems = supplierId is { } sid
            ? await db.ItemAliases.AsNoTracking().Where(a => a.SupplierId == sid && codes.Contains(a.SupplierItemCode))
                .ToDictionaryAsync(a => a.SupplierItemCode, a => a.ItemId, ct)
            : new Dictionary<string, int>();
        var candidates = await db.Items.AsNoTracking()
            .Where(i => i.IsActive && (aliasItems.Values.Contains(i.Id) || codes.Contains(i.Code)
                || (i.Barcode != null && codes.Contains(i.Barcode)) || names.Contains(i.Name)))
            .Select(ItemEndpoints.ToDto).ToListAsync(ct);

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var lines = bill.Lines.Select(l =>
        {
            var code = l.ItemCode.Trim().ToUpperInvariant();
            var (match, how) = aliasItems.TryGetValue(code, out var aliased) && candidates.FirstOrDefault(c => c.Id == aliased) is { } a
                ? (a, "remembered code")
                : candidates.FirstOrDefault(c => c.Code == code) is { } byCode ? (byCode, "item code")
                : candidates.FirstOrDefault(c => c.Barcode == code) is { } byBarcode ? (byBarcode, "barcode")
                : candidates.FirstOrDefault(c => string.Equals(c.Name, l.ItemName.Trim(), StringComparison.OrdinalIgnoreCase)) is { } byName
                    ? (byName, "name")
                : ((ItemDto?)null, "");

            if (l.ExpiryDate is { } exp && exp < today)
                warnings.Add($"{l.ItemName} batch {l.BatchNo} expired on {exp:dd-MM-yyyy}.");
            return new EdeImportLineDto
            {
                SupplierItemCode = code, ItemName = l.ItemName.Trim(), Pack = l.PackForm, Manufacturer = l.Manufacturer,
                BatchNo = l.BatchNo, ExpiryDate = l.ExpiryDate, Qty = l.Quantity, FreeQty = l.FreeQuantity,
                Rate = l.PurchaseRate, DiscountPercent = l.Discount, GstRate = l.TaxRate, Mrp = l.MRP,
                MatchedItem = match, MatchedBy = how,
            };
        }).ToList();

        var unmatched = lines.Count(l => l.MatchedItem is null);
        if (unmatched > 0) warnings.Insert(0, $"{unmatched} of {lines.Count} line(s) need to be matched to your items.");

        return Results.Ok(new EdeImportDto
        {
            SupplierInvoiceNo = bill.BillNumber,
            SupplierInvoiceDate = bill.BillDate,
            SupplierNameInFile = string.IsNullOrWhiteSpace(bill.SupplierName) ? null : bill.SupplierName,
            FileTotal = bill.NetAmount > 0 ? bill.NetAmount : bill.TotalAmount,
            Lines = lines,
            Warnings = warnings,
        });
    }
}
