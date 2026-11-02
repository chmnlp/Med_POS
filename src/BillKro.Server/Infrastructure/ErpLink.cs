using System.Text.RegularExpressions;
using BillKro.Data;
using BillKro.Shared;
using BillKro.Sync.Core.Models;
using BillKro.Sync.Parser.Exporters;
using Microsoft.EntityFrameworkCore;

namespace BillKro.Server.Infrastructure;

/// <summary>
/// Sends POS purchases to MARG as EDE files in its Method\ folder. Every attempt is recorded in
/// <see cref="ErpExport"/>, so a purchase is written at most once even if the server restarts mid-run.
/// </summary>
public sealed partial class ErpLinkService(PosDbContext db, TimeProvider clock, ILogger<ErpLinkService> logger)
{
    public const string PurchaseKind = "Purchase";
    public const string DebitNoteKind = "DebitNote";
    public const string PurchaseCancelKind = "PurchaseCancel";

    /// <summary>Background and "Send now" runs share this, so two runs never write the same purchase.</summary>
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public async Task<ErpLinkSettings> SettingsAsync(CancellationToken ct) =>
        await db.ErpLinkSettings.FirstOrDefaultAsync(ct) ?? new ErpLinkSettings();

    public static ErpLinkSettingsDto ToDto(ErpLinkSettings s) => new()
    {
        Enabled = s.Enabled, MethodFolder = s.MethodFolder, CompanyCode = s.CompanyCode, SendFrom = s.SendFrom,
        PollMinutes = s.PollMinutes,
    };

