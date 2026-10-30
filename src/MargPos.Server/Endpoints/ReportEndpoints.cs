using System.Security.Claims;
using MargPos.Data;
using MargPos.Server.Auth;
using MargPos.Server.Infrastructure;
using MargPos.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MargPos.Server.Endpoints;

/// <summary>Counter closing (day end), sales summary and the GST (GSTR-1 working) report.</summary>
public static class ReportEndpoints
{
    public static void MapReports(this IEndpointRouteBuilder app)
    {
        // Every cashier closes their own counter; managers see everyone's.
        var d = app.MapGroup("/api/day-end");
        d.MapGet("/summary", Summary);
        d.MapPost("/close", Close);
        d.MapGet("/closings", Closings);

        var r = app.MapGroup("/api/reports").RequireAuthorization(Policies.Manager);
        r.MapGet("/sales-summary", SalesSummary);
        r.MapGet("/gst", Gst);
    }

    private static bool IsManager(ClaimsPrincipal p) =>
        p.IsInRole(nameof(UserRole.Manager)) || p.IsInRole(nameof(UserRole.Admin));

    private static (DateTime Start, DateTime End) Day(DateOnly date) =>
        (date.ToDateTime(TimeOnly.MinValue), date.AddDays(1).ToDateTime(TimeOnly.MinValue));

    // ---------------------------------------------------------------- day end

