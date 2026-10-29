namespace MargSync.Core.Interfaces;

public interface IRepository<T> where T : class
{
    Task UpsertAsync(IEnumerable<T> records, CancellationToken ct = default);
}
