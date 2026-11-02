namespace BillKro.Sync.Core.Models;

public class GstEntry
{
    public string CompanyCode { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public string GstinSupplier { get; set; } = string.Empty;
    public string? GstinRecipient { get; set; }
    public string? HsnCode { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal CgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal IgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public string? IrnNumber { get; set; }
    public string ReturnPeriod { get; set; } = string.Empty; // MMYYYY
}