    /// <summary>One user's (or, with null, every counter's) takings for a day.</summary>
    public static async Task<DayEndSummaryDto> DayEndAsync(PosDbContext db, DateOnly date, int? userId, CancellationToken ct)
    {
        var (start, end) = Day(date);
        var invoices = db.SaleInvoices.AsNoTracking().Where(i => i.InvoiceDate >= start && i.InvoiceDate < end);
        var returns = db.SaleReturns.AsNoTracking()
            .Where(x => x.ReturnDate >= start && x.ReturnDate < end && x.Invoice.Status == InvoiceStatus.Completed);
        var receipts = db.CustomerReceipts.AsNoTracking()
            .Where(x => x.Date >= start && x.Date < end && x.Status == AccountEntryStatus.Active);
        var payouts = db.SupplierPayments.AsNoTracking()
            .Where(x => x.Date >= start && x.Date < end && x.Status == AccountEntryStatus.Active && x.Mode == AccountPaymentMode.Cash);
        if (userId is { } uid)
        {
            invoices = invoices.Where(i => i.CreatedByUserId == uid);
            returns = returns.Where(x => x.CreatedByUserId == uid);
            receipts = receipts.Where(x => x.CreatedByUserId == uid);
            payouts = payouts.Where(x => x.CreatedByUserId == uid);
        }

        var completed = invoices.Where(i => i.Status == InvoiceStatus.Completed);
        var byMode = await completed.SelectMany(i => i.Payments).GroupBy(p => p.Mode)
            .Select(g => new { Mode = g.Key, Amount = g.Sum(p => p.Amount) }).ToDictionaryAsync(x => x.Mode, x => x.Amount, ct);
        var returnTotals = await returns.GroupBy(x => x.RefundMode)
            .Select(g => new { Mode = g.Key, Count = g.Count(), Amount = g.Sum(x => x.Total) }).ToListAsync(ct);
        var receiptTotals = await receipts.GroupBy(x => x.Mode == AccountPaymentMode.Cash)
            .Select(g => new { IsCash = g.Key, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);

        var name = userId is { } id
            ? await db.Users.Where(u => u.Id == id).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? ""
            : "All counters";
        var closing = userId is { } closer
            ? await db.DayCloses.AsNoTracking().Where(c => c.Date == date && c.UserId == closer)
                .Select(c => new DayCloseDto(c.Id, c.Date, c.UserId, c.User.FullName, c.ClosedAt, c.OpeningFloat, c.NetCash,
                    c.ExpectedCash, c.CountedCash, c.Denominations, c.Note, c.Bills, c.SalesTotal)).FirstOrDefaultAsync(ct)
            : null;

        return new DayEndSummaryDto
        {
            Date = date, UserId = userId, UserName = name,
            Bills = await completed.CountAsync(ct),
            CancelledBills = await invoices.CountAsync(i => i.Status == InvoiceStatus.Cancelled, ct),
            SalesTotal = await completed.SumAsync(i => (decimal?)i.Total, ct) ?? 0,
            CashSales = byMode.GetValueOrDefault(PaymentMode.Cash),
            UpiSales = byMode.GetValueOrDefault(PaymentMode.Upi),
            CardSales = byMode.GetValueOrDefault(PaymentMode.Card),
            CreditSales = byMode.GetValueOrDefault(PaymentMode.Credit),
            Returns = returnTotals.Sum(x => x.Count),
            CashRefunds = returnTotals.Where(x => x.Mode == RefundMode.Cash).Sum(x => x.Amount),
            AccountRefunds = returnTotals.Where(x => x.Mode == RefundMode.Account).Sum(x => x.Amount),
            CashReceipts = receiptTotals.Where(x => x.IsCash).Sum(x => x.Amount),
            OtherReceipts = receiptTotals.Where(x => !x.IsCash).Sum(x => x.Amount),
            CashPayouts = await payouts.SumAsync(x => (decimal?)x.Amount, ct) ?? 0,
            Closing = closing,
        };
    }

    private static async Task<IResult> Summary(DateOnly? date, int? userId, bool? allCounters, ClaimsPrincipal principal,
        PosDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var day = date ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var me = principal.GetUserId();
        if (!IsManager(principal) && (allCounters == true || (userId is { } other && other != me)))
            return Results.Forbid();
        return Results.Ok(await DayEndAsync(db, day, allCounters == true ? null : userId ?? me, ct));
    }

    private static async Task<IResult> Close(DayCloseRequest req, ClaimsPrincipal principal, PosDbContext db, TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetLocalNow().DateTime;
        var e = new Errors().MaxLength(nameof(req.Note), req.Note, 200).MaxLength(nameof(req.Denominations), req.Denominations, 200);
        if (req.Date > DateOnly.FromDateTime(now)) e.Add(nameof(req.Date), "You cannot close a day that has not happened yet.");
        if (req.OpeningFloat < 0 || req.CountedCash < 0) e.Add(nameof(req.CountedCash), "Amounts cannot be negative.");
        if (e.Any) return e.ToResult();

        var userId = principal.GetUserId();
        var summary = await DayEndAsync(db, req.Date, userId, ct);
        if (summary.Closing is not null) return DbHelpers.Conflict($"Your counter is already closed for {req.Date:dd-MM-yyyy}.");

        var close = new DayClose
        {
            Date = req.Date, UserId = userId, ClosedAt = now, OpeningFloat = req.OpeningFloat, NetCash = summary.NetCash,
            ExpectedCash = req.OpeningFloat + summary.NetCash, CountedCash = req.CountedCash,
            Denominations = Errors.Clean(req.Denominations), Note = Errors.Clean(req.Note),
            Bills = summary.Bills, SalesTotal = summary.SalesTotal,
        };
        db.DayCloses.Add(close);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return DbHelpers.Conflict($"Your counter is already closed for {req.Date:dd-MM-yyyy}.");
        }
        return Results.Created($"/api/day-end/closings/{close.Id}", (await DayEndAsync(db, req.Date, userId, ct)).Closing);
    }

    private static async Task<List<DayCloseDto>> Closings(DateOnly? from, DateOnly? to, ClaimsPrincipal principal, PosDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var t = to ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var f = from ?? t.AddDays(-30);
        var q = db.DayCloses.AsNoTracking().Where(c => c.Date >= f && c.Date <= t);
        if (!IsManager(principal)) { var me = principal.GetUserId(); q = q.Where(c => c.UserId == me); }
        return await q.OrderByDescending(c => c.Date).ThenBy(c => c.User.FullName)
            .Select(c => new DayCloseDto(c.Id, c.Date, c.UserId, c.User.FullName, c.ClosedAt, c.OpeningFloat, c.NetCash,
                c.ExpectedCash, c.CountedCash, c.Denominations, c.Note, c.Bills, c.SalesTotal))
            .ToListAsync(ct);
    }

    // ---------------------------------------------------------------- sales summary

