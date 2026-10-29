using System.Linq.Expressions;
using MargPos.Data;
using MargPos.Server.Auth;
using MargPos.Server.Infrastructure;
using MargPos.Shared;
using Microsoft.EntityFrameworkCore;

namespace MargPos.Server.Endpoints;

public static class ItemEndpoints
{
    private const string DuplicateMessage = "An item with this code or barcode already exists.";

    public static void MapItems(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/items");
        g.MapGet("/", List);
        g.MapGet("/{id:int}", Get);
        g.MapGet("/by-barcode/{barcode}", GetByBarcode);
        g.MapPost("/", Create).RequireAuthorization(Policies.Manager);
        g.MapPut("/{id:int}", Update).RequireAuthorization(Policies.Manager);
        g.MapDelete("/{id:int}", Deactivate).RequireAuthorization(Policies.Manager);

        g.MapGet("/{id:int}/batches", ListBatches);
        g.MapPost("/{id:int}/batches", CreateBatch).RequireAuthorization(Policies.Manager);
        app.MapPut("/api/batches/{id:int}", UpdateBatch).RequireAuthorization(Policies.Manager);
    }

    private static readonly Expression<Func<Item, ItemDto>> ToDto = i => new ItemDto
    {
        Id = i.Id,
        Code = i.Code,
        Name = i.Name,
        Barcode = i.Barcode,
        Manufacturer = i.Manufacturer,
        Category = i.Category,
        Pack = i.Pack,
        Unit = i.Unit,
        HsnCode = i.HsnCode,
        GstRate = i.GstRate,
        Mrp = i.Mrp,
        SaleRate = i.SaleRate,
        WholesaleRate = i.WholesaleRate,
        PurchaseRate = i.PurchaseRate,
        IsBatchTracked = i.IsBatchTracked,
        Schedule = i.Schedule,
        ReorderLevel = i.ReorderLevel,
        IsActive = i.IsActive,
        StockQty = i.Batches.Sum(b => b.QtyOnHand),
    };

    private static readonly Expression<Func<Batch, BatchDto>> ToBatchDto = b => new BatchDto
    {
        Id = b.Id,
        ItemId = b.ItemId,
        BatchNo = b.BatchNo,
        MfgDate = b.MfgDate,
        ExpiryDate = b.ExpiryDate,
        Mrp = b.Mrp,
        PurchaseRate = b.PurchaseRate,
        SaleRate = b.SaleRate,
        QtyOnHand = b.QtyOnHand,
    };

