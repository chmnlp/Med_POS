using System.Security.Claims;
using MargPos.Data;
using MargPos.Server.Auth;
using MargPos.Server.Infrastructure;
using MargPos.Shared;
using Microsoft.EntityFrameworkCore;

namespace MargPos.Server.Endpoints;

/// <summary>Customer receipts, supplier payments, statements and ageing (receivables / payables).</summary>
public static class AccountEndpoints
{
    public const string ReceiptKind = "RCT";
    public const string ReceiptPrefix = "RC";
    public const string PaymentKind = "PAY";
    public const string PaymentPrefix = "PM";
    public const decimal MaxAmount = 99_99_99_999m;

    public static void MapAccounts(this IEndpointRouteBuilder app)
    {
        // The counter collects dues, so any signed-in user can take a receipt; cancelling is a manager task.
        var r = app.MapGroup("/api/receipts");
        r.MapPost("/", CreateReceipt);
        r.MapGet("/", ListReceipts);
        r.MapGet("/{id:int}", GetReceipt);
        r.MapPost("/{id:int}/cancel", CancelReceipt).RequireAuthorization(Policies.Manager);

        var p = app.MapGroup("/api/supplier-payments").RequireAuthorization(Policies.Manager);
        p.MapPost("/", CreatePayment);
        p.MapGet("/", ListPayments);
        p.MapPost("/{id:int}/cancel", CancelPayment);

        app.MapGet("/api/customers/{id:int}/statement", CustomerStatement);
        app.MapGet("/api/customers/{id:int}/open-bills", CustomerOpenBills);
        app.MapGet("/api/suppliers/{id:int}/statement", SupplierStatement).RequireAuthorization(Policies.Manager);
        app.MapGet("/api/suppliers/{id:int}/open-bills", SupplierOpenBills).RequireAuthorization(Policies.Manager);
        app.MapGet("/api/reports/receivables", Receivables);
        app.MapGet("/api/reports/payables", Payables).RequireAuthorization(Policies.Manager);
        app.MapGet("/api/sale-returns/{id:int}", CreditNote);
    }

    // ---------------------------------------------------------------- receipts

    private static Errors ValidateMoney(decimal amount, AccountPaymentMode mode, string? reference, string? note)
    {
        var e = new Errors().MaxLength("Reference", reference, 50).MaxLength("Note", note, 200);
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
            e.Add("Amount", "Enter an amount above zero (at most 2 decimals).");
        if (!Enum.IsDefined(mode)) e.Add("Mode", "Unknown payment mode.");
        if (mode is AccountPaymentMode.Cheque or AccountPaymentMode.BankTransfer && string.IsNullOrWhiteSpace(reference))
            e.Add("Reference", "Enter the cheque / transaction number.");
        return e;
    }

    private static async Task<IResult> CreateReceipt(ReceiptRequest req, ClaimsPrincipal principal, PosDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var e = ValidateMoney(req.Amount, req.Mode, req.Reference, req.Note);
        if (e.Any) return e.ToResult();
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CustomerId, ct);
        if (customer is null) return e.Add(nameof(req.CustomerId), "Customer not found.").ToResult();

