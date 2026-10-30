using System.Net;
using MargPos.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MargPos.Tests;

[Collection(PosApiCollection.Name)]
public class SalesTests(PosApiFixture api)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Creates a batch-tracked item with the given batches (expiry offset in days, qty).</summary>
    private async Task<(ItemDto Item, List<BatchDto> Batches)> StockedItem(
        decimal mrp = 50, decimal rate = 45, decimal gst = 12, DrugSchedule schedule = DrugSchedule.None,
        decimal wholesale = 0, params (int ExpiryDays, decimal Qty)[] batches)
    {
        var item = await (await api.Admin.PostJson("/api/items", new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Med"), Unit = "STRIP", Mrp = mrp, SaleRate = rate, GstRate = gst,
            WholesaleRate = wholesale, IsBatchTracked = true, Schedule = schedule,
        })).Read<ItemDto>();

        var created = new List<BatchDto>();
        foreach (var (days, qty) in batches)
            created.Add(await (await api.Admin.PostJson($"/api/items/{item.Id}/batches", new BatchUpsertRequest
            {
                BatchNo = PosApiFixture.Unique("B"), ExpiryDate = Today.AddDays(days), OpeningQty = qty,
            })).Read<BatchDto>());
        return (item, created);
    }

    private static SaleRequest Bill(ItemDto item, int batchId, decimal qty, decimal rate, decimal total,
        PaymentMode mode = PaymentMode.Cash, int? customerId = null, decimal discount = 0) => new()
    {
        CustomerId = customerId,
        Lines = [new SaleLineRequest(item.Id, batchId, qty, rate, discount)],
        Payments = [new PaymentRequest(mode, total)],
    };

    private async Task<decimal> StockOf(int itemId) => (await api.Admin.GetJson<ItemDto>($"/api/items/{itemId}")).StockQty;

    [Fact]
    public async Task Sale_deducts_stock_computes_gst_and_numbers_invoices()
    {
        var (item, batches) = await StockedItem(mrp: 112, rate: 112, gst: 12, batches: [(365, 10)]);

        var first = await (await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 2, 112, 224))).Read<SaleInvoiceDto>();
        Assert.Equal(224m, first.Total);
        Assert.Equal(200m, first.Taxable);
        Assert.Equal(12m, first.Cgst);
        Assert.Equal(12m, first.Sgst);
        Assert.Equal(InvoiceStatus.Completed, first.Status);
        Assert.Matches(@"^S\d{4}-\d{6}$", first.InvoiceNo);
        Assert.Equal(8m, await StockOf(item.Id));

        var second = await (await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 112, 112))).Read<SaleInvoiceDto>();
        Assert.Equal(int.Parse(first.InvoiceNo[^6..]) + 1, int.Parse(second.InvoiceNo[^6..]));
    }

    [Fact]
    public async Task Sellable_batches_are_fefo_and_exclude_expired_and_empty()
    {
        var (item, batches) = await StockedItem(batches: [(400, 5), (-5, 9), (60, 3), (200, 0)]);

        var sellable = await api.Admin.GetJson<List<SellableBatchDto>>($"/api/items/{item.Id}/sellable-batches");
        Assert.Equal([batches[2].Id, batches[0].Id], sellable.Select(b => b.Id));
    }

    [Fact]
    public async Task Expired_batch_cannot_be_sold()
    {
        var (item, batches) = await StockedItem(batches: [(-1, 5)]);
        var res = await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 45, 45));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("expired", await res.Content.ReadAsStringAsync());
        Assert.Equal(5m, await StockOf(item.Id));
    }

    [Fact]
    public async Task Overselling_is_rejected_and_stock_untouched()
    {
        var (item, batches) = await StockedItem(batches: [(300, 3)]);
        var res = await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 4, 45, 180));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Contains("3 available", await res.Content.ReadAsStringAsync());
        Assert.Equal(3m, await StockOf(item.Id));
    }

    [Fact]
    public async Task Concurrent_counters_cannot_both_sell_the_last_unit()
    {
        var (item, batches) = await StockedItem(batches: [(300, 1)]);
        var counters = Enumerable.Range(0, 5).Select(_ => api.ClientWithToken(
            api.Admin.DefaultRequestHeaders.Authorization!.Parameter!)).ToList();

        var results = await Task.WhenAll(counters.Select(c => c.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 45, 45))));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0m, await StockOf(item.Id));
    }

    [Fact]
    public async Task Payments_must_match_total_and_rate_cannot_exceed_mrp()
    {
        var (item, batches) = await StockedItem(mrp: 50, rate: 45, batches: [(300, 10)]);

        var underpaid = await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 45, 40));
        Assert.Equal(HttpStatusCode.BadRequest, underpaid.StatusCode);
        Assert.Contains("must equal the bill total", await underpaid.Content.ReadAsStringAsync());

        var aboveMrp = await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 55, 55));
        Assert.Equal(HttpStatusCode.BadRequest, aboveMrp.StatusCode);
        Assert.Contains("cannot exceed MRP", await aboveMrp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Split_payment_is_recorded()
    {
        var (item, batches) = await StockedItem(mrp: 100, rate: 100, gst: 5, batches: [(300, 10)]);
        var req = Bill(item, batches[0].Id, 3, 100, 0) with
        {
            Payments = [new PaymentRequest(PaymentMode.Cash, 100), new PaymentRequest(PaymentMode.Upi, 200, "UPI-REF-1")],
        };
        var invoice = await (await api.Admin.PostJson("/api/sales", req)).Read<SaleInvoiceDto>();
        Assert.Equal(2, invoice.Payments.Count);
        Assert.Equal(0m, invoice.BalanceDue);
    }

    [Fact]
    public async Task Cashier_limits_on_rate_and_discount()
    {
        var (cashier, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        var (item, batches) = await StockedItem(mrp: 100, rate: 90, gst: 0, batches: [(300, 10)]);

        var rateChange = await cashier.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 80, 80));
        Assert.Equal(HttpStatusCode.BadRequest, rateChange.StatusCode);
        Assert.Contains("Only a manager can change the rate", await rateChange.Content.ReadAsStringAsync());

        var bigDiscount = await cashier.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 90, 72, discount: 20));
        Assert.Equal(HttpStatusCode.BadRequest, bigDiscount.StatusCode);
        Assert.Contains("limited to 10%", await bigDiscount.Content.ReadAsStringAsync());

        var ok = await cashier.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 90, 81, discount: 10));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Schedule_h1_requires_doctor_and_patient()
    {
        var (item, batches) = await StockedItem(gst: 0, rate: 50, schedule: DrugSchedule.H1, batches: [(300, 10)]);

        var missing = await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 50, 50));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("Doctor and patient", await missing.Content.ReadAsStringAsync());

        var ok = await api.Admin.PostJson("/api/sales",
            Bill(item, batches[0].Id, 1, 50, 50) with { DoctorName = "Dr. Rao", PatientName = "A. Kumar" });
        var invoice = await ok.Read<SaleInvoiceDto>();
        Assert.Equal("Dr. Rao", invoice.DoctorName);
    }

    [Fact]
    public async Task Credit_sales_respect_customer_and_limit()
    {
        var (item, batches) = await StockedItem(mrp: 1000, rate: 1000, gst: 0, wholesale: 900, batches: [(300, 20)]);

        var walkIn = await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 1000, 1000, PaymentMode.Credit));
        Assert.Equal(HttpStatusCode.BadRequest, walkIn.StatusCode);

        var cashOnly = await (await api.Admin.PostJson("/api/customers",
            new CustomerUpsertRequest { Name = "Cash Only" })).Read<CustomerDto>();
        var refused = await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 1000, 1000, PaymentMode.Credit, cashOnly.Id));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var hospital = await (await api.Admin.PostJson("/api/customers", new CustomerUpsertRequest
        {
            Name = "City Hospital", Type = CustomerType.Hospital, CreditLimit = 2000, OpeningBalance = 500,
        })).Read<CustomerDto>();

        // Hospital price list = wholesale 900. Opening 500 + 900 = 1400 ≤ 2000.
        var first = await (await api.Admin.PostJson("/api/sales",
            Bill(item, batches[0].Id, 1, 900, 900, PaymentMode.Credit, hospital.Id))).Read<SaleInvoiceDto>();
        Assert.Equal(900m, first.BalanceDue);
        Assert.Equal(CustomerType.Hospital, first.CustomerType);

        var balance = await api.Admin.GetJson<CustomerBalanceDto>($"/api/customers/{hospital.Id}/balance");
        Assert.Equal(1400m, balance.Outstanding);

        // 1400 + 900 > 2000
        var over = await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 900, 900, PaymentMode.Credit, hospital.Id));
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Contains("Credit limit exceeded", await over.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Returns_restock_and_cannot_exceed_sold_quantity()
    {
        var (item, batches) = await StockedItem(mrp: 10, rate: 10, gst: 12, batches: [(300, 10)]);
        var invoice = await (await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 3, 10, 30))).Read<SaleInvoiceDto>();
        var lineId = invoice.Lines[0].Id;
        Assert.Equal(7m, await StockOf(item.Id));

        var partial = await (await api.Admin.PostJson($"/api/sales/{invoice.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(lineId, 1)], RefundMode.Cash, "Wrong item"))).Read<SaleReturnDto>();
        Assert.StartsWith("CN", partial.ReturnNo);
        Assert.Equal(10m, partial.Total);
        Assert.Equal(8m, await StockOf(item.Id));

        var tooMany = await api.Admin.PostJson($"/api/sales/{invoice.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(lineId, 3)], RefundMode.Cash, null));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Contains("Only 2 of", await tooMany.Content.ReadAsStringAsync());

        // Returning the rest takes exactly the remaining line value.
        var rest = await (await api.Admin.PostJson($"/api/sales/{invoice.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(lineId, 2)], RefundMode.Cash, null))).Read<SaleReturnDto>();
        Assert.Equal(20m, rest.Total);
        Assert.Equal(10m, await StockOf(item.Id));

        var detail = await api.Admin.GetJson<SaleInvoiceDto>($"/api/sales/{invoice.Id}");
        Assert.Equal(3m, detail.Lines[0].ReturnedQty);
        Assert.Equal(2, detail.Returns.Count);

        // A bill with returns cannot be cancelled.
        var cancel = await api.Admin.PostJson($"/api/sales/{invoice.Id}/cancel", new CancelSaleRequest("test"));
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
    }

    [Fact]
    public async Task Credit_return_reduces_outstanding_and_cash_refund_is_refused()
    {
        var (item, batches) = await StockedItem(mrp: 100, rate: 100, gst: 0, batches: [(300, 10)]);
        var customer = await (await api.Admin.PostJson("/api/customers",
            new CustomerUpsertRequest { Name = "Credit Co", CreditLimit = 1000 })).Read<CustomerDto>();
        var invoice = await (await api.Admin.PostJson("/api/sales",
            Bill(item, batches[0].Id, 2, 100, 200, PaymentMode.Credit, customer.Id))).Read<SaleInvoiceDto>();

        var cash = await api.Admin.PostJson($"/api/sales/{invoice.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(invoice.Lines[0].Id, 1)], RefundMode.Cash, null));
        Assert.Equal(HttpStatusCode.BadRequest, cash.StatusCode);

        (await api.Admin.PostJson($"/api/sales/{invoice.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(invoice.Lines[0].Id, 1)], RefundMode.Account, null))).EnsureSuccessStatusCode();
        var balance = await api.Admin.GetJson<CustomerBalanceDto>($"/api/customers/{customer.Id}/balance");
        Assert.Equal(100m, balance.Outstanding);
    }

    [Fact]
    public async Task Cancel_is_manager_only_and_restocks()
    {
        var (item, batches) = await StockedItem(mrp: 10, rate: 10, gst: 0, batches: [(300, 5)]);
        var invoice = await (await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 2, 10, 20))).Read<SaleInvoiceDto>();

        var (cashier, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostJson($"/api/sales/{invoice.Id}/cancel", new CancelSaleRequest("mistake"))).StatusCode);

        var cancelled = await (await api.Admin.PostJson($"/api/sales/{invoice.Id}/cancel",
            new CancelSaleRequest("Customer changed mind"))).Read<SaleInvoiceDto>();
        Assert.Equal(InvoiceStatus.Cancelled, cancelled.Status);
        Assert.Equal(5m, await StockOf(item.Id));

        Assert.Equal(HttpStatusCode.Conflict,
            (await api.Admin.PostJson($"/api/sales/{invoice.Id}/cancel", new CancelSaleRequest("again"))).StatusCode);
    }

    [Fact]
    public async Task Inter_state_customer_is_billed_igst()
    {
        var settings = await api.Admin.GetJson<StoreSettingsDto>("/api/settings");
        (await api.Admin.PutJson("/api/settings", settings with { Gstin = "27AAAAA0000A1Z5" })).EnsureSuccessStatusCode();

        var (item, batches) = await StockedItem(mrp: 118, rate: 118, gst: 18, batches: [(300, 5)]);
        var karnataka = await (await api.Admin.PostJson("/api/customers",
            new CustomerUpsertRequest { Name = "Bengaluru Pharma", Gstin = "29ABCDE1234F1Z5" })).Read<CustomerDto>();

        var invoice = await (await api.Admin.PostJson("/api/sales",
            Bill(item, batches[0].Id, 1, 118, 118, customerId: karnataka.Id))).Read<SaleInvoiceDto>();
        Assert.True(invoice.IsInterState);
        Assert.Equal(18m, invoice.Igst);
        Assert.Equal(0m, invoice.Cgst);
        Assert.Equal("29ABCDE1234F1Z5", invoice.CustomerGstin);
    }

    [Fact]
    public async Task Held_bills_round_trip()
    {
        var created = await api.Admin.PostJson("/api/held-bills", new HoldBillRequest("Counter 1 — Ravi", "{\"lines\":[]}"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var held = await api.Admin.GetJson<List<HeldBillDto>>("/api/held-bills");
        var bill = Assert.Single(held, h => h.Label == "Counter 1 — Ravi");
        Assert.Equal("{\"lines\":[]}", bill.Payload);

        Assert.Equal(HttpStatusCode.NoContent, (await api.Admin.DeleteAsync($"/api/held-bills/{bill.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Admin.DeleteAsync($"/api/held-bills/{bill.Id}")).StatusCode);
    }

    [Fact]
    public async Task Stock_ledger_always_matches_batch_quantities()
    {
        // Exercise every movement type, then check the invariant across the whole test database.
        var (item, batches) = await StockedItem(mrp: 10, rate: 10, gst: 0, batches: [(300, 6)]);
        var sold = await (await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 2, 10, 20))).Read<SaleInvoiceDto>();
        await api.Admin.PostJson($"/api/sales/{sold.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(sold.Lines[0].Id, 1)], RefundMode.Cash, null));
        var cancelled = await (await api.Admin.PostJson("/api/sales", Bill(item, batches[0].Id, 1, 10, 10))).Read<SaleInvoiceDto>();
        await api.Admin.PostJson($"/api/sales/{cancelled.Id}/cancel", new CancelSaleRequest("test"));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MargPos.Data.PosDbContext>();
        var mismatches = await db.Batches
            .Select(b => new { b.Id, b.QtyOnHand, Ledger = db.StockMovements.Where(m => m.BatchId == b.Id).Sum(m => (decimal?)m.QtyChange) ?? 0 })
            .Where(x => x.QtyOnHand != x.Ledger)
            .ToListAsync();
        Assert.Empty(mismatches);
        Assert.Equal(5m, await StockOf(item.Id));
    }

    [Fact]
    public async Task Sales_list_search_and_dashboard_today()
    {
        var before = await api.Admin.GetJson<DashboardSummary>("/api/dashboard/summary");
        var (item, batches) = await StockedItem(mrp: 10, rate: 10, gst: 0, batches: [(300, 5)]);
        var phone = "97" + Random.Shared.Next(10_000_000, 99_999_999);
        var invoice = await (await api.Admin.PostJson("/api/sales",
            Bill(item, batches[0].Id, 1, 10, 10) with { CustomerName = "Walk-in Meena", CustomerPhone = phone })).Read<SaleInvoiceDto>();

        var found = await api.Admin.GetJson<PagedResult<SaleSummaryDto>>($"/api/sales?q={phone}");
        var row = Assert.Single(found.Items);
        Assert.Equal(invoice.InvoiceNo, row.InvoiceNo);
        Assert.Equal("Cash", row.PaymentModes);

        var after = await api.Admin.GetJson<DashboardSummary>("/api/dashboard/summary");
        Assert.Equal(before.TodayBills + 1, after.TodayBills);
        Assert.Equal(before.TodaySales + 10, after.TodaySales);
    }
}
