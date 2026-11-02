using BillKro.Sync.Core.Models;

namespace BillKro.Sync.Core.Interfaces;

public interface ISyncLogRepository
{
    Task<SyncLog> CreateAsync(SyncLog log, CancellationToken ct = default);
    Task UpdateAsync(SyncLog log, CancellationToken ct = default);
}
