using BillKro.Sync.Core.Interfaces;
using BillKro.Sync.Core.Models;
using System.Globalization;

namespace BillKro.Sync.Parser;

/// <summary>
/// Parses MARG semicolon-delimited master data format (master.csv).
/// Produces Party and Item records from C/H/T multi-row blocks.
///
/// C001001;{CompanyName};;{Address};
/// H001{PartyName};{Type};{AcctCode};{Date};;...;{Balance};{DrCr};...;{BankName};...
/// H002 ... (financial continuation)
/// H003 ... (continuation)
/// T001{seq}{acctcode};{ItemName};;{acctcode};{date};{purchRate};{taxable};
///      {openQty};{packSize};...;{batchNo};;{mfgDate};{expiryDate};{group};...;{mrp};...
/// T002 ... (continuation)
/// </summary>
public class MasterDataParser
{
    public async Task<MasterParseResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var result = new MasterParseResult();
        string? companyCode = null;

        Party? currentParty = null;
        Item? currentItem = null;
        int itemSeq = 0;

        foreach (var raw in lines)
        {
            ct.ThrowIfCancellationRequested();
            var line = raw.TrimEnd();
            if (string.IsNullOrEmpty(line)) continue;

            // Detect record type by prefix character
            if (line.StartsWith("C"))
            {
                var f = line.Split(';');
                // C001001 -> code = f[0][1..] trim leading zeros, or first 3 digits
                companyCode = f.Length > 0 ? ParseCompanyCode(f[0]) : "001";
                var name = f.Length > 1 ? f[1].Trim() : string.Empty;
                var address = f.Length > 3 ? f[3].Trim() : null;
                result.Company = new Company { Code = companyCode, Name = name, Address = address };
            }
            else if (line.StartsWith("H001") || (line.Length > 4 && line[..1] == "H" && line[1..4] == "001"))
            {
                SaveParty(currentParty, result);
                currentParty = ParsePartyH1(line, companyCode ?? "001");
            }
            else if (line.StartsWith("H002") && currentParty != null)
            {
                ParsePartyH2(line, currentParty);
            }
            else if (line.StartsWith("H003") && currentParty != null)
            {
                // H003 — mostly system fields, skip detailed parsing
            }
            else if (line.StartsWith("T") && line.Length > 4 && IsDigit(line[1]))
            {
                var ttype = line[4] == '1' || (line.Length > 4 && line[3] == '1') ? 1
                          : line[4] == '2' || (line.Length > 4 && line[3] == '2') ? 2 : 0;

                if (IsT1Record(line))
                {
                    SaveItem(currentItem, result);
                    currentItem = ParseItemT1(line, companyCode ?? "001", ++itemSeq);
                }
                else if (IsT2Record(line) && currentItem != null)
                {
                    ParseItemT2(line, currentItem);
                }
            }
        }

        SaveParty(currentParty, result);
        SaveItem(currentItem, result);
        return result;
    }

    private static bool IsT1Record(string line)
    {
        // T0001001... or T001001... — ends with digit 1 in position 3 or 4
        if (line.Length < 5) return false;
        // Pattern: T + 4 digits where last digit is 1
        if (char.IsDigit(line[1]) && char.IsDigit(line[2]) && char.IsDigit(line[3]) && line[4] == '1')
            return true;
        return false;
    }

    private static bool IsT2Record(string line)
    {
        if (line.Length < 5) return false;
        if (char.IsDigit(line[1]) && char.IsDigit(line[2]) && char.IsDigit(line[3]) && line[4] == '2')
            return true;
        return false;
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static string ParseCompanyCode(string s)
    {
        // C001001 -> "001"
        var digits = new string(s.Skip(1).Take(3).ToArray());
        return digits;
    }

    private static Party ParsePartyH1(string line, string companyCode)
    {
        // H001{Name};{Type};{AcctCode};{Date};;...;{Balance1};...;{Balance2};...
        var name = line[4..];
        var f = name.Split(';');
        return new Party
        {
            Name        = f[0].Trim(),
            Type        = f.Length > 1 ? f[1].Trim() : string.Empty,
            AccountCode = f.Length > 2 ? f[2].Trim() : null,
            OpeningBalance = f.Length > 6 ? ParseDecimal(f[6]) : 0m,
            CompanyCode = companyCode,
        };
    }

    private static void ParsePartyH2(string line, Party party)
    {
        var f = line[4..].Split(';');
        // Position 24 in H002 often has bank name
        if (f.Length > 3) party.BankName = f[3].Trim().NullIfEmpty();
    }

    private static Item ParseItemT1(string line, string companyCode, int seq)
    {
        // T0001{acctCode};{ItemName};;{acctCode};{date};{purchRate};T/F;{openQty};{packSize};...
        var body = line[5..]; // skip T0001
        var f = body.Split(';');
        return new Item
        {
            Code         = f[0].Trim(),
            Name         = f.Length > 1 ? f[1].Trim() : string.Empty,
            AccountCode  = f.Length > 3 ? f[3].Trim() : null,
            PurchaseRate = f.Length > 5 ? ParseDecimal(f[5]) : 0m,
            OpeningQty   = f.Length > 7 ? ParseDecimal(f[7]) : 0m,
            PackSize     = f.Length > 8 ? ParseDecimal(f[8]) : 0m,
            BatchNo      = f.Length > 16 ? f[16].Trim().NullIfEmpty() : null,
            ManufacturingDate = f.Length > 18 ? ParseDateSafe(f[18]) : null,
            ExpiryDate   = f.Length > 19 ? ParseDateSafe(f[19]) : null,
            TaxRate      = f.Length > 24 ? ParseDecimal(f[24]) : 0m,
            MRP          = f.Length > 28 ? ParseDecimal(f[28]) : 0m,
            CompanyCode  = companyCode,
        };
    }

    private static void ParseItemT2(string line, Item item)
    {
        var body = line[5..];
        var f = body.Split(';');
        // T002 row has discount, landing cost at various positions
        if (f.Length > 1 && string.IsNullOrEmpty(item.Category))
            item.Category = f[0].Trim().NullIfEmpty();
    }

    private static void SaveParty(Party? p, MasterParseResult r)
    {
        if (p != null && !string.IsNullOrEmpty(p.Name)) r.Parties.Add(p);
    }

    private static void SaveItem(Item? i, MasterParseResult r)
    {
        if (i != null && !string.IsNullOrEmpty(i.Name)) r.Items.Add(i);
    }

    private static decimal ParseDecimal(string s) =>
        decimal.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static DateOnly? ParseDateSafe(string s)
    {
        s = s.Trim();
        if (string.IsNullOrEmpty(s) || s == "-  -    " || s == "  -  -    ") return null;
        if (DateOnly.TryParseExact(s, "dd-MM-yyyy", null, DateTimeStyles.None, out var d1)) return d1;
        if (DateOnly.TryParse(s, out var d2)) return d2;
        return null;
    }
}

public class MasterParseResult
{
    public Company? Company { get; set; }
    public List<Party> Parties { get; } = [];
    public List<Item> Items { get; } = [];
}

