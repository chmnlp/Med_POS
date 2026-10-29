using MargSync.Core.Interfaces;
using MargSync.Core.Models;
using System.Text;

namespace MargSync.Parser.Exporters;

/// <summary>
/// Writes PurchaseBill records as MARG EDE comma-delimited import files
/// for drop into MARG's Method\ folder.
/// </summary>
public class EdeExporter : IExporter<PurchaseBill>
{
    public async Task ExportAsync(IEnumerable<PurchaseBill> records, string outputPath, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        int seq = 1;
        foreach (var bill in records)
        {
            // H record
            sb.AppendLine(
                $"H,{seq},{bill.CompanyCode},{bill.BillNumber},," +
                $"{bill.BillDate:ddMMyyyy},{bill.DeliveryMode ?? "DIRECT"},,,," +
                $"{bill.DeliveryLocation ?? string.Empty},,,,,,,,,,,,");

            // T records
            foreach (var line in bill.Lines)
            {
                var expiry = line.ExpiryDate.HasValue
                    ? line.ExpiryDate.Value.ToString("ddMMyyyy")
                    : string.Empty;
                sb.AppendLine(
                    $"T,{bill.CompanyCode},{bill.SupplierName},," +
                    $"{line.ItemCode},{EscapeCsv(line.ItemName)},{line.PackForm ?? "S10"}," +
                    $"{EscapeCsv(line.Manufacturer ?? string.Empty)},{line.BatchNo ?? string.Empty},{expiry}," +
                    $"1,0,{line.TaxRate},{line.PurchaseRate},{line.CostRate},," +
                    $"{line.MRP},0,{line.TaxAmount},," +
                    $"{line.Quantity},0,,,0");
            }

            // F record
            sb.AppendLine(
                $"F,{bill.TotalAmount},,,,,,{bill.TotalTax},0,,,,,,,,,,,,," +
                $"0,{bill.NetAmount},");

            seq++;
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, ct);
    }

    private static string EscapeCsv(string s) =>
        s.Contains(',') ? $"\"{s}\"" : s;
}
