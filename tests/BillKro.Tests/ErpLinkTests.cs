using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BillKro.Shared;
using BillKro.Sync.Parser;

namespace BillKro.Tests;

/// <summary>Phase 6: purchases out to MARG as EDE files, masters in from MARG, sales register out.</summary>
[Collection(PosApiCollection.Name)]
public class ErpLinkTests(PosApiFixture api) : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private readonly string _folder = Directory.CreateTempSubdirectory("billkro-method-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private async Task UseFolder(string folder) =>
        (await api.Admin.PutJson("/api/erp/settings", new ErpLinkSettingsDto
        {
            Enabled = true, MethodFolder = folder, CompanyCode = "POS", SendFrom = Today, PollMinutes = 5,
        })).EnsureSuccessStatusCode();

    private async Task<SupplierDto> Supplier() =>
        await (await api.Admin.PostJson("/api/suppliers", new SupplierUpsertRequest { Name = PosApiFixture.Unique("Dist") })).Read<SupplierDto>();

    private async Task<ItemDto> Item() =>
        await (await api.Admin.PostJson("/api/items", new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Erp"), Unit = "STRIP", Mrp = 100, SaleRate = 95, GstRate = 12, IsBatchTracked = true,
        })).Read<ItemDto>();

    private async Task<PurchaseDto> Receive(SupplierDto s, ItemDto item) =>
        await (await api.Admin.PostJson("/api/purchases", new PurchaseRequest
        {
            SupplierId = s.Id, SupplierInvoiceNo = PosApiFixture.Unique("INV"), SupplierInvoiceDate = Today,
            Lines = [new PurchaseLineRequest
            {
                ItemId = item.Id, BatchNo = "MG1", ExpiryDate = Today.AddDays(400), Qty = 10, FreeQty = 2, Rate = 50,
                DiscountPercent = 5, Mrp = 100, SaleRate = 95,
            }],
        })).Read<PurchaseDto>();

    private static string FileFor(PurchaseDto p) => $"POS_{string.Concat(p.PurchaseNo.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '_'))}.csv";

    private async Task<ErpLinkStatusDto> Status() => await api.Admin.GetJson<ErpLinkStatusDto>("/api/erp/status");

    [Fact]
    public async Task Purchase_is_written_once_as_an_ede_file_erp_can_read()
    {
        await UseFolder(_folder);
        var s = await Supplier();
        var item = await Item();
        var purchase = await Receive(s, item);

        var result = await (await api.Admin.PostAsync("/api/erp/send-now", null)).Read<ErpSendResult>();
        Assert.Null(result.Error);
        Assert.True(result.Sent >= 1);

        var path = Path.Combine(_folder, FileFor(purchase));
        var bill = Assert.Single(await new EdeParser().ParseAsync(path));
        Assert.Equal(purchase.SupplierInvoiceNo, bill.BillNumber);
        Assert.Equal(Today, bill.BillDate);
        Assert.Equal(purchase.Total, bill.NetAmount);
        var line = Assert.Single(bill.Lines);
        Assert.Equal(item.Code, line.ItemCode);
        Assert.Equal((10m, 2m, 50m, 5m, 100m, 12m), (line.Quantity, line.FreeQuantity, line.PurchaseRate, line.Discount, line.MRP, line.TaxRate));
        Assert.Equal("MG1", line.BatchNo);

        // MARG moves or deletes the files it imports; a later run must not write the purchase again.
        File.Delete(path);
        await (await api.Admin.PostAsync("/api/erp/send-now", null)).Read<ErpSendResult>();
        Assert.False(File.Exists(path));

        var status = await Status();
        Assert.Equal(0, status.PendingPurchases);
        var sent = Assert.Single(status.RecentExports, x => x.DocumentNo == purchase.PurchaseNo);
        Assert.Equal(ErpExportStatus.Sent, sent.Status);
    }

    [Fact]
    public async Task Missing_folder_is_reported_and_purchases_stay_pending()
    {
        await UseFolder(Path.Combine(_folder, "not-there"));
        await Receive(await Supplier(), await Item());

        var result = await (await api.Admin.PostAsync("/api/erp/send-now", null)).Read<ErpSendResult>();
        Assert.Equal(0, result.Sent);
        Assert.Contains("cannot see", result.Error);

        var status = await Status();
        Assert.True(status.PendingPurchases >= 1);
        Assert.Contains("cannot see", status.LastError);

        Assert.False((await (await api.Admin.PostJson("/api/erp/test-folder", new { methodFolder = Path.Combine(_folder, "x") })).Read<ErpFolderCheck>()).Ok);
        Assert.True((await (await api.Admin.PostJson("/api/erp/test-folder", new { methodFolder = _folder })).Read<ErpFolderCheck>()).Ok);
    }

    [Fact]
    public async Task Cancelled_after_sending_and_debit_notes_become_manual_tasks()
    {
        await UseFolder(_folder);
        var s = await Supplier();
        var item = await Item();
        var cancelledLater = await Receive(s, item);
        var kept = await Receive(s, item);
        await (await api.Admin.PostAsync("/api/erp/send-now", null)).Read<ErpSendResult>();

        (await api.Admin.PostJson($"/api/purchases/{cancelledLater.Id}/cancel", new CancelPurchaseRequest("wrong supplier")))
            .EnsureSuccessStatusCode();
        var debit = await (await api.Admin.PostJson("/api/purchase-returns", new PurchaseReturnRequest
        {
            SupplierId = s.Id, Reason = "Breakage", Lines = [new PurchaseReturnLineRequest { BatchId = kept.Lines[0].BatchId, Qty = 1 }],
        })).Read<PurchaseReturnDto>();

        var status = await Status();
        var cancelTask = Assert.Single(status.ManualTasks, t => t.DocumentNo == cancelledLater.PurchaseNo);
        Assert.Contains(cancelledLater.SupplierInvoiceNo, cancelTask.Action);
        Assert.Contains(status.ManualTasks, t => t.DocumentNo == debit.ReturnNo && t.Kind == "DebitNote");
        Assert.True(Assert.Single(status.RecentExports, x => x.DocumentNo == cancelledLater.PurchaseNo).CancelledAfterSend);

        (await api.Admin.PostAsync($"/api/erp/manual/PurchaseCancel/{cancelledLater.Id}/done", null)).EnsureSuccessStatusCode();
        (await api.Admin.PostAsync($"/api/erp/manual/DebitNote/{debit.Id}/done", null)).EnsureSuccessStatusCode();
        status = await Status();
        Assert.DoesNotContain(status.ManualTasks, t => t.DocumentNo == cancelledLater.PurchaseNo || t.DocumentNo == debit.ReturnNo);

        Assert.Equal(HttpStatusCode.NotFound, (await api.Admin.PostAsync($"/api/erp/manual/PurchaseCancel/{kept.Id}/done", null)).StatusCode);
    }

    [Fact]
    public async Task Settings_are_validated_and_admin_only()
    {
        var noDate = await api.Admin.PutJson("/api/erp/settings", new ErpLinkSettingsDto { Enabled = true, MethodFolder = _folder });
        Assert.Equal(HttpStatusCode.BadRequest, noDate.StatusCode);
        Assert.Contains("SendFrom", await noDate.Content.ReadAsStringAsync());

        var (manager, _) = await api.CreateUserClientAsync(UserRole.Manager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutJson("/api/erp/settings", new ErpLinkSettingsDto())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/erp/status")).StatusCode);

        var (cashier, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/erp/status")).StatusCode);
    }

    // ---------------------------------------------------------------- masters

    /// <summary>An item row (T0001) laid out the way <see cref="MasterDataParser"/> reads MARG's export.</summary>
    private static string ItemRow(string code, string name, decimal rate, decimal qty, string? batch, string? expiry, decimal gst, decimal mrp)
    {
        var f = new string[30];
        Array.Fill(f, "");
        (f[0], f[1], f[5], f[7], f[16], f[19], f[24], f[28]) = (code, name, $"{rate}", $"{qty}", batch ?? "", expiry ?? "", $"{gst}", $"{mrp}");
        return "T0001" + string.Join(';', f);
    }

    private static string PartyRow(string name, string type, string code, decimal balance) =>
        $"H001{name};{type};{code};01-04-2026;;;{balance};Dr;";

    private static MultipartFormDataContent Upload(string text)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", "master.csv" } };
    }

    [Fact]
    public async Task Master_import_creates_items_with_opening_lots_and_reimport_keeps_stock()
    {
        var tracked = PosApiFixture.Unique("MT").ToUpperInvariant();
        var loose = PosApiFixture.Unique("ML").ToUpperInvariant();
        var party = PosApiFixture.Unique("Sharma Medical");
        string File(string trackedName, decimal qty) => string.Join("\r\n",
            "C001001;Demo Pharma;;Pune;",
            PartyRow(party, "C", PosApiFixture.Unique("AC"), 1500),
            ItemRow(tracked, trackedName, 40, qty, "B1", "31-12-2028", 12, 50),
            ItemRow(tracked, trackedName, 42, 5, "B1", "31-12-2028", 12, 55),
            ItemRow(loose, "Cotton Roll", 20, 7, null, null, 5, 30));

        var preview = await (await api.Admin.PostAsync("/api/erp/master/preview", Upload(File("Dolo 650", 10)))).Read<ErpMasterPreview>();
        Assert.Equal("Demo Pharma", preview.CompanyName);
        Assert.Equal(3, preview.Items.Count);
        Assert.Equal(3, preview.NewItems);
        var p = Assert.Single(preview.Parties);
        Assert.Equal((party, "C", 1500m, false), (p.Name, p.Type, p.OpeningBalance, p.ExistsAsCustomer));

        var result = await (await api.Admin.PostAsync($"/api/erp/master/apply?parties={ErpPartyImport.Customers}",
            Upload(File("Dolo 650", 10)))).Read<ErpMasterImportResult>();
        Assert.Equal((2, 0, 2, 1, 0), (result.ItemsCreated, result.ItemsUpdated, result.LotsCreated, result.PartiesCreated, result.PartiesUpdated));

        var item = (await api.Admin.GetJson<PagedResult<ItemDto>>($"/api/items?q={tracked}")).Items.Single();
        Assert.True(item.IsBatchTracked);
        Assert.Equal(15m, item.StockQty);
        var lots = (await api.Admin.GetJson<List<BatchDto>>($"/api/items/{item.Id}/batches")).ToDictionary(b => b.BatchNo);
        Assert.Equal((10m, 50m, new DateOnly(2028, 12, 31)), (lots["B1"].QtyOnHand, lots["B1"].Mrp, lots["B1"].ExpiryDate));
        Assert.Equal((5m, 55m), (lots["B1/2"].QtyOnHand, lots["B1/2"].Mrp));

        var cotton = (await api.Admin.GetJson<PagedResult<ItemDto>>($"/api/items?q={loose}")).Items.Single();
        Assert.Equal((false, 7m, 30m, 5m), (cotton.IsBatchTracked, cotton.StockQty, cotton.SaleRate, cotton.GstRate));

        var customer = (await api.Admin.GetJson<PagedResult<CustomerDto>>($"/api/customers?q={Uri.EscapeDataString(party)}")).Items.Single();
        Assert.Equal(1500m, customer.OpeningBalance);

        // MARG's stock has moved on since; the POS keeps its own stock and only refreshes names and prices.
        var again = await (await api.Admin.PostAsync($"/api/erp/master/apply?parties={ErpPartyImport.Customers}",
            Upload(File("Dolo 650 Tab", 99)))).Read<ErpMasterImportResult>();
        Assert.Equal((0, 2, 0, 0, 1), (again.ItemsCreated, again.ItemsUpdated, again.LotsCreated, again.PartiesCreated, again.PartiesUpdated));
        item = (await api.Admin.GetJson<PagedResult<ItemDto>>($"/api/items?q={tracked}")).Items.Single();
        Assert.Equal(("Dolo 650 Tab", 15m), (item.Name, item.StockQty));
    }

    [Fact]
    public async Task Master_import_rejects_other_files()
    {
        var res = await api.Admin.PostAsync("/api/erp/master/preview", Upload("hello,world\r\n1,2"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ---------------------------------------------------------------- sales register

    [Fact]
    public async Task Sales_register_lists_completed_sale_lines_with_payment_modes()
    {
        var item = await (await api.Admin.PostJson("/api/items", new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Reg"), Unit = "PCS", Mrp = 100, SaleRate = 100, GstRate = 18, OpeningQty = 10,
        })).Read<ItemDto>();
        var batchId = (await api.Admin.GetJson<List<SellableBatchDto>>($"/api/items/{item.Id}/sellable-batches"))[0].Id;
        async Task<JsonElement> Sell(decimal qty, params PaymentRequest[] payments)
        {
            var res = await api.Admin.PostJson("/api/sales", new SaleRequest
            {
                Lines = [new SaleLineRequest(item.Id, batchId, qty, 100, 0)], Payments = [.. payments],
            });
            return await res.Read<JsonElement>();
        }

        var split = (await Sell(2, new PaymentRequest(PaymentMode.Upi, 150), new PaymentRequest(PaymentMode.Cash, 50)))
            .GetProperty("invoiceNo").GetString();
        var cancelled = await Sell(1, new PaymentRequest(PaymentMode.Cash, 100));
        (await api.Admin.PostJson($"/api/sales/{cancelled.GetProperty("id").GetInt32()}/cancel", new CancelSaleRequest("test")))
            .EnsureSuccessStatusCode();

        var rows = await api.Admin.GetJson<List<SalesRegisterRow>>($"/api/erp/sales-register?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        var row = Assert.Single(rows, r => r.ItemCode == item.Code);
        Assert.Equal((split, 2m, 200m, "Cash+Upi", ""), (row.InvoiceNo, row.Qty, row.Amount, row.PaymentModes, row.BatchNo));
        Assert.Equal(200m, row.Taxable + row.Tax);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await api.Admin.GetAsync($"/api/erp/sales-register?from={Today:yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}")).StatusCode);
    }
}
