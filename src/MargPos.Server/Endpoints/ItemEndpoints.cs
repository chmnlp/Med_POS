using System.Linq.Expressions;
using System.Security.Claims;
using MargPos.Data;
using MargPos.Server.Auth;
using MargPos.Server.Infrastructure;
using MargPos.Shared;
using Microsoft.EntityFrameworkCore;

namespace MargPos.Server.Endpoints;

public static class ItemEndpoints
{
    private const string DuplicateMessage = "An item with this code or barcode already exists.";
    private const string BarcodeTakenMessage = "This barcode is already used by another item or batch.";

    public static void MapItems(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/items");
        g.MapGet("/", List);
        g.MapGet("/{id:int}", Get);
        g.MapGet("/by-barcode/{barcode}", GetByBarcode);
        g.MapGet("/scan/{code}", Scan);
        g.MapPost("/", Create).RequireAuthorization(Policies.Manager);
        g.MapPut("/{id:int}", Update).RequireAuthorization(Policies.Manager);
        g.MapPut("/{id:int}/barcode", SetBarcode).RequireAuthorization(Policies.Manager);
        g.MapDelete("/{id:int}", Deactivate).RequireAuthorization(Policies.Manager);

        g.MapGet("/{id:int}/batches", ListBatches);
        g.MapGet("/{id:int}/sellable-batches", SellableBatches);
        g.MapPost("/{id:int}/batches", CreateBatch).RequireAuthorization(Policies.Manager);
        app.MapPut("/api/batches/{id:int}", UpdateBatch).RequireAuthorization(Policies.Manager);
        app.MapPost("/api/labels", Labels).RequireAuthorization(Policies.Manager);
    }

