using System.Net;
using MargPos.Shared;

namespace MargPos.Tests;

[Collection(PosApiCollection.Name)]
public class ReportTests(PosApiFixture api)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// <summary>A unique, valid-format HSN so a test can find its own rows in shared reports.</summary>
    private static string UniqueHsn() => Random.Shared.Next(10_000_000, 99_999_999).ToString();

    /// <summary>A valid-format GSTIN for the given state that no other test will use.</summary>
    private static string UniqueGstin(string state)
    {
        var letters = new string(Enumerable.Range(0, 5).Select(_ => (char)('A' + Random.Shared.Next(26))).ToArray());
        return $"{state}{letters}{Random.Shared.Next(1000, 9999)}{(char)('A' + Random.Shared.Next(26))}1Z5";
    }

    private async Task<(ItemDto Item, int BatchId)> Stock(decimal gst, string? hsn = null, decimal rate = 100)
    {
        var item = await (await api.Admin.PostJson("/api/items", new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Rpt"), Unit = "STRIP", Mrp = rate, SaleRate = rate, GstRate = gst, HsnCode = hsn, OpeningQty = 100,
        })).Read<ItemDto>();
        return (item, (await api.Admin.GetJson<List<BatchDto>>($"/api/items/{item.Id}/batches"))[0].Id);
    }

    private static SaleRequest Bill((ItemDto Item, int BatchId) s, decimal qty, decimal rate, params PaymentRequest[] pay) => new()
    {
        Lines = [new SaleLineRequest(s.Item.Id, s.BatchId, qty, rate, 0)], Payments = [.. pay],
    };

    [Fact]
    public async Task Day_end_works_out_expected_cash_for_the_cashier()
    {
        var (cashier, me) = await api.CreateUserClientAsync(UserRole.Cashier);
        var s = await Stock(gst: 0);
        var customer = await (await api.Admin.PostJson("/api/customers",
            new CustomerUpsertRequest { Name = PosApiFixture.Unique("DE"), CreditLimit = 5000 })).Read<CustomerDto>();

        await cashier.PostJson("/api/sales", Bill(s, 5, 100, new PaymentRequest(PaymentMode.Cash, 500)));
        await cashier.PostJson("/api/sales", Bill(s, 2, 100, new PaymentRequest(PaymentMode.Upi, 200)));
        var credit = await (await cashier.PostJson("/api/sales", Bill(s, 3, 100, new PaymentRequest(PaymentMode.Credit, 300)) with
            { CustomerId = customer.Id })).Read<SaleInvoiceDto>();
        var cashBill = await (await cashier.PostJson("/api/sales", Bill(s, 1, 100, new PaymentRequest(PaymentMode.Cash, 100))))
            .Read<SaleInvoiceDto>();
        await cashier.PostJson($"/api/sales/{cashBill.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(cashBill.Lines[0].Id, 1)], RefundMode.Cash, null));   // −100 cash
        await cashier.PostJson("/api/receipts", new ReceiptRequest(customer.Id, 150, AccountPaymentMode.Cash, null, null));
        _ = credit;

        // Someone else's sale does not appear on this counter.
        await api.Admin.PostJson("/api/sales", Bill(s, 1, 100, new PaymentRequest(PaymentMode.Cash, 100)));

        var summary = await cashier.GetJson<DayEndSummaryDto>("/api/day-end/summary");
        Assert.Equal(me.Id, summary.UserId);
        Assert.Equal(4, summary.Bills);
        Assert.Equal(1100m, summary.SalesTotal);
        Assert.Equal((600m, 200m, 300m), (summary.CashSales, summary.UpiSales, summary.CreditSales));
        Assert.Equal(100m, summary.CashRefunds);
        Assert.Equal(150m, summary.CashReceipts);
        Assert.Equal(650m, summary.NetCash);   // 600 − 100 + 150
        Assert.Null(summary.Closing);

        // Cashiers can only see their own counter.
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/day-end/summary?allCounters=true")).StatusCode);

        var closed = await (await cashier.PostJson("/api/day-end/close",
            new DayCloseRequest(Today, 1000, 1640, "500x3,100x1,20x2", "₹10 short"))).Read<DayCloseDto>();
        Assert.Equal(1650m, closed.ExpectedCash);
        Assert.Equal(-10m, closed.Difference);

        var again = await cashier.PostJson("/api/day-end/close", new DayCloseRequest(Today, 1000, 1650, null, null));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var future = await cashier.PostJson("/api/day-end/close", new DayCloseRequest(Today.AddDays(1), 0, 0, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);

        var mine = await cashier.GetJson<List<DayCloseDto>>("/api/day-end/closings");
        Assert.All(mine, c => Assert.Equal(me.Id, c.UserId));
        var all = await api.Admin.GetJson<List<DayCloseDto>>("/api/day-end/closings");
        Assert.Contains(all, c => c.Id == closed.Id);
        var managerView = await api.Admin.GetJson<DayEndSummaryDto>($"/api/day-end/summary?userId={me.Id}");
        Assert.Equal(closed.Id, managerView.Closing?.Id);
    }

    [Fact]
    public async Task Sales_summary_breaks_down_by_mode_cashier_and_item()
    {
        var (manager, me) = await api.CreateUserClientAsync(UserRole.Manager);
        var s = await Stock(gst: 12, rate: 112);
        await manager.PostJson("/api/sales", Bill(s, 2, 112, new PaymentRequest(PaymentMode.Cash, 224)));
        var bill = await (await manager.PostJson("/api/sales",
            Bill(s, 1, 112, new PaymentRequest(PaymentMode.Card, 50), new PaymentRequest(PaymentMode.Upi, 62)))).Read<SaleInvoiceDto>();
        var cancel = await (await manager.PostJson("/api/sales", Bill(s, 1, 112, new PaymentRequest(PaymentMode.Cash, 112)))).Read<SaleInvoiceDto>();
        await manager.PostJson($"/api/sales/{cancel.Id}/cancel", new CancelSaleRequest("test"));
        _ = bill;

        var report = await api.Admin.GetJson<SalesSummaryReport>($"/api/reports/sales-summary?from={D(Today)}&to={D(Today)}");
        var cashier = Assert.Single(report.ByCashier, c => c.UserId == me.Id);
        Assert.Equal((2, 336m), (cashier.Bills, cashier.Sales));
        Assert.Contains(report.ByMode, m => m.Mode == "Card" && m.Amount >= 50);
        Assert.True(report.CancelledBills >= 1);
        Assert.Equal(report.Total - report.ReturnsValue, report.Net);

        var top = Assert.Single(report.TopItems, t => t.ItemId == s.Item.Id);
        Assert.Equal((3m, 336m), (top.Qty, top.Amount));
        Assert.Contains(report.ByDay, d => d.Date == Today);

        var (cashierClient, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashierClient.GetAsync($"/api/reports/sales-summary?from={D(Today)}&to={D(Today)}")).StatusCode);
    }

    [Fact]
    public async Task Gst_report_splits_b2b_b2c_hsn_credit_notes_and_purchases()
    {
        var settings = await api.Admin.GetJson<StoreSettingsDto>("/api/settings");
        (await api.Admin.PutJson("/api/settings", settings with { Gstin = "27AAAAA0000A1Z5", StateCode = null })).EnsureSuccessStatusCode();

        var hsn = UniqueHsn();
        var s = await Stock(gst: 12, hsn: hsn, rate: 112);
        var b2bGstin = UniqueGstin("29");   // Karnataka buyer → IGST
        var b2b = await (await api.Admin.PostJson("/api/customers",
            new CustomerUpsertRequest { Name = "Bengaluru Medicals", Gstin = b2bGstin })).Read<CustomerDto>();

        var registered = await (await api.Admin.PostJson("/api/sales",
            Bill(s, 10, 112, new PaymentRequest(PaymentMode.Cash, 1120)) with { CustomerId = b2b.Id })).Read<SaleInvoiceDto>();
        var walkIn = await (await api.Admin.PostJson("/api/sales", Bill(s, 5, 112, new PaymentRequest(PaymentMode.Cash, 560)))).Read<SaleInvoiceDto>();
        await api.Admin.PostJson($"/api/sales/{walkIn.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(walkIn.Lines[0].Id, 1)], RefundMode.Cash, null));

        var report = await api.Admin.GetJson<GstReport>($"/api/reports/gst?from={D(Today)}&to={D(Today)}");

        var row = Assert.Single(report.B2B, r => r.Gstin == b2bGstin);
        Assert.Equal(registered.InvoiceNo, row.InvoiceNo);
        Assert.Equal("29", row.PlaceOfSupply);
        Assert.Equal((12m, 1000m, 120m, 0m, 0m), (row.Rate, row.Taxable, row.Igst, row.Cgst, row.Sgst));

        var b2cs = Assert.Single(report.B2CS, r => r.Rate == 12m);
        Assert.Equal("27", b2cs.PlaceOfSupply);
        Assert.Equal(0m, b2cs.Igst);
        Assert.True(b2cs.Cgst > 0 && b2cs.Sgst > 0);   // summed from each bill's own split, so they can differ by paise

        var hsnRow = Assert.Single(report.Hsn, r => r.Hsn == hsn);
        Assert.Equal((15m, "STRIP", 1500m), (hsnRow.Qty, hsnRow.Uqc, hsnRow.Taxable));
        Assert.Equal(120m, hsnRow.Igst);                 // the inter-state invoice
        Assert.Equal(60m, hsnRow.Cgst + hsnRow.Sgst);    // the walk-in bill

        var note = Assert.Single(report.CreditNotes, n => n.InvoiceNo == walkIn.InvoiceNo);
        Assert.Null(note.Gstin);
        Assert.Equal((100m, 6m, 6m), (note.Taxable, note.Cgst, note.Sgst));

        var invoicesDoc = Assert.Single(report.Documents, d => d.Nature.StartsWith("Invoices"));
        Assert.True(invoicesDoc.Total >= 2);

        // Purchase from a Gujarat supplier → IGST input tax; debit note goes negative.
        var supplier = await (await api.Admin.PostJson("/api/suppliers",
            new SupplierUpsertRequest { Name = PosApiFixture.Unique("GJ"), Gstin = UniqueGstin("24") })).Read<SupplierDto>();
        var tracked = await (await api.Admin.PostJson("/api/items", new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("ITC"), Unit = "STRIP", Mrp = 100, SaleRate = 90, GstRate = 12, IsBatchTracked = true,
        })).Read<ItemDto>();
        var purchase = await (await api.Admin.PostJson("/api/purchases", new PurchaseRequest
        {
            SupplierId = supplier.Id, SupplierInvoiceNo = PosApiFixture.Unique("GJ"), SupplierInvoiceDate = Today,
            Lines = [new PurchaseLineRequest { ItemId = tracked.Id, BatchNo = "ITC1", ExpiryDate = Today.AddYears(1), Qty = 10, Rate = 50 }],
        })).Read<PurchaseDto>();
        await api.Admin.PostJson("/api/purchase-returns", new PurchaseReturnRequest
        {
            SupplierId = supplier.Id, Lines = [new PurchaseReturnLineRequest { BatchId = purchase.Lines[0].BatchId, Qty = 2 }],
        });

        var withItc = await api.Admin.GetJson<GstReport>($"/api/reports/gst?from={D(Today)}&to={D(Today)}");
        var mine = withItc.Purchases.Where(p => p.SupplierName == supplier.Name).ToList();
        Assert.Equal(["Purchase", "Debit note"], mine.Select(p => p.Type));
        Assert.Equal((500m, 60m), (mine[0].Taxable, mine[0].Igst));
        Assert.Equal((-100m, -12m), (mine[1].Taxable, mine[1].Igst));
    }

    [Fact]
    public async Task Dashboard_shows_profit_only_to_managers()
    {
        var manager = await api.Admin.GetJson<DashboardSummary>("/api/dashboard/summary");
        Assert.True(manager.ShowsProfit);

        var (cashier, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        var cashierView = await cashier.GetJson<DashboardSummary>("/api/dashboard/summary");
        Assert.False(cashierView.ShowsProfit);
        Assert.Null(cashierView.MonthProfit);
    }
}
