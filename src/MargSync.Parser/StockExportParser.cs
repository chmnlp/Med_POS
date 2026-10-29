using MargSync.Core.Interfaces;
using MargSync.Core.Models;
using System.Globalization;

namespace MargSync.Parser;

/// <summary>
/// Parses MARG stock/inventory CSV exports.
/// Expected headers: CompanyCode, ItemCode, ItemName, BatchNo, Expiry,
///                   ClosingQty, ClosingValue, MRP, AsOfDate
/// </summary>
public class StockExportParser : IParser<StockEntry>
{
    public bool CanParse(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var first = File.ReadLines(filePath).FirstOrDefault() ?? string.Empty;
        return first.Contains("ClosingQty", StringComparison.OrdinalIgnoreCase)
            || first.Contains("Closing Qty", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<StockEntry>> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        if (lines.Length < 2) return [];

        var headers = lines[0].Split(',').Select(h => h.Trim().ToLowerInvariant().Replace(" ", "")).ToArray();
        int Idx(string name) => Array.IndexOf(headers, name);

        int idxCompany = Idx("companycode");
        int idxCode    = Idx("itemcode");
        int idxName    = Idx("itemname");
        int idxBatch   = Idx("batchno");
        int idxExpiry  = Idx("expiry");
        int idxQty     = Idx("closingqty");
        int idxValue   = Idx("closingvalue");
        int idxMrp     = Idx("mrp");
        int idxDate    = Idx("asofdate");

        var result = new List<StockEntry>();
        for (int i = 1; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = lines[i].Split(',');
            if (f.Length < 2) continue;
            result.Add(new StockEntry
            {
                CompanyCode   = SafeGet(f, idxCompany),
                ItemCode      = SafeGet(f, idxCode),
                ItemName      = SafeGet(f, idxName),
                BatchNo       = SafeGet(f, idxBatch).NullIfEmpty(),
                ExpiryDate    = ParseDateOpt(SafeGet(f, idxExpiry)),
                ClosingQty    = ParseDecimal(SafeGet(f, idxQty)),
                ClosingValue  = ParseDecimal(SafeGet(f, idxValue)),
                MRP           = ParseDecimal(SafeGet(f, idxMrp)),
                AsOfDate      = ParseDate(SafeGet(f, idxDate)),
            });
        }
        return result;
    }

    private static string SafeGet(string[] f, int idx) =>
        idx >= 0 && idx < f.Length ? f[idx].Trim() : string.Empty;
    private static decimal ParseDecimal(string s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    private static DateOnly ParseDate(string s) =>
        DateOnly.TryParse(s, out var d) ? d : DateOnly.FromDateTime(DateTime.Today);
    private static DateOnly? ParseDateOpt(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : DateOnly.TryParse(s, out var d) ? d : null;
}

