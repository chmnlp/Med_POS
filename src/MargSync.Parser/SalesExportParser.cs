using MargSync.Core.Interfaces;
using MargSync.Core.Models;
using System.Globalization;

namespace MargSync.Parser;

/// <summary>
/// Parses MARG sales CSV exports.
/// MARG can export sales reports as CSV via Reports > Export.
/// Expected format (column-header row, then data rows):
///   InvoiceNo, Date, CustomerCode, CustomerName, ItemCode, ItemName,
///   BatchNo, Expiry, Qty, Rate, MRP, Disc%, Tax%, TaxAmt, LineTotal,
///   NetAmount, IRN, EwayBill
/// </summary>
public class SalesExportParser : IParser<SalesInvoice>
{
    public bool CanParse(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var first = File.ReadLines(filePath).FirstOrDefault() ?? string.Empty;
        return first.Contains("InvoiceNo", StringComparison.OrdinalIgnoreCase)
            || first.Contains("Invoice No", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<SalesInvoice>> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        if (lines.Length < 2) return [];

        var headers = lines[0].Split(',').Select(h => h.Trim().ToLowerInvariant()).ToArray();
        int Idx(string name) => Array.IndexOf(headers, name);

        int idxInvoiceNo  = Idx("invoiceno");
        int idxDate       = Idx("date");
        int idxCustCode   = Idx("customercode");
        int idxCustName   = Idx("customername");
        int idxItemCode   = Idx("itemcode");
        int idxItemName   = Idx("itemname");
        int idxBatch      = Idx("batchno");
        int idxExpiry     = Idx("expiry");
        int idxQty        = Idx("qty");
        int idxRate       = Idx("rate");
        int idxMrp        = Idx("mrp");
        int idxDisc       = Idx("disc%");
        int idxTaxRate    = Idx("tax%");
        int idxTaxAmt     = Idx("taxamt");
        int idxLineTotal  = Idx("linetotal");
        int idxNetAmt     = Idx("netamount");
        int idxIrn        = Idx("irn");
        int idxEway       = Idx("ewaybill");

        var invoices = new Dictionary<string, SalesInvoice>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = lines[i].Split(',');
            if (f.Length < 2) continue;

            string invNo = SafeGet(f, idxInvoiceNo);
            if (string.IsNullOrEmpty(invNo)) continue;

            if (!invoices.TryGetValue(invNo, out var inv))
            {
                inv = new SalesInvoice
                {
                    InvoiceNumber = invNo,
                    InvoiceDate   = ParseDate(SafeGet(f, idxDate)),
                    CustomerCode  = SafeGet(f, idxCustCode),
                    CustomerName  = SafeGet(f, idxCustName),
                    NetAmount     = ParseDecimal(SafeGet(f, idxNetAmt)),
                    IrnNumber     = SafeGet(f, idxIrn).NullIfEmpty(),
                    EwayBillNumber = SafeGet(f, idxEway).NullIfEmpty(),
                };
                invoices[invNo] = inv;
            }

            var line = new SalesInvoiceLine
            {
                ItemCode   = SafeGet(f, idxItemCode),
                ItemName   = SafeGet(f, idxItemName),
                BatchNo    = SafeGet(f, idxBatch).NullIfEmpty(),
                ExpiryDate = ParseDateOpt(SafeGet(f, idxExpiry)),
                Quantity   = ParseDecimal(SafeGet(f, idxQty)),
                Rate       = ParseDecimal(SafeGet(f, idxRate)),
                MRP        = ParseDecimal(SafeGet(f, idxMrp)),
                Discount   = ParseDecimal(SafeGet(f, idxDisc)),
                TaxRate    = ParseDecimal(SafeGet(f, idxTaxRate)),
                TaxAmount  = ParseDecimal(SafeGet(f, idxTaxAmt)),
                LineTotal  = ParseDecimal(SafeGet(f, idxLineTotal)),
            };
            inv.Lines.Add(line);
            inv.TotalTax += line.TaxAmount;
            inv.TotalDiscount += line.Discount * line.Quantity;
        }

        return [.. invoices.Values];
    }

    private static string SafeGet(string[] f, int idx) =>
        idx >= 0 && idx < f.Length ? f[idx].Trim() : string.Empty;

    private static decimal ParseDecimal(string s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static DateOnly ParseDate(string s)
    {
        if (DateOnly.TryParse(s, out var d)) return d;
        return DateOnly.FromDateTime(DateTime.Today);
    }

    private static DateOnly? ParseDateOpt(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateOnly.TryParse(s, out var d)) return d;
        return null;
    }
}