    /// <summary>Checks the server can see and write to the folder (it runs as a service, so mapped drives do not count).</summary>
    public static ErpFolderCheck CheckFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return new(false, "Enter the ERP's Method folder.");
        try
        {
            if (!Directory.Exists(folder))
                return new(false, $"The server cannot see {folder}. Use a \\\\PC\\share path, and give the server's account access to it.");
            var probe = Path.Combine(folder, $"billkro-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return new(true, "The server can write to this folder.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"The server cannot write to {folder}: {ex.Message}");
        }
    }

    private IQueryable<Purchase> Unsent(ErpLinkSettings s)
    {
        var from = s.SendFrom?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        return db.Purchases.Where(p => p.Status == PurchaseStatus.Completed && p.ReceivedAt >= from
            && !db.ErpExports.Any(x => x.Kind == PurchaseKind && x.DocumentId == p.Id && x.Status == ErpExportStatus.Sent));
    }

    public Task<int> PendingCountAsync(ErpLinkSettings s, CancellationToken ct) => Unsent(s).CountAsync(ct);

    /// <summary>Writes every unsent purchase to MARG's folder, one file per purchase.</summary>
    public async Task<ErpSendResult> SendPendingAsync(CancellationToken ct)
    {
        await RunLock.WaitAsync(ct);
        try
        {
            var s = await db.ErpLinkSettings.FirstOrDefaultAsync(ct);
            if (s is null) return new(0, 0, "Data Exchange settings are missing.");

            var check = CheckFolder(s.MethodFolder);
            if (!check.Ok)
            {
                await RecordRunAsync(s, check.Message, ct);
                return new(0, 0, check.Message);
            }

            var ids = await Unsent(s).OrderBy(p => p.Id).Select(p => p.Id).Take(200).ToListAsync(ct);
            int sent = 0, failed = 0;
            string? lastError = null;
            foreach (var id in ids)
            {
                var purchase = await db.Purchases.AsNoTracking().Include(p => p.Supplier)
                    .Include(p => p.Lines).ThenInclude(l => l.Item)
                    .FirstAsync(p => p.Id == id, ct);
                var row = await db.ErpExports.FirstOrDefaultAsync(x => x.Kind == PurchaseKind && x.DocumentId == id, ct);
                if (row is null)
                {
                    row = new ErpExport { Kind = PurchaseKind, DocumentId = id, DocumentNo = purchase.PurchaseNo };
                    db.ErpExports.Add(row);
                }

                var fileName = FileNameFor(purchase.PurchaseNo);
                try
                {
                    await new EdeExporter().ExportAsync([ToBill(purchase, s.CompanyCode)], Path.Combine(s.MethodFolder!, fileName), ct);
                    row.Status = ErpExportStatus.Sent;
                    row.Error = null;
                    sent++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    row.Status = ErpExportStatus.Failed;
                    row.Error = Errors.Truncate(ex.Message, 500);
                    lastError = row.Error;
                    failed++;
                    logger.LogWarning(ex, "Could not send purchase {No} to the ERP", purchase.PurchaseNo);
                }
                row.FileName = fileName;
                row.CreatedAt = DateTime.UtcNow;
                // Saved per file: a crash after writing a file must not lose the record that it was sent.
                await db.SaveChangesAsync(ct);
            }

            await RecordRunAsync(s, lastError, ct);
            if (sent > 0) logger.LogInformation("Sent {Count} purchase(s) to the ERP", sent);
            return new(sent, failed, lastError);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task RecordRunAsync(ErpLinkSettings s, string? error, CancellationToken ct)
    {
        s.LastRunAt = clock.GetLocalNow().DateTime;
        s.LastError = error is null ? null : Errors.Truncate(error, 500);
        await db.SaveChangesAsync(ct);
    }

    internal static string FileNameFor(string documentNo) => $"POS_{UnsafeChars().Replace(documentNo, "_")}.csv";

    [GeneratedRegex("[^A-Za-z0-9-]")]
    private static partial Regex UnsafeChars();

    internal static PurchaseBill ToBill(Purchase p, string companyCode) => new()
    {
        BillNumber = p.SupplierInvoiceNo,
        CompanyCode = companyCode,
        SupplierCode = p.Supplier.Code,
        SupplierName = p.Supplier.Name,
        BillDate = p.SupplierInvoiceDate,
        TotalAmount = p.Total,
        TotalTax = p.Tax,
        NetAmount = p.Total,
        Lines = p.Lines.Select(l => new PurchaseBillLine
        {
            ItemCode = l.ItemCode,
            ItemName = l.ItemName,
            PackForm = l.Item.Pack,
            Manufacturer = l.Item.Manufacturer,
            BatchNo = l.BatchNo == Batch.DefaultBatchNo ? "" : l.BatchNo,
            ExpiryDate = l.ExpiryDate,
            Quantity = l.Qty,
            FreeQuantity = l.FreeQty,
            TaxRate = l.GstRate,
            PurchaseRate = l.Rate,
            CostRate = l.LandingCost,
            MRP = l.Mrp,
            Discount = l.DiscountPercent,
            TaxAmount = l.Qty == 0 ? 0 : Math.Round(l.Tax / l.Qty, 4),
        }).ToList(),
    };

    /// <summary>What MARG cannot take from a file: debit notes, and purchases cancelled after they were sent.</summary>
    public async Task<List<ErpManualTask>> ManualTasksAsync(ErpLinkSettings s, CancellationToken ct)
    {
        var from = s.SendFrom?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        var debitNotes = await db.PurchaseReturns.AsNoTracking()
            .Where(r => r.ReturnDate >= from
                && !db.ErpExports.Any(x => x.Kind == DebitNoteKind && x.DocumentId == r.Id))
            .OrderBy(r => r.ReturnDate)
            .Select(r => new ErpManualTask(DebitNoteKind, r.Id, r.ReturnNo, r.ReturnDate, r.Supplier.Name, r.Total,
                "Enter this debit note (purchase return) in the ERP."))
            .ToListAsync(ct);

        var cancelled = await db.Purchases.AsNoTracking()
            .Where(p => p.Status == PurchaseStatus.Cancelled
                && db.ErpExports.Any(x => x.Kind == PurchaseKind && x.DocumentId == p.Id && x.Status == ErpExportStatus.Sent)
                && !db.ErpExports.Any(x => x.Kind == PurchaseCancelKind && x.DocumentId == p.Id))
            .OrderBy(p => p.CancelledAt)
            .Select(p => new ErpManualTask(PurchaseCancelKind, p.Id, p.PurchaseNo, p.CancelledAt ?? p.ReceivedAt,
                p.Supplier.Name, p.Total,
                "Cancelled in the POS after it was sent: delete purchase bill " + p.SupplierInvoiceNo + " in the ERP."))
            .ToListAsync(ct);

        return [.. cancelled, .. debitNotes];
    }

    /// <summary>Marks a manual task as entered in MARG so it leaves the list.</summary>
    public async Task<bool> MarkDoneAsync(string kind, int documentId, CancellationToken ct)
    {
        var docNo = kind switch
        {
            DebitNoteKind => await db.PurchaseReturns.Where(r => r.Id == documentId).Select(r => r.ReturnNo).FirstOrDefaultAsync(ct),
            PurchaseCancelKind => await db.Purchases.Where(p => p.Id == documentId && p.Status == PurchaseStatus.Cancelled)
                .Select(p => p.PurchaseNo).FirstOrDefaultAsync(ct),
            _ => null,
        };
        if (docNo is null) return false;
        if (!await db.ErpExports.AnyAsync(x => x.Kind == kind && x.DocumentId == documentId, ct))
        {
            db.ErpExports.Add(new ErpExport { Kind = kind, DocumentId = documentId, DocumentNo = docNo, Status = ErpExportStatus.DoneByHand });
            await db.SaveChangesAsync(ct);
        }
        return true;
    }
}

/// <summary>Sends new purchases to MARG every few minutes while the link is switched on.</summary>
public sealed class ErpLinkWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<ErpLinkWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("ErpLink:Background", true)) return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        var lastRun = DateTime.MinValue;
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var link = scope.ServiceProvider.GetRequiredService<ErpLinkService>();
                var s = await link.SettingsAsync(stoppingToken);
                if (s.Enabled && DateTime.UtcNow - lastRun >= TimeSpan.FromMinutes(Math.Max(1, s.PollMinutes)))
                {
                    lastRun = DateTime.UtcNow;
                    await link.SendPendingAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Keep running: the database may be briefly unavailable.
                logger.LogError(ex, "Data Exchange run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