        // Advances are allowed (balance goes negative); the cashier sees the balance before saving.
        var now = clock.GetLocalNow().DateTime;
        var fy = Documents.FinancialYear(now);
        var strategy = db.Database.CreateExecutionStrategy();
        var receipt = await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var number = await Documents.NextNumberAsync(db, ReceiptKind, fy, ct);
            var entity = new CustomerReceipt
            {
                ReceiptNo = $"{ReceiptPrefix}{fy}-{number:D6}", FinancialYear = fy, Date = now, CustomerId = customer.Id,
                Amount = req.Amount, Mode = req.Mode, Reference = Errors.Clean(req.Reference), Note = Errors.Clean(req.Note),
                CreatedByUserId = principal.GetUserId(),
            };
            db.CustomerReceipts.Add(entity);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return entity;
        });
        return Results.Created($"/api/receipts/{receipt.Id}", await LoadReceipt(db, receipt.Id, ct));
    }

    private static async Task<PagedResult<ReceiptDto>> ListReceipts(PosDbContext db, int? customerId, DateOnly? from,
        DateOnly? to, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var q = db.CustomerReceipts.AsNoTracking();
        if (customerId is { } cid) q = q.Where(r => r.CustomerId == cid);
        if (from is { } f) q = q.Where(r => r.Date >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) q = q.Where(r => r.Date < t.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).Skip((p - 1) * size).Take(size)
            .Select(r => new ReceiptDto(r.Id, r.ReceiptNo, r.Date, r.CustomerId, r.Customer.Name, r.Customer.Phone, r.Amount,
                r.Mode, r.Reference, r.Note, r.Status, r.CancelReason, r.CreatedBy.FullName, 0))
            .ToListAsync(ct);
        return new PagedResult<ReceiptDto>(rows, total, p, size);
    }

    private static async Task<IResult> GetReceipt(int id, PosDbContext db, CancellationToken ct) =>
        await LoadReceipt(db, id, ct) is { } dto ? Results.Ok(dto) : DbHelpers.NotFound("Receipt");

    /// <summary>Includes the customer's balance now, for printing on the receipt.</summary>
    private static async Task<ReceiptDto?> LoadReceipt(PosDbContext db, int id, CancellationToken ct)
    {
        var r = await db.CustomerReceipts.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new ReceiptDto(x.Id, x.ReceiptNo, x.Date, x.CustomerId, x.Customer.Name, x.Customer.Phone, x.Amount,
                x.Mode, x.Reference, x.Note, x.Status, x.CancelReason, x.CreatedBy.FullName, 0))
            .FirstOrDefaultAsync(ct);
        return r is null ? null : r with { BalanceAfter = await Documents.OutstandingAsync(db, r.CustomerId, ct) };
    }

    private static async Task<IResult> CancelReceipt(int id, CancelEntryRequest req, PosDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) return new Errors().Add(nameof(req.Reason), "Give a reason for cancelling.").ToResult();
        var receipt = await db.CustomerReceipts.FindAsync([id], ct);
        if (receipt is null) return DbHelpers.NotFound("Receipt");
        if (receipt.Status == AccountEntryStatus.Cancelled) return DbHelpers.Conflict("This receipt is already cancelled.");
        receipt.Status = AccountEntryStatus.Cancelled;
        receipt.CancelReason = Errors.Truncate(req.Reason.Trim(), 200);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadReceipt(db, id, ct));
    }

    // ---------------------------------------------------------------- supplier payments

    private static async Task<IResult> CreatePayment(SupplierPaymentRequest req, ClaimsPrincipal principal, PosDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var e = ValidateMoney(req.Amount, req.Mode, req.Reference, req.Note);
        if (e.Any) return e.ToResult();
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SupplierId, ct);
        if (supplier is null) return e.Add(nameof(req.SupplierId), "Supplier not found.").ToResult();

        var now = clock.GetLocalNow().DateTime;
        var fy = Documents.FinancialYear(now);
        var strategy = db.Database.CreateExecutionStrategy();
        var payment = await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var number = await Documents.NextNumberAsync(db, PaymentKind, fy, ct);
            var entity = new SupplierPayment
            {
                PaymentNo = $"{PaymentPrefix}{fy}-{number:D6}", FinancialYear = fy, Date = now, SupplierId = supplier.Id,
                Amount = req.Amount, Mode = req.Mode, Reference = Errors.Clean(req.Reference), Note = Errors.Clean(req.Note),
                CreatedByUserId = principal.GetUserId(),
            };
            db.SupplierPayments.Add(entity);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return entity;
        });
        return Results.Created($"/api/supplier-payments/{payment.Id}", await LoadPayment(db, payment.Id, ct));
    }

    private static Task<SupplierPaymentDto?> LoadPayment(PosDbContext db, int id, CancellationToken ct) =>
        db.SupplierPayments.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new SupplierPaymentDto(x.Id, x.PaymentNo, x.Date, x.SupplierId, x.Supplier.Name, x.Amount, x.Mode,
                x.Reference, x.Note, x.Status, x.CancelReason, x.CreatedBy.FullName))
            .FirstOrDefaultAsync(ct);

    private static async Task<PagedResult<SupplierPaymentDto>> ListPayments(PosDbContext db, int? supplierId, DateOnly? from,
        DateOnly? to, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var q = db.SupplierPayments.AsNoTracking();
        if (supplierId is { } sid) q = q.Where(x => x.SupplierId == sid);
        if (from is { } f) q = q.Where(x => x.Date >= f.ToDateTime(TimeOnly.MinValue));
        if (to is { } t) q = q.Where(x => x.Date < t.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Skip((p - 1) * size).Take(size)
            .Select(x => new SupplierPaymentDto(x.Id, x.PaymentNo, x.Date, x.SupplierId, x.Supplier.Name, x.Amount, x.Mode,
                x.Reference, x.Note, x.Status, x.CancelReason, x.CreatedBy.FullName))
            .ToListAsync(ct);
        return new PagedResult<SupplierPaymentDto>(rows, total, p, size);
    }

    private static async Task<IResult> CancelPayment(int id, CancelEntryRequest req, PosDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) return new Errors().Add(nameof(req.Reason), "Give a reason for cancelling.").ToResult();
        var payment = await db.SupplierPayments.FindAsync([id], ct);
        if (payment is null) return DbHelpers.NotFound("Payment");
        if (payment.Status == AccountEntryStatus.Cancelled) return DbHelpers.Conflict("This payment is already cancelled.");
        payment.Status = AccountEntryStatus.Cancelled;
        payment.CancelReason = Errors.Truncate(req.Reason.Trim(), 200);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadPayment(db, id, ct));
    }

    // ---------------------------------------------------------------- ledgers

    private record Movement(DateTime Date, string Type, string RefNo, string? Description, decimal Debit, decimal Credit);

    /// <summary>Everything that changes a customer's balance. Debit = they owe more; credit = they owe less.</summary>
    private static async Task<List<Movement>> CustomerMovements(PosDbContext db, int customerId, CancellationToken ct)
    {
        var sales = await db.SaleInvoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId && i.Status == InvoiceStatus.Completed && i.BalanceDue > 0)
            .Select(i => new Movement(i.InvoiceDate, "Credit sale", i.InvoiceNo, "Bill total " + i.Total, i.BalanceDue, 0))
            .ToListAsync(ct);
        var returns = await db.SaleReturns.AsNoTracking()
            .Where(r => r.Invoice.CustomerId == customerId && r.RefundMode == RefundMode.Account && r.Invoice.Status == InvoiceStatus.Completed)
            .Select(r => new Movement(r.ReturnDate, "Return", r.ReturnNo, "Against " + r.Invoice.InvoiceNo, 0, r.Total))
            .ToListAsync(ct);
        var receipts = (await db.CustomerReceipts.AsNoTracking()
            .Where(r => r.CustomerId == customerId && r.Status == AccountEntryStatus.Active)
            .Select(r => new { r.Date, r.ReceiptNo, r.Mode, r.Reference, r.Amount })
            .ToListAsync(ct))
            .Select(r => new Movement(r.Date, "Receipt", r.ReceiptNo, Describe(r.Mode, r.Reference), 0, r.Amount));
        return [.. sales.Concat(returns).Concat(receipts).OrderBy(m => m.Date).ThenBy(m => m.RefNo)];
    }

    private static string Describe(AccountPaymentMode mode, string? reference) =>
        PaymentModeText.Of(mode) + (reference is null ? "" : $" {reference}");

    /// <summary>Everything that changes what we owe a supplier. Credit = we owe more; debit = we owe less.</summary>
    private static async Task<List<Movement>> SupplierMovements(PosDbContext db, int supplierId, CancellationToken ct)
    {
        var purchases = await db.Purchases.AsNoTracking()
            .Where(p => p.SupplierId == supplierId && p.Status == PurchaseStatus.Completed)
            .Select(p => new Movement(p.ReceivedAt, "Purchase", p.PurchaseNo, "Their invoice " + p.SupplierInvoiceNo, 0, p.Total))
            .ToListAsync(ct);
        var returns = await db.PurchaseReturns.AsNoTracking().Where(r => r.SupplierId == supplierId)
            .Select(r => new Movement(r.ReturnDate, "Debit note", r.ReturnNo, r.Reason, r.Total, 0))
            .ToListAsync(ct);
        var payments = (await db.SupplierPayments.AsNoTracking()
            .Where(p => p.SupplierId == supplierId && p.Status == AccountEntryStatus.Active)
            .Select(p => new { p.Date, p.PaymentNo, p.Mode, p.Reference, p.Amount })
            .ToListAsync(ct))
            .Select(p => new Movement(p.Date, "Payment", p.PaymentNo, Describe(p.Mode, p.Reference), p.Amount, 0));
        return [.. purchases.Concat(returns).Concat(payments).OrderBy(m => m.Date).ThenBy(m => m.RefNo)];
    }

    private static StatementDto Statement(int id, string name, string? phone, string? gstin, decimal openingBalance,
        List<Movement> movements, DateOnly from, DateOnly to, bool owedIsDebit)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        decimal Change(Movement m) => owedIsDebit ? m.Debit - m.Credit : m.Credit - m.Debit;

        var opening = openingBalance + movements.Where(m => m.Date < start).Sum(Change);
        var balance = opening;
        var entries = new List<StatementEntry>();
        foreach (var m in movements.Where(m => m.Date >= start && m.Date < end))
        {
            balance += Change(m);
            entries.Add(new StatementEntry(m.Date, m.Type, m.RefNo, m.Description, m.Debit, m.Credit, balance));
        }
        return new StatementDto(id, name, phone, gstin, from, to, opening, entries, balance);
    }

    private static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to, TimeProvider clock)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var t = to ?? today;
        return (from ?? new DateOnly(t.Month >= 4 ? t.Year : t.Year - 1, 4, 1), t); // default: this financial year
    }

    private static async Task<IResult> CustomerStatement(int id, DateOnly? from, DateOnly? to, PosDbContext db, TimeProvider clock,
        CancellationToken ct)
    {
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return DbHelpers.NotFound("Customer");
        var (f, t) = Range(from, to, clock);
        return Results.Ok(Statement(c.Id, c.Name, c.Phone, c.Gstin, c.OpeningBalance, await CustomerMovements(db, id, ct), f, t, owedIsDebit: true));
    }

    private static async Task<IResult> SupplierStatement(int id, DateOnly? from, DateOnly? to, PosDbContext db, TimeProvider clock,
        CancellationToken ct)
    {
        var s = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return DbHelpers.NotFound("Supplier");
        var (f, t) = Range(from, to, clock);
        return Results.Ok(Statement(s.Id, s.Name, s.Phone, s.Gstin, s.OpeningBalance, await SupplierMovements(db, id, ct), f, t, owedIsDebit: false));
    }

    // ---------------------------------------------------------------- open bills and ageing

    /// <summary>Bills that raise the balance, with an opening balance counted as the oldest bill.</summary>
    private static List<Ageing.Debit> Debits(decimal openingBalance, DateTime createdAt, List<Movement> movements, bool owedIsDebit)
    {
        var debits = movements.Where(m => (owedIsDebit ? m.Debit : m.Credit) > 0)
            .Select(m => new Ageing.Debit(m.RefNo, DateOnly.FromDateTime(m.Date), owedIsDebit ? m.Debit : m.Credit)).ToList();
        if (openingBalance > 0) debits.Insert(0, new Ageing.Debit("Opening balance", DateOnly.FromDateTime(createdAt.ToLocalTime()), openingBalance));
        return debits;
    }

    private static decimal Credits(decimal openingBalance, List<Movement> movements, bool owedIsDebit) =>
        movements.Sum(m => owedIsDebit ? m.Credit : m.Debit) + (openingBalance < 0 ? -openingBalance : 0);

    private static DateOnly AsOf(DateOnly? asOf, TimeProvider clock) => asOf ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private static async Task<IResult> CustomerOpenBills(int id, DateOnly? asOf, PosDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return DbHelpers.NotFound("Customer");
        var m = await CustomerMovements(db, id, ct);
        return Results.Ok(Ageing.OpenItems(Debits(c.OpeningBalance, c.CreatedAt, m, true), Credits(c.OpeningBalance, m, true), c.CreditDays, AsOf(asOf, clock)));
    }

    private static async Task<IResult> SupplierOpenBills(int id, DateOnly? asOf, PosDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var s = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return DbHelpers.NotFound("Supplier");
        var m = await SupplierMovements(db, id, ct);
        return Results.Ok(Ageing.OpenItems(Debits(s.OpeningBalance, s.CreatedAt, m, false), Credits(s.OpeningBalance, m, false), s.CreditDays, AsOf(asOf, clock)));
    }

    /// <summary>Customers who owe money, aged by how overdue their oldest unpaid bills are.</summary>
    public static async Task<AgeingReport> ReceivablesAsync(PosDbContext db, DateOnly asOf, CancellationToken ct)
    {
        // Only customers with any account activity or an opening balance can owe anything.
        var ids = await db.Customers.AsNoTracking()
            .Where(c => c.OpeningBalance != 0
                || db.SaleInvoices.Any(i => i.CustomerId == c.Id && i.BalanceDue > 0 && i.Status == InvoiceStatus.Completed)
                || db.CustomerReceipts.Any(r => r.CustomerId == c.Id))
            .Select(c => new { c.Id, c.Name, c.Phone, c.CreditDays, c.CreditLimit, c.OpeningBalance, c.CreatedAt })
            .ToListAsync(ct);

        var rows = new List<AgeingRow>();
        foreach (var c in ids)
        {
            var m = await CustomerMovements(db, c.Id, ct);
            var outstanding = c.OpeningBalance + m.Sum(x => x.Debit - x.Credit);
            if (outstanding == 0) continue;
            var open = Ageing.OpenItems(Debits(c.OpeningBalance, c.CreatedAt, m, true), Credits(c.OpeningBalance, m, true), c.CreditDays, asOf);
            rows.Add(Ageing.Row(c.Id, c.Name, c.Phone, c.CreditDays, c.CreditLimit, outstanding, open));
        }
        return new AgeingReport(asOf, [.. rows.OrderByDescending(r => r.Overdue).ThenByDescending(r => r.Outstanding)]);
    }

    private static async Task<AgeingReport> Receivables(DateOnly? asOf, PosDbContext db, TimeProvider clock, CancellationToken ct) =>
        await ReceivablesAsync(db, AsOf(asOf, clock), ct);

    private static async Task<AgeingReport> Payables(DateOnly? asOf, PosDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var date = AsOf(asOf, clock);
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => s.OpeningBalance != 0 || db.Purchases.Any(p => p.SupplierId == s.Id) || db.SupplierPayments.Any(p => p.SupplierId == s.Id))
            .Select(s => new { s.Id, s.Name, s.Phone, s.CreditDays, s.OpeningBalance, s.CreatedAt })
            .ToListAsync(ct);

        var rows = new List<AgeingRow>();
        foreach (var s in suppliers)
        {
            var m = await SupplierMovements(db, s.Id, ct);
            var outstanding = s.OpeningBalance + m.Sum(x => x.Credit - x.Debit);
            if (outstanding == 0) continue;
            var open = Ageing.OpenItems(Debits(s.OpeningBalance, s.CreatedAt, m, false), Credits(s.OpeningBalance, m, false), s.CreditDays, date);
            rows.Add(Ageing.Row(s.Id, s.Name, s.Phone, s.CreditDays, 0, outstanding, open));
        }
        return new AgeingReport(date, [.. rows.OrderByDescending(r => r.Overdue).ThenByDescending(r => r.Outstanding)]);
    }

    // ---------------------------------------------------------------- credit note

    private static async Task<IResult> CreditNote(int id, PosDbContext db, CancellationToken ct)
    {
        var r = await db.SaleReturns.AsNoTracking().Include(x => x.Invoice).Include(x => x.Lines).ThenInclude(l => l.InvoiceLine)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return DbHelpers.NotFound("Credit note");
        var by = await db.Users.Where(u => u.Id == r.CreatedByUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "";
        var i = r.Invoice;
        return Results.Ok(new CreditNoteDto(r.Id, r.ReturnNo, r.ReturnDate, i.InvoiceNo, i.InvoiceDate, i.CustomerName, i.CustomerPhone,
            i.CustomerGstin, r.RefundMode, r.Reason, i.IsInterState, r.Taxable, r.Tax, r.RoundOff, r.Total,
            r.Lines.Select(l => new CreditNoteLineDto(l.InvoiceLine.ItemName, l.InvoiceLine.BatchNo, l.InvoiceLine.ExpiryDate, l.Qty,
                l.InvoiceLine.Rate, l.InvoiceLine.GstRate, l.Taxable, l.Tax, l.Amount)).ToList(), by));
    }
}
