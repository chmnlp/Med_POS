using System.IO;
using System.Text.Json;

namespace MargPos.Desktop.Services;

/// <summary>Per-PC settings stored under %LocalAppData%\MargPos\client.json.</summary>
public class ClientSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MargPos", "client.json");

    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string? LastUsername { get; set; }

    /// <summary>System, Light or Dark.</summary>
    public string Theme { get; set; } = "System";

    public static ClientSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings fall back to defaults.
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
