using MargPos.Data;
using MargPos.Shared;
using Microsoft.EntityFrameworkCore;

namespace MargPos.Server.Infrastructure;

public static class Documents
{
    public const string SaleKind = "SALE";
    public const string CreditNoteKind = "CN";
    public const string CreditNotePrefix = "CN";

    /// <summary>Indian financial year (April–March) as four digits, e.g. 2627 for Apr 2026–Mar 2027.</summary>
    public static string FinancialYear(DateTime localDate)
    {
        var start = localDate.Month >= 4 ? localDate.Year : localDate.Year - 1;
        return $"{start % 100:D2}{(start + 1) % 100:D2}";
    }

    /// <summary>
    /// Atomically claims the next number for a document kind and year. Must run inside the caller's
    /// transaction so a rolled-back sale releases its number; HOLDLOCK serialises concurrent counters.
    /// </summary>
    public static async Task<int> NextNumberAsync(PosDbContext db, string kind, string financialYear, CancellationToken ct)
    {
        var numbers = await db.Database.SqlQuery<int>($"""
            MERGE [pos].[DocumentSequences] WITH (HOLDLOCK) AS t
            USING (SELECT {kind} AS Kind, {financialYear} AS FinancialYear) AS s
               ON t.Kind = s.Kind AND t.FinancialYear = s.FinancialYear
            WHEN MATCHED THEN UPDATE SET LastNumber = t.LastNumber + 1
            WHEN NOT MATCHED THEN INSERT (Kind, FinancialYear, LastNumber) VALUES (s.Kind, s.FinancialYear, 1)
            OUTPUT INSERTED.LastNumber AS [Value];
            """).ToListAsync(ct);
        return numbers.Single();
    }

    /// <summary>
    /// What a customer owes: opening balance + credit on completed invoices − returns credited to their account
    /// − receipts.
    /// </summary>
    public static async Task<decimal> OutstandingAsync(PosDbContext db, int customerId, CancellationToken ct)
    {
        var opening = await db.Customers.Where(c => c.Id == customerId).Select(c => c.OpeningBalance).FirstOrDefaultAsync(ct);
        var due = await db.SaleInvoices
            .Where(i => i.CustomerId == customerId && i.Status == InvoiceStatus.Completed)
            .SumAsync(i => (decimal?)i.BalanceDue, ct) ?? 0;
        var credited = await db.SaleReturns
            .Where(r => r.Invoice.CustomerId == customerId && r.RefundMode == RefundMode.Account
                && r.Invoice.Status == InvoiceStatus.Completed)
            .SumAsync(r => (decimal?)r.Total, ct) ?? 0;
        var received = await db.CustomerReceipts
            .Where(r => r.CustomerId == customerId && r.Status == AccountEntryStatus.Active)
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0;
        return opening + due - credited - received;
    }

    /// <summary>What we owe a supplier: opening + completed purchases − debit notes − payments.</summary>
    public static async Task<decimal> SupplierOutstandingAsync(PosDbContext db, int supplierId, CancellationToken ct)
    {
        var opening = await db.Suppliers.Where(s => s.Id == supplierId).Select(s => s.OpeningBalance).FirstOrDefaultAsync(ct);
        var purchased = await db.Purchases.Where(p => p.SupplierId == supplierId && p.Status == PurchaseStatus.Completed)
            .SumAsync(p => (decimal?)p.Total, ct) ?? 0;
        var returned = await db.PurchaseReturns.Where(r => r.SupplierId == supplierId).SumAsync(r => (decimal?)r.Total, ct) ?? 0;
        var paid = await db.SupplierPayments.Where(p => p.SupplierId == supplierId && p.Status == AccountEntryStatus.Active)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        return opening + purchased - returned - paid;
    }
}