    private static async Task<PagedResult<ItemDto>> List(PosDbContext db, string? q, bool? lowStock,
        bool? includeInactive, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var query = db.Items.AsNoTracking();
        if (includeInactive != true) query = query.Where(i => i.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = DbHelpers.Contains(q);
            query = query.Where(i => EF.Functions.Like(i.Name, like)
                || EF.Functions.Like(i.Code, like)
                || (i.Barcode != null && EF.Functions.Like(i.Barcode, like))
                || (i.Manufacturer != null && EF.Functions.Like(i.Manufacturer, like)));
        }
        if (lowStock == true)
            query = query.Where(i => i.ReorderLevel > 0 && i.Batches.Sum(b => b.QtyOnHand) <= i.ReorderLevel);

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(i => i.Name).ThenBy(i => i.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(ToDto).ToListAsync(ct);
        return new PagedResult<ItemDto>(items, total, p, size);
    }

    private static async Task<IResult> Get(int id, PosDbContext db, CancellationToken ct) =>
        await FindDto(db, id, ct) is { } dto ? Results.Ok(dto) : DbHelpers.NotFound("Item");

    private static async Task<IResult> GetByBarcode(string barcode, PosDbContext db, CancellationToken ct)
    {
        var code = barcode.Trim();
        var dto = await db.Items.AsNoTracking()
            .Where(i => i.IsActive && (i.Barcode == code || i.Code == code))
            .OrderBy(i => i.Barcode == code ? 0 : 1)
            .Select(ToDto).FirstOrDefaultAsync(ct);
        return dto is null ? DbHelpers.NotFound("Item") : Results.Ok(dto);
    }

    private static async Task<IResult> Create(ItemUpsertRequest req, PosDbContext db, CancellationToken ct)
    {
        var errors = Validate(req).NonNegative(nameof(req.OpeningQty), req.OpeningQty);
        if (req.IsBatchTracked && req.OpeningQty != 0)
            errors.Add(nameof(req.OpeningQty), "Batch-tracked items take opening stock per batch.");
        if (errors.Any) return errors.ToResult();

        var item = new Item();
        Apply(item, req);
        item.Code = Errors.Clean(req.Code)?.ToUpperInvariant()
            ?? await DbHelpers.NextCodeAsync(db.Items.Select(i => i.Code), "I", ct);
        if (await IsDuplicate(db, item, ct)) return DbHelpers.Conflict(DuplicateMessage);

        if (!item.IsBatchTracked)
            item.Batches.Add(DefaultBatchFor(item, req.OpeningQty));
        db.Items.Add(item);
        if (await db.SaveOrConflictAsync(DuplicateMessage, ct) is { } conflict) return conflict;

        return Results.Created($"/api/items/{item.Id}", await FindDto(db, item.Id, ct));
    }

    private static async Task<IResult> Update(int id, ItemUpsertRequest req, PosDbContext db, CancellationToken ct)
    {
        var item = await db.Items.Include(i => i.Batches).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return DbHelpers.NotFound("Item");

        var errors = Validate(req);
        if (errors.Any) return errors.ToResult();

        if (item.IsBatchTracked != req.IsBatchTracked && item.Batches.Any(b => b.QtyOnHand != 0))
            return DbHelpers.Conflict("Batch tracking can only be changed while the item has no stock.");

        Apply(item, req);
        if (Errors.Clean(req.Code) is { } code) item.Code = code.ToUpperInvariant();
        if (await IsDuplicate(db, item, ct)) return DbHelpers.Conflict(DuplicateMessage);

        if (!item.IsBatchTracked)
        {
            var batch = item.Batches.FirstOrDefault(b => b.BatchNo == Batch.DefaultBatchNo);
            if (batch is null) item.Batches.Add(DefaultBatchFor(item, 0));
            else
            {
                batch.Mrp = item.Mrp;
                batch.SaleRate = item.SaleRate;
                batch.PurchaseRate = item.PurchaseRate;
            }
        }

        if (await db.SaveOrConflictAsync(DuplicateMessage, ct) is { } conflict) return conflict;
        return Results.Ok(await FindDto(db, id, ct));
    }

    private static async Task<IResult> Deactivate(int id, PosDbContext db, CancellationToken ct)
    {
        var item = await db.Items.FindAsync([id], ct);
        if (item is null) return DbHelpers.NotFound("Item");
        item.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListBatches(int id, bool? includeEmpty, PosDbContext db, CancellationToken ct)
    {
        if (!await db.Items.AnyAsync(i => i.Id == id, ct)) return DbHelpers.NotFound("Item");
        var query = db.Batches.AsNoTracking().Where(b => b.ItemId == id);
        if (includeEmpty != true) query = query.Where(b => b.QtyOnHand != 0);
        var batches = await query
            .OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.BatchNo)
            .Select(ToBatchDto).ToListAsync(ct);
        return Results.Ok(batches);
    }

    private static async Task<IResult> CreateBatch(int id, BatchUpsertRequest req, PosDbContext db, CancellationToken ct)
    {
        var item = await db.Items.FindAsync([id], ct);
        if (item is null) return DbHelpers.NotFound("Item");
        if (!item.IsBatchTracked)
            return DbHelpers.Conflict("This item is not batch-tracked; its stock is held on the item itself.");

        var batch = new Batch { ItemId = id, QtyOnHand = req.OpeningQty };
        var errors = ApplyBatch(batch, req, item).NonNegative(nameof(req.OpeningQty), req.OpeningQty);
        if (errors.Any) return errors.ToResult();

        const string dup = "This item already has a batch with that number.";
        if (await db.Batches.AnyAsync(b => b.ItemId == id && b.BatchNo == batch.BatchNo, ct))
            return DbHelpers.Conflict(dup);

        db.Batches.Add(batch);
        if (await db.SaveOrConflictAsync(dup, ct) is { } conflict) return conflict;
        return Results.Created($"/api/batches/{batch.Id}",
            await db.Batches.AsNoTracking().Where(b => b.Id == batch.Id).Select(ToBatchDto).FirstAsync(ct));
    }

    private static async Task<IResult> UpdateBatch(int id, BatchUpsertRequest req, PosDbContext db, CancellationToken ct)
    {
        var batch = await db.Batches.Include(b => b.Item).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return DbHelpers.NotFound("Batch");
        if (batch.BatchNo == Batch.DefaultBatchNo)
            return DbHelpers.Conflict("This is the item's default stock batch; edit the item instead.");

        var errors = ApplyBatch(batch, req, batch.Item);
        if (errors.Any) return errors.ToResult();

        const string dup = "This item already has a batch with that number.";
        if (await db.Batches.AnyAsync(b => b.ItemId == batch.ItemId && b.BatchNo == batch.BatchNo && b.Id != id, ct))
            return DbHelpers.Conflict(dup);

        if (await db.SaveOrConflictAsync(dup, ct) is { } conflict) return conflict;
        return Results.Ok(await db.Batches.AsNoTracking().Where(b => b.Id == id).Select(ToBatchDto).FirstAsync(ct));
    }

    private static Task<ItemDto?> FindDto(PosDbContext db, int id, CancellationToken ct) =>
        db.Items.AsNoTracking().Where(i => i.Id == id).Select(ToDto).FirstOrDefaultAsync(ct);

    private static Task<bool> IsDuplicate(PosDbContext db, Item item, CancellationToken ct) =>
        db.Items.AnyAsync(i => i.Id != item.Id
            && (i.Code == item.Code || (item.Barcode != null && i.Barcode == item.Barcode)), ct);

    private static Batch DefaultBatchFor(Item item, decimal qty) => new()
    {
        BatchNo = Batch.DefaultBatchNo,
        Mrp = item.Mrp,
        SaleRate = item.SaleRate,
        PurchaseRate = item.PurchaseRate,
        QtyOnHand = qty,
    };

    private static Errors Validate(ItemUpsertRequest req)
    {
        var e = new Errors()
            .Required(nameof(req.Name), req.Name, 200)
            .MaxLength(nameof(req.Code), req.Code, 30)
            .MaxLength(nameof(req.Barcode), req.Barcode, 50)
            .MaxLength(nameof(req.Manufacturer), req.Manufacturer, 100)
            .MaxLength(nameof(req.Category), req.Category, 100)
            .MaxLength(nameof(req.Pack), req.Pack, 30)
            .Required(nameof(req.Unit), req.Unit, 20)
            .NonNegative(nameof(req.Mrp), req.Mrp)
            .NonNegative(nameof(req.SaleRate), req.SaleRate)
            .NonNegative(nameof(req.WholesaleRate), req.WholesaleRate)
            .NonNegative(nameof(req.PurchaseRate), req.PurchaseRate)
            .NonNegative(nameof(req.ReorderLevel), req.ReorderLevel);

        if (Errors.Clean(req.Code) == Batch.DefaultBatchNo) e.Add(nameof(req.Code), "Code is not valid.");
        if (req.GstRate is < 0 or > 40) e.Add(nameof(req.GstRate), "GST rate must be between 0 and 40%.");
        if (Errors.Clean(req.HsnCode) is { } hsn && !Errors.HsnRegex().IsMatch(hsn))
            e.Add(nameof(req.HsnCode), "HSN code must be 4 to 8 digits.");
        if (!Enum.IsDefined(req.Schedule)) e.Add(nameof(req.Schedule), "Unknown drug schedule.");
        if (req.Mrp > 0)
        {
            if (req.SaleRate > req.Mrp) e.Add(nameof(req.SaleRate), "Sale rate cannot exceed MRP.");
            if (req.WholesaleRate > req.Mrp) e.Add(nameof(req.WholesaleRate), "Wholesale rate cannot exceed MRP.");
        }
        return e;
    }

    private static void Apply(Item item, ItemUpsertRequest req)
    {
        item.Name = req.Name.Trim();
        item.Barcode = Errors.Clean(req.Barcode);
        item.Manufacturer = Errors.Clean(req.Manufacturer);
        item.Category = Errors.Clean(req.Category);
        item.Pack = Errors.Clean(req.Pack);
        item.Unit = req.Unit.Trim().ToUpperInvariant();
        item.HsnCode = Errors.Clean(req.HsnCode);
        item.GstRate = req.GstRate;
        item.Mrp = req.Mrp;
        item.SaleRate = req.SaleRate;
        item.WholesaleRate = req.WholesaleRate;
        item.PurchaseRate = req.PurchaseRate;
        item.IsBatchTracked = req.IsBatchTracked;
        item.Schedule = req.Schedule;
        item.ReorderLevel = req.ReorderLevel;
        item.IsActive = req.IsActive;
    }

    /// <summary>Validates and applies batch fields; zero rates fall back to the item's rates.</summary>
    private static Errors ApplyBatch(Batch batch, BatchUpsertRequest req, Item item)
    {
        var e = new Errors()
            .Required(nameof(req.BatchNo), req.BatchNo, 30)
            .NonNegative(nameof(req.Mrp), req.Mrp)
            .NonNegative(nameof(req.SaleRate), req.SaleRate)
            .NonNegative(nameof(req.PurchaseRate), req.PurchaseRate);

        var batchNo = req.BatchNo?.Trim().ToUpperInvariant() ?? "";
        if (batchNo == Batch.DefaultBatchNo) e.Add(nameof(req.BatchNo), "Batch number is not valid.");
        if (req.ExpiryDate is null) e.Add(nameof(req.ExpiryDate), "Expiry date is required.");
        if (req.MfgDate is { } mfg && req.ExpiryDate is { } exp && mfg >= exp)
            e.Add(nameof(req.MfgDate), "Manufacturing date must be before expiry.");

        var mrp = req.Mrp > 0 ? req.Mrp : item.Mrp;
        var sale = req.SaleRate > 0 ? req.SaleRate : item.SaleRate;
        if (mrp > 0 && sale > mrp) e.Add(nameof(req.SaleRate), "Sale rate cannot exceed MRP.");
        if (e.Any) return e;

        batch.BatchNo = batchNo;
        batch.MfgDate = req.MfgDate;
        batch.ExpiryDate = req.ExpiryDate;
        batch.Mrp = mrp;
        batch.SaleRate = sale;
        batch.PurchaseRate = req.PurchaseRate > 0 ? req.PurchaseRate : item.PurchaseRate;
        return e;
    }
}
