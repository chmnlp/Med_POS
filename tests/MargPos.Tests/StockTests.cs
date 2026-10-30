using System.Net;
using System.Text;
using MargPos.Shared;

namespace MargPos.Tests;

[Collection(PosApiCollection.Name)]
public class StockTests(PosApiFixture api)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private async Task<SupplierDto> Supplier() =>
        await (await api.Admin.PostJson("/api/suppliers", new SupplierUpsertRequest { Name = PosApiFixture.Unique("Distributor") }))
            .Read<SupplierDto>();

    private async Task<ItemDto> Item(bool batchTracked = true, decimal gst = 12, decimal reorder = 0, string? code = null) =>
        await (await api.Admin.PostJson("/api/items", new ItemUpsertRequest
        {
            Code = code, Name = PosApiFixture.Unique("Item"), Unit = "STRIP", Mrp = 100, SaleRate = 95, GstRate = gst,
            IsBatchTracked = batchTracked, ReorderLevel = reorder,
        })).Read<ItemDto>();

    private static PurchaseRequest Purchase(SupplierDto s, params PurchaseLineRequest[] lines) => new()
    {
        SupplierId = s.Id, SupplierInvoiceNo = PosApiFixture.Unique("INV"), SupplierInvoiceDate = Today, Lines = [.. lines],
    };

    private static PurchaseLineRequest Line(ItemDto item, string? batch, int expiryDays, decimal qty, decimal rate,
        decimal free = 0, decimal mrp = 0, decimal sale = 0) => new()
    {
        ItemId = item.Id, BatchNo = batch, ExpiryDate = batch is null ? null : Today.AddDays(expiryDays),
        Qty = qty, FreeQty = free, Rate = rate, Mrp = mrp, SaleRate = sale,
    };

    private async Task<List<BatchDto>> Batches(int itemId) =>
        await api.Admin.GetJson<List<BatchDto>>($"/api/items/{itemId}/batches?includeEmpty=true");

    [Fact]
    public async Task Purchase_creates_batch_adds_stock_with_free_goods_and_landing_cost()
    {
        var supplier = await Supplier();
        var item = await Item(gst: 12);

        // 10 + 2 free at 60 excl. GST: taxable 600, GST 72, total 672; landed cost 600 / 12 = 50.
        var purchase = await (await api.Admin.PostJson("/api/purchases",
            Purchase(supplier, Line(item, "nb-1", 400, 10, 60, free: 2, mrp: 110, sale: 105)))).Read<PurchaseDto>();

        Assert.Matches(@"^P\d{4}-\d{6}$", purchase.PurchaseNo);
        Assert.Equal(600m, purchase.Taxable);
        Assert.Equal(72m, purchase.Tax);
        Assert.Equal(672m, purchase.Total);

        var batch = Assert.Single(await Batches(item.Id));
        Assert.Equal("NB-1", batch.BatchNo);
        Assert.Equal(12m, batch.QtyOnHand);
        Assert.Equal(50m, batch.PurchaseRate);
        Assert.Equal(110m, batch.Mrp);

        var refreshed = await api.Admin.GetJson<ItemDto>($"/api/items/{item.Id}");
        Assert.Equal(105m, refreshed.SaleRate);
        Assert.Equal(50m, refreshed.PurchaseRate);

        var balance = await api.Admin.GetJson<SupplierBalanceDto>($"/api/suppliers/{supplier.Id}/balance");
        Assert.Equal(672m, balance.Outstanding);
    }

    [Fact]
    public async Task Landed_cost_keeps_four_decimals()
    {
        // 2150 spread over 110 units = 19.5454… — must not round to 19.55 (overstates stock value).
        var supplier = await Supplier();
        var item = await Item(gst: 5);
        (await api.Admin.PostJson("/api/purchases",
            Purchase(supplier, Line(item, "LC1", 300, 100, 21.5m, free: 10, mrp: 33.5m, sale: 31)))).EnsureSuccessStatusCode();

        Assert.Equal(19.5455m, Assert.Single(await Batches(item.Id)).PurchaseRate);
        var valuation = await api.Admin.GetJson<StockValuationReport>($"/api/reports/stock-valuation?q={item.Name}");
        Assert.Equal(2150.01m, Assert.Single(valuation.Rows).PurchaseValue); // 110 × 19.5455
    }

    [Fact]
    public async Task Same_supplier_invoice_cannot_be_entered_twice()
    {
        var supplier = await Supplier();
        var item = await Item();
        var req = Purchase(supplier, Line(item, "D1", 300, 1, 10));
        (await api.Admin.PostJson("/api/purchases", req)).EnsureSuccessStatusCode();
        var again = await api.Admin.PostJson("/api/purchases", req);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already been entered", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Existing_batch_is_topped_up_only_with_matching_expiry()
    {
        var supplier = await Supplier();
        var item = await Item();
        (await api.Admin.PostJson("/api/purchases", Purchase(supplier, Line(item, "EX1", 300, 5, 40)))).EnsureSuccessStatusCode();

        var mismatch = await api.Admin.PostJson("/api/purchases", Purchase(supplier, Line(item, "EX1", 500, 5, 40)));
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Contains("already in stock with expiry", await mismatch.Content.ReadAsStringAsync());

        (await api.Admin.PostJson("/api/purchases", Purchase(supplier, Line(item, "EX1", 300, 7, 40)))).EnsureSuccessStatusCode();
        Assert.Equal(12m, Assert.Single(await Batches(item.Id)).QtyOnHand);
    }

    [Fact]
    public async Task Purchase_validation_and_untracked_items()
    {
        var supplier = await Supplier();
        var tracked = await Item();
        var expired = await api.Admin.PostJson("/api/purchases", Purchase(supplier, Line(tracked, "OLD", -1, 5, 40)));
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Contains("do not receive expired stock", await expired.Content.ReadAsStringAsync());

        var noBatch = await api.Admin.PostJson("/api/purchases", Purchase(supplier, Line(tracked, null, 0, 5, 40)));
        Assert.Equal(HttpStatusCode.BadRequest, noBatch.StatusCode);

        var aboveMrp = await api.Admin.PostJson("/api/purchases", Purchase(supplier, Line(tracked, "X", 300, 1, 40, mrp: 50, sale: 60)));
        Assert.Equal(HttpStatusCode.BadRequest, aboveMrp.StatusCode);

        var plain = await Item(batchTracked: false);
        (await api.Admin.PostJson("/api/purchases", Purchase(supplier, Line(plain, null, 0, 20, 30)))).EnsureSuccessStatusCode();
        var batch = Assert.Single(await Batches(plain.Id));
        Assert.Equal("-", batch.BatchNo);
        Assert.Equal(20m, batch.QtyOnHand);
    }

    [Fact]
    public async Task Cashier_cannot_receive_goods_or_adjust_stock()
    {
        var (cashier, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostJson("/api/purchases", new PurchaseRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostJson("/api/stock/adjustments", new StockAdjustmentRequest(1, -1, AdjustmentReason.Damaged, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/reports/expiry")).StatusCode);
    }

    [Fact]
    public async Task Cancel_purchase_reverses_stock_unless_already_sold()
    {
        var supplier = await Supplier();
        var item = await Item(gst: 0);
        var first = await (await api.Admin.PostJson("/api/purchases", Purchase(supplier, Line(item, "C1", 300, 10, 50)))).Read<PurchaseDto>();
        var cancelled = await (await api.Admin.PostJson($"/api/purchases/{first.Id}/cancel", new CancelPurchaseRequest("Wrong supplier")))
            .Read<PurchaseDto>();
        Assert.Equal(PurchaseStatus.Cancelled, cancelled.Status);
        Assert.Equal(0m, Assert.Single(await Batches(item.Id)).QtyOnHand);

        // After cancelling, the same invoice may be entered again.
        var second = await (await api.Admin.PostJson("/api/purchases",
            Purchase(supplier, Line(item, "C1", 300, 10, 50)) with { SupplierInvoiceNo = first.SupplierInvoiceNo })).Read<PurchaseDto>();

        var batchId = second.Lines[0].BatchId;
        (await api.Admin.PostJson("/api/sales", new SaleRequest
        {
            Lines = [new SaleLineRequest(item.Id, batchId, 3, 95, 0)], Payments = [new PaymentRequest(PaymentMode.Cash, 285)],
        })).EnsureSuccessStatusCode();

        var refused = await api.Admin.PostJson($"/api/purchases/{second.Id}/cancel", new CancelPurchaseRequest("oops"));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("only 7 of", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Adjustment_rules()
    {
        var supplier = await Supplier();
        var item = await Item();
        var purchase = await (await api.Admin.PostJson("/api/purchases", Purchase(supplier, Line(item, "AD1", 300, 10, 40)))).Read<PurchaseDto>();
        var batchId = purchase.Lines[0].BatchId;

        var damaged = await (await api.Admin.PostJson("/api/stock/adjustments",
            new StockAdjustmentRequest(batchId, -2, AdjustmentReason.Damaged, "Crushed strip"))).Read<StockAdjustmentDto>();
        Assert.StartsWith("A", damaged.AdjustmentNo);

        var tooMuch = await api.Admin.PostJson("/api/stock/adjustments", new StockAdjustmentRequest(batchId, -9, AdjustmentReason.Lost, null));
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Contains("Only 8", await tooMuch.Content.ReadAsStringAsync());

        var addByDamage = await api.Admin.PostJson("/api/stock/adjustments", new StockAdjustmentRequest(batchId, 1, AdjustmentReason.Damaged, null));
        Assert.Equal(HttpStatusCode.BadRequest, addByDamage.StatusCode);

        var countNoNote = await api.Admin.PostJson("/api/stock/adjustments", new StockAdjustmentRequest(batchId, 1, AdjustmentReason.CountCorrection, null));
        Assert.Equal(HttpStatusCode.BadRequest, countNoNote.StatusCode);

        // Supplier returns are debit notes now, not adjustments.
        var supplierReturn = await api.Admin.PostJson("/api/stock/adjustments",
            new StockAdjustmentRequest(batchId, -1, AdjustmentReason.ReturnToSupplier, "Near expiry", supplier.Id));
        Assert.Equal(HttpStatusCode.BadRequest, supplierReturn.StatusCode);
        Assert.Contains("Supplier returns", await supplierReturn.Content.ReadAsStringAsync());

        (await api.Admin.PostJson("/api/stock/adjustments",
            new StockAdjustmentRequest(batchId, 1, AdjustmentReason.CountCorrection, "Found in drawer"))).EnsureSuccessStatusCode();

        Assert.Equal(9m, Assert.Single(await Batches(item.Id)).QtyOnHand);
    }

    [Fact]
    public async Task Stock_history_shows_running_balance_newest_first()
    {
        var supplier = await Supplier();
        var item = await Item(gst: 0);
        var purchase = await (await api.Admin.PostJson("/api/purchases", Purchase(supplier, Line(item, "H1", 300, 10, 40)))).Read<PurchaseDto>();
        var batchId = purchase.Lines[0].BatchId;
        (await api.Admin.PostJson("/api/sales", new SaleRequest
        {
            Lines = [new SaleLineRequest(item.Id, batchId, 4, 95, 0)], Payments = [new PaymentRequest(PaymentMode.Cash, 380)],
        })).EnsureSuccessStatusCode();
        (await api.Admin.PostJson("/api/stock/adjustments",
            new StockAdjustmentRequest(batchId, -1, AdjustmentReason.Damaged, null))).EnsureSuccessStatusCode();

        var page1 = await api.Admin.GetJson<PagedResult<StockMovementDto>>($"/api/items/{item.Id}/movements?pageSize=2");
        Assert.Equal(3, page1.Total);
        Assert.Equal(["Adjustment", "Sale"], page1.Items.Select(m => m.Type));
        Assert.Equal([5m, 6m], page1.Items.Select(m => m.Balance));

        var page2 = await api.Admin.GetJson<PagedResult<StockMovementDto>>($"/api/items/{item.Id}/movements?pageSize=2&page=2");
        var first = Assert.Single(page2.Items);
        Assert.Equal("Purchase", first.Type);
        Assert.Equal(10m, first.Balance);
        Assert.Equal(purchase.PurchaseNo, first.RefNo);
    }

    [Fact]
    public async Task Reports_expiry_reorder_and_valuation()
    {
        var supplier = await Supplier();
        var item = await Item(gst: 0, reorder: 20);
        (await api.Admin.PostJson("/api/purchases", Purchase(supplier,
            Line(item, "SOON", 20, 4, 30, mrp: 100, sale: 90),
            Line(item, "LATER", 700, 6, 30, mrp: 100, sale: 90)))).EnsureSuccessStatusCode();

        var expiry = await api.Admin.GetJson<List<ExpiryReportRow>>("/api/reports/expiry?days=60");
        var soon = Assert.Single(expiry, r => r.ItemId == item.Id);
        Assert.Equal("SOON", soon.BatchNo);
        Assert.Equal(20, soon.DaysLeft);
        Assert.Equal(120m, soon.PurchaseValue);

        var reorder = await api.Admin.GetJson<List<ReorderReportRow>>("/api/reports/reorder");
        var row = Assert.Single(reorder, r => r.ItemId == item.Id);
        Assert.Equal(10m, row.Stock);
        Assert.Equal(30m, row.SuggestedQty);
        Assert.Equal(supplier.Name, row.LastSupplier);
        Assert.Equal(30m, row.LastPurchaseRate);

        var valuation = await api.Admin.GetJson<StockValuationReport>($"/api/reports/stock-valuation?q={item.Name}");
        var v = Assert.Single(valuation.Rows);
        Assert.Equal(10m, v.Qty);
        Assert.Equal(300m, v.PurchaseValue);
        Assert.Equal(1000m, v.MrpValue);
    }

    [Fact]
    public async Task Ede_import_matches_items_and_remembers_supplier_codes()
    {
        var supplier = await Supplier();
        var exp = Today.AddYears(1);
        var supplierCode = PosApiFixture.Unique("AMLX").ToUpperInvariant();
        var ede =
            "H,1,022IA10775,INV7781,,30102026,DIRECT,,,6112026,HAND TO HAND DELIVERY,,,PUNE,2,0,,,,,,,,,\n" +
            $"T,AML,ADITYA MEDISALES LTD,,{supplierCode},CONCOR 5 10T,S10,ADITYA MEDISALES LTD- DADRA,BS90799,{exp:ddMMyyyy},1,2,12,18.50,16.53,,24,0,0.64,,40,0,,,0\n" +
            "F,740,,,,,,88.8,0,,,,,,,,,,,,,,,0.2,829,\n";

        async Task<EdeImportDto> Import()
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(ede)), "file", "bill.csv");
            return await (await api.Admin.PostAsync($"/api/purchases/import-ede?supplierId={supplier.Id}", form)).Read<EdeImportDto>();
        }

        var first = await Import();
        Assert.Equal("INV7781", first.SupplierInvoiceNo);
        var line = Assert.Single(first.Lines);
        Assert.Null(line.MatchedItem);
        Assert.Equal(40m, line.Qty);
        Assert.Equal(2m, line.FreeQty);
        Assert.Equal(12m, line.GstRate);
        Assert.Contains(first.Warnings, w => w.Contains("need to be matched"));

        // The user maps the line to an item and posts; the code is remembered for this supplier.
        var item = await Item();
        (await api.Admin.PostJson("/api/purchases", new PurchaseRequest
        {
            SupplierId = supplier.Id, SupplierInvoiceNo = first.SupplierInvoiceNo, SupplierInvoiceDate = Today,
            Lines = [new PurchaseLineRequest
            {
                ItemId = item.Id, BatchNo = line.BatchNo, ExpiryDate = line.ExpiryDate, Qty = line.Qty, FreeQty = line.FreeQty,
                Rate = line.Rate, GstRate = line.GstRate, Mrp = line.Mrp, SaleRate = line.Mrp, SupplierItemCode = line.SupplierItemCode,
            }],
        })).EnsureSuccessStatusCode();

        var second = await Import();
        var matched = Assert.Single(second.Lines);
        Assert.Equal(item.Id, matched.MatchedItem?.Id);
        Assert.Equal("remembered code", matched.MatchedBy);
    }

    [Fact]
    public async Task Ede_import_rejects_other_files()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("C001001;DEMO;;\n")), "file", "master.csv");
        var res = await api.Admin.PostAsync("/api/purchases/import-ede", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("not an EDE", await res.Content.ReadAsStringAsync());
    }
}
