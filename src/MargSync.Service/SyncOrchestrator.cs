using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MargSync.Core.Configuration;
using MargSync.Core.Interfaces;
using MargSync.Core.Models;
using MargSync.Parser;

namespace MargSync.Service;

/// <summary>
/// Picks up CSV files from the MARG export folder, routes them to the right
/// parser, and calls the appropriate repository to upsert into SQL Server.
/// </summary>
public class SyncOrchestrator(
    ILogger<SyncOrchestrator> logger,
    IOptions<SyncOptions> opts,
    IRepository<Party> partyRepo,
    IRepository<Item> itemRepo,
    IRepository<PurchaseBill> billRepo,
    IRepository<SalesInvoice> salesRepo,
    IRepository<StockEntry> stockRepo,
    IRepository<LedgerEntry> ledgerRepo,
    IRepository<GstEntry> gstRepo,
    ISyncLogRepository logRepo,
    EdeParser edeParser,
    MasterDataParser masterParser,
    SalesExportParser salesParser,
    StockExportParser stockParser,
    LedgerExportParser ledgerParser,
    GstExportParser gstParser)
{
    private readonly SyncOptions _opts = opts.Value;

    public async Task RunAsync(CancellationToken ct)
    {
        var patterns = _opts.FilePatterns.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var files = patterns
            .SelectMany(p => Directory.GetFiles(_opts.MargExportFolder, p.Trim()))
            .Distinct()
            .OrderBy(f => File.GetCreationTimeUtc(f))
            .ToList();

        if (files.Count == 0)
        {
            logger.LogDebug("No files found in {Folder}", _opts.MargExportFolder);
            return;
        }

        logger.LogInformation("Found {Count} file(s) to process", files.Count);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessFileAsync(file, ct);
        }
    }

    private async Task ProcessFileAsync(string filePath, CancellationToken ct)
    {
        var log = new SyncLog
        {
            FileName  = Path.GetFileName(filePath),
            Direction = SyncDirection.MargToSql,
            Status    = SyncStatus.Pending,
            StartedAt = DateTime.UtcNow,
        };

        try
        {
            log = await logRepo.CreateAsync(log, ct);
            int processed = await RouteFileAsync(filePath, log, ct);
            log.RecordsProcessed = processed;
            if (log.Status != SyncStatus.Skipped)
                log.Status = SyncStatus.Success;
            log.CompletedAt      = DateTime.UtcNow;
            await logRepo.UpdateAsync(log, ct);

            ArchiveFile(filePath);
            logger.LogInformation("Processed {File}: {Count} record(s)", log.FileName, processed);
        }
        catch (Exception ex)
        {
            log.Status       = SyncStatus.Failed;
            log.ErrorMessage = ex.Message;
            log.CompletedAt  = DateTime.UtcNow;
            await logRepo.UpdateAsync(log, ct);

            MoveToError(filePath);
            logger.LogError(ex, "Failed to process {File}", log.FileName);
        }
    }

    private async Task<int> RouteFileAsync(string filePath, SyncLog log, CancellationToken ct)
    {
        // EDE purchase bill format (H,T,F comma-separated)
        if (edeParser.CanParse(filePath))
        {
            log.FileType = "EDE-Purchase";
            var bills = await edeParser.ParseAsync(filePath, ct);
            await billRepo.UpsertAsync(bills, ct);
            return bills.Count;
        }

        // MARG master data (semicolon-separated, C/H/T rows)
        if (IsMasterFormat(filePath))
        {
            log.FileType = "Master";
            var result = await masterParser.ParseAsync(filePath, ct);
            await partyRepo.UpsertAsync(result.Parties, ct);
            await itemRepo.UpsertAsync(result.Items, ct);
            return result.Parties.Count + result.Items.Count;
        }

        // Sales export (header row with InvoiceNo column)
        if (salesParser.CanParse(filePath))
        {
            log.FileType = "Sales";
            var invoices = await salesParser.ParseAsync(filePath, ct);
            await salesRepo.UpsertAsync(invoices, ct);
            return invoices.Count;
        }

        // Stock export (header row with ClosingQty column)
        if (stockParser.CanParse(filePath))
        {
            log.FileType = "Stock";
            var entries = await stockParser.ParseAsync(filePath, ct);
            await stockRepo.UpsertAsync(entries, ct);
            return entries.Count;
        }

        // Ledger export (header row with VoucherType column)
        if (ledgerParser.CanParse(filePath))
        {
            log.FileType = "Ledger";
            var entries = await ledgerParser.ParseAsync(filePath, ct);
            await ledgerRepo.UpsertAsync(entries, ct);
            return entries.Count;
        }

        // GST export (header row with GSTIN column)
        if (gstParser.CanParse(filePath))
        {
            log.FileType = "GST";
            var entries = await gstParser.ParseAsync(filePath, ct);
            await gstRepo.UpsertAsync(entries, ct);
            return entries.Count;
        }

        log.Status  = SyncStatus.Skipped;
        log.FileType = "Unknown";
        logger.LogWarning("Unrecognised file format, skipping: {File}", Path.GetFileName(filePath));
        return 0;
    }

    private static bool IsMasterFormat(string filePath)
    {
        var first = File.ReadLines(filePath).FirstOrDefault() ?? string.Empty;
        return first.StartsWith("C0", StringComparison.Ordinal) && first.Contains(';');
    }

    private void ArchiveFile(string filePath)
    {
        var dest = Path.Combine(_opts.ArchiveFolder,
            $"{Path.GetFileNameWithoutExtension(filePath)}_{DateTime.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(filePath)}");
        Directory.CreateDirectory(_opts.ArchiveFolder);
        File.Move(filePath, dest, overwrite: true);
    }

    private void MoveToError(string filePath)
    {
        var dest = Path.Combine(_opts.ErrorFolder,
            $"{Path.GetFileNameWithoutExtension(filePath)}_{DateTime.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(filePath)}");
        Directory.CreateDirectory(_opts.ErrorFolder);
        File.Move(filePath, dest, overwrite: true);
    }
}
