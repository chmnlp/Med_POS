using MargSync.Core.Interfaces;
using MargSync.Core.Models;
using System.Globalization;

namespace MargSync.Parser;

/// <summary>
/// Parses MARG EDE comma-delimited purchase bill format (standarddemo.csv).
/// Format: H=header, T=transaction line, F=footer.
///
/// T-record field positions (0-based):
///   0=RecordType, 1=CompanyCode, 2=SupplierName, 3=(empty), 4=ItemCode,
///   5=ItemName, 6=PackForm, 7=Manufacturer, 8=BatchNo, 9=ExpiryDDMMYYYY,
///   10=PackQtyType, 11=FreeQty, 12=TaxRate, 13=PurchaseRate, 14=CostRate, 15=(empty),
///   16=MRP, 17=Discount, 18=TaxPerUnit, 19=(empty), 20=Qty
/// </summary>
public class EdeParser : IParser<PurchaseBill>
{
    public bool CanParse(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var firstLine = File.ReadLines(filePath).FirstOrDefault() ?? string.Empty;
        // EDE files start with an H record (comma-separated)
        return firstLine.StartsWith("H,", StringComparison.OrdinalIgnoreCase)
            || firstLine.StartsWith("H,1,", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<PurchaseBill>> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var bills = new List<PurchaseBill>();
        PurchaseBill? current = null;

        foreach (var raw in lines)
        {
            ct.ThrowIfCancellationRequested();
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var fields = SplitCsv(line);
            if (fields.Length == 0) continue;

            var recordType = fields[0].ToUpperInvariant();

            if (recordType == "H")
            {
                if (current != null) bills.Add(current);
                current = ParseHeader(fields);
            }
            else if (recordType == "T" && current != null)
            {
                var tline = ParseTransactionLine(fields);
                if (tline != null)
                {
                    // Fill supplier from T-record if header didn't have it
                    if (string.IsNullOrEmpty(current.SupplierName) && fields.Length > 2)
                        current.SupplierName = fields[2].Trim();
                    if (string.IsNullOrEmpty(current.CompanyCode) && fields.Length > 1)
                        current.CompanyCode = fields[1].Trim();

                    current.Lines.Add(tline);
                }
            }
            else if (recordType == "F" && current != null)
            {
                ParseFooter(fields, current);
                bills.Add(current);
                current = null;
            }
        }

        if (current != null) bills.Add(current);
        return bills;
    }

    private static PurchaseBill ParseHeader(string[] f)
    {
        // H,{seq},{companyCode},{billNo},,{billDateDDMMYYYY},{deliveryMode},,,{dueDate},{deliveryLoc},...
        return new PurchaseBill
        {
            BillNumber  = SafeGet(f, 3),
            CompanyCode = SafeGet(f, 2),
            BillDate    = ParseDate(SafeGet(f, 5)),
            DeliveryMode     = SafeGet(f, 6),
            DeliveryLocation = SafeGet(f, 10),
        };
    }

    private static PurchaseBillLine? ParseTransactionLine(string[] f)
    {
        if (f.Length < 21) return null;
        return new PurchaseBillLine
        {
            ItemCode     = SafeGet(f, 4),
            ItemName     = SafeGet(f, 5),
            PackForm     = SafeGet(f, 6),
            Manufacturer = SafeGet(f, 7),
            BatchNo      = SafeGet(f, 8),
            ExpiryDate   = ParseExpiryDate(SafeGet(f, 9)),
            TaxRate      = ParseDecimal(SafeGet(f, 12)),
            PurchaseRate = ParseDecimal(SafeGet(f, 13)),
            CostRate     = ParseDecimal(SafeGet(f, 14)),
            MRP          = ParseDecimal(SafeGet(f, 16)),
            Discount     = ParseDecimal(SafeGet(f, 17)),
            TaxAmount    = ParseDecimal(SafeGet(f, 18)),
            Quantity     = ParseDecimal(SafeGet(f, 20)),
            FreeQuantity = ParseDecimal(SafeGet(f, 11)),
        };
    }

    private static void ParseFooter(string[] f, PurchaseBill bill)
    {
        // F,{totalAmt},,,,,,,{totalTax},,,,,,,,,,,,,{netDisc},{netAmt},...
        bill.TotalAmount = ParseDecimal(SafeGet(f, 1));
        bill.TotalTax    = ParseDecimal(SafeGet(f, 7));
        bill.NetAmount   = ParseDecimal(SafeGet(f, 23));
    }

    private static string[] SplitCsv(string line)
    {
        var result = new List<string>();
        bool inQuote = false;
        var current = new System.Text.StringBuilder();
        foreach (var ch in line)
        {
            if (ch == '"') { inQuote = !inQuote; }
            else if (ch == ',' && !inQuote) { result.Add(current.ToString()); current.Clear(); }
            else { current.Append(ch); }
        }
        result.Add(current.ToString());
        return [.. result];
    }

    private static string SafeGet(string[] f, int idx) =>
        idx < f.Length ? f[idx].Trim() : string.Empty;

    private static decimal ParseDecimal(string s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static DateOnly ParseDate(string s)
    {
        // DDMMYYYY
        if (s.Length == 8 &&
            int.TryParse(s[..2], out int d) &&
            int.TryParse(s[2..4], out int m) &&
            int.TryParse(s[4..], out int y))
            return new DateOnly(y, m, d);
        if (DateOnly.TryParse(s, out var result)) return result;
        return DateOnly.FromDateTime(DateTime.Today);
    }

    private static DateOnly? ParseExpiryDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // DDMMYYYY
        if (s.Length == 8 &&
            int.TryParse(s[..2], out int d) &&
            int.TryParse(s[2..4], out int m) &&
            int.TryParse(s[4..], out int y))
            return new DateOnly(y, m, d);
        if (DateOnly.TryParse(s, out var result)) return result;
        return null;
    }
}