    private static async Task<SalesSummaryReport> SalesSummary(DateOnly from, DateOnly to, PosDbContext db, CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var all = db.SaleInvoices.AsNoTracking().Where(i => i.InvoiceDate >= start && i.InvoiceDate < end);
        var done = all.Where(i => i.Status == InvoiceStatus.Completed);
        var returns = db.SaleReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= start && r.ReturnDate < end && r.Invoice.Status == InvoiceStatus.Completed);

        var totals = await done.GroupBy(_ => 1).Select(g => new
        {
            Bills = g.Count(), Gross = g.Sum(i => i.Gross), Discount = g.Sum(i => i.Discount), Taxable = g.Sum(i => i.Taxable),
            Tax = g.Sum(i => i.Cgst + i.Sgst + i.Igst), Total = g.Sum(i => i.Total),
        }).FirstOrDefaultAsync(ct);
        var cancelled = await all.Where(i => i.Status == InvoiceStatus.Cancelled).GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Value = g.Sum(i => i.Total) }).FirstOrDefaultAsync(ct);
        var ret = await returns.GroupBy(_ => 1).Select(g => new { Count = g.Count(), Value = g.Sum(r => r.Total) }).FirstOrDefaultAsync(ct);

        var byMode = await done.SelectMany(i => i.Payments).GroupBy(p => p.Mode)
            .Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
        var receipts = await db.CustomerReceipts.AsNoTracking()
            .Where(r => r.Date >= start && r.Date < end && r.Status == AccountEntryStatus.Active)
            .GroupBy(r => r.Mode).Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(r => r.Amount) }).ToListAsync(ct);

        var salesByDay = (await done.Select(i => new { i.InvoiceDate, i.Total }).ToListAsync(ct))
            .GroupBy(i => DateOnly.FromDateTime(i.InvoiceDate)).ToDictionary(g => g.Key, g => (Bills: g.Count(), Sales: g.Sum(i => i.Total)));
        var returnsByDay = (await returns.Select(r => new { r.ReturnDate, r.Total }).ToListAsync(ct))
            .GroupBy(r => DateOnly.FromDateTime(r.ReturnDate)).ToDictionary(g => g.Key, g => g.Sum(r => r.Total));
        var days = salesByDay.Keys.Union(returnsByDay.Keys).Order()
            .Select(dy => new DaySales(dy, salesByDay.GetValueOrDefault(dy).Bills, salesByDay.GetValueOrDefault(dy).Sales,
                returnsByDay.GetValueOrDefault(dy)))
            .ToList();

        var cashiers = await done.GroupBy(i => new { i.CreatedByUserId, i.CreatedBy.FullName })
            .Select(g => new CashierSales(g.Key.CreatedByUserId, g.Key.FullName, g.Count(), g.Sum(i => i.Total)))
            .ToListAsync(ct);
        var top = await db.SaleInvoiceLines.AsNoTracking()
            .Where(l => l.Invoice.Status == InvoiceStatus.Completed && l.Invoice.InvoiceDate >= start && l.Invoice.InvoiceDate < end)
            .GroupBy(l => new { l.ItemId, l.ItemName })
            .Select(g => new { g.Key.ItemId, g.Key.ItemName, Qty = g.Sum(l => l.Qty - l.ReturnedQty), Amount = g.Sum(l => l.Amount) })
            .OrderByDescending(x => x.Amount).Take(20).ToListAsync(ct);

        return new SalesSummaryReport
        {
            From = from, To = to,
            Bills = totals?.Bills ?? 0, Gross = totals?.Gross ?? 0, Discount = totals?.Discount ?? 0, Taxable = totals?.Taxable ?? 0,
            Tax = totals?.Tax ?? 0, Total = totals?.Total ?? 0,
            CancelledBills = cancelled?.Count ?? 0, CancelledValue = cancelled?.Value ?? 0,
            Returns = ret?.Count ?? 0, ReturnsValue = ret?.Value ?? 0,
            ByMode = [.. byMode.OrderBy(m => m.Key).Select(m => new ModeTotal(m.Key == PaymentMode.Upi ? "UPI" : m.Key.ToString(), m.Count, m.Amount))],
            ReceiptsByMode = [.. receipts.OrderBy(m => m.Key).Select(m => new ModeTotal(PaymentModeText.Of(m.Key), m.Count, m.Amount))],
            ByDay = days,
            ByCashier = [.. cashiers.OrderByDescending(c => c.Sales)],
            TopItems = [.. top.Select(t => new TopItem(t.ItemId, t.ItemName, t.Qty, t.Amount))],
        };
    }

    // ---------------------------------------------------------------- GST

    private static (decimal Igst, decimal Cgst, decimal Sgst) Split(decimal tax, bool interState)
    {
        if (interState) return (tax, 0, 0);
        var cgst = BillCalculator.Round2(tax / 2m);
        return (0, cgst, tax - cgst);
    }

    /// <summary>
    /// Working figures for GSTR-1 (outward supplies) plus purchase tax for matching input credit. Dates follow the
    /// document date: invoice / credit-note date for sales, the supplier's invoice date for purchases.
    /// </summary>
    private static async Task<GstReport> Gst(DateOnly from, DateOnly to, PosDbContext db, CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var settings = await db.StoreSettings.AsNoTracking().FirstAsync(ct);
        var storeState = settings.EffectiveStateCode ?? "";

        var invoices = await db.SaleInvoices.AsNoTracking()
            .Where(i => i.InvoiceDate >= start && i.InvoiceDate < end && i.Status == InvoiceStatus.Completed)
            .Select(i => new
            {
                i.Id, i.InvoiceNo, i.InvoiceDate, i.CustomerName, i.CustomerGstin, i.IsInterState, i.Total,
                Lines = i.Lines.Select(l => new { l.HsnCode, l.GstRate, l.Qty, l.Taxable, l.Tax, Unit = l.Item.Unit }).ToList(),
            })
            .ToListAsync(ct);

        // B2B: one row per invoice per rate.
        var b2b = invoices.Where(i => i.CustomerGstin is not null)
            .SelectMany(i => i.Lines.GroupBy(l => l.GstRate).Select(g =>
            {
                var (igst, cgst, sgst) = Split(g.Sum(l => l.Tax), i.IsInterState);
                return new GstB2BRow(i.CustomerGstin!, i.CustomerName, i.InvoiceNo, DateOnly.FromDateTime(i.InvoiceDate), i.Total,
                    i.CustomerGstin![..2], g.Key, g.Sum(l => l.Taxable), igst, cgst, sgst);
            }))
            .OrderBy(r => r.Date).ThenBy(r => r.InvoiceNo).ToList();

        // B2C (small): unregistered buyers are billed at the counter, so place of supply is the store's state.
        // Sum each invoice's own CGST/SGST split (as printed) so the return agrees with the bills to the paisa.
        var b2cs = invoices.Where(i => i.CustomerGstin is null)
            .SelectMany(i => BillCalculator.BuildSlabs(i.Lines.Select(l => (l.GstRate, l.Taxable, l.Tax)), i.IsInterState))
            .GroupBy(sl => sl.GstRate).OrderBy(g => g.Key)
            .Select(g => new GstB2CSRow(storeState, g.Key, g.Sum(x => x.Taxable), g.Sum(x => x.Igst), g.Sum(x => x.Cgst), g.Sum(x => x.Sgst)))
            .ToList();

        // HSN summary of outward supplies.
        var hsn = invoices.SelectMany(i => i.Lines.Select(l => new { l, i.IsInterState }))
            .GroupBy(x => (Hsn: x.l.HsnCode ?? "(none)", x.l.Unit, x.l.GstRate))
            .OrderBy(g => g.Key.Hsn).ThenBy(g => g.Key.GstRate)
            .Select(g =>
            {
                var inter = g.Where(x => x.IsInterState).Sum(x => x.l.Tax);
                var (_, cgst, sgst) = Split(g.Where(x => !x.IsInterState).Sum(x => x.l.Tax), false);
                return new GstHsnRow(g.Key.Hsn, g.Key.Unit, g.Sum(x => x.l.Qty), g.Key.GstRate, g.Sum(x => x.l.Taxable), inter, cgst, sgst);
            }).ToList();

        // Credit notes issued in the period (returns on any invoice that is not cancelled).
        var notes = await db.SaleReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= start && r.ReturnDate < end && r.Invoice.Status == InvoiceStatus.Completed)
            .Select(r => new
            {
                r.ReturnNo, r.ReturnDate, r.Invoice.InvoiceNo, r.Invoice.InvoiceDate, r.Invoice.CustomerGstin, r.Invoice.CustomerName,
                r.Invoice.IsInterState,
                Lines = r.Lines.Select(l => new { l.InvoiceLine.GstRate, l.Taxable, l.Tax }).ToList(),
            })
            .ToListAsync(ct);
        var creditNotes = notes.SelectMany(n => n.Lines.GroupBy(l => l.GstRate).Select(g =>
            {
                var (igst, cgst, sgst) = Split(g.Sum(l => l.Tax), n.IsInterState);
                return new GstCreditNoteRow(n.CustomerGstin, n.CustomerName, n.ReturnNo, DateOnly.FromDateTime(n.ReturnDate), n.InvoiceNo,
                    DateOnly.FromDateTime(n.InvoiceDate), g.Key, g.Sum(l => l.Taxable), igst, cgst, sgst);
            }))
            .OrderBy(r => r.NoteDate).ThenBy(r => r.NoteNo).ToList();

        // Documents issued (number ranges include cancelled bills, which GSTR-1 reports separately).
        var docInvoices = await db.SaleInvoices.AsNoTracking().Where(i => i.InvoiceDate >= start && i.InvoiceDate < end)
            .OrderBy(i => i.Id).Select(i => new { i.InvoiceNo, i.Status }).ToListAsync(ct);
        var docNotes = notes.OrderBy(n => n.ReturnNo).Select(n => n.ReturnNo).ToList();
        var documents = new List<GstDocumentRow>
        {
            new("Invoices for outward supply", docInvoices.FirstOrDefault()?.InvoiceNo, docInvoices.LastOrDefault()?.InvoiceNo,
                docInvoices.Count, docInvoices.Count(i => i.Status == InvoiceStatus.Cancelled)),
            new("Credit notes", docNotes.FirstOrDefault(), docNotes.LastOrDefault(), docNotes.Count, 0),
        };

        // Purchases (input tax) by supplier invoice date, and debit notes against them.
        var purchases = await db.Purchases.AsNoTracking()
            .Where(p => p.Status == PurchaseStatus.Completed && p.SupplierInvoiceDate >= from && p.SupplierInvoiceDate <= to)
            .Select(p => new { p.Supplier.Gstin, p.Supplier.Name, p.SupplierInvoiceNo, p.SupplierInvoiceDate, p.Taxable, p.Tax })
            .ToListAsync(ct);
        var debitNotes = await db.PurchaseReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= start && r.ReturnDate < end)
            .Select(r => new { r.Supplier.Gstin, r.Supplier.Name, r.ReturnNo, r.ReturnDate, r.Taxable, r.Tax })
            .ToListAsync(ct);
        bool Inter(string? gstin) => gstin is { Length: >= 2 } && storeState.Length == 2 && gstin[..2] != storeState;
        var purchaseRows = purchases.Select(p =>
            {
                var (igst, cgst, sgst) = Split(p.Tax, Inter(p.Gstin));
                return new GstPurchaseRow(p.Gstin, p.Name, p.SupplierInvoiceNo, p.SupplierInvoiceDate, "Purchase", p.Taxable, igst, cgst, sgst);
            })
            .Concat(debitNotes.Select(r =>
            {
                var (igst, cgst, sgst) = Split(r.Tax, Inter(r.Gstin));
                return new GstPurchaseRow(r.Gstin, r.Name, r.ReturnNo, DateOnly.FromDateTime(r.ReturnDate), "Debit note",
                    -r.Taxable, -igst, -cgst, -sgst);
            }))
            .OrderBy(r => r.Date).ThenBy(r => r.Type == "Debit note").ThenBy(r => r.Document).ToList();

        return new GstReport
        {
            From = from, To = to, StoreGstin = settings.Gstin,
            B2B = b2b, B2CS = b2cs, Hsn = hsn, CreditNotes = creditNotes, Documents = documents, Purchases = purchaseRows,
        };
    }
}
