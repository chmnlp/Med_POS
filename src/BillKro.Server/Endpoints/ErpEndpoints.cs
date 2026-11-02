using System.Security.Claims;
using BillKro.Data;
using BillKro.Server.Auth;
using BillKro.Server.Infrastructure;
using BillKro.Shared;
using BillKro.Sync.Parser;
using Microsoft.EntityFrameworkCore;
using ErpItem = BillKro.Sync.Core.Models.Item;

namespace BillKro.Server.Endpoints;

/// <summary>
/// File exchange with MARG ERP: masters come in from MARG's export file, purchases go out as EDE files
/// (see <see cref="ErpLinkService"/>), and sales go out as a register to key in, as MARG has no known sales import.
/// </summary>
public static class ErpEndpoints
{
    public const long MaxMasterFileBytes = 20 * 1024 * 1024;
    public const int MaxRegisterDays = 366;
    private const string ImportRef = "ERP-IMPORT";

    public static void MapErp(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/erp").RequireAuthorization(Policies.Manager);
        g.MapGet("/status", Status);
        g.MapPost("/send-now", SendNow);
        g.MapPost("/manual/{kind}/{documentId:int}/done", MarkDone);
        g.MapGet("/sales-register", SalesRegister);
        g.MapPost("/master/preview", PreviewMaster).DisableAntiforgery();
        g.MapPost("/master/apply", ApplyMaster).DisableAntiforgery();

        // Where the server writes files is a system setting.
        g.MapPut("/settings", SaveSettings).RequireAuthorization(Policies.Admin);
        g.MapPost("/test-folder", TestFolder).RequireAuthorization(Policies.Admin);
    }

    // ---------------------------------------------------------------- purchases out

