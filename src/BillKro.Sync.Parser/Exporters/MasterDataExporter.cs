using BillKro.Sync.Core.Interfaces;
using BillKro.Sync.Core.Models;
using System.Text;

namespace BillKro.Sync.Parser.Exporters;

/// <summary>
/// Writes Party and Item master data as MARG semicolon-delimited import files
/// for drop into MARG's Method\ folder.
/// </summary>
public class MasterDataExporter
{
    public async Task ExportAsync(
        Company company,
        IEnumerable<Party> parties,
        IEnumerable<Item> items,
        string outputPath,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();

        // C record
        sb.AppendLine($"C001001;{company.Name};;{company.Address ?? string.Empty};");

        int partySeq = 1;
        foreach (var p in parties)
        {
            var bal = p.OpeningBalance.ToString("F2");
            sb.AppendLine($"H001{p.Name};{p.Type};{p.AccountCode ?? string.Empty};{DateTime.Today:dd-MM-yyyy};;  -  -    ;{bal};0.00;0.00;;0.00;;0.00;0.00;SZTZOZ;0.00;#29;0.00;#30;0.00;;0.00;0.00;{bal};0.00;SZTZEX;;0.00;Discount;0.00;0.00;;0.00;0.00;;0.00;0.00");
            sb.AppendLine($"H002;0.00;0.00;STACVT;0.00;0.00;;0.00;0.00;0;;;;  -  -    ;;;{DateTime.Today:dd-MM-yyyy};0;;;;;{p.BankName ?? string.Empty};;;;N;Z;;;;;;;0.00;0.00;A;;;;;R8;A12;1;M;0;0.000;;; 24.00 2.0030 0.00 0   0.00#  0.00R 0.00 0.00 0.00NN T   0.00NAN   0.0");
            sb.AppendLine($"H0030NN  0Y  0.00     Z 0.00  0.00      0.00      0.00      0.00;;0.00;0.00;0.00;;0.00;0.00;  0.00;0.00;SZHZCX;0.00;;;;;");
            partySeq++;
        }

        int itemSeq = 1;
        foreach (var item in items)
        {
            var expiryStr = item.ExpiryDate.HasValue ? item.ExpiryDate.Value.ToString("dd-MM-yyyy") : "  -  -    ";
            var mfgStr    = item.ManufacturingDate.HasValue ? item.ManufacturingDate.Value.ToString("dd-MM-yyyy") : "  -  -    ";
            sb.AppendLine(
                $"T{itemSeq:D4}1{item.Code};{item.Name};;{item.AccountCode ?? string.Empty};{DateTime.Today:dd-MM-yyyy};" +
                $"{item.PurchaseRate:F5};T;{item.OpeningQty:F6};{item.PackSize:F6};0.00;0.00;0.000;0.00;" +
                $"{item.PurchaseRate:F4};{item.PurchaseRate:F4};2.00;0.00;{item.BatchNo ?? string.Empty};;" +
                $"{mfgStr};{expiryStr};G;;;{item.TaxRate:F5};0.00000;0.0000;0.00;0.00;F;" +
                $"{item.MRP:F2};0.000;0.00;{item.LandingCost:F4};0.000;0.000");
            sb.AppendLine(
                $"T{itemSeq:D4}2O;0.00;0.00;0.00;0.00;0.00;0.00;0.00;0.00;0.00;0.00;" +
                $"0.00;$40;A;R8;A12;;;;     0;0;0.000;0.000;0.000;0.000;  0.00      E M    #       0.00A1        0;;  -  -    ;9;;;;");
            itemSeq++;
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, ct);
    }
}
