namespace MargSync.Core.Models;

public class Item
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PackForm { get; set; }
    public string? Manufacturer { get; set; }
    public string? AccountCode { get; set; }
    public string? HsnCode { get; set; }
    public decimal MRP { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal LandingCost { get; set; }
    public decimal TaxRate { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal OpeningQty { get; set; }
    public decimal PackSize { get; set; }
    public string? BatchNo { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Category { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
}
