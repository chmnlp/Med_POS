using BillKro.Sync.Core.Interfaces;
using BillKro.Sync.Core.Models;
using System.Globalization;

namespace BillKro.Sync.Parser;

/// <summary>
/// Parses MARG GST export CSV (GSTR-1 / GSTR-3B data).
/// Expected headers: CompanyCode, InvoiceNo, Date, GSTINSupplier, GSTINRecipient,
///                   HSN, TaxableValue, CGSTRate, SGSTRate, IGSTRate,
///                   CGSTAmt, SGSTAmt, IGSTAmt, IRN, ReturnPeriod
/// </summary>
public class GstExportParser : IParser<GstEntry>
{
    public bool CanParse(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var first = File.ReadLines(filePath).FirstOrDefault() ?? string.Empty;
        return first.Contains("GSTIN", StringComparison.OrdinalIgnoreCase)
            || first.Contains("ReturnPeriod", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<GstEntry>> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        if (lines.Length < 2) return [];

        var headers = lines[0].Split(',').Select(h => h.Trim().ToLowerInvariant().Replace(" ", "")).ToArray();
        int Idx(string n) => Array.IndexOf(headers, n);

        int idxCo     = Idx("companycode");
        int idxInv    = Idx("invoiceno");
        int idxDate   = Idx("date");
        int idxGstSup = Idx("gstinsupplier");
        int idxGstRec = Idx("gstinrecipient");
        int idxHsn    = Idx("hsn");
        int idxTaxVal = Idx("taxablevalue");
        int idxCgstR  = Idx("cgstrate");
        int idxSgstR  = Idx("sgstrate");
        int idxIgstR  = Idx("igstrate");
        int idxCgstA  = Idx("cgstamt");
        int idxSgstA  = Idx("sgstamt");
        int idxIgstA  = Idx("igstamt");
        int idxIrn    = Idx("irn");
        int idxPeriod = Idx("returnperiod");

        var result = new List<GstEntry>();
        for (int i = 1; i < lines.Length; i++)
        {
            var f = lines[i].Split(',');
            if (f.Length < 2) continue;
            result.Add(new GstEntry
            {
                CompanyCode    = SafeGet(f, idxCo),
                InvoiceNumber  = SafeGet(f, idxInv),
                InvoiceDate    = ParseDate(SafeGet(f, idxDate)),
                GstinSupplier  = SafeGet(f, idxGstSup),
                GstinRecipient = SafeGet(f, idxGstRec).NullIfEmpty(),
                HsnCode        = SafeGet(f, idxHsn).NullIfEmpty(),
                TaxableValue   = ParseDecimal(SafeGet(f, idxTaxVal)),
                CgstRate       = ParseDecimal(SafeGet(f, idxCgstR)),
                SgstRate       = ParseDecimal(SafeGet(f, idxSgstR)),
                IgstRate       = ParseDecimal(SafeGet(f, idxIgstR)),
                CgstAmount     = ParseDecimal(SafeGet(f, idxCgstA)),
                SgstAmount     = ParseDecimal(SafeGet(f, idxSgstA)),
                IgstAmount     = ParseDecimal(SafeGet(f, idxIgstA)),
                IrnNumber      = SafeGet(f, idxIrn).NullIfEmpty(),
                ReturnPeriod   = SafeGet(f, idxPeriod),
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
}

