namespace MargPos.Shared;

public enum PaymentMode { Cash = 0, Upi = 1, Card = 2, Credit = 3 }

public enum InvoiceStatus { Completed = 0, Cancelled = 1 }

/// <summary>How a return is settled: cash back at the counter, or reduce what the customer owes.</summary>
public enum RefundMode { Cash = 0, Account = 1 }

public record SaleLineRequest(int ItemId, int BatchId, decimal Qty, decimal Rate, decimal DiscountPercent);

public record PaymentRequest(PaymentMode Mode, decimal Amount, string? Reference = null);

public record SaleRequest
{
    /// <summary>Registered customer; null for a walk-in sale.</summary>
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }

    /// <summary>Required when the bill has Schedule H1 or X items.</summary>
    public string? DoctorName { get; init; }
    public string? PatientName { get; init; }

    public List<SaleLineRequest> Lines { get; init; } = [];
    public List<PaymentRequest> Payments { get; init; } = [];
}

public record SaleLineDto
{
    public int Id { get; init; }
    public int LineNo { get; init; }
    public int ItemId { get; init; }
    public int BatchId { get; init; }
    public string ItemCode { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string? Pack { get; init; }
    public string? HsnCode { get; init; }
    public string BatchNo { get; init; } = "";
    public DateOnly? ExpiryDate { get; init; }
    public decimal Mrp { get; init; }
    public decimal Qty { get; init; }
    public decimal Rate { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal GstRate { get; init; }
    public decimal Gross { get; init; }
    public decimal Discount { get; init; }
    public decimal Taxable { get; init; }
    public decimal Tax { get; init; }
    public decimal Amount { get; init; }
    public decimal ReturnedQty { get; init; }
}

public record PaymentDto(PaymentMode Mode, decimal Amount, string? Reference);

public record SaleInvoiceDto
{
    public int Id { get; init; }
    public string InvoiceNo { get; init; } = "";
    public DateTime InvoiceDate { get; init; }
    public InvoiceStatus Status { get; init; }
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string? CustomerGstin { get; init; }
    public CustomerType CustomerType { get; init; }
    public string? DoctorName { get; init; }
    public string? PatientName { get; init; }
    public bool IsInterState { get; init; }
    public decimal Gross { get; init; }
    public decimal Discount { get; init; }
    public decimal Taxable { get; init; }
    public decimal Cgst { get; init; }
    public decimal Sgst { get; init; }
    public decimal Igst { get; init; }
    public decimal RoundOff { get; init; }
    public decimal Total { get; init; }
    public decimal BalanceDue { get; init; }
    public string CashierName { get; init; } = "";
    public string? CancelReason { get; init; }
    public List<SaleLineDto> Lines { get; init; } = [];
    public List<PaymentDto> Payments { get; init; } = [];
    public List<GstSlab> Slabs { get; init; } = [];
    public List<SaleReturnSummaryDto> Returns { get; init; } = [];
}

public record SaleSummaryDto(
    int Id, string InvoiceNo, DateTime InvoiceDate, string? CustomerName, string? CustomerPhone,
    decimal Total, decimal BalanceDue, string PaymentModes, InvoiceStatus Status, bool HasReturns);

public record CancelSaleRequest(string Reason);

public record ReturnLineRequest(int InvoiceLineId, decimal Qty);

public record SaleReturnRequest(List<ReturnLineRequest> Lines, RefundMode RefundMode, string? Reason);

public record SaleReturnSummaryDto(int Id, string ReturnNo, DateTime ReturnDate, RefundMode RefundMode, decimal Total);

public record SaleReturnDto(
    int Id, string ReturnNo, DateTime ReturnDate, string InvoiceNo, RefundMode RefundMode,
    decimal Taxable, decimal Tax, decimal RoundOff, decimal Total);

/// <summary>A bill parked at a counter, resumable from any counter. Payload is opaque client JSON.</summary>
public record HeldBillDto(int Id, string Label, string Payload, string CreatedBy, DateTime CreatedAt);

public record HoldBillRequest(string Label, string Payload);

public record SellableBatchDto
{
    public int Id { get; init; }
    public string BatchNo { get; init; } = "";
    public DateOnly? ExpiryDate { get; init; }
    public decimal Mrp { get; init; }
    public decimal SaleRate { get; init; }

    /// <summary>Lot wholesale / hospital rates with the item's rates filled in where the lot has none.</summary>
    public decimal WholesaleRate { get; init; }
    public decimal HospitalRate { get; init; }
    public decimal QtyOnHand { get; init; }

    public decimal RateFor(CustomerType type) => Pricing.DefaultRate(type, Mrp, SaleRate, WholesaleRate, HospitalRate);
}

public record CustomerBalanceDto(int CustomerId, decimal CreditLimit, decimal Outstanding)
{
    public decimal Available => Math.Max(0, CreditLimit - Outstanding);
}

public record StoreSettingsDto
{
    public string StoreName { get; init; } = "My Medical Store";
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gstin { get; init; }
    public string? DrugLicenseNo { get; init; }

    /// <summary>Two-digit GST state code; taken from the GSTIN when blank.</summary>
    public string? StateCode { get; init; }
    public string InvoicePrefix { get; init; } = "S";
    public string? InvoiceFooter { get; init; }
    public decimal MaxCashierDiscountPercent { get; init; } = 10;
}
