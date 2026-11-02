namespace BillKro.Shared;

// ---------------------------------------------------------------- day end / counter closing

/// <summary>A cashier's day at the counter, worked out from their own bills, returns, receipts and payouts.</summary>
public record DayEndSummaryDto
{
    public DateOnly Date { get; init; }
    public int? UserId { get; init; }
    public string UserName { get; init; } = "All counters";
    public int Bills { get; init; }
    public int CancelledBills { get; init; }
    public decimal SalesTotal { get; init; }
    public decimal CashSales { get; init; }
    public decimal UpiSales { get; init; }
    public decimal CardSales { get; init; }
    public decimal CreditSales { get; init; }
    public int Returns { get; init; }
    public decimal CashRefunds { get; init; }
    public decimal AccountRefunds { get; init; }
    public decimal CashReceipts { get; init; }
    public decimal OtherReceipts { get; init; }
    public decimal CashPayouts { get; init; }

    /// <summary>Cash that should be in the drawer, before the opening float.</summary>
    public decimal NetCash => CashSales - CashRefunds + CashReceipts - CashPayouts;

    /// <summary>Set when this counter has already been closed for the day.</summary>
    public DayCloseDto? Closing { get; init; }
}

public record DayCloseRequest(DateOnly Date, decimal OpeningFloat, decimal CountedCash, string? Denominations, string? Note);

public record DayCloseDto(int Id, DateOnly Date, int UserId, string UserName, DateTime ClosedAt, decimal OpeningFloat,
    decimal NetCash, decimal ExpectedCash, decimal CountedCash, string? Denominations, string? Note, int Bills, decimal SalesTotal)
{
    /// <summary>Counted − expected: negative is a shortage.</summary>
    public decimal Difference => CountedCash - ExpectedCash;
}

/// <summary>Indian notes and coins, for counting the drawer.</summary>
public static class Denominations
{
    public static IReadOnlyList<int> All { get; } = [500, 200, 100, 50, 20, 10, 5, 2, 1];
}

// ---------------------------------------------------------------- sales summary

public record ModeTotal(string Mode, int Count, decimal Amount);
public record DaySales(DateOnly Date, int Bills, decimal Sales, decimal Returns)
{
    public decimal Net => Sales - Returns;
}
public record CashierSales(int UserId, string Name, int Bills, decimal Sales);
public record TopItem(int ItemId, string ItemName, decimal Qty, decimal Amount);

public record SalesSummaryReport
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public int Bills { get; init; }
    public decimal Gross { get; init; }
    public decimal Discount { get; init; }
    public decimal Taxable { get; init; }
    public decimal Tax { get; init; }
    public decimal Total { get; init; }
    public int CancelledBills { get; init; }
    public decimal CancelledValue { get; init; }
    public int Returns { get; init; }
    public decimal ReturnsValue { get; init; }
    public decimal Net => Total - ReturnsValue;
    public decimal AverageBill => Bills == 0 ? 0 : Math.Round(Total / Bills, 2);
    public List<ModeTotal> ByMode { get; init; } = [];
    public List<ModeTotal> ReceiptsByMode { get; init; } = [];
    public List<DaySales> ByDay { get; init; } = [];
    public List<CashierSales> ByCashier { get; init; } = [];
    public List<TopItem> TopItems { get; init; } = [];
}

// ---------------------------------------------------------------- GST (GSTR-1 working)

/// <summary>Invoice-wise, rate-wise outward supplies to registered buyers.</summary>
public record GstB2BRow(string Gstin, string? Name, string InvoiceNo, DateOnly Date, decimal InvoiceValue, string PlaceOfSupply,
    decimal Rate, decimal Taxable, decimal Igst, decimal Cgst, decimal Sgst);

/// <summary>Supplies to unregistered buyers, totalled by place of supply and rate.</summary>
public record GstB2CSRow(string PlaceOfSupply, decimal Rate, decimal Taxable, decimal Igst, decimal Cgst, decimal Sgst);

public record GstHsnRow(string Hsn, string Uqc, decimal Qty, decimal Rate, decimal Taxable, decimal Igst, decimal Cgst, decimal Sgst)
{
    public decimal Total => Taxable + Igst + Cgst + Sgst;
}

/// <summary>Credit notes (sales returns), rate-wise; GSTIN blank for unregistered buyers.</summary>
public record GstCreditNoteRow(string? Gstin, string? Name, string NoteNo, DateOnly NoteDate, string InvoiceNo, DateOnly InvoiceDate,
    decimal Rate, decimal Taxable, decimal Igst, decimal Cgst, decimal Sgst);

public record GstDocumentRow(string Nature, string? From, string? To, int Total, int Cancelled);

/// <summary>Tax on purchases (input tax credit to match against GSTR-2B); debit notes are negative.</summary>
public record GstPurchaseRow(string? SupplierGstin, string SupplierName, string Document, DateOnly Date, string Type,
    decimal Taxable, decimal Igst, decimal Cgst, decimal Sgst)
{
    public decimal Tax => Igst + Cgst + Sgst;
}

public record GstReport
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string? StoreGstin { get; init; }
    public List<GstB2BRow> B2B { get; init; } = [];
    public List<GstB2CSRow> B2CS { get; init; } = [];
    public List<GstHsnRow> Hsn { get; init; } = [];
    public List<GstCreditNoteRow> CreditNotes { get; init; } = [];
    public List<GstDocumentRow> Documents { get; init; } = [];
    public List<GstPurchaseRow> Purchases { get; init; } = [];

    public decimal OutputTax => B2B.Sum(r => r.Igst + r.Cgst + r.Sgst) + B2CS.Sum(r => r.Igst + r.Cgst + r.Sgst)
        - CreditNotes.Sum(r => r.Igst + r.Cgst + r.Sgst);
    public decimal InputTax => Purchases.Sum(r => r.Tax);
}
