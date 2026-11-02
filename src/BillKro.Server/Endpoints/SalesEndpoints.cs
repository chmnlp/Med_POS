using System.Security.Claims;
using BillKro.Data;
using BillKro.Server.Auth;
using BillKro.Server.Infrastructure;
using BillKro.Shared;
using Microsoft.EntityFrameworkCore;

namespace BillKro.Server.Endpoints;

public static class SalesEndpoints
{
    public const int MaxLines = 200;

    public static void MapSales(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sales");
        g.MapPost("/", Create);
        g.MapGet("/", List);
        g.MapGet("/{id:int}", Get);
        g.MapPost("/{id:int}/cancel", Cancel).RequireAuthorization(Policies.Manager);
        g.MapPost("/{id:int}/returns", CreateReturn);
    }

    // ---------------------------------------------------------------- create

    private static async Task<IResult> Create(SaleRequest req, ClaimsPrincipal principal, PosDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var errors = ValidateShape(req);
        if (errors.Any) return errors.ToResult();

        var now = clock.GetLocalNow().DateTime;
        var today = DateOnly.FromDateTime(now);
        var userId = principal.GetUserId();
        var isCashier = principal.IsInRole(nameof(UserRole.Cashier));
        var settings = await db.StoreSettings.AsNoTracking().FirstAsync(ct);

        Customer? customer = null;
        if (req.CustomerId is { } customerId)
        {
            customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId && c.IsActive, ct);
            if (customer is null) return errors.Add(nameof(req.CustomerId), "Customer not found or inactive.").ToResult();
        }
        var customerType = customer?.Type ?? CustomerType.Retail;