    private static async Task<ErpLinkStatusDto> Status(ErpLinkService link, PosDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var s = await link.SettingsAsync(ct);
        var cancelledIds = db.Purchases.Where(p => p.Status == PurchaseStatus.Cancelled).Select(p => p.Id);
        var recent = await db.ErpExports.AsNoTracking()
            .Where(x => x.Kind == ErpLinkService.PurchaseKind)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(50)
            .Select(x => new ErpExportDto(x.Id, x.Kind, x.DocumentNo, x.FileName, x.Status, x.Error, x.CreatedAt,
                x.Status == ErpExportStatus.Sent && cancelledIds.Contains(x.DocumentId)))
            .ToListAsync(ct);
        // Stored in UTC; shown in shop time like every other date.
        recent = recent.Select(x => x with
        {
            CreatedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc), clock.LocalTimeZone),
        }).ToList();
        return new ErpLinkStatusDto(ErpLinkService.ToDto(s), s.LastRunAt, s.LastError, await link.PendingCountAsync(s, ct),
            recent, await link.ManualTasksAsync(s, ct));
    }

    private static async Task<ErpSendResult> SendNow(ErpLinkService link, CancellationToken ct) =>
        await link.SendPendingAsync(ct);

    private static async Task<IResult> MarkDone(string kind, int documentId, ErpLinkService link, CancellationToken ct) =>
        await link.MarkDoneAsync(kind, documentId, ct) ? Results.NoContent() : DbHelpers.NotFound("Task");

    private static async Task<IResult> SaveSettings(ErpLinkSettingsDto req, PosDbContext db, CancellationToken ct)
    {
        var e = new Errors().MaxLength(nameof(req.MethodFolder), req.MethodFolder, 260)
            .Required(nameof(req.CompanyCode), req.CompanyCode, 20)
            .Add(req.PollMinutes is < 1 or > 1440, nameof(req.PollMinutes), "Check every 1 to 1440 minutes.");
        if (req.Enabled && string.IsNullOrWhiteSpace(req.MethodFolder))
            e.Add(nameof(req.MethodFolder), "Enter the ERP's Method folder before switching sending on.");
        if (req.Enabled && req.SendFrom is null)
            e.Add(nameof(req.SendFrom), "Choose the date to send from, so older purchases already in the ERP are not sent again.");
        if (e.Any) return e.ToResult();

        var s = await db.ErpLinkSettings.FirstOrDefaultAsync(ct);
        if (s is null) db.ErpLinkSettings.Add(s = new ErpLinkSettings());
        s.Enabled = req.Enabled;
        s.MethodFolder = Errors.Clean(req.MethodFolder);
        s.CompanyCode = req.CompanyCode.Trim();
        s.SendFrom = req.SendFrom;
        s.PollMinutes = req.PollMinutes;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ErpLinkService.ToDto(s));
    }

    private record FolderRequest(string? MethodFolder);

    private static ErpFolderCheck TestFolder(FolderRequest req) => ErpLinkService.CheckFolder(req.MethodFolder?.Trim());

    // ---------------------------------------------------------------- sales out

    private static async Task<IResult> SalesRegister(DateOnly from, DateOnly to, PosDbContext db, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxRegisterDays)
            return new Errors().Add("to", $"Choose a range of up to {MaxRegisterDays} days.").ToResult();

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var lines = await db.SaleInvoiceLines.AsNoTracking()
            .Where(l => l.Invoice.Status == InvoiceStatus.Completed && l.Invoice.InvoiceDate >= start && l.Invoice.InvoiceDate < end)
            .OrderBy(l => l.Invoice.InvoiceDate).ThenBy(l => l.InvoiceId).ThenBy(l => l.LineNo)
            .Select(l => new
            {
                l.InvoiceId, l.Invoice.InvoiceNo, l.Invoice.InvoiceDate, l.Invoice.CustomerName, l.Invoice.CustomerGstin,
                l.ItemCode, l.ItemName, l.HsnCode, l.BatchNo, l.ExpiryDate, l.Qty, l.Rate, l.DiscountPercent, l.GstRate,
                l.Taxable, l.Tax, l.Amount,
            })
            .ToListAsync(ct);

        var invoiceIds = lines.Select(l => l.InvoiceId).Distinct().ToList();
        var modes = (await db.Set<SalePayment>().AsNoTracking().Where(p => invoiceIds.Contains(p.InvoiceId))
                .Select(p => new { p.InvoiceId, p.Mode }).ToListAsync(ct))
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => string.Join("+", g.Select(p => p.Mode).Distinct().Order()));

        return Results.Ok(lines.Select(l => new SalesRegisterRow(l.InvoiceNo, l.InvoiceDate, l.CustomerName, l.CustomerGstin,
            l.ItemCode, l.ItemName, l.HsnCode, l.BatchNo == Batch.DefaultBatchNo ? "" : l.BatchNo, l.ExpiryDate, l.Qty, l.Rate,
            l.DiscountPercent, l.GstRate, l.Taxable, l.Tax, l.Amount, modes.GetValueOrDefault(l.InvoiceId, ""))).ToList());
    }

    // ---------------------------------------------------------------- masters in

    private static async Task<(MasterParseResult? Result, IResult? Error)> ParseMasterAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > MaxMasterFileBytes)
            return (null, new Errors().Add("file", "Choose the ERP's master export file (under 20 MB).").ToResult());

        var path = Path.Combine(Path.GetTempPath(), $"billkro-master-{Guid.NewGuid():N}.csv");
        try
        {
            await using (var stream = File.Create(path))
                await file.CopyToAsync(stream, ct);
            var result = await new MasterDataParser().ParseAsync(path, ct);
            if (result.Items.Count == 0 && result.Parties.Count == 0)
                return (null, new Errors().Add("file", "No items or parties were found. Is this the ERP's master export (C/H/T lines)?").ToResult());
            return (result, null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CodeOf(ErpItem i) => i.Code.Trim().ToUpperInvariant();

    private static string? Text(string? s, int max) => Errors.Clean(s) is { } t ? Errors.Truncate(t, max) : null;

    private static async Task<IResult> PreviewMaster(IFormFile? file, PosDbContext db, CancellationToken ct)
    {
        var (parsed, error) = await ParseMasterAsync(file, ct);
        if (error is not null) return error;

        var codes = parsed!.Items.Select(CodeOf).Distinct().ToList();
        var existing = (await db.Items.AsNoTracking().Where(i => codes.Contains(i.Code)).Select(i => i.Code).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = parsed.Parties.Select(p => p.Name.Trim()).Distinct().ToList();
        var customers = (await db.Customers.AsNoTracking().Where(c => names.Contains(c.Name)).Select(c => c.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suppliers = (await db.Suppliers.AsNoTracking().Where(c => names.Contains(c.Name)).Select(c => c.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Results.Ok(new ErpMasterPreview(parsed.Company?.Name,
            parsed.Items.Select(i => new ErpMasterItemPreview(CodeOf(i), i.Name, i.MRP, i.PurchaseRate, i.TaxRate, i.BatchNo,
                i.ExpiryDate, i.OpeningQty, existing.Contains(CodeOf(i)))).ToList(),
            parsed.Parties.Select(p => new ErpMasterPartyPreview(p.AccountCode, p.Name.Trim(), p.Type, p.OpeningBalance,
                customers.Contains(p.Name.Trim()), suppliers.Contains(p.Name.Trim()))).ToList()));
    }

    /// <summary>
    /// Creates or updates items by MARG code, and parties by name. Stock is set only for new items (as opening stock);
    /// re-importing never changes stock or balances, which the POS tracks itself from then on.
    /// </summary>
    private static async Task<IResult> ApplyMaster(IFormFile? file, ErpPartyImport? parties, string? partyType,
        ClaimsPrincipal principal, PosDbContext db, CancellationToken ct)
    {
        var (parsed, error) = await ParseMasterAsync(file, ct);
        if (error is not null) return error;

        var userId = principal.GetUserId();
        var warnings = new List<string>();
        int itemsCreated = 0, itemsUpdated = 0, lotsCreated = 0;

        var groups = parsed!.Items.Where(i => CodeOf(i) is { Length: > 0 and <= 30 } c && c != Batch.DefaultBatchNo)
            .GroupBy(CodeOf).ToList();
        var skipped = parsed.Items.Count - groups.Sum(g => g.Count());
        if (skipped > 0) warnings.Add($"{skipped} item row(s) had no usable code and were skipped.");

        var codes = groups.Select(g => g.Key).ToList();
        var existing = await db.Items.Include(i => i.Batches).Where(i => codes.Contains(i.Code))
            .ToDictionaryAsync(i => i.Code, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var g in groups)
        {
            var first = g.First();
            var mrp = Math.Max(0, first.MRP);
            var gst = first.TaxRate is > 0 and <= 40 ? first.TaxRate : 0;
            var name = Text(first.Name, 200) ?? g.Key;

            if (existing.TryGetValue(g.Key, out var item))
            {
                item.Name = name;
                if (mrp > 0) item.Mrp = mrp;
                if (first.PurchaseRate > 0) item.PurchaseRate = first.PurchaseRate;
                if (gst > 0) item.GstRate = gst;
                item.Manufacturer = Text(first.Manufacturer, 100) ?? item.Manufacturer;
                item.Pack = Text(first.PackForm, 30) ?? item.Pack;
                item.Category = Text(first.Category, 100) ?? item.Category;
                if (item.Mrp > 0 && item.SaleRate > item.Mrp) item.SaleRate = item.Mrp;
                if (!item.IsBatchTracked && item.Batches.FirstOrDefault(b => b.BatchNo == Batch.DefaultBatchNo) is { } def)
                {
                    def.Mrp = item.Mrp;
                    def.SaleRate = item.SaleRate;
                    def.PurchaseRate = item.PurchaseRate;
                }
                item.UpdatedAt = DateTime.UtcNow;
                itemsUpdated++;
                continue;
            }

            item = new Item
            {
                Code = g.Key, Name = name, Mrp = mrp, SaleRate = mrp, PurchaseRate = Math.Max(0, first.PurchaseRate), GstRate = gst,
                Manufacturer = Text(first.Manufacturer, 100), Pack = Text(first.PackForm, 30), Category = Text(first.Category, 100),
                IsBatchTracked = g.Any(r => Errors.Clean(r.BatchNo) is not null),
            };
            if (Errors.Clean(first.HsnCode) is { } hsn && Errors.HsnRegex().IsMatch(hsn)) item.HsnCode = hsn;

            if (item.IsBatchTracked)
            {
                var family = new List<(string BatchNo, decimal Mrp, decimal Cost)>();
                foreach (var row in g)
                {
                    var batchNo = Text(row.BatchNo, 25)?.ToUpperInvariant();
                    if (batchNo is null || batchNo == Batch.DefaultBatchNo)
                    {
                        if (row.OpeningQty != 0) warnings.Add($"{g.Key}: stock {row.OpeningQty} without a batch number was not imported.");
                        continue;
                    }
                    var rowMrp = row.MRP > 0 ? row.MRP : mrp;
                    var rowCost = row.PurchaseRate > 0 ? row.PurchaseRate : item.PurchaseRate;
                    var lot = PurchaseEndpoints.ResolveLot(batchNo,
                        rowMrp, rowCost, family.Where(f => f.BatchNo == batchNo || f.BatchNo.StartsWith(batchNo + "/")).ToList());
                    if (family.Any(f => f.BatchNo == lot))
                    {
                        warnings.Add($"{g.Key}: batch {batchNo} appears twice at the same MRP and cost; only the first was imported.");
                        continue;
                    }
                    family.Add((lot, rowMrp, rowCost));
                    var qty = Math.Max(0, row.OpeningQty);
                    if (row.OpeningQty < 0) warnings.Add($"{g.Key} batch {batchNo}: negative stock in the ERP was imported as 0.");
                    if (row.ExpiryDate is null) warnings.Add($"{g.Key} batch {batchNo}: no expiry date in the file.");
                    var batch = new Batch
                    {
                        BatchNo = lot, MfgDate = row.ManufacturingDate, ExpiryDate = row.ExpiryDate, Mrp = rowMrp, SaleRate = rowMrp,
                        PurchaseRate = rowCost, QtyOnHand = qty,
                    };
                    item.Batches.Add(batch);
                    lotsCreated++;
                    if (qty != 0) db.StockMovements.Add(Opening(item, batch, userId));
                }
            }
            else
            {
                var qty = g.Sum(r => r.OpeningQty);
                if (qty < 0)
                {
                    warnings.Add($"{g.Key}: negative stock in the ERP was imported as 0.");
                    qty = 0;
                }
                var batch = new Batch
                {
                    BatchNo = Batch.DefaultBatchNo, Mrp = item.Mrp, SaleRate = item.SaleRate, PurchaseRate = item.PurchaseRate, QtyOnHand = qty,
                };
                item.Batches.Add(batch);
                if (qty != 0) db.StockMovements.Add(Opening(item, batch, userId));
            }
            db.Items.Add(item);
            itemsCreated++;
        }

        var (partiesCreated, partiesUpdated) = parties switch
        {
            ErpPartyImport.Customers => await ImportCustomersAsync(db, Filter(parsed, partyType), ct),
            ErpPartyImport.Suppliers => await ImportSuppliersAsync(db, Filter(parsed, partyType), ct),
            _ => (0, 0),
        };

        if (await db.SaveOrConflictAsync("Another user changed items or parties at the same time; import again.", ct) is { } conflict)
            return conflict;
        return Results.Ok(new ErpMasterImportResult(itemsCreated, itemsUpdated, lotsCreated, partiesCreated, partiesUpdated, warnings));
    }

    private static StockMovement Opening(Item item, Batch batch, int userId) => new()
    {
        Item = item, Batch = batch, QtyChange = batch.QtyOnHand, Type = StockMovementType.Opening, RefNo = ImportRef, UserId = userId,
    };

    private static List<BillKro.Sync.Core.Models.Party> Filter(MasterParseResult parsed, string? type) =>
        parsed.Parties.Where(p => Errors.Clean(p.Name) is not null
                && (string.IsNullOrWhiteSpace(type) || string.Equals(p.Type.Trim(), type.Trim(), StringComparison.OrdinalIgnoreCase)))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

    private static async Task<(int, int)> ImportCustomersAsync(PosDbContext db, List<BillKro.Sync.Core.Models.Party> parties,
        CancellationToken ct)
    {
        var names = parties.Select(p => p.Name.Trim()).ToList();
        var existing = await db.Customers.Where(c => names.Contains(c.Name)).ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase, ct);
        var usedCodes = (await db.Customers.Select(c => c.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = NextNumber(usedCodes, "C");
        int created = 0, updated = 0;
        foreach (var p in parties)
        {
            if (existing.TryGetValue(p.Name.Trim(), out var c))
            {
                c.Phone ??= Text(p.Phone, 20);
                c.Address ??= Text(p.Address, 500);
                updated++;
                continue;
            }
            var code = PartyCode(p.AccountCode, usedCodes, "C", ref next);
            db.Customers.Add(new Customer
            {
                Code = code, Name = Errors.Truncate(p.Name.Trim(), 200), Phone = Text(p.Phone, 20), Address = Text(p.Address, 500),
                OpeningBalance = p.OpeningBalance,
            });
            created++;
        }
        return (created, updated);
    }

    private static async Task<(int, int)> ImportSuppliersAsync(PosDbContext db, List<BillKro.Sync.Core.Models.Party> parties,
        CancellationToken ct)
    {
        var names = parties.Select(p => p.Name.Trim()).ToList();
        var existing = await db.Suppliers.Where(c => names.Contains(c.Name)).ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase, ct);
        var usedCodes = (await db.Suppliers.Select(c => c.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = NextNumber(usedCodes, "S");
        int created = 0, updated = 0;
        foreach (var p in parties)
        {
            if (existing.TryGetValue(p.Name.Trim(), out var s))
            {
                s.Phone ??= Text(p.Phone, 20);
                s.Address ??= Text(p.Address, 500);
                updated++;
                continue;
            }
            var code = PartyCode(p.AccountCode, usedCodes, "S", ref next);
            db.Suppliers.Add(new Supplier
            {
                Code = code, Name = Errors.Truncate(p.Name.Trim(), 200), Phone = Text(p.Phone, 20), Address = Text(p.Address, 500),
                OpeningBalance = p.OpeningBalance,
            });
            created++;
        }
        return (created, updated);
    }

    private static int NextNumber(HashSet<string> codes, string prefix) =>
        codes.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(c => int.TryParse(c[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;

    /// <summary>Keeps MARG's account code when it is free, else numbers the party like the POS does.</summary>
    private static string PartyCode(string? erpCode, HashSet<string> used, string prefix, ref int next)
    {
        var code = Text(erpCode, 30)?.ToUpperInvariant();
        if (code is null || used.Contains(code))
        {
            do code = $"{prefix}{next++:D5}"; while (used.Contains(code));
        }
        used.Add(code);
        return code;
    }
}
