using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MargSync.Data.Entities;

[Table("SyncLogs")]
public class SyncLogEntity
{
    [Key]
    public long Id { get; set; }
    [MaxLength(500)]
    public string FileName { get; set; } = string.Empty;
    [MaxLength(30)]
    public string FileType { get; set; } = string.Empty;
    [MaxLength(20)]
    public string Direction { get; set; } = string.Empty;
    [MaxLength(20)]
    public string Status { get; set; } = string.Empty;
    public int RecordsProcessed { get; set; }
    public int RecordsFailed { get; set; }
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
