namespace MargSync.Core.Models;

public class Party
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>S = Supplier, C = Customer, B = Both</summary>
    public string Type { get; set; } = string.Empty;
    public string? AccountCode { get; set; }
    public decimal OpeningBalance { get; set; }
    public string? DrCr { get; set; }
    public string? BankName { get; set; }
    public string? GstNumber { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
}
