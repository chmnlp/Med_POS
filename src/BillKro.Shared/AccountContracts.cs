namespace BillKro.Shared;

/// <summary>How money moves on an account (receipts from customers, payments to suppliers).</summary>
public enum AccountPaymentMode { Cash = 0, Upi = 1, Card = 2, Cheque = 3, BankTransfer = 4 }

public enum AccountEntryStatus { Active = 0, Cancelled = 1 }

public static class PaymentModeText
{
    public static string Of(AccountPaymentMode mode) => mode switch
    {
        AccountPaymentMode.Upi => "UPI",
        AccountPaymentMode.BankTransfer => "Bank transfer",
        _ => mode.ToString(),
    };
}

public record ReceiptRequest(int CustomerId, decimal Amount, AccountPaymentMode Mode, string? Reference, string? Note);

public record ReceiptDto(int Id, string ReceiptNo, DateTime Date, int CustomerId, string CustomerName, string? CustomerPhone,
    decimal Amount, AccountPaymentMode Mode, string? Reference, string? Note, AccountEntryStatus Status, string? CancelReason,
    string CreatedBy, decimal BalanceAfter);

public record SupplierPaymentRequest(int SupplierId, decimal Amount, AccountPaymentMode Mode, string? Reference, string? Note);

public record SupplierPaymentDto(int Id, string PaymentNo, DateTime Date, int SupplierId, string SupplierName, decimal Amount,
    AccountPaymentMode Mode, string? Reference, string? Note, AccountEntryStatus Status, string? CancelReason, string CreatedBy);

public record CancelEntryRequest(string Reason);

/// <summary>One line of an account statement. For customers balance is what they owe; for suppliers, what we owe.</summary>
public record StatementEntry(DateTime Date, string Type, string RefNo, string? Description, decimal Debit, decimal Credit, decimal Balance);

public record StatementDto(int PartyId, string PartyName, string? Phone, string? Gstin, DateOnly From, DateOnly To,
    decimal Opening, List<StatementEntry> Entries, decimal Closing);

/// <summary>A bill not yet fully paid (payments clear the oldest bills first).</summary>
public record OpenItemDto(string RefNo, DateOnly Date, DateOnly DueDate, decimal Amount, decimal Open, int DaysOverdue);

public record AgeingRow(int PartyId, string Name, string? Phone, int CreditDays, decimal CreditLimit, decimal Outstanding,
    decimal NotDue, decimal Overdue1To30, decimal Overdue31To60, decimal Overdue61To90, decimal Overdue90Plus, DateOnly? OldestDue)
{
    public decimal Overdue => Overdue1To30 + Overdue31To60 + Overdue61To90 + Overdue90Plus;
}

public record AgeingReport(DateOnly AsOf, List<AgeingRow> Rows)
{
    public decimal Outstanding => Rows.Sum(r => r.Outstanding);
    public decimal Overdue => Rows.Sum(r => r.Overdue);
}

public record CreditNoteLineDto(string ItemName, string BatchNo, DateOnly? ExpiryDate, decimal Qty, decimal Rate,
    decimal GstRate, decimal Taxable, decimal Tax, decimal Amount);

/// <summary>Printable credit note for goods a customer returned.</summary>
public record CreditNoteDto(int Id, string ReturnNo, DateTime ReturnDate, string InvoiceNo, DateTime InvoiceDate,
    string? CustomerName, string? CustomerPhone, string? CustomerGstin, RefundMode RefundMode, string? Reason,
    bool IsInterState, decimal Taxable, decimal Tax, decimal RoundOff, decimal Total, List<CreditNoteLineDto> Lines, string CreatedBy);

/// <summary>
/// Open bills after applying credits oldest-first (FIFO). Shared by the server's ageing report and its tests.
/// </summary>
public static class Ageing
{
    public record Debit(string RefNo, DateOnly Date, decimal Amount);

    public static List<OpenItemDto> OpenItems(IEnumerable<Debit> debits, decimal credits, int creditDays, DateOnly asOf)
    {
        var remainingCredit = credits;
        var open = new List<OpenItemDto>();
        foreach (var d in debits.OrderBy(d => d.Date))
        {
            var applied = Math.Min(Math.Max(remainingCredit, 0), d.Amount);
            remainingCredit -= applied;
            var left = d.Amount - applied;
            if (left <= 0) continue;
            var due = d.Date.AddDays(creditDays);
            open.Add(new OpenItemDto(d.RefNo, d.Date, due, d.Amount, left, Math.Max(0, asOf.DayNumber - due.DayNumber)));
        }
        return open;
    }

    public static AgeingRow Row(int id, string name, string? phone, int creditDays, decimal creditLimit,
        decimal outstanding, List<OpenItemDto> open) => new(
        id, name, phone, creditDays, creditLimit, outstanding,
        NotDue: open.Where(o => o.DaysOverdue == 0).Sum(o => o.Open),
        Overdue1To30: open.Where(o => o.DaysOverdue is > 0 and <= 30).Sum(o => o.Open),
        Overdue31To60: open.Where(o => o.DaysOverdue is > 30 and <= 60).Sum(o => o.Open),
        Overdue61To90: open.Where(o => o.DaysOverdue is > 60 and <= 90).Sum(o => o.Open),
        Overdue90Plus: open.Where(o => o.DaysOverdue > 90).Sum(o => o.Open),
        OldestDue: open.Where(o => o.DaysOverdue > 0).Select(o => (DateOnly?)o.DueDate).Min());
}
