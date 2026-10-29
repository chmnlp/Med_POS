using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MargSync.Data.Entities;

[Table("GstEntries")]
public class GstEntryEntity
{
    [Key]
    public int Id { get; set; }
    [MaxLength(10)]
    public string CompanyCode { get; set; } = string.Empty;
    [MaxLength(50)]
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    [MaxLength(15)]
    public string GstinSupplier { get; set; } = string.Empty;
    [MaxLength(15)]
    public string? GstinRecipient { get; set; }
    [MaxLength(8)]
    public string? HsnCode { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal TaxableValue { get; set; }
    [Column(TypeName = "decimal(5,2)")]
    public decimal CgstRate { get; set; }
    [Column(TypeName = "decimal(5,2)")]
    public decimal SgstRate { get; set; }
    [Column(TypeName = "decimal(5,2)")]
    public decimal IgstRate { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal CgstAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal SgstAmount { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal IgstAmount { get; set; }
    [MaxLength(100)]
    public string? IrnNumber { get; set; }
    [MaxLength(6)]
    public string ReturnPeriod { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
