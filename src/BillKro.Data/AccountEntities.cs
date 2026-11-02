using BillKro.Shared;

namespace BillKro.Data;

/// <summary>Money received from a customer against their account.</summary>
public class CustomerReceipt : EntityBase
{
    public string ReceiptNo { get; set; } = "";
    public string FinancialYear { get; set; } = "";
    public DateTime Date { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public decimal Amount { get; set; }
    public AccountPaymentMode Mode { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public AccountEntryStatus Status { get; set; }
    public string? CancelReason { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;
}

/// <summary>Money paid to a supplier against what we owe them.</summary>
public class SupplierPayment : EntityBase
{
    public string PaymentNo { get; set; } = "";
    public string FinancialYear { get; set; } = "";
    public DateTime Date { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public decimal Amount { get; set; }
    public AccountPaymentMode Mode { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public AccountEntryStatus Status { get; set; }
    public string? CancelReason { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;
}