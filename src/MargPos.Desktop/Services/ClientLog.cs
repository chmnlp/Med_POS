using System.IO;

namespace MargPos.Desktop.Services;

/// <summary>Append-only error log at %LocalAppData%\MargPos\logs\client-yyyyMMdd.log for support.</summary>
public static class ClientLog
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MargPos", "logs");

    public static string FilePath => Path.Combine(Folder, $"client-{DateTime.Now:yyyyMMdd}.log");

    public static void Error(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{context}] {ex}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
    }
}
