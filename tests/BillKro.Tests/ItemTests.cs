using System.Net;
using BillKro.Shared;

namespace BillKro.Tests;

[Collection(PosApiCollection.Name)]
public class ItemTests(PosApiFixture api)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private Task<HttpResponseMessage> CreateItem(ItemUpsertRequest req) => api.Admin.PostJson("/api/items", req);

    [Fact]
    public async Task Plain_item_gets_auto_code_and_opening_stock()
    {
        var item = await (await CreateItem(new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Soap"), Unit = "pcs", Mrp = 40, SaleRate = 38, GstRate = 18, OpeningQty = 25,
        })).Read<ItemDto>();

        Assert.StartsWith("I", item.Code);
        Assert.Equal("PCS", item.Unit);
        Assert.Equal(25, item.StockQty);

        var batches = await api.Admin.GetJson<List<BatchDto>>($"/api/items/{item.Id}/batches");
        var batch = Assert.Single(batches);
        Assert.Equal(25, batch.QtyOnHand);
        Assert.Equal(38, batch.SaleRate);
    }

    [Fact]
    public async Task Item_validation_rules()
    {
        var res = await CreateItem(new ItemUpsertRequest
        {
            Name = "", Unit = "PCS", Mrp = 100, SaleRate = 120, GstRate = 50, HsnCode = "12AB",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Name", body);
        Assert.Contains("Sale rate cannot exceed MRP", body);
        Assert.Contains("GstRate", body);
        Assert.Contains("HsnCode", body);
    }

    [Fact]
    public async Task Duplicate_barcode_is_a_conflict()
    {
        var barcode = PosApiFixture.Unique("890");
        (await CreateItem(new ItemUpsertRequest { Name = "A", Unit = "PCS", Barcode = barcode })).EnsureSuccessStatusCode();
        var res = await CreateItem(new ItemUpsertRequest { Name = "B", Unit = "PCS", Barcode = barcode });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Batch_tracked_item_holds_stock_per_batch_in_expiry_order()
    {
        var item = await (await CreateItem(new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Paracetamol 500"), Unit = "STRIP", Mrp = 30, SaleRate = 28,
            GstRate = 5, IsBatchTracked = true, Schedule = DrugSchedule.H,
        })).Read<ItemDto>();
        Assert.Equal(0, item.StockQty);

        var later = await (await api.Admin.PostJson($"/api/items/{item.Id}/batches", new BatchUpsertRequest
        {
            BatchNo = "b-late", ExpiryDate = Today.AddMonths(18), OpeningQty = 10,
        })).Read<BatchDto>();
        Assert.Equal("B-LATE", later.BatchNo);
        Assert.Equal(30, later.Mrp); // falls back to item MRP

        (await api.Admin.PostJson($"/api/items/{item.Id}/batches", new BatchUpsertRequest
        {
            BatchNo = "B-SOON", ExpiryDate = Today.AddMonths(2), OpeningQty = 5, Mrp = 32, SaleRate = 30,
        })).EnsureSuccessStatusCode();

        var batches = await api.Admin.GetJson<List<BatchDto>>($"/api/items/{item.Id}/batches");
        Assert.Equal(["B-SOON", "B-LATE"], batches.Select(b => b.BatchNo));

        var refreshed = await api.Admin.GetJson<ItemDto>($"/api/items/{item.Id}");
        Assert.Equal(15, refreshed.StockQty);

        // Same batch number again is rejected.
        var dup = await api.Admin.PostJson($"/api/items/{item.Id}/batches",
            new BatchUpsertRequest { BatchNo = "B-SOON", ExpiryDate = Today.AddMonths(3) });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        // Batch tracking cannot be switched off while stock exists.
        var toggle = await api.Admin.PutJson($"/api/items/{item.Id}", new ItemUpsertRequest
        {
            Name = item.Name, Unit = item.Unit, Mrp = 30, SaleRate = 28, IsBatchTracked = false,
        });
        Assert.Equal(HttpStatusCode.Conflict, toggle.StatusCode);
    }

    [Fact]
    public async Task Batch_rules()
    {
        var tracked = await (await CreateItem(new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Syrup"), Unit = "BTL", Mrp = 100, IsBatchTracked = true,
        })).Read<ItemDto>();

        var noExpiry = await api.Admin.PostJson($"/api/items/{tracked.Id}/batches",
            new BatchUpsertRequest { BatchNo = "X1", SaleRate = 150 });
        Assert.Equal(HttpStatusCode.BadRequest, noExpiry.StatusCode);
        var body = await noExpiry.Content.ReadAsStringAsync();
        Assert.Contains("ExpiryDate", body);
        Assert.Contains("Sale rate cannot exceed MRP", body);

        var openingOnItem = await CreateItem(new ItemUpsertRequest
        {
            Name = "Bad", Unit = "PCS", IsBatchTracked = true, OpeningQty = 5,
        });
        Assert.Equal(HttpStatusCode.BadRequest, openingOnItem.StatusCode);

        var plain = await (await CreateItem(new ItemUpsertRequest { Name = PosApiFixture.Unique("Pen"), Unit = "PCS" })).Read<ItemDto>();
        var batchOnPlain = await api.Admin.PostJson($"/api/items/{plain.Id}/batches",
            new BatchUpsertRequest { BatchNo = "Z", ExpiryDate = Today.AddYears(1) });
        Assert.Equal(HttpStatusCode.Conflict, batchOnPlain.StatusCode);
    }

    [Fact]
    public async Task Search_and_barcode_lookup()
    {
        var name = PosApiFixture.Unique("Crocin");
        var barcode = PosApiFixture.Unique("890");
        var item = await (await CreateItem(new ItemUpsertRequest
        {
            Name = name, Unit = "STRIP", Barcode = barcode, Manufacturer = "GSK",
        })).Read<ItemDto>();

        var byName = await api.Admin.GetJson<PagedResult<ItemDto>>($"/api/items?q={name[..10].ToLowerInvariant()}");
        Assert.Contains(byName.Items, i => i.Id == item.Id);

        var byCode = await api.Admin.GetJson<ItemDto>($"/api/items/by-barcode/{barcode}");
        Assert.Equal(item.Id, byCode.Id);

        // A generated code can be typed as its plain number: I00042 → "42", in the list (first) and the Enter lookup.
        var number = int.Parse(item.Code[1..]).ToString();
        var byNumber = await api.Admin.GetJson<PagedResult<ItemDto>>($"/api/items?q={number}");
        Assert.Equal(item.Id, byNumber.Items[0].Id);
        Assert.Equal(item.Id, (await api.Admin.GetJson<ItemDto>($"/api/items/by-barcode/{number}")).Id);

        // Wildcards in the search box are treated literally.
        var wildcard = await api.Admin.GetJson<PagedResult<ItemDto>>("/api/items?q=%25%25%25");
        Assert.DoesNotContain(wildcard.Items, i => i.Id == item.Id);

        (await api.Admin.DeleteAsync($"/api/items/{item.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await api.Admin.GetAsync($"/api/items/by-barcode/{barcode}")).StatusCode);
    }

    [Fact]
    public async Task Low_stock_filter_and_dashboard()
    {
        var before = await api.Admin.GetJson<DashboardSummary>("/api/dashboard/summary");

        var low = await (await CreateItem(new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Low"), Unit = "PCS", ReorderLevel = 10, OpeningQty = 3,
        })).Read<ItemDto>();
        await CreateItem(new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Ok"), Unit = "PCS", ReorderLevel = 10, OpeningQty = 50,
        });
        var expiring = await (await CreateItem(new ItemUpsertRequest
        {
            Name = PosApiFixture.Unique("Exp"), Unit = "PCS", IsBatchTracked = true,
        })).Read<ItemDto>();
        (await api.Admin.PostJson($"/api/items/{expiring.Id}/batches",
            new BatchUpsertRequest { BatchNo = "E1", ExpiryDate = Today.AddDays(30), OpeningQty = 4 })).EnsureSuccessStatusCode();

        var lowList = await api.Admin.GetJson<PagedResult<ItemDto>>("/api/items?lowStock=true&pageSize=200");
        Assert.Contains(lowList.Items, i => i.Id == low.Id);
        Assert.All(lowList.Items, i => Assert.True(i.StockQty <= i.ReorderLevel));

        var after = await api.Admin.GetJson<DashboardSummary>("/api/dashboard/summary");
        Assert.Equal(before.ActiveItems + 3, after.ActiveItems);
        Assert.Equal(before.LowStockItems + 1, after.LowStockItems);
        Assert.Equal(before.NearExpiryBatches + 1, after.NearExpiryBatches);
    }
}
