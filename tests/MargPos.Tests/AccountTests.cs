using System.Net;
using MargPos.Shared;

namespace MargPos.Tests;

public class AgeingTests
{
    private static readonly DateOnly D = new(2026, 4, 1);

    [Fact]
    public void Credits_clear_oldest_bills_first()
    {
        var open = Ageing.OpenItems(
            [new Ageing.Debit("B", D.AddDays(10), 200), new Ageing.Debit("A", D, 100), new Ageing.Debit("C", D.AddDays(20), 50)],
            credits: 150, creditDays: 30, asOf: D.AddDays(45));

        Assert.Equal(["B", "C"], open.Select(o => o.RefNo));
        Assert.Equal(150m, open[0].Open);        // 200 − 50 left after clearing A
        Assert.Equal(5, open[0].DaysOverdue);    // due D+40
        Assert.Equal(0, open[1].DaysOverdue);    // due D+50
    }

    [Fact]
    public void Overpayment_leaves_nothing_open_and_buckets_add_up()
    {
        Assert.Empty(Ageing.OpenItems([new Ageing.Debit("A", D, 100)], 120, 30, D.AddDays(100)));

        var open = Ageing.OpenItems(
            [new Ageing.Debit("A", D, 100), new Ageing.Debit("B", D.AddDays(40), 100), new Ageing.Debit("C", D.AddDays(80), 100)],
            0, 0, D.AddDays(100));
        var row = Ageing.Row(1, "X", null, 0, 0, 300, open);
        Assert.Equal(100m, row.Overdue90Plus);   // 100 days
        Assert.Equal(100m, row.Overdue31To60);   // 60 days
        Assert.Equal(100m, row.Overdue1To30);    // 20 days
        Assert.Equal(300m, row.Overdue);
        Assert.Equal(D, row.OldestDue);
    }
}

