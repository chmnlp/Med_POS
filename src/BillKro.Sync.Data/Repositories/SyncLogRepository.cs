using BillKro.Sync.Core.Interfaces;
using BillKro.Sync.Core.Models;
using BillKro.Sync.Data.Entities;

namespace BillKro.Sync.Data.Repositories;

public class SyncLogRepository(SyncDbContext db) : ISyncLogRepository
{
    public async Task<SyncLog> CreateAsync(SyncLog log, CancellationToken ct = default)
    {
        var entity = new SyncLogEntity
        {
            FileName         = log.FileName,
            FileType         = log.FileType,
            Direction        = log.Direction.ToString(),
            Status           = log.Status.ToString(),
            RecordsProcessed = log.RecordsProcessed,
            RecordsFailed    = log.RecordsFailed,
            ErrorMessage     = log.ErrorMessage,
            StartedAt        = log.StartedAt,
            CompletedAt      = log.CompletedAt,
        };
        db.SyncLogs.Add(entity);
        await db.SaveChangesAsync(ct);
        log.Id = entity.Id;
        return log;
    }

    public async Task UpdateAsync(SyncLog log, CancellationToken ct = default)
    {
        var entity = await db.SyncLogs.FindAsync([log.Id], ct)
            ?? throw new InvalidOperationException($"SyncLog {log.Id} not found.");
        entity.Status           = log.Status.ToString();
        entity.RecordsProcessed = log.RecordsProcessed;
        entity.RecordsFailed    = log.RecordsFailed;
        entity.ErrorMessage     = log.ErrorMessage;
        entity.CompletedAt      = log.CompletedAt;
        await db.SaveChangesAsync(ct);
    }
}
