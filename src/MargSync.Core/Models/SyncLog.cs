namespace MargSync.Core.Models;

public enum SyncStatus { Pending, Success, Failed, Skipped }
public enum SyncDirection { MargToSql, SqlToMarg }

public class SyncLog
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public SyncDirection Direction { get; set; }
    public SyncStatus Status { get; set; }
    public int RecordsProcessed { get; set; }
    public int RecordsFailed { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
