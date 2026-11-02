using BillKro.Sync.Core.Interfaces;
using BillKro.Sync.Core.Models;
using System.Globalization;

namespace BillKro.Sync.Parser;

/// <summary>
/// Parses MARG ledger/accounts CSV exports.
/// Expected headers: CompanyCode, AccountCode, AccountName, Date,
///                   VoucherType, VoucherNo, Narration, Debit, Credit
/// </summary>
public class LedgerExportParser : IParser<LedgerEntry>
{
    public bool CanParse(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var first = File.ReadLines(filePath).FirstOrDefault() ?? string.Empty;
        return first.Contains("VoucherType", StringComparison.OrdinalIgnoreCase)
            || first.Contains("VoucherNo", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<LedgerEntry>> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        if (lines.Length < 2) return [];

        var headers = lines[0].Split(',').Select(h => h.Trim().ToLowerInvariant().Replace(" ", "")).ToArray();
        int Idx(string n) => Array.IndexOf(headers, n);

        int idxCompany = Idx("companycode");
        int idxAccCode = Idx("accountcode");
        int idxAccName = Idx("accountname");
        int idxDate    = Idx("date");
        int idxVType   = Idx("vouchertype");
        int idxVNo     = Idx("voucherno");
        int idxNarr    = Idx("narration");
        int idxDebit   = Idx("debit");
        int idxCredit  = Idx("credit");

        var result = new List<LedgerEntry>();
        for (int i = 1; i < lines.Length; i++)
        {
            var f = lines[i].Split(',');
            if (f.Length < 2) continue;
            result.Add(new LedgerEntry
            {
                CompanyCode   = SafeGet(f, idxCompany),
                AccountCode   = SafeGet(f, idxAccCode),
                AccountName   = SafeGet(f, idxAccName),
                EntryDate     = ParseDate(SafeGet(f, idxDate)),
                VoucherType   = SafeGet(f, idxVType),
                VoucherNumber = SafeGet(f, idxVNo),
                Narration     = SafeGet(f, idxNarr).NullIfEmpty(),
                DebitAmount   = ParseDecimal(SafeGet(f, idxDebit)),
                CreditAmount  = ParseDecimal(SafeGet(f, idxCredit)),
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

