namespace BillKro.Sync.Core.Interfaces;

public interface IParser<T>
{
    Task<IReadOnlyList<T>> ParseAsync(string filePath, CancellationToken ct = default);
    bool CanParse(string filePath);
}
