using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MargSync.Data.Entities;

[Table("SalesInvoices")]
public class SalesInvoiceEntity
{
    [Key]
    public int Id { get; set; }
    [MaxLength(10)]
    public string CompanyCode { get; set; } = string.Empty;
    [MaxLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;
    [MaxLength(50)]
    public string CustomerCode { get; set; } = string.Empty;
    [MaxLength(200)]
    public string CustomerName { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalDiscount { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal TotalTax { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal NetAmount { get; set; }
    [MaxLength(100)]
    public string? IrnNumber { get; set; }
    [MaxLength(50)]
    public string? EwayBillNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<SalesInvoiceLineEntity> Lines { get; set; } = [];
}

[Table("SalesInvoiceLines")]
public class SalesInvoiceLineEntity
{
    [Key]
    public int Id { get; set; }
    public int SalesInvoiceId { get; set; }
    public SalesInvoiceEntity Invoice { get; set; } = null!;
    [MaxLength(50)]
    public string ItemCode { get; set; } = string.Empty;
    [MaxLength(200)]
    public string ItemName { get; set; } = string.Empty;
    [MaxLength(30)]
    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal Rate { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal MRP { get; set; }
    [Column(TypeName = "decimal(5,2)")]
    public decimal Discount { get; set; }
    [Column(TypeName = "decimal(5,2)")]
    public decimal TaxRate { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal TaxAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal LineTotal { get; set; }
}
