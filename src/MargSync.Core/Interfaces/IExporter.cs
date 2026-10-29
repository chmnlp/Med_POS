namespace MargSync.Core.Interfaces;

/// <summary>Generates MARG-compatible CSV import files for write-back.</summary>
public interface IExporter<T>
{
    Task ExportAsync(IEnumerable<T> records, string outputPath, CancellationToken ct = default);
}