    internal static readonly Expression<Func<Item, ItemDto>> ToDto = i => new ItemDto
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
        HospitalRate = i.HospitalRate,
        PurchaseRate = i.PurchaseRate,
        MarginPercent = i.MarginPercent,
        WholesaleMarginPercent = i.WholesaleMarginPercent,
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
        WholesaleRate = b.WholesaleRate,
        HospitalRate = b.HospitalRate,
        QtyOnHand = b.QtyOnHand,
        Barcode = b.Barcode,
    };

    private static async Task<PagedResult<ItemDto>> List(PosDbContext db, string? q, bool? lowStock,
        bool? includeInactive, bool? missingBarcode, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var query = db.Items.AsNoTracking();
        if (includeInactive != true) query = query.Where(i => i.IsActive);
        if (missingBarcode == true) query = query.Where(i => i.Barcode == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = DbHelpers.Contains(q);
            var shortCode = ShortCode(q);
            query = query.Where(i => EF.Functions.Like(i.Name, like)
                || i.Code == shortCode
                || EF.Functions.Like(i.Code, like)
                || (i.Barcode != null && EF.Functions.Like(i.Barcode, like))
                || (i.Manufacturer != null && EF.Functions.Like(i.Manufacturer, like)));
        }
        if (lowStock == true)
            query = query.Where(i => i.ReorderLevel > 0 && i.Batches.Sum(b => b.QtyOnHand) <= i.ReorderLevel);

        var total = await query.CountAsync(ct);
        // An exact code or barcode hit goes first, ahead of names that merely contain the text.
        var exact = ShortCode(q) ?? q?.Trim();
        var items = await query.OrderBy(i => i.Code == exact || i.Barcode == exact ? 0 : 1).ThenBy(i => i.Name).ThenBy(i => i.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(ToDto).ToListAsync(ct);
        return new PagedResult<ItemDto>(items, total, p, size);
    }

    private static async Task<IResult> Get(int id, PosDbContext db, CancellationToken ct) =>
        await FindDto(db, id, ct) is { } dto ? Results.Ok(dto) : DbHelpers.NotFound("Item");

    /// <summary>A plain number typed at the counter ("10") as the generated item code it stands for ("I00010").</summary>
    private static string? ShortCode(string? q) =>
        q?.Trim() is { Length: > 0 and <= 5 } n && n.All(char.IsAsciiDigit) && int.Parse(n) > 0 ? $"I{int.Parse(n):D5}" : null;

    private static async Task<IResult> GetByBarcode(string barcode, PosDbContext db, CancellationToken ct)
    {
        var code = barcode.Trim();
        var shortCode = ShortCode(code);
        var dto = await db.Items.AsNoTracking()
            .Where(i => i.IsActive && (i.Barcode == code || i.Code == code || i.Code == shortCode))
            .OrderBy(i => i.Barcode == code ? 0 : 1)
            .Select(ToDto).FirstOrDefaultAsync(ct);
        return dto is null ? DbHelpers.NotFound("Item") : Results.Ok(dto);
    }

    /// <summary>
    /// Counter scan: a lot barcode picks that exact batch; otherwise the item's barcode, its code, or a short code.
    /// </summary>
    private static async Task<IResult> Scan(string code, PosDbContext db, CancellationToken ct)
    {
        var text = code.Trim();
        var lot = await db.Batches.AsNoTracking()
            .Where(b => b.Barcode == text && b.Item.IsActive)
            .Select(b => new { b.Id, b.ItemId, b.BatchNo })
            .FirstOrDefaultAsync(ct);
        if (lot is not null)
            return Results.Ok(new ScanResultDto((await FindDto(db, lot.ItemId, ct))!, lot.Id, lot.BatchNo));

        var shortCode = ShortCode(text);
        var item = await db.Items.AsNoTracking()
            .Where(i => i.IsActive && (i.Barcode == text || i.Code == text || i.Code == shortCode))
            .OrderBy(i => i.Barcode == text ? 0 : 1)
            .Select(ToDto).FirstOrDefaultAsync(ct);
        return item is null ? DbHelpers.NotFound("Item") : Results.Ok(new ScanResultDto(item, null, null));
    }

    /// <summary>Sets or clears just the item's barcode, for scanning barcodes in item after item.</summary>
    private static async Task<IResult> SetBarcode(int id, SetBarcodeRequest req, PosDbContext db, CancellationToken ct)
    {
        var errors = new Errors().MaxLength(nameof(req.Barcode), req.Barcode, 50);
        if (errors.Any) return errors.ToResult();

        var item = await db.Items.FindAsync([id], ct);
        if (item is null) return DbHelpers.NotFound("Item");
        item.Barcode = Errors.Clean(req.Barcode);
        if (await IsDuplicate(db, item, ct)) return DbHelpers.Conflict(BarcodeTakenMessage);
        if (await db.SaveOrConflictAsync(BarcodeTakenMessage, ct) is { } conflict) return conflict;
        return Results.Ok(await FindDto(db, id, ct));
    }

    /// <summary>
    /// Label data for items and batches. A batch-tracked lot without a barcode gets one now, so the label scans
    /// straight to that lot; items without batches use their own barcode, or their code when they have none.
    /// </summary>
    private static async Task<IResult> Labels(LabelRequest req, PosDbContext db, CancellationToken ct)
    {
        var itemIds = req.ItemIds.Distinct().ToList();
        var batchIds = req.BatchIds.Distinct().ToList();
        if (itemIds.Count + batchIds.Count is 0 or > 500)
            return new Errors().Add("Items", "Choose between 1 and 500 items or batches.").ToResult();

        // Whole items: every batch in stock, or the item's default stock row even when it is empty.
        var batches = await db.Batches.Include(b => b.Item)
            .Where(b => batchIds.Contains(b.Id)
                || (itemIds.Contains(b.ItemId) && (b.QtyOnHand > 0 || b.BatchNo == Batch.DefaultBatchNo)))
            .OrderBy(b => b.Item.Name).ThenBy(b => b.ExpiryDate).ThenBy(b => b.Id)
            .ToListAsync(ct);

        foreach (var b in batches.Where(b => b.Item.IsBatchTracked && b.Barcode is null && b.BatchNo != Batch.DefaultBatchNo))
            b.Barcode = await FreeLotBarcodeAsync(db, b.Id, ct);
        await db.SaveChangesAsync(ct);

        return Results.Ok(batches.Select(b => new LabelDto
        {
            ItemId = b.ItemId, BatchId = b.Id, ItemName = b.Item.Name, Pack = b.Item.Pack,
            BatchNo = b.BatchNo == Batch.DefaultBatchNo ? null : b.BatchNo,
            MfgDate = b.MfgDate, ExpiryDate = b.ExpiryDate, Mrp = b.Mrp, SaleRate = b.SaleRate,
            Barcode = b.Barcode ?? b.Item.Barcode ?? b.Item.Code, QtyOnHand = b.QtyOnHand,
        }).ToList());
    }

    /// <summary>
    /// A generated lot barcode ("29" + batch id: digits only, so it prints compactly in Code 128 set C) that no item
    /// code, item barcode or batch uses yet.
    /// </summary>
    private static async Task<string> FreeLotBarcodeAsync(PosDbContext db, int batchId, CancellationToken ct)
    {
        for (var n = 0; ; n++)
        {
            var candidate = n == 0 ? $"29{batchId:D6}" : $"29{batchId:D6}{n}";
            if (!await IsBarcodeTakenAsync(db, candidate, null, null, ct)) return candidate;
        }
    }

    /// <summary>Whether a barcode is in use by any item (code or barcode) or batch other than the ones given.</summary>
    private static async Task<bool> IsBarcodeTakenAsync(PosDbContext db, string barcode, int? itemId, int? batchId, CancellationToken ct) =>
        await db.Items.AnyAsync(i => i.Id != itemId && (i.Barcode == barcode || i.Code == barcode), ct)
        || await db.Batches.AnyAsync(b => b.Id != batchId && b.Barcode == barcode, ct);

    private static async Task<IResult> Create(ItemUpsertRequest req, ClaimsPrincipal principal, PosDbContext db,
        CancellationToken ct)
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
        {
            var batch = DefaultBatchFor(item, req.OpeningQty);
            item.Batches.Add(batch);
            if (batch.QtyOnHand != 0) db.StockMovements.Add(OpeningMovement(item, batch, principal.GetUserId()));
        }
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
                batch.WholesaleRate = item.WholesaleRate;
                batch.HospitalRate = item.HospitalRate;
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

    /// <summary>In-stock, unexpired batches, nearest expiry first (FEFO), for billing.</summary>
    private static async Task<IResult> SellableBatches(int id, PosDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Items.AnyAsync(i => i.Id == id && i.IsActive, ct)) return DbHelpers.NotFound("Item");
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var batches = await db.Batches.AsNoTracking()
            .Where(b => b.ItemId == id && b.QtyOnHand > 0 && (b.ExpiryDate == null || b.ExpiryDate >= today))
            .OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.Id)
            .Select(b => new SellableBatchDto
            {
                Id = b.Id, BatchNo = b.BatchNo, ExpiryDate = b.ExpiryDate, Mrp = b.Mrp, SaleRate = b.SaleRate, QtyOnHand = b.QtyOnHand,
                WholesaleRate = b.WholesaleRate > 0 ? b.WholesaleRate : b.Item.WholesaleRate,
                HospitalRate = b.HospitalRate > 0 ? b.HospitalRate : b.Item.HospitalRate,
            })
            .ToListAsync(ct);
        return Results.Ok(batches);
    }

    private static StockMovement OpeningMovement(Item item, Batch batch, int userId) => new()
    {
        Item = item, Batch = batch, QtyChange = batch.QtyOnHand,
        Type = StockMovementType.Opening, RefNo = "OPENING", UserId = userId,
    };

    private static async Task<IResult> CreateBatch(int id, BatchUpsertRequest req, ClaimsPrincipal principal,
        PosDbContext db, CancellationToken ct)
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
        if (batch.Barcode is { } barcode && await IsBarcodeTakenAsync(db, barcode, null, null, ct))
            return DbHelpers.Conflict(BarcodeTakenMessage);

        db.Batches.Add(batch);
        if (batch.QtyOnHand != 0) db.StockMovements.Add(OpeningMovement(item, batch, principal.GetUserId()));
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
        if (batch.Barcode is { } barcode && await IsBarcodeTakenAsync(db, barcode, null, id, ct))
            return DbHelpers.Conflict(BarcodeTakenMessage);

        if (await db.SaveOrConflictAsync(dup, ct) is { } conflict) return conflict;
        return Results.Ok(await db.Batches.AsNoTracking().Where(b => b.Id == id).Select(ToBatchDto).FirstAsync(ct));
    }

    private static Task<ItemDto?> FindDto(PosDbContext db, int id, CancellationToken ct) =>
        db.Items.AsNoTracking().Where(i => i.Id == id).Select(ToDto).FirstOrDefaultAsync(ct);

    private static async Task<bool> IsDuplicate(PosDbContext db, Item item, CancellationToken ct) =>
        await db.Items.AnyAsync(i => i.Id != item.Id
            && (i.Code == item.Code || (item.Barcode != null && (i.Barcode == item.Barcode || i.Code == item.Barcode))), ct)
        || (item.Barcode != null && await db.Batches.AnyAsync(b => b.Barcode == item.Barcode, ct));

    private static Batch DefaultBatchFor(Item item, decimal qty) => new()
    {
        BatchNo = Batch.DefaultBatchNo,
        Mrp = item.Mrp,
        SaleRate = item.SaleRate,
        WholesaleRate = item.WholesaleRate,
        HospitalRate = item.HospitalRate,
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
            .NonNegative(nameof(req.HospitalRate), req.HospitalRate)
            .NonNegative(nameof(req.PurchaseRate), req.PurchaseRate)
            .NonNegative(nameof(req.ReorderLevel), req.ReorderLevel);

        if (req.MarginPercent is < 0 or > 1000) e.Add(nameof(req.MarginPercent), "Margin must be between 0 and 1000%.");
        if (req.WholesaleMarginPercent is < 0 or > 1000) e.Add(nameof(req.WholesaleMarginPercent), "Margin must be between 0 and 1000%.");

        if (Errors.Clean(req.Code) == Batch.DefaultBatchNo) e.Add(nameof(req.Code), "Code is not valid.");
        if (req.GstRate is < 0 or > 40) e.Add(nameof(req.GstRate), "GST rate must be between 0 and 40%.");
        if (Errors.Clean(req.HsnCode) is { } hsn && !Errors.HsnRegex().IsMatch(hsn))
            e.Add(nameof(req.HsnCode), "HSN code must be 4 to 8 digits.");
        if (!Enum.IsDefined(req.Schedule)) e.Add(nameof(req.Schedule), "Unknown drug schedule.");
        if (req.Mrp > 0)
        {
            if (req.SaleRate > req.Mrp) e.Add(nameof(req.SaleRate), "Sale rate cannot exceed MRP.");
            if (req.WholesaleRate > req.Mrp) e.Add(nameof(req.WholesaleRate), "Wholesale rate cannot exceed MRP.");
            if (req.HospitalRate > req.Mrp) e.Add(nameof(req.HospitalRate), "Hospital rate cannot exceed MRP.");
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
        item.HospitalRate = req.HospitalRate;
        item.PurchaseRate = req.PurchaseRate;
        item.MarginPercent = req.MarginPercent;
        item.WholesaleMarginPercent = req.WholesaleMarginPercent;
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
            .MaxLength(nameof(req.Barcode), req.Barcode, 50)
            .NonNegative(nameof(req.Mrp), req.Mrp)
            .NonNegative(nameof(req.SaleRate), req.SaleRate)
            .NonNegative(nameof(req.WholesaleRate), req.WholesaleRate)
            .NonNegative(nameof(req.HospitalRate), req.HospitalRate)
            .NonNegative(nameof(req.PurchaseRate), req.PurchaseRate);

        var batchNo = req.BatchNo?.Trim().ToUpperInvariant() ?? "";
        if (batchNo == Batch.DefaultBatchNo) e.Add(nameof(req.BatchNo), "Batch number is not valid.");
        if (req.ExpiryDate is null) e.Add(nameof(req.ExpiryDate), "Expiry date is required.");
        if (req.MfgDate is { } mfg && req.ExpiryDate is { } exp && mfg >= exp)
            e.Add(nameof(req.MfgDate), "Manufacturing date must be before expiry.");

        var mrp = req.Mrp > 0 ? req.Mrp : item.Mrp;
        var sale = req.SaleRate > 0 ? req.SaleRate : item.SaleRate;
        if (mrp > 0 && sale > mrp) e.Add(nameof(req.SaleRate), "Sale rate cannot exceed MRP.");
        if (mrp > 0 && (req.WholesaleRate > mrp || req.HospitalRate > mrp))
            e.Add(nameof(req.WholesaleRate), "Wholesale and hospital rates cannot exceed MRP.");
        if (e.Any) return e;

        batch.BatchNo = batchNo;
        batch.Barcode = Errors.Clean(req.Barcode);
        batch.MfgDate = req.MfgDate;
        batch.ExpiryDate = req.ExpiryDate;
        batch.Mrp = mrp;
        batch.SaleRate = sale;
        batch.WholesaleRate = req.WholesaleRate;
        batch.HospitalRate = req.HospitalRate;
        batch.PurchaseRate = req.PurchaseRate > 0 ? req.PurchaseRate : item.PurchaseRate;
        return e;
    }
}
