using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MargSync.Data.Entities;

[Table("LedgerEntries")]
public class LedgerEntryEntity
{
    [Key]
    public int Id { get; set; }
    [MaxLength(10)]
    public string CompanyCode { get; set; } = string.Empty;
    [MaxLength(50)]
    public string AccountCode { get; set; } = string.Empty;
    [MaxLength(200)]
    public string AccountName { get; set; } = string.Empty;
    public DateOnly EntryDate { get; set; }
    [MaxLength(30)]
    public string VoucherType { get; set; } = string.Empty;
    [MaxLength(50)]
    public string VoucherNumber { get; set; } = string.Empty;
    [MaxLength(500)]
    public string? Narration { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal DebitAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal CreditAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
