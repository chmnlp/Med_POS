namespace BillKro.Shared;

public enum PurchaseStatus { Completed = 0, Cancelled = 1 }

public enum AdjustmentReason { Damaged = 0, Expired = 1, Lost = 2, CountCorrection = 3, ReturnToSupplier = 4, Other = 5 }

/// <summary>Purchase line as on a distributor bill: rate excludes GST.</summary>
public record PurchaseLineRequest
{
    public int ItemId { get; init; }
    public string? BatchNo { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public DateOnly? MfgDate { get; init; }
    public decimal Qty { get; init; }
    public decimal FreeQty { get; init; }
    public decimal Rate { get; init; }
    public decimal DiscountPercent { get; init; }

    /// <summary>GST charged by the supplier; null uses the item's GST rate.</summary>
    public decimal? GstRate { get; init; }

    /// <summary>
    /// MRP and selling rates for the lot; 0 uses the item's current rates. A different MRP from an existing lot of
    /// the same batch number creates a new lot (e.g. PC24A/2) so old stock keeps its price.
    /// </summary>
    public decimal Mrp { get; init; }
    public decimal SaleRate { get; init; }
    public decimal WholesaleRate { get; init; }
    public decimal HospitalRate { get; init; }

    /// <summary>When the sale rate is 0: price at this margin on landed cost (falls back to the item's margin).</summary>
    public decimal? MarginPercent { get; init; }

    /// <summary>Distributor's code for the item (from EDE); remembered so future imports match automatically.</summary>
    public string? SupplierItemCode { get; init; }
}

public record PurchaseRequest
{
    public int SupplierId { get; init; }
    public string SupplierInvoiceNo { get; init; } = "";
    public DateOnly SupplierInvoiceDate { get; init; }
    public string? Notes { get; init; }
    public List<PurchaseLineRequest> Lines { get; init; } = [];
}

public record PurchaseLineDto
{
    public int Id { get; init; }
    public int ItemId { get; init; }
    public string ItemCode { get; init; } = "";
    public string ItemName { get; init; } = "";
    public int BatchId { get; init; }
    public string BatchNo { get; init; } = "";
    public DateOnly? ExpiryDate { get; init; }
    public decimal Qty { get; init; }
    public decimal FreeQty { get; init; }
    public decimal Rate { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal GstRate { get; init; }
    public decimal Mrp { get; init; }
    public decimal SaleRate { get; init; }
    public decimal WholesaleRate { get; init; }
    public decimal HospitalRate { get; init; }
    public decimal LandingCost { get; init; }
    public decimal? MarginPercent { get; init; }
    public decimal Taxable { get; init; }
    public decimal Tax { get; init; }
    public decimal Amount { get; init; }
}

public record PurchaseDto
{
    public int Id { get; init; }
    public string PurchaseNo { get; init; } = "";
    public PurchaseStatus Status { get; init; }
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = "";
    public string SupplierInvoiceNo { get; init; } = "";
    public DateOnly SupplierInvoiceDate { get; init; }
    public DateTime ReceivedAt { get; init; }
    public string? Notes { get; init; }
    public decimal Taxable { get; init; }
    public decimal Tax { get; init; }
    public decimal RoundOff { get; init; }
    public decimal Total { get; init; }
    public string ReceivedBy { get; init; } = "";
    public string? CancelReason { get; init; }
    public List<PurchaseLineDto> Lines { get; init; } = [];
}

public record PurchaseSummaryDto(int Id, string PurchaseNo, string SupplierName, string SupplierInvoiceNo,
    DateOnly SupplierInvoiceDate, DateTime ReceivedAt, int LineCount, decimal Total, PurchaseStatus Status);

public record CancelPurchaseRequest(string Reason);

public record EdeImportLineDto
{
    public string SupplierItemCode { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string? Pack { get; init; }
    public string? Manufacturer { get; init; }
    public string? BatchNo { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public decimal Qty { get; init; }
    public decimal FreeQty { get; init; }
    public decimal Rate { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal GstRate { get; init; }
    public decimal Mrp { get; init; }

    /// <summary>The POS item this line was matched to, if any.</summary>
    public ItemDto? MatchedItem { get; init; }

    /// <summary>How the match was made: "remembered code", "item code", "barcode", "name", or empty.</summary>
    public string MatchedBy { get; init; } = "";
}

public record EdeImportDto
{
    public string SupplierInvoiceNo { get; init; } = "";
    public DateOnly? SupplierInvoiceDate { get; init; }
    public string? SupplierNameInFile { get; init; }
    public decimal FileTotal { get; init; }
    public List<EdeImportLineDto> Lines { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public record StockAdjustmentRequest(int BatchId, decimal QtyChange, AdjustmentReason Reason, string? Note, int? SupplierId = null);

public record StockAdjustmentDto(int Id, string AdjustmentNo, DateTime Date, string ItemName, string BatchNo,
    decimal QtyChange, AdjustmentReason Reason, string? Note, string? SupplierName, string User);

public record StockMovementDto(long Id, DateTime At, string Type, string BatchNo, decimal QtyChange, decimal Balance,
    string? RefNo, string? Note, string? User);

public record ExpiryReportRow(int ItemId, string ItemCode, string ItemName, string? Manufacturer, int BatchId, string BatchNo,
    DateOnly ExpiryDate, int DaysLeft, decimal Qty, decimal PurchaseRate, decimal Mrp, decimal PurchaseValue);

public record ReorderReportRow(int ItemId, string ItemCode, string ItemName, string? Manufacturer, decimal Stock,
    decimal ReorderLevel, decimal SuggestedQty, string? LastSupplier, decimal? LastPurchaseRate);

public record StockValuationRow(int ItemId, string ItemCode, string ItemName, string? Manufacturer, decimal Qty,
    decimal PurchaseValue, decimal MrpValue);

public record StockValuationReport(List<StockValuationRow> Rows, int Total, int Page, int PageSize,
    decimal TotalQty, decimal TotalPurchaseValue, decimal TotalMrpValue);

public record SupplierBalanceDto(int SupplierId, decimal Outstanding);

// ---------------------------------------------------------------- purchase returns (debit notes)

public record PurchaseReturnLineRequest
{
    public int BatchId { get; init; }
    public decimal Qty { get; init; }

    /// <summary>Credit rate per unit, ex GST; null uses the lot's landed cost.</summary>
    public decimal? Rate { get; init; }

    /// <summary>GST on the return; null uses the original purchase line's rate, else the item's.</summary>
    public decimal? GstRate { get; init; }

    /// <summary>Original purchase line, when returning against a specific purchase.</summary>
    public int? PurchaseLineId { get; init; }
}

public record PurchaseReturnRequest
{
    public int SupplierId { get; init; }
    public int? PurchaseId { get; init; }
    public string? Reason { get; init; }
    public List<PurchaseReturnLineRequest> Lines { get; init; } = [];
}

public record PurchaseReturnLineDto(int Id, int ItemId, string ItemName, int BatchId, string BatchNo, DateOnly? ExpiryDate,
    decimal Qty, decimal Rate, decimal GstRate, decimal Taxable, decimal Tax, decimal Amount);

public record PurchaseReturnDto
{
    public int Id { get; init; }
    public string ReturnNo { get; init; } = "";
    public DateTime ReturnDate { get; init; }
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = "";
    public string? SupplierGstin { get; init; }
    public string? PurchaseNo { get; init; }
    public string? SupplierInvoiceNo { get; init; }
    public string? Reason { get; init; }
    public decimal Taxable { get; init; }
    public decimal Tax { get; init; }
    public decimal RoundOff { get; init; }
    public decimal Total { get; init; }
    public string CreatedBy { get; init; } = "";
    public List<PurchaseReturnLineDto> Lines { get; init; } = [];
}

public record PurchaseReturnSummaryDto(int Id, string ReturnNo, DateTime ReturnDate, string SupplierName, string? PurchaseNo,
    int LineCount, decimal Total);

/// <summary>A lot that can go back to a supplier (expired or near expiry, bought from them).</summary>
public record ReturnCandidateDto(int ItemId, string ItemName, string? Pack, int BatchId, string BatchNo, DateOnly? ExpiryDate,
    decimal QtyOnHand, decimal LandingCost, decimal GstRate);

/// <summary>What can still be returned from one line of a purchase.</summary>
public record ReturnablePurchaseLineDto(int PurchaseLineId, int ItemId, string ItemName, int BatchId, string BatchNo,
    DateOnly? ExpiryDate, decimal Received, decimal AlreadyReturned, decimal InStock, decimal LandingCost, decimal GstRate);

// ---------------------------------------------------------------- profit

public enum ProfitGrouping { Day = 0, Item = 1 }

/// <summary>Revenue is ex GST (taxable value) net of returns; cost is the lot cost recorded at sale.</summary>
public record ProfitRow(string Key, string Label, decimal Qty, decimal Revenue, decimal Cost)
{
    public decimal Profit => Revenue - Cost;
    public decimal? MarginPercent => Revenue == 0 ? null : Math.Round(Profit / Revenue * 100m, 2);
}

public record ProfitReport(List<ProfitRow> Rows, decimal Revenue, decimal Cost)
{
    public decimal Profit => Revenue - Cost;
    public decimal? MarginPercent => Revenue == 0 ? null : Math.Round(Profit / Revenue * 100m, 2);
}

/// <summary>Purchase maths: rates exclude GST; GST is added per line; bill total rounded to the rupee.</summary>
public static class PurchaseCalculator
{
    public record Line(decimal Taxable, decimal Tax, decimal Amount);

    public static Line CalculateLine(decimal qty, decimal rate, decimal discountPercent, decimal gstRate)
    {
        var taxable = BillCalculator.Round2(qty * rate * (100m - discountPercent) / 100m);
        var tax = BillCalculator.Round2(taxable * gstRate / 100m);
        return new Line(taxable, tax, taxable + tax);
    }

    /// <summary>Cost per unit received (free goods lower it), excluding GST.</summary>
    public static decimal LandingCost(decimal taxable, decimal qty, decimal freeQty) =>
        qty + freeQty == 0 ? 0 : Math.Round(taxable / (qty + freeQty), 4, MidpointRounding.AwayFromZero);

    public static (decimal Taxable, decimal Tax, decimal RoundOff, decimal Total) Totals(IEnumerable<Line> lines)
    {
        var list = lines.ToList();
        var taxable = list.Sum(l => l.Taxable);
        var tax = list.Sum(l => l.Tax);
        var net = taxable + tax;
        var total = Math.Round(net, 0, MidpointRounding.AwayFromZero);
        return (taxable, tax, total - net, total);
    }
}
