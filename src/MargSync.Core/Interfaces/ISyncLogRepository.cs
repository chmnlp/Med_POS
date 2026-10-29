using MargSync.Core.Models;

namespace MargSync.Core.Interfaces;

public interface ISyncLogRepository
{
    Task<SyncLog> CreateAsync(SyncLog log, CancellationToken ct = default);
    Task UpdateAsync(SyncLog log, CancellationToken ct = default);
}