        // Price and validate every line against current master data.
        var batchIds = req.Lines.Select(l => l.BatchId).Distinct().ToList();
        var batches = await db.Batches.AsNoTracking().Include(b => b.Item)
            .Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);

        var priced = new List<(SaleLineRequest Req, Batch Batch)>();
        for (var i = 0; i < req.Lines.Count; i++)
        {
            var line = req.Lines[i];
            var key = $"Lines[{i}]";
            if (!batches.TryGetValue(line.BatchId, out var batch) || batch.ItemId != line.ItemId)
            {
                errors.Add(key, "Item or batch not found.");
                continue;
            }
            var item = batch.Item;
            if (!item.IsActive) errors.Add(key, $"{item.Name} is inactive and cannot be sold.");
            if (batch.ExpiryDate is { } expiry && expiry < today)
                errors.Add(key, $"{item.Name} batch {batch.BatchNo} expired on {expiry:dd-MM-yyyy} and cannot be sold.");
            if (batch.Mrp > 0 && line.Rate > batch.Mrp)
                errors.Add(key, $"Rate for {item.Name} cannot exceed MRP {batch.Mrp:0.00}.");
            if (isCashier)
            {
                var defaultRate = Pricing.DefaultRate(customerType, batch.Mrp, batch.SaleRate,
                    Pricing.LotOrItem(batch.WholesaleRate, item.WholesaleRate), Pricing.LotOrItem(batch.HospitalRate, item.HospitalRate));
                if (line.Rate != defaultRate)
                    errors.Add(key, $"Only a manager can change the rate of {item.Name} (default {defaultRate:0.00}).");
                if (line.DiscountPercent > settings.MaxCashierDiscountPercent)
                    errors.Add(key, $"Cashier discount is limited to {settings.MaxCashierDiscountPercent:0.##}%.");
            }
            priced.Add((line, batch));
        }
        if (errors.Any) return errors.ToResult();

        if (priced.Any(p => p.Batch.Item.Schedule is DrugSchedule.H1 or DrugSchedule.X)
            && (string.IsNullOrWhiteSpace(req.DoctorName) || string.IsNullOrWhiteSpace(req.PatientName)))
            return errors.Add(nameof(req.DoctorName), "Doctor and patient names are required for Schedule H1/X medicines.").ToResult();

        var interState = BillCalculator.IsInterState(settings.EffectiveStateCode, customer?.Gstin);
        var totals = BillCalculator.Calculate(
            priced.Select(p => new BillLineInput(p.Req.Qty, p.Req.Rate, p.Req.DiscountPercent, p.Batch.Item.GstRate)).ToList(),
            interState);

        var paid = req.Payments.Sum(p => p.Amount);
        if (paid != totals.Total)
            return errors.Add(nameof(req.Payments), $"Payments ({paid:0.00}) must equal the bill total ({totals.Total:0.00}).").ToResult();

        var credit = req.Payments.Where(p => p.Mode == PaymentMode.Credit).Sum(p => p.Amount);
        if (credit > 0)
        {
            if (customer is null)
                return errors.Add(nameof(req.Payments), "Credit sales need a registered customer.").ToResult();
            if (customer.CreditLimit <= 0)
                return DbHelpers.Conflict($"{customer.Name} is a cash-only customer (no credit limit set).");
            var outstanding = await Documents.OutstandingAsync(db, customer.Id, ct);
            if (outstanding + credit > customer.CreditLimit)
                return DbHelpers.Conflict(
                    $"Credit limit exceeded for {customer.Name}: outstanding {outstanding:0.00} + this bill {credit:0.00} > limit {customer.CreditLimit:0.00}.");
        }

        SaleInvoice Build() => new()
        {
            FinancialYear = Documents.FinancialYear(now),
            InvoiceDate = now,
            Status = InvoiceStatus.Completed,
            CustomerId = customer?.Id,
            CustomerName = customer?.Name ?? Errors.Clean(req.CustomerName),
            CustomerPhone = customer?.Phone ?? Errors.Clean(req.CustomerPhone),
            CustomerGstin = customer?.Gstin,
            CustomerType = customerType,
            DoctorName = Errors.Clean(req.DoctorName),
            PatientName = Errors.Clean(req.PatientName),
            IsInterState = interState,
            Gross = totals.Gross,
            Discount = totals.Discount,
            Taxable = totals.Taxable,
            Cgst = totals.Cgst,
            Sgst = totals.Sgst,
            Igst = totals.Igst,
            RoundOff = totals.RoundOff,
            Total = totals.Total,
            BalanceDue = credit,
            CreatedByUserId = userId,
            Lines = priced.Select((p, i) => new SaleInvoiceLine
            {
                LineNo = i + 1,
                ItemId = p.Batch.ItemId,
                BatchId = p.Batch.Id,
                ItemCode = p.Batch.Item.Code,
                ItemName = p.Batch.Item.Name,
                Pack = p.Batch.Item.Pack,
                HsnCode = p.Batch.Item.HsnCode,
                BatchNo = p.Batch.BatchNo,
                ExpiryDate = p.Batch.ExpiryDate,
                Schedule = p.Batch.Item.Schedule,
                Mrp = p.Batch.Mrp,
                Qty = p.Req.Qty,
                Rate = p.Req.Rate,
                DiscountPercent = p.Req.DiscountPercent,
                GstRate = p.Batch.Item.GstRate,
                Gross = totals.Lines[i].Gross,
                Discount = totals.Lines[i].Discount,
                Taxable = totals.Lines[i].Taxable,
                Tax = totals.Lines[i].Tax,
                Amount = totals.Lines[i].Amount,
                CostRate = p.Batch.PurchaseRate,
            }).ToList(),
            Payments = req.Payments.Select(p => new SalePayment
            {
                Mode = p.Mode, Amount = p.Amount, Reference = Errors.Clean(p.Reference),
            }).ToList(),
        };

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var invoice = Build();

            // Conditional decrement: two counters selling the last units cannot both succeed.
            foreach (var group in invoice.Lines.GroupBy(l => l.BatchId))
            {
                var qty = group.Sum(l => l.Qty);
                var updated = await db.Batches
                    .Where(b => b.Id == group.Key && b.QtyOnHand >= qty)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.QtyOnHand, b => b.QtyOnHand - qty)
                        .SetProperty(b => b.UpdatedAt, DateTime.UtcNow), ct);
                if (updated == 0)
                {
                    await tx.RollbackAsync(ct);
                    var available = await db.Batches.Where(b => b.Id == group.Key).Select(b => b.QtyOnHand).FirstAsync(ct);
                    var first = group.First();
                    return DbHelpers.Conflict(
                        $"Not enough stock of {first.ItemName} (batch {first.BatchNo}): {available:0.###} available, {qty:0.###} on the bill.");
                }
            }

            var number = await Documents.NextNumberAsync(db, Documents.SaleKind, invoice.FinancialYear, ct);
            invoice.InvoiceNo = $"{settings.InvoicePrefix}{invoice.FinancialYear}-{number:D6}";
            db.SaleInvoices.Add(invoice);
            db.StockMovements.AddRange(invoice.Lines.Select(l => new StockMovement
            {
                ItemId = l.ItemId, BatchId = l.BatchId, QtyChange = -l.Qty,
                Type = StockMovementType.Sale, RefNo = invoice.InvoiceNo, UserId = userId,
            }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Created($"/api/sales/{invoice.Id}", await LoadDto(db, invoice.Id, ct));
        });
    }

    private static Errors ValidateShape(SaleRequest req)
    {
        var e = new Errors()
            .MaxLength(nameof(req.CustomerName), req.CustomerName, 200)
            .Phone(nameof(req.CustomerPhone), req.CustomerPhone)
            .MaxLength(nameof(req.DoctorName), req.DoctorName, 100)
            .MaxLength(nameof(req.PatientName), req.PatientName, 100);

        if (req.Lines is not { Count: > 0 }) e.Add(nameof(req.Lines), "Add at least one item to the bill.");
        else if (req.Lines.Count > MaxLines) e.Add(nameof(req.Lines), $"A bill can have at most {MaxLines} lines.");
        else
        {
            for (var i = 0; i < req.Lines.Count; i++)
            {
                var l = req.Lines[i];
                if (l.Qty <= 0) e.Add($"Lines[{i}]", "Quantity must be more than zero.");
                else if (decimal.Round(l.Qty, 3) != l.Qty) e.Add($"Lines[{i}]", "Quantity can have at most 3 decimals.");
                if (l.Rate < 0) e.Add($"Lines[{i}]", "Rate cannot be negative.");
                if (l.DiscountPercent is < 0 or > 100) e.Add($"Lines[{i}]", "Discount must be between 0 and 100%.");
            }
        }

        if (req.Payments is not { Count: > 0 }) e.Add(nameof(req.Payments), "Add a payment.");
        else if (req.Payments.Any(p => p.Amount <= 0 || !Enum.IsDefined(p.Mode)))
            e.Add(nameof(req.Payments), "Each payment needs a valid mode and an amount above zero.");
        return e;
    }

    // ---------------------------------------------------------------- read

    private static async Task<PagedResult<SaleSummaryDto>> List(PosDbContext db, DateOnly? from, DateOnly? to,
        string? q, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var query = db.SaleInvoices.AsNoTracking();
        if (from is { } f) query = query.Where(i => i.InvoiceDate >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) query = query.Where(i => i.InvoiceDate < t.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = DbHelpers.Contains(q);
            query = query.Where(i => EF.Functions.Like(i.InvoiceNo, like)
                || (i.CustomerName != null && EF.Functions.Like(i.CustomerName, like))
                || (i.CustomerPhone != null && EF.Functions.Like(i.CustomerPhone, like)));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(i => new
            {
                i.Id, i.InvoiceNo, i.InvoiceDate, i.CustomerName, i.CustomerPhone, i.Total, i.BalanceDue, i.Status,
                Modes = i.Payments.Select(x => x.Mode).ToList(),
                HasReturns = i.Returns.Any(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new SaleSummaryDto(r.Id, r.InvoiceNo, r.InvoiceDate, r.CustomerName, r.CustomerPhone,
            r.Total, r.BalanceDue, string.Join(" + ", r.Modes.Distinct()), r.Status, r.HasReturns)).ToList();
        return new PagedResult<SaleSummaryDto>(items, total, p, size);
    }

    private static async Task<IResult> Get(int id, PosDbContext db, CancellationToken ct) =>
        await LoadDto(db, id, ct) is { } dto ? Results.Ok(dto) : DbHelpers.NotFound("Invoice");

    private static async Task<SaleInvoiceDto?> LoadDto(PosDbContext db, int id, CancellationToken ct)
    {
        var i = await db.SaleInvoices.AsNoTracking()
            .Include(x => x.Lines.OrderBy(l => l.LineNo))
            .Include(x => x.Payments)
            .Include(x => x.Returns)
            .Include(x => x.CreatedBy)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return null;

        return new SaleInvoiceDto
        {
            Id = i.Id, InvoiceNo = i.InvoiceNo, InvoiceDate = i.InvoiceDate, Status = i.Status,
            CustomerId = i.CustomerId, CustomerName = i.CustomerName, CustomerPhone = i.CustomerPhone,
            CustomerGstin = i.CustomerGstin, CustomerType = i.CustomerType,
            DoctorName = i.DoctorName, PatientName = i.PatientName, IsInterState = i.IsInterState,
            Gross = i.Gross, Discount = i.Discount, Taxable = i.Taxable, Cgst = i.Cgst, Sgst = i.Sgst, Igst = i.Igst,
            RoundOff = i.RoundOff, Total = i.Total, BalanceDue = i.BalanceDue,
            CashierName = i.CreatedBy.FullName, CancelReason = i.CancelReason,
            Lines = i.Lines.Select(l => new SaleLineDto
            {
                Id = l.Id, LineNo = l.LineNo, ItemId = l.ItemId, BatchId = l.BatchId, ItemCode = l.ItemCode,
                ItemName = l.ItemName, Pack = l.Pack, HsnCode = l.HsnCode, BatchNo = l.BatchNo, ExpiryDate = l.ExpiryDate,
                Mrp = l.Mrp, Qty = l.Qty, Rate = l.Rate, DiscountPercent = l.DiscountPercent, GstRate = l.GstRate,
                Gross = l.Gross, Discount = l.Discount, Taxable = l.Taxable, Tax = l.Tax, Amount = l.Amount,
                ReturnedQty = l.ReturnedQty,
            }).ToList(),
            Payments = i.Payments.Select(p => new PaymentDto(p.Mode, p.Amount, p.Reference)).ToList(),
            Slabs = BillCalculator.BuildSlabs(i.Lines.Select(l => (l.GstRate, l.Taxable, l.Tax)), i.IsInterState),
            Returns = i.Returns.OrderBy(r => r.Id)
                .Select(r => new SaleReturnSummaryDto(r.Id, r.ReturnNo, r.ReturnDate, r.RefundMode, r.Total)).ToList(),
        };
    }

    // ---------------------------------------------------------------- cancel

    private static async Task<IResult> Cancel(int id, CancelSaleRequest req, ClaimsPrincipal principal, PosDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return new Errors().Add(nameof(req.Reason), "Give a reason for cancelling.").ToResult();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var invoice = await db.SaleInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null) return DbHelpers.NotFound("Invoice");
            if (invoice.Status == InvoiceStatus.Cancelled) return DbHelpers.Conflict("This invoice is already cancelled.");
            if (await db.SaleReturns.AnyAsync(r => r.InvoiceId == id, ct))
                return DbHelpers.Conflict("This invoice has returns and cannot be cancelled.");

            invoice.Status = InvoiceStatus.Cancelled;
            invoice.CancelReason = Errors.Truncate(req.Reason.Trim(), 200);
            invoice.CancelledAt = DateTime.UtcNow;
            invoice.CancelledByUserId = principal.GetUserId();
            await RestockAsync(db, invoice.Lines.Select(l => (l.ItemId, l.BatchId, l.Qty)),
                StockMovementType.SaleCancel, invoice.InvoiceNo, principal.GetUserId(), ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(await LoadDto(db, id, ct));
        });
    }

    // ---------------------------------------------------------------- returns

    private static async Task<IResult> CreateReturn(int id, SaleReturnRequest req, ClaimsPrincipal principal,
        PosDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var errors = new Errors().MaxLength(nameof(req.Reason), req.Reason, 200);
        if (req.Lines is not { Count: > 0 }) errors.Add(nameof(req.Lines), "Choose at least one item to return.");
        else if (req.Lines.Any(l => l.Qty <= 0 || decimal.Round(l.Qty, 3) != l.Qty))
            errors.Add(nameof(req.Lines), "Return quantities must be above zero with at most 3 decimals.");
        if (!Enum.IsDefined(req.RefundMode)) errors.Add(nameof(req.RefundMode), "Unknown refund mode.");
        if (errors.Any) return errors.ToResult();

        var userId = principal.GetUserId();
        var now = clock.GetLocalNow().DateTime;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var invoice = await db.SaleInvoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null) return DbHelpers.NotFound("Invoice");
            if (invoice.Status == InvoiceStatus.Cancelled) return DbHelpers.Conflict("A cancelled invoice cannot be returned.");
            if (req.RefundMode == RefundMode.Account && invoice.CustomerId is null)
                return errors.Add(nameof(req.RefundMode), "Only a registered customer's bill can be credited to their account.").ToResult();
            if (req.RefundMode == RefundMode.Cash && invoice.BalanceDue > 0)
                return errors.Add(nameof(req.RefundMode), "This bill was sold on credit; credit the return to the customer's account.").ToResult();

            // Amounts already returned per line, so a final return takes exactly what is left (no rounding drift).
            var lineIds = invoice.Lines.Select(l => l.Id).ToList();
            var previously = await db.Set<SaleReturnLine>().Where(r => lineIds.Contains(r.InvoiceLineId))
                .GroupBy(r => r.InvoiceLineId)
                .Select(g => new { g.Key, Amount = g.Sum(x => x.Amount), Taxable = g.Sum(x => x.Taxable) })
                .ToDictionaryAsync(x => x.Key, ct);

            var returnLines = new List<SaleReturnLine>();
            foreach (var group in req.Lines.GroupBy(l => l.InvoiceLineId))
            {
                var line = invoice.Lines.FirstOrDefault(l => l.Id == group.Key);
                if (line is null) return errors.Add(nameof(req.Lines), "Item is not on this invoice.").ToResult();

                var qty = group.Sum(l => l.Qty);
                var remaining = line.Qty - line.ReturnedQty;
                if (qty > remaining)
                    return errors.Add(nameof(req.Lines),
                        $"Only {remaining:0.###} of {line.ItemName} can still be returned.").ToResult();

                decimal amount, taxable;
                if (qty == remaining)
                {
                    var prev = previously.GetValueOrDefault(line.Id);
                    amount = line.Amount - (prev?.Amount ?? 0);
                    taxable = line.Taxable - (prev?.Taxable ?? 0);
                }
                else
                {
                    amount = BillCalculator.Round2(line.Amount * qty / line.Qty);
                    taxable = BillCalculator.Round2(line.Taxable * qty / line.Qty);
                }

                line.ReturnedQty += qty;
                returnLines.Add(new SaleReturnLine { InvoiceLineId = line.Id, Qty = qty, Amount = amount, Taxable = taxable, Tax = amount - taxable });
            }

            var net = returnLines.Sum(l => l.Amount);
            var total = Math.Round(net, 0, MidpointRounding.AwayFromZero);
            var fy = Documents.FinancialYear(now);
            var number = await Documents.NextNumberAsync(db, Documents.CreditNoteKind, fy, ct);
            var ret = new SaleReturn
            {
                ReturnNo = $"{Documents.CreditNotePrefix}{fy}-{number:D6}",
                FinancialYear = fy,
                ReturnDate = now,
                InvoiceId = invoice.Id,
                RefundMode = req.RefundMode,
                Reason = Errors.Clean(req.Reason),
                Taxable = returnLines.Sum(l => l.Taxable),
                Tax = returnLines.Sum(l => l.Tax),
                RoundOff = total - net,
                Total = total,
                CreatedByUserId = userId,
                Lines = returnLines,
            };
            db.SaleReturns.Add(ret);

            await RestockAsync(db,
                returnLines.Select(r => { var l = invoice.Lines.First(x => x.Id == r.InvoiceLineId); return (l.ItemId, l.BatchId, r.Qty); }),
                StockMovementType.SaleReturn, ret.ReturnNo, userId, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/sales/{id}",
                new SaleReturnDto(ret.Id, ret.ReturnNo, ret.ReturnDate, invoice.InvoiceNo, ret.RefundMode,
                    ret.Taxable, ret.Tax, ret.RoundOff, ret.Total));
        });
    }

    /// <summary>Puts quantities back on their batches and records the movements (caller saves and commits).</summary>
    private static async Task RestockAsync(PosDbContext db, IEnumerable<(int ItemId, int BatchId, decimal Qty)> lines,
        StockMovementType type, string refNo, int userId, CancellationToken ct)
    {
        foreach (var group in lines.Where(l => l.Qty > 0).GroupBy(l => (l.ItemId, l.BatchId)))
        {
            var qty = group.Sum(l => l.Qty);
            await db.Batches.Where(b => b.Id == group.Key.BatchId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.QtyOnHand, b => b.QtyOnHand + qty)
                    .SetProperty(b => b.UpdatedAt, DateTime.UtcNow), ct);
            db.StockMovements.Add(new StockMovement
            {
                ItemId = group.Key.ItemId, BatchId = group.Key.BatchId, QtyChange = qty,
                Type = type, RefNo = refNo, UserId = userId,
            });
        }
    }
}
