namespace BillKro.Sync.Core.Models;

public class SalesInvoice
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public List<SalesInvoiceLine> Lines { get; set; } = [];
    public decimal TotalAmount { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalTax { get; set; }
    public decimal NetAmount { get; set; }
    public string? IrnNumber { get; set; }
    public string? EwayBillNumber { get; set; }
}

public class SalesInvoiceLine
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? BatchNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal MRP { get; set; }
    public decimal Discount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}
