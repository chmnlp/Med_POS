using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BillKro.Sync.Data.Entities;

[Table("Parties")]
public class PartyEntity
{
    [Key]
    public int Id { get; set; }
    [MaxLength(10)]
    public string CompanyCode { get; set; } = string.Empty;
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(1)]
    public string Type { get; set; } = string.Empty;
    [MaxLength(50)]
    public string? AccountCode { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal OpeningBalance { get; set; }
    [MaxLength(2)]
    public string? DrCr { get; set; }
    [MaxLength(100)]
    public string? BankName { get; set; }
    [MaxLength(15)]
    public string? GstNumber { get; set; }
    [MaxLength(20)]
    public string? Phone { get; set; }
    [MaxLength(500)]
    public string? Address { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
