using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MargSync.Data.Entities;

[Table("Companies")]
public class CompanyEntity
{
    [Key, MaxLength(10)]
    public string Code { get; set; } = string.Empty;
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(500)]
    public string? Address { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
