namespace MargSync.Core.Configuration;

public class SyncOptions
{
    public const string Section = "Sync";

    /// <summary>Folder where MARG exports CSV files (shared drive path).</summary>
    public string MargExportFolder { get; set; } = string.Empty;

    /// <summary>Folder where we drop CSV files for MARG to import (Method\ folder).</summary>
    public string MargImportFolder { get; set; } = string.Empty;

    /// <summary>Folder where processed files are archived.</summary>
    public string ArchiveFolder { get; set; } = string.Empty;

    /// <summary>Folder for files that failed to parse.</summary>
    public string ErrorFolder { get; set; } = string.Empty;

    /// <summary>How often to poll the export folder (seconds).</summary>
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>File patterns to watch, comma-separated (e.g. "*.csv,*.txt").</summary>
    public string FilePatterns { get; set; } = "*.csv";

    public string SqlConnectionString { get; set; } = string.Empty;
}
