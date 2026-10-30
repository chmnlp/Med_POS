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

    /// <summary>Shown on held bills so other counters know where a bill came from.</summary>
    public string CounterName { get; set; } = Environment.MachineName;

    /// <summary>Printer for invoices; null means ask the first time.</summary>
    public string? PrinterName { get; set; }

    /// <summary>Thermal80 (80 mm roll) or A4.</summary>
    public string PaperSize { get; set; } = "Thermal80";

    public bool AutoPrint { get; set; } = true;

    /// <summary>Barcode label printer; null means ask the first time labels are printed.</summary>
    public string? LabelPrinterName { get; set; }

    public double LabelWidthMm { get; set; } = 50;
    public double LabelHeightMm { get; set; } = 25;

    /// <summary>Labels side by side on the roll (1–3), with this gap between them.</summary>
    public int LabelsAcross { get; set; } = 1;
    public double LabelGapMm { get; set; } = 2;

    public bool LabelShowStoreName { get; set; } = true;
    public bool LabelShowSaleRate { get; set; }

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
