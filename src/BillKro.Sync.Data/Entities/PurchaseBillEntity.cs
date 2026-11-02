using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BillKro.Sync.Data.Entities;

[Table("PurchaseBills")]
public class PurchaseBillEntity
{
    [Key]
    public int Id { get; set; }
    [MaxLength(10)]
    public string CompanyCode { get; set; } = string.Empty;
    [MaxLength(50)]
    public string BillNumber { get; set; } = string.Empty;
    [MaxLength(50)]
    public string SupplierCode { get; set; } = string.Empty;
    [MaxLength(200)]
    public string SupplierName { get; set; } = string.Empty;
    public DateOnly BillDate { get; set; }
    [MaxLength(50)]
    public string? DeliveryMode { get; set; }
    [MaxLength(100)]
    public string? DeliveryLocation { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalTax { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal NetAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<PurchaseBillLineEntity> Lines { get; set; } = [];
}

[Table("PurchaseBillLines")]
public class PurchaseBillLineEntity
{
    [Key]
    public int Id { get; set; }
    public int PurchaseBillId { get; set; }
    public PurchaseBillEntity Bill { get; set; } = null!;
    [MaxLength(50)]
    public string ItemCode { get; set; } = string.Empty;
    [MaxLength(200)]
    public string ItemName { get; set; } = string.Empty;
    [MaxLength(10)]
    public string? PackForm { get; set; }
    [MaxLength(200)]
    public string? Manufacturer { get; set; }
    [MaxLength(30)]
    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(5,2)")]
    public decimal TaxRate { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal PurchaseRate { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal CostRate { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal MRP { get; set; }
    [Column(TypeName = "decimal(5,2)")]
    public decimal Discount { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal TaxAmount { get; set; }
}
