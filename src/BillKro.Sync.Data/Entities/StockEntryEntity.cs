using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BillKro.Sync.Data.Entities;

[Table("StockEntries")]
public class StockEntryEntity
{
    [Key]
    public int Id { get; set; }
    [MaxLength(10)]
    public string CompanyCode { get; set; } = string.Empty;
    [MaxLength(50)]
    public string ItemCode { get; set; } = string.Empty;
    [MaxLength(200)]
    public string ItemName { get; set; } = string.Empty;
    [MaxLength(30)]
    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    [Column(TypeName = "decimal(18,3)")]
    public decimal ClosingQty { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal ClosingValue { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal MRP { get; set; }
    public DateOnly AsOfDate { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
