namespace MargSync.Core.Models;

public class PurchaseBill
{
    public string BillNumber { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateOnly BillDate { get; set; }
    public string? DeliveryMode { get; set; }
    public string? DeliveryLocation { get; set; }
    public List<PurchaseBillLine> Lines { get; set; } = [];
    public decimal TotalAmount { get; set; }
    public decimal TotalTax { get; set; }
    public decimal NetAmount { get; set; }
}

public class PurchaseBillLine
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? PackForm { get; set; }
    public string? Manufacturer { get; set; }
    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal FreeQuantity { get; set; }
    public decimal TaxRate { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal CostRate { get; set; }
    public decimal MRP { get; set; }
    public decimal Discount { get; set; }
    public decimal TaxAmount { get; set; }
}