[Collection(PosApiCollection.Name)]
public class AccountTests(PosApiFixture api)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private async Task<(ItemDto Item, int BatchId)> Stock(decimal qty = 50)
    {
        var item = await (await api.Admin.PostJson("/api/items", new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Acc"), Unit = "PCS", Mrp = 100, SaleRate = 100, GstRate = 0, OpeningQty = qty,
        })).Read<ItemDto>();
        var batch = (await api.Admin.GetJson<List<BatchDto>>($"/api/items/{item.Id}/batches"))[0];
        return (item, batch.Id);
    }

    private async Task<CustomerDto> Customer(decimal limit = 1000, decimal opening = 0, int creditDays = 30) =>
        await (await api.Admin.PostJson("/api/customers", new CustomerUpsertRequest
        {
            Name = PosApiFixture.Unique("Cust"), CreditLimit = limit, OpeningBalance = opening, CreditDays = creditDays,
        })).Read<CustomerDto>();

    private async Task<SaleInvoiceDto> CreditSale(CustomerDto c, (ItemDto Item, int BatchId) s, decimal qty) =>
        await (await api.Admin.PostJson("/api/sales", new SaleRequest
        {
            CustomerId = c.Id, Lines = [new SaleLineRequest(s.Item.Id, s.BatchId, qty, 100, 0)],
            Payments = [new PaymentRequest(PaymentMode.Credit, qty * 100)],
        })).Read<SaleInvoiceDto>();

    private async Task<decimal> Owes(CustomerDto c) =>
        (await api.Admin.GetJson<CustomerBalanceDto>($"/api/customers/{c.Id}/balance")).Outstanding;

    [Fact]
    public async Task Receipt_reduces_outstanding_and_frees_credit_limit()
    {
        var s = await Stock();
        var c = await Customer(limit: 500);
        await CreditSale(c, s, 5);   // 500 = at the limit
        Assert.Equal(HttpStatusCode.Conflict, (await api.Admin.PostJson("/api/sales", new SaleRequest
        {
            CustomerId = c.Id, Lines = [new SaleLineRequest(s.Item.Id, s.BatchId, 1, 100, 0)],
            Payments = [new PaymentRequest(PaymentMode.Credit, 100)],
        })).StatusCode);

        // A cashier at the counter takes the payment.
        var (cashier, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        var receipt = await (await cashier.PostJson("/api/receipts",
            new ReceiptRequest(c.Id, 300, AccountPaymentMode.Upi, "UPI-991", null))).Read<ReceiptDto>();
        Assert.Matches(@"^RC\d{4}-\d{6}$", receipt.ReceiptNo);
        Assert.Equal(200m, receipt.BalanceAfter);
        Assert.Equal(200m, await Owes(c));

        await CreditSale(c, s, 1);   // fits again: 200 + 100 ≤ 500

        // Cancelling is for managers; it puts the amount back on the account.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostJson($"/api/receipts/{receipt.Id}/cancel", new CancelEntryRequest("wrong customer"))).StatusCode);
        var cancelled = await (await api.Admin.PostJson($"/api/receipts/{receipt.Id}/cancel",
            new CancelEntryRequest("Cheque bounced"))).Read<ReceiptDto>();
        Assert.Equal(AccountEntryStatus.Cancelled, cancelled.Status);
        Assert.Equal(600m, await Owes(c));
    }

    [Fact]
    public async Task Receipt_validation()
    {
        var c = await Customer();
        var noRef = await api.Admin.PostJson("/api/receipts", new ReceiptRequest(c.Id, 100, AccountPaymentMode.Cheque, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, noRef.StatusCode);
        Assert.Contains("cheque", await noRef.Content.ReadAsStringAsync());

        var zero = await api.Admin.PostJson("/api/receipts", new ReceiptRequest(c.Id, 0, AccountPaymentMode.Cash, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        // Advances are allowed: the customer simply ends up in credit.
        (await api.Admin.PostJson("/api/receipts", new ReceiptRequest(c.Id, 250, AccountPaymentMode.Cash, null, "Advance")))
            .EnsureSuccessStatusCode();
        Assert.Equal(-250m, await Owes(c));
    }

    [Fact]
    public async Task Customer_statement_has_running_balance_and_opening_for_the_range()
    {
        var s = await Stock();
        var c = await Customer(limit: 5000, opening: 500);
        var sale = await CreditSale(c, s, 3);   // +300
        await api.Admin.PostJson($"/api/sales/{sale.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(sale.Lines[0].Id, 1)], RefundMode.Account, null));   // −100
        await api.Admin.PostJson("/api/receipts", new ReceiptRequest(c.Id, 200, AccountPaymentMode.Cash, null, null)); // −200

        var st = await api.Admin.GetJson<StatementDto>($"/api/customers/{c.Id}/statement?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        Assert.Equal(500m, st.Opening);
        Assert.Equal(["Credit sale", "Return", "Receipt"], st.Entries.Select(e => e.Type));
        Assert.Equal([800m, 700m, 500m], st.Entries.Select(e => e.Balance));
        Assert.Equal(500m, st.Closing);
        Assert.Equal(500m, await Owes(c));

        // From tomorrow everything so far is in the opening balance.
        var later = await api.Admin.GetJson<StatementDto>(
            $"/api/customers/{c.Id}/statement?from={Today.AddDays(1):yyyy-MM-dd}&to={Today.AddDays(2):yyyy-MM-dd}");
        Assert.Empty(later.Entries);
        Assert.Equal(500m, later.Opening);
    }

    [Fact]
    public async Task Receivables_age_open_bills_oldest_paid_first()
    {
        var s = await Stock();
        var c = await Customer(limit: 5000, creditDays: 30);
        var first = await CreditSale(c, s, 1);    // 100
        var second = await CreditSale(c, s, 2);   // 200
        await api.Admin.PostJson("/api/receipts", new ReceiptRequest(c.Id, 150, AccountPaymentMode.Cash, null, null));

        var asOf = Today.AddDays(45);
        var open = await api.Admin.GetJson<List<OpenItemDto>>($"/api/customers/{c.Id}/open-bills?asOf={asOf:yyyy-MM-dd}");
        var bill = Assert.Single(open);           // the first bill was cleared
        Assert.Equal(second.InvoiceNo, bill.RefNo);
        Assert.Equal(150m, bill.Open);
        Assert.Equal(15, bill.DaysOverdue);

        var report = await api.Admin.GetJson<AgeingReport>($"/api/reports/receivables?asOf={asOf:yyyy-MM-dd}");
        var row = Assert.Single(report.Rows, r => r.PartyId == c.Id);
        Assert.Equal(150m, row.Outstanding);
        Assert.Equal(150m, row.Overdue1To30);
        Assert.Equal(0m, row.NotDue);
        _ = first;

        // Not overdue yet today.
        var todayRow = (await api.Admin.GetJson<AgeingReport>("/api/reports/receivables")).Rows.Single(r => r.PartyId == c.Id);
        Assert.Equal(150m, todayRow.NotDue);
        Assert.Equal(0m, todayRow.Overdue);
    }

    [Fact]
    public async Task Supplier_payments_statement_and_payables()
    {
        var supplier = await (await api.Admin.PostJson("/api/suppliers",
            new SupplierUpsertRequest { Name = PosApiFixture.Unique("Sup"), CreditDays = 30 })).Read<SupplierDto>();
        var item = await (await api.Admin.PostJson("/api/items", new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Pay"), Unit = "PCS", Mrp = 100, SaleRate = 90, GstRate = 12, IsBatchTracked = true,
        })).Read<ItemDto>();
        var purchase = await (await api.Admin.PostJson("/api/purchases", new PurchaseRequest
        {
            SupplierId = supplier.Id, SupplierInvoiceNo = PosApiFixture.Unique("INV"), SupplierInvoiceDate = Today,
            Lines = [new PurchaseLineRequest { ItemId = item.Id, BatchNo = "SP1", ExpiryDate = Today.AddYears(1), Qty = 10, Rate = 50 }],
        })).Read<PurchaseDto>();   // 560

        var (cashier, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostJson("/api/supplier-payments",
            new SupplierPaymentRequest(supplier.Id, 100, AccountPaymentMode.Cash, null, null))).StatusCode);

        var payment = await (await api.Admin.PostJson("/api/supplier-payments",
            new SupplierPaymentRequest(supplier.Id, 160, AccountPaymentMode.BankTransfer, "NEFT-77", null))).Read<SupplierPaymentDto>();
        Assert.Matches(@"^PM\d{4}-\d{6}$", payment.PaymentNo);
        Assert.Equal(400m, (await api.Admin.GetJson<SupplierBalanceDto>($"/api/suppliers/{supplier.Id}/balance")).Outstanding);

        var st = await api.Admin.GetJson<StatementDto>($"/api/suppliers/{supplier.Id}/statement?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        Assert.Equal(["Purchase", "Payment"], st.Entries.Select(e => e.Type));
        Assert.Equal([560m, 400m], st.Entries.Select(e => e.Balance));
        Assert.Equal(purchase.PurchaseNo, st.Entries[0].RefNo);

        var payables = await api.Admin.GetJson<AgeingReport>($"/api/reports/payables?asOf={Today.AddDays(100):yyyy-MM-dd}");
        var row = Assert.Single(payables.Rows, r => r.PartyId == supplier.Id);
        Assert.Equal(400m, row.Overdue61To90);   // due day 30, 70 days overdue

        (await api.Admin.PostJson($"/api/supplier-payments/{payment.Id}/cancel", new CancelEntryRequest("Entered twice")))
            .EnsureSuccessStatusCode();
        Assert.Equal(560m, (await api.Admin.GetJson<SupplierBalanceDto>($"/api/suppliers/{supplier.Id}/balance")).Outstanding);
    }

    [Fact]
    public async Task Credit_note_has_the_returned_lines()
    {
        var s = await Stock();
        var sale = await (await api.Admin.PostJson("/api/sales", new SaleRequest
        {
            CustomerName = "Walk-in", Lines = [new SaleLineRequest(s.Item.Id, s.BatchId, 3, 100, 0)],
            Payments = [new PaymentRequest(PaymentMode.Cash, 300)],
        })).Read<SaleInvoiceDto>();
        var ret = await (await api.Admin.PostJson($"/api/sales/{sale.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(sale.Lines[0].Id, 2)], RefundMode.Cash, "Not needed"))).Read<SaleReturnDto>();

        var note = await api.Admin.GetJson<CreditNoteDto>($"/api/sale-returns/{ret.Id}");
        Assert.Equal(sale.InvoiceNo, note.InvoiceNo);
        var line = Assert.Single(note.Lines);
        Assert.Equal((2m, 200m), (line.Qty, line.Amount));
        Assert.Equal("Not needed", note.Reason);
    }
}
