namespace MargSync.Core.Models;

public class StockEntry
{
    public string CompanyCode { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal ClosingQty { get; set; }
    public decimal ClosingValue { get; set; }
    public decimal MRP { get; set; }
    public DateOnly AsOfDate { get; set; }
}
