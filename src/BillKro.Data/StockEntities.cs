using BillKro.Shared;

namespace BillKro.Data;

/// <summary>Goods received from a supplier against their invoice.</summary>
public class Purchase : EntityBase
{
    public string PurchaseNo { get; set; } = "";
    public string FinancialYear { get; set; } = "";
    public PurchaseStatus Status { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string SupplierInvoiceNo { get; set; } = "";
    public DateOnly SupplierInvoiceDate { get; set; }

    /// <summary>Shop-local time the goods were entered.</summary>
    public DateTime ReceivedAt { get; set; }
    public string? Notes { get; set; }
    public decimal Taxable { get; set; }
    public decimal Tax { get; set; }
    public decimal RoundOff { get; set; }
    public decimal Total { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public List<PurchaseLine> Lines { get; set; } = [];
}

public class PurchaseLine
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public Purchase Purchase { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public DateOnly? ExpiryDate { get; set; }
    public decimal Qty { get; set; }
    public decimal FreeQty { get; set; }
    public decimal Rate { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal GstRate { get; set; }
    public decimal Mrp { get; set; }
    public decimal SaleRate { get; set; }
    public decimal WholesaleRate { get; set; }
    public decimal HospitalRate { get; set; }

    /// <summary>Cost per unit received after discount, spread over free goods (ex GST).</summary>
    public decimal LandingCost { get; set; }
    public decimal? MarginPercent { get; set; }
    public decimal Taxable { get; set; }
    public decimal Tax { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Goods sent back to a supplier (expiry, breakage, wrong supply); the document is the debit note.</summary>
public class PurchaseReturn : EntityBase
{
    public string ReturnNo { get; set; } = "";
    public string FinancialYear { get; set; } = "";
    public DateTime ReturnDate { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public int? PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }
    public string? Reason { get; set; }
    public decimal Taxable { get; set; }
    public decimal Tax { get; set; }
    public decimal RoundOff { get; set; }
    public decimal Total { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedBy { get; set; } = null!;
    public List<PurchaseReturnLine> Lines { get; set; } = [];
}

public class PurchaseReturnLine
{
    public int Id { get; set; }
    public int ReturnId { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;
    public int? PurchaseLineId { get; set; }
    public PurchaseLine? PurchaseLine { get; set; }
    public string ItemName { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public DateOnly? ExpiryDate { get; set; }
    public decimal Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal GstRate { get; set; }
    public decimal Taxable { get; set; }
    public decimal Tax { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>A recorded stock correction on one batch (damage, expiry write-off, count difference, return to supplier).</summary>
public class StockAdjustment
{
    public int Id { get; set; }
    public string AdjustmentNo { get; set; } = "";
    public DateTime Date { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;
    public decimal QtyChange { get; set; }
    public AdjustmentReason Reason { get; set; }
    public string? Note { get; set; }
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}

/// <summary>Distributor's item code mapped to our item (MARG's MALTER), learned from EDE imports.</summary>
public class ItemAlias
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public string SupplierItemCode { get; set; } = "";
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
}
