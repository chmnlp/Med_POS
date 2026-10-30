using System.Net;
using MargPos.Shared;

namespace MargPos.Tests;

[Collection(PosApiCollection.Name)]
public class BarcodeTests(PosApiFixture api)
{
    private static readonly DateOnly NextYear = DateOnly.FromDateTime(DateTime.Today).AddYears(1);

    private async Task<ItemDto> CreateItem(bool batchTracked, string? barcode = null, decimal openingQty = 0) =>
        await (await api.Admin.PostJson("/api/items", new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Label item"), Unit = "PCS", Mrp = 50, SaleRate = 45, Pack = "10 TAB",
            IsBatchTracked = batchTracked, Barcode = barcode, OpeningQty = openingQty,
        })).Read<ItemDto>();

    private async Task<BatchDto> CreateBatch(ItemDto item, string batchNo, int qty, string? barcode = null) =>
        await (await api.Admin.PostJson($"/api/items/{item.Id}/batches", new BatchUpsertRequest
        {
            BatchNo = batchNo, ExpiryDate = NextYear, Mrp = 50, SaleRate = 45, OpeningQty = qty, Barcode = barcode,
        })).Read<BatchDto>();

    [Fact]
    public async Task Labels_give_each_lot_its_own_barcode_and_scanning_it_picks_that_lot()
    {
        var item = await CreateItem(batchTracked: true, barcode: PosApiFixture.Unique("890"));
        var first = await CreateBatch(item, "LA1", 10);
        var second = await CreateBatch(item, "LA2", 4);
        await CreateBatch(item, "LA3", 0); // empty lots are left out of whole-item labels

        var labels = await (await api.Admin.PostJson("/api/labels", new LabelRequest { ItemIds = [item.Id] })).Read<List<LabelDto>>();
        Assert.Equal([first.Id, second.Id], labels.Select(l => l.BatchId));
        Assert.All(labels, l => Assert.Matches("^29[0-9]{6,}$", l.Barcode));
        Assert.Equal("LA1", labels[0].BatchNo);
        Assert.Equal(10, labels[0].QtyOnHand);
        Assert.Equal(NextYear, labels[0].ExpiryDate);

        // Printing again keeps the same codes.
        var again = await (await api.Admin.PostJson("/api/labels", new LabelRequest { BatchIds = [second.Id] })).Read<List<LabelDto>>();
        Assert.Equal(labels[1].Barcode, Assert.Single(again).Barcode);

        var scan = await api.Admin.GetJson<ScanResultDto>($"/api/items/scan/{labels[1].Barcode}");
        Assert.Equal(item.Id, scan.Item.Id);
        Assert.Equal(second.Id, scan.BatchId);
        Assert.Equal("LA2", scan.BatchNo);

        // The pack's own barcode still finds the item, without choosing a lot.
        var byItem = await api.Admin.GetJson<ScanResultDto>($"/api/items/scan/{item.Barcode}");
        Assert.Equal(item.Id, byItem.Item.Id);
        Assert.Null(byItem.BatchId);

        var batches = await api.Admin.GetJson<List<BatchDto>>($"/api/items/{item.Id}/batches");
        Assert.Equal(labels[0].Barcode, batches.Single(b => b.Id == first.Id).Barcode);
    }

    [Fact]
    public async Task Plain_items_label_with_their_barcode_or_code()
    {
        var withBarcode = await CreateItem(batchTracked: false, barcode: PosApiFixture.Unique("890"), openingQty: 6);
        var withoutBarcode = await CreateItem(batchTracked: false); // no stock: still gets a label

        var labels = await (await api.Admin.PostJson("/api/labels",
            new LabelRequest { ItemIds = [withBarcode.Id, withoutBarcode.Id] })).Read<List<LabelDto>>();

        Assert.Equal(withBarcode.Barcode, labels.Single(l => l.ItemId == withBarcode.Id).Barcode);
        var plain = labels.Single(l => l.ItemId == withoutBarcode.Id);
        Assert.Equal(withoutBarcode.Code, plain.Barcode);
        Assert.Null(plain.BatchNo);

        var scan = await api.Admin.GetJson<ScanResultDto>($"/api/items/scan/{withoutBarcode.Code}");
        Assert.Equal(withoutBarcode.Id, scan.Item.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Admin.GetAsync($"/api/items/scan/{PosApiFixture.Unique("none")}")).StatusCode);
    }

    [Fact]
    public async Task A_barcode_belongs_to_one_item_or_lot_only()
    {
        var item = await CreateItem(batchTracked: true);
        var lotCode = PosApiFixture.Unique("LOT");
        var lot = await CreateBatch(item, "LB1", 3, lotCode);
        Assert.Equal(lotCode, lot.Barcode);

        // Another item may not take a lot's barcode, and another lot may not take an item's.
        var other = await CreateItem(batchTracked: true, barcode: PosApiFixture.Unique("890"));
        Assert.Equal(HttpStatusCode.Conflict,
            (await api.Admin.PutJson($"/api/items/{other.Id}/barcode", new SetBarcodeRequest(lotCode))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await api.Admin.PostJson($"/api/items/{item.Id}/batches", new BatchUpsertRequest
        {
            BatchNo = "LB2", ExpiryDate = NextYear, Barcode = other.Barcode,
        })).StatusCode);

        // Editing a lot keeps its barcode when it is sent back unchanged.
        var edited = await (await api.Admin.PutJson($"/api/batches/{lot.Id}", new BatchUpsertRequest
        {
            BatchNo = "LB1", ExpiryDate = NextYear.AddMonths(1), Mrp = 50, SaleRate = 44, Barcode = lotCode,
        })).Read<BatchDto>();
        Assert.Equal(lotCode, edited.Barcode);
    }

    [Fact]
    public async Task Barcodes_can_be_set_one_by_one_from_the_missing_list()
    {
        var item = await CreateItem(batchTracked: false);
        var missing = await api.Admin.GetJson<PagedResult<ItemDto>>($"/api/items?missingBarcode=true&q={item.Name}");
        Assert.Contains(missing.Items, i => i.Id == item.Id);

        var code = PosApiFixture.Unique("890");
        var saved = await (await api.Admin.PutJson($"/api/items/{item.Id}/barcode", new SetBarcodeRequest($" {code} "))).Read<ItemDto>();
        Assert.Equal(code, saved.Barcode);

        missing = await api.Admin.GetJson<PagedResult<ItemDto>>($"/api/items?missingBarcode=true&q={item.Name}");
        Assert.DoesNotContain(missing.Items, i => i.Id == item.Id);

        var (cashier, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PutJson($"/api/items/{item.Id}/barcode", new SetBarcodeRequest("123"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostJson("/api/labels", new LabelRequest { ItemIds = [item.Id] })).StatusCode);
    }
}
