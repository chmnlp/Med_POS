using System.Net;
using MargPos.Shared;

namespace MargPos.Tests;

/// <summary>Phase 3.5: lot identity by MRP, weighted cost, margin pricing, lot price lists, profit, purchase returns.</summary>
[Collection(PosApiCollection.Name)]
public class LotPricingTests(PosApiFixture api)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private async Task<SupplierDto> Supplier() =>
        await (await api.Admin.PostJson("/api/suppliers", new SupplierUpsertRequest { Name = PosApiFixture.Unique("Dist") })).Read<SupplierDto>();

    private async Task<ItemDto> Item(decimal gst = 0, decimal margin = 0, decimal wholesaleMargin = 0) =>
        await (await api.Admin.PostJson("/api/items", new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Lot"), Unit = "STRIP", Mrp = 100, SaleRate = 95, GstRate = gst, IsBatchTracked = true,
            MarginPercent = margin, WholesaleMarginPercent = wholesaleMargin,
        })).Read<ItemDto>();

    private async Task<PurchaseDto> Receive(SupplierDto s, ItemDto item, string batch, decimal qty, decimal rate,
        decimal mrp = 100, decimal sale = 90, int expiryDays = 400, decimal wholesale = 0, decimal hospital = 0,
        decimal? margin = null) =>
        await (await api.Admin.PostJson("/api/purchases", new PurchaseRequest
        {
            SupplierId = s.Id, SupplierInvoiceNo = PosApiFixture.Unique("INV"), SupplierInvoiceDate = Today,
            Lines = [new PurchaseLineRequest
            {
                ItemId = item.Id, BatchNo = batch, ExpiryDate = Today.AddDays(expiryDays), Qty = qty, Rate = rate,
                Mrp = mrp, SaleRate = sale, WholesaleRate = wholesale, HospitalRate = hospital, MarginPercent = margin,
            }],
        })).Read<PurchaseDto>();

    private async Task<List<BatchDto>> Lots(int itemId) =>
        await api.Admin.GetJson<List<BatchDto>>($"/api/items/{itemId}/batches?includeEmpty=true");

    [Fact]
    public async Task Different_mrp_creates_a_new_lot_and_old_stock_keeps_its_price()
    {
        var s = await Supplier();
        var item = await Item();
        await Receive(s, item, "PC24A", 10, 50, mrp: 100, sale: 95);
        var second = await Receive(s, item, "pc24a", 5, 52, mrp: 110, sale: 105);
        Assert.Equal("PC24A/2", second.Lines[0].BatchNo);

        var third = await Receive(s, item, "PC24A", 4, 55, mrp: 120, sale: 115);
        Assert.Equal("PC24A/3", third.Lines[0].BatchNo);

        // Same MRP as the first box: tops up the original lot rather than making another.
        var again = await Receive(s, item, "PC24A", 2, 50, mrp: 100, sale: 95);
        Assert.Equal("PC24A", again.Lines[0].BatchNo);

        var lots = (await Lots(item.Id)).ToDictionary(b => b.BatchNo);
        Assert.Equal(3, lots.Count);
        Assert.Equal((12m, 100m, 95m), (lots["PC24A"].QtyOnHand, lots["PC24A"].Mrp, lots["PC24A"].SaleRate));
        Assert.Equal((5m, 110m), (lots["PC24A/2"].QtyOnHand, lots["PC24A/2"].Mrp));
        Assert.Equal((4m, 120m), (lots["PC24A/3"].QtyOnHand, lots["PC24A/3"].Mrp));

        var sellable = await api.Admin.GetJson<List<SellableBatchDto>>($"/api/items/{item.Id}/sellable-batches");
        Assert.Equal(["PC24A", "PC24A/2", "PC24A/3"], sellable.Select(b => b.BatchNo));
    }

    [Fact]
    public async Task Two_mrps_in_one_purchase_split_into_two_lots()
    {
        var s = await Supplier();
        var item = await Item();
        var purchase = await (await api.Admin.PostJson("/api/purchases", new PurchaseRequest
        {
            SupplierId = s.Id, SupplierInvoiceNo = PosApiFixture.Unique("INV"), SupplierInvoiceDate = Today,
            Lines =
            [
                new PurchaseLineRequest { ItemId = item.Id, BatchNo = "MX1", ExpiryDate = Today.AddYears(1), Qty = 3, Rate = 40, Mrp = 90, SaleRate = 85 },
                new PurchaseLineRequest { ItemId = item.Id, BatchNo = "MX1", ExpiryDate = Today.AddYears(1), Qty = 4, Rate = 40, Mrp = 95, SaleRate = 90 },
                new PurchaseLineRequest { ItemId = item.Id, BatchNo = "MX1", ExpiryDate = Today.AddYears(1), Qty = 1, Rate = 40, Mrp = 90, SaleRate = 85 },
            ],
        })).Read<PurchaseDto>();
        Assert.Equal(["MX1", "MX1/2", "MX1"], purchase.Lines.Select(l => l.BatchNo));
        var lots = (await Lots(item.Id)).ToDictionary(b => b.BatchNo, b => b.QtyOnHand);
        Assert.Equal(4m, lots["MX1"]);
        Assert.Equal(4m, lots["MX1/2"]);
    }

    [Fact]
    public async Task Same_lot_at_a_new_cost_takes_the_weighted_average()
    {
        var s = await Supplier();
        var item = await Item();
        await Receive(s, item, "WA1", 10, 50);
        await Receive(s, item, "WA1", 30, 60);   // (10×50 + 30×60) / 40 = 57.5
        Assert.Equal(57.5m, Assert.Single(await Lots(item.Id)).PurchaseRate);
    }

    [Fact]
    public async Task Selling_rates_from_margin_on_landed_cost()
    {
        var s = await Supplier();
        var item = await Item(gst: 12, margin: 20, wholesaleMargin: 10);

        // Item margins: retail 50 × 1.20 × 1.12 = 67.20; wholesale 50 × 1.10 × 1.12 = 61.60.
        var byItemMargin = await Receive(s, item, "MG1", 10, 50, sale: 0);
        Assert.Equal(67.20m, byItemMargin.Lines[0].SaleRate);
        Assert.Equal(20m, byItemMargin.Lines[0].MarginPercent);
        Assert.Equal(61.60m, byItemMargin.Lines[0].WholesaleRate);

        // Margin on the line wins; still capped at MRP.
        var capped = await Receive(s, item, "MG2", 10, 50, sale: 0, margin: 300);
        Assert.Equal(100m, capped.Lines[0].SaleRate);

        // An explicit sale rate is kept and no margin recorded.
        var manual = await Receive(s, item, "MG3", 10, 50, sale: 80);
        Assert.Equal(80m, manual.Lines[0].SaleRate);
        Assert.Null(manual.Lines[0].MarginPercent);
    }

    [Fact]
    public async Task Wholesale_and_hospital_customers_get_the_lot_price_list()
    {
        var s = await Supplier();
        var item = await Item();
        var purchase = await Receive(s, item, "PL1", 20, 50, sale: 95, wholesale: 80, hospital: 75);
        var lot = await api.Admin.GetJson<List<SellableBatchDto>>($"/api/items/{item.Id}/sellable-batches");
        Assert.Equal((80m, 75m), (lot[0].WholesaleRate, lot[0].HospitalRate));

        var wholesaler = await (await api.Admin.PostJson("/api/customers",
            new CustomerUpsertRequest { Name = "Wholesale Co", Type = CustomerType.Wholesale })).Read<CustomerDto>();
        var hospital = await (await api.Admin.PostJson("/api/customers",
            new CustomerUpsertRequest { Name = "Care Hospital", Type = CustomerType.Hospital })).Read<CustomerDto>();
        var (cashier, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        var batchId = purchase.Lines[0].BatchId;

        SaleRequest Bill(int customerId, decimal rate) => new()
        {
            CustomerId = customerId, Lines = [new SaleLineRequest(item.Id, batchId, 1, rate, 0)],
            Payments = [new PaymentRequest(PaymentMode.Cash, rate)],
        };

        // Cashiers must sell at the lot's rate for the customer's type.
        Assert.Equal(HttpStatusCode.Created, (await cashier.PostJson("/api/sales", Bill(wholesaler.Id, 80))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await cashier.PostJson("/api/sales", Bill(hospital.Id, 75))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cashier.PostJson("/api/sales", Bill(wholesaler.Id, 95))).StatusCode);
    }

    [Fact]
    public async Task Profit_uses_the_cost_recorded_at_sale_and_nets_returns()
    {
        var s = await Supplier();
        var item = await Item(gst: 0);
        var purchase = await Receive(s, item, "PR1", 10, 50, sale: 90);
        var batchId = purchase.Lines[0].BatchId;

        var sale = await (await api.Admin.PostJson("/api/sales", new SaleRequest
        {
            Lines = [new SaleLineRequest(item.Id, batchId, 4, 90, 0)], Payments = [new PaymentRequest(PaymentMode.Cash, 360)],
        })).Read<SaleInvoiceDto>();
        await api.Admin.PostJson($"/api/sales/{sale.Id}/returns",
            new SaleReturnRequest([new ReturnLineRequest(sale.Lines[0].Id, 1)], RefundMode.Cash, null));

        // A later, dearer purchase into the same lot must not change the profit of the sale already made.
        await Receive(s, item, "PR1", 10, 70, sale: 90);

        var report = await api.Admin.GetJson<ProfitReport>(
            $"/api/reports/profit?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&groupBy=Item");
        var row = Assert.Single(report.Rows, r => r.Key == item.Id.ToString());
        Assert.Equal(3m, row.Qty);
        Assert.Equal(270m, row.Revenue);  // 360 − 90 returned
        Assert.Equal(150m, row.Cost);     // 3 × 50
        Assert.Equal(120m, row.Profit);
        Assert.Equal(44.44m, row.MarginPercent);

        var byDay = await api.Admin.GetJson<ProfitReport>($"/api/reports/profit?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        Assert.Equal(Today.ToString("yyyy-MM-dd"), Assert.Single(byDay.Rows).Key);
    }

    [Fact]
    public async Task Expiry_return_to_supplier_raises_debit_note_and_lowers_balance()
    {
        var s = await Supplier();
        var item = await Item(gst: 12);
        var purchase = await Receive(s, item, "XR1", 10, 50, expiryDays: 20);   // total 560
        var before = await api.Admin.GetJson<SupplierBalanceDto>($"/api/suppliers/{s.Id}/balance");
        Assert.Equal(560m, before.Outstanding);

        var candidates = await api.Admin.GetJson<List<ReturnCandidateDto>>($"/api/purchase-returns/candidates?supplierId={s.Id}&days=30");
        var lot = Assert.Single(candidates, c => c.ItemId == item.Id);
        Assert.Equal(50m, lot.LandingCost);

        var other = await Supplier();
        Assert.Empty(await api.Admin.GetJson<List<ReturnCandidateDto>>($"/api/purchase-returns/candidates?supplierId={other.Id}&days=30"));

        var debit = await (await api.Admin.PostJson("/api/purchase-returns", new PurchaseReturnRequest
        {
            SupplierId = s.Id, Reason = "Near expiry", Lines = [new PurchaseReturnLineRequest { BatchId = lot.BatchId, Qty = 3 }],
        })).Read<PurchaseReturnDto>();
        Assert.Matches(@"^D\d{4}-\d{6}$", debit.ReturnNo);
        Assert.Equal(150m, debit.Taxable);
        Assert.Equal(18m, debit.Tax);
        Assert.Equal(168m, debit.Total);

        Assert.Equal(7m, Assert.Single(await Lots(item.Id)).QtyOnHand);
        var after = await api.Admin.GetJson<SupplierBalanceDto>($"/api/suppliers/{s.Id}/balance");
        Assert.Equal(392m, after.Outstanding);

        var tooMany = await api.Admin.PostJson("/api/purchase-returns", new PurchaseReturnRequest
        {
            SupplierId = s.Id, Lines = [new PurchaseReturnLineRequest { BatchId = lot.BatchId, Qty = 8 }],
        });
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);

        var movements = await api.Admin.GetJson<PagedResult<StockMovementDto>>($"/api/items/{item.Id}/movements");
        Assert.Equal("PurchaseReturn", movements.Items[0].Type);
        Assert.Equal(debit.ReturnNo, movements.Items[0].RefNo);
        _ = purchase;
    }

    [Fact]
    public async Task Return_against_a_purchase_is_limited_and_blocks_cancel()
    {
        var s = await Supplier();
        var item = await Item();
        var purchase = await (await api.Admin.PostJson("/api/purchases", new PurchaseRequest
        {
            SupplierId = s.Id, SupplierInvoiceNo = PosApiFixture.Unique("INV"), SupplierInvoiceDate = Today,
            Lines = [new PurchaseLineRequest { ItemId = item.Id, BatchNo = "RP1", ExpiryDate = Today.AddYears(1), Qty = 5, FreeQty = 1, Rate = 60, Mrp = 100, SaleRate = 90 }],
        })).Read<PurchaseDto>();

        var returnable = Assert.Single(await api.Admin.GetJson<List<ReturnablePurchaseLineDto>>($"/api/purchases/{purchase.Id}/returnable"));
        Assert.Equal(6m, returnable.Received);
        Assert.Equal(50m, returnable.LandingCost);

        PurchaseReturnRequest Ret(decimal qty) => new()
        {
            SupplierId = s.Id, PurchaseId = purchase.Id,
            Lines = [new PurchaseReturnLineRequest { BatchId = returnable.BatchId, PurchaseLineId = returnable.PurchaseLineId, Qty = qty }],
        };
        var first = await (await api.Admin.PostJson("/api/purchase-returns", Ret(4))).Read<PurchaseReturnDto>();
        Assert.Equal(purchase.PurchaseNo, first.PurchaseNo);
        Assert.Equal(200m, first.Taxable);   // 4 × landed 50

        var over = await api.Admin.PostJson("/api/purchase-returns", Ret(3));
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Contains("Only 2", await over.Content.ReadAsStringAsync());

        var wrongSupplier = await api.Admin.PostJson("/api/purchase-returns", Ret(1) with { SupplierId = (await Supplier()).Id });
        Assert.Equal(HttpStatusCode.BadRequest, wrongSupplier.StatusCode);

        var cancel = await api.Admin.PostJson($"/api/purchases/{purchase.Id}/cancel", new CancelPurchaseRequest("mistake"));
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
    }
}
