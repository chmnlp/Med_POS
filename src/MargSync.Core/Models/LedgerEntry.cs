namespace MargSync.Core.Models;

public class LedgerEntry
{
    public string CompanyCode { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public DateOnly EntryDate { get; set; }
    public string VoucherType { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string? Narration { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
}
