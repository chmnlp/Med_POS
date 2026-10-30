using MargPos.Shared;

namespace MargPos.Data;

public class SaleInvoice : EntityBase
{
    public string InvoiceNo { get; set; } = "";
    public string FinancialYear { get; set; } = "";

    /// <summary>Shop-local date and time of the sale.</summary>
    public DateTime InvoiceDate { get; set; }
    public InvoiceStatus Status { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // Snapshot of buyer details as printed on the invoice.
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerGstin { get; set; }
    public CustomerType CustomerType { get; set; }
    public string? DoctorName { get; set; }
    public string? PatientName { get; set; }

    public bool IsInterState { get; set; }
    public decimal Gross { get; set; }
    public decimal Discount { get; set; }
    public decimal Taxable { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public decimal RoundOff { get; set; }
    public decimal Total { get; set; }

    /// <summary>Portion paid on credit (added to the customer's outstanding).</summary>
    public decimal BalanceDue { get; set; }

    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;

    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int? CancelledByUserId { get; set; }

    public List<SaleInvoiceLine> Lines { get; set; } = [];
    public List<SalePayment> Payments { get; set; } = [];
    public List<SaleReturn> Returns { get; set; } = [];
}

public class SaleInvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public SaleInvoice Invoice { get; set; } = null!;
    public int LineNo { get; set; }

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;

    // Snapshot as sold.
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string? Pack { get; set; }
    public string? HsnCode { get; set; }
    public string BatchNo { get; set; } = "";
    public DateOnly? ExpiryDate { get; set; }
    public DrugSchedule Schedule { get; set; }

    public decimal Mrp { get; set; }
    public decimal Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal GstRate { get; set; }
    public decimal Gross { get; set; }
    public decimal Discount { get; set; }
    public decimal Taxable { get; set; }
    public decimal Tax { get; set; }
    public decimal Amount { get; set; }
    public decimal ReturnedQty { get; set; }

    /// <summary>Lot landed cost per unit at the moment of sale, for profit.</summary>
    public decimal CostRate { get; set; }
}

public class SalePayment
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public PaymentMode Mode { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
}

public class SaleReturn : EntityBase
{
    public string ReturnNo { get; set; } = "";
    public string FinancialYear { get; set; } = "";
    public DateTime ReturnDate { get; set; }
    public int InvoiceId { get; set; }
    public SaleInvoice Invoice { get; set; } = null!;
    public RefundMode RefundMode { get; set; }
    public string? Reason { get; set; }
    public decimal Taxable { get; set; }
    public decimal Tax { get; set; }
    public decimal RoundOff { get; set; }
    public decimal Total { get; set; }
    public int CreatedByUserId { get; set; }
    public List<SaleReturnLine> Lines { get; set; } = [];
}

public class SaleReturnLine
{
    public int Id { get; set; }
    public int ReturnId { get; set; }
    public int InvoiceLineId { get; set; }
    public SaleInvoiceLine InvoiceLine { get; set; } = null!;
    public decimal Qty { get; set; }
    public decimal Taxable { get; set; }
    public decimal Tax { get; set; }
    public decimal Amount { get; set; }
}

public enum StockMovementType
{
    Opening = 0, Sale = 1, SaleReturn = 2, SaleCancel = 3, Purchase = 4, PurchaseCancel = 5, Adjustment = 6, PurchaseReturn = 7,
}

/// <summary>Append-only ledger of every stock change. Sum of QtyChange per batch equals Batch.QtyOnHand.</summary>
public class StockMovement
{
    public long Id { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;
    public decimal QtyChange { get; set; }
    public StockMovementType Type { get; set; }
    public string? RefNo { get; set; }
    public string? Note { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Running document number per kind (sale, credit note) and financial year.</summary>
public class DocumentSequence
{
    public string Kind { get; set; } = "";
    public string FinancialYear { get; set; } = "";
    public int LastNumber { get; set; }
}

/// <summary>Single-row store profile printed on invoices.</summary>
public class StoreSettings
{
    public int Id { get; set; } = 1;
    public string StoreName { get; set; } = "My Medical Store";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Gstin { get; set; }
    public string? DrugLicenseNo { get; set; }
    public string? StateCode { get; set; }
    public string InvoicePrefix { get; set; } = "S";
    public string? InvoiceFooter { get; set; }
    public decimal MaxCashierDiscountPercent { get; set; } = 10;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>State code used for CGST/SGST vs IGST: explicit setting, else from the store GSTIN.</summary>
    public string? EffectiveStateCode =>
        !string.IsNullOrWhiteSpace(StateCode) ? StateCode : Gstin is { Length: >= 2 } g ? g[..2] : null;
}

public class HeldBill
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public string Payload { get; set; } = "";
    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
