using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BillKro.Sync.Data.Entities;

[Table("Items")]
public class ItemEntity
{
    [Key]
    public int Id { get; set; }
    [MaxLength(10)]
    public string CompanyCode { get; set; } = string.Empty;
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(10)]
    public string? PackForm { get; set; }
    [MaxLength(200)]
    public string? Manufacturer { get; set; }
    [MaxLength(50)]
    public string? AccountCode { get; set; }
    [MaxLength(8)]
    public string? HsnCode { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal MRP { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal PurchaseRate { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal LandingCost { get; set; }
    [Column(TypeName = "decimal(5,2)")]
    public decimal TaxRate { get; set; }
    [Column(TypeName = "decimal(5,2)")]
    public decimal DiscountPercent { get; set; }
    [Column(TypeName = "decimal(18,3)")]
    public decimal OpeningQty { get; set; }
    [Column(TypeName = "decimal(18,3)")]
    public decimal PackSize { get; set; }
    [MaxLength(30)]
    public string? BatchNo { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    [MaxLength(50)]
    public string? Category { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
