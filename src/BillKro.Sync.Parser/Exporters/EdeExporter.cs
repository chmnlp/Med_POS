using BillKro.Sync.Core.Interfaces;
using BillKro.Sync.Core.Models;
using System.Globalization;
using System.Text;

namespace BillKro.Sync.Parser.Exporters;

/// <summary>
/// Writes PurchaseBill records as MARG EDE comma-delimited import files
/// for drop into MARG's Method\ folder. Field positions mirror <see cref="EdeParser"/>.
/// </summary>
public class EdeExporter : IExporter<PurchaseBill>
{
    /// <summary>No byte-order mark: MARG (Visual FoxPro) reads the first bytes as data.</summary>
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public string Build(IEnumerable<PurchaseBill> records)
    {
        var sb = new StringBuilder();
        int seq = 1;
        foreach (var bill in records)
        {
            // H record
            sb.Append(
                $"H,{seq},{Clean(bill.CompanyCode)},{Clean(bill.BillNumber)},," +
                $"{bill.BillDate:ddMMyyyy},{bill.DeliveryMode ?? "DIRECT"},,,," +
                $"{Clean(bill.DeliveryLocation ?? string.Empty)},,,,,,,,,,,,\r\n");

            // T records
            foreach (var line in bill.Lines)
            {
                var expiry = line.ExpiryDate.HasValue
                    ? line.ExpiryDate.Value.ToString("ddMMyyyy", CultureInfo.InvariantCulture)
                    : string.Empty;
                sb.Append(
                    $"T,{Clean(bill.CompanyCode)},{Clean(bill.SupplierName)},," +
                    $"{Clean(line.ItemCode)},{Clean(line.ItemName)},{Clean(line.PackForm ?? "S10")}," +
                    $"{Clean(line.Manufacturer ?? string.Empty)},{Clean(line.BatchNo ?? string.Empty)},{expiry}," +
                    $"1,{N(line.FreeQuantity)},{N(line.TaxRate)},{N(line.PurchaseRate)},{N(line.CostRate)},," +
                    $"{N(line.MRP)},{N(line.Discount)},{N(line.TaxAmount)},," +
                    $"{N(line.Quantity)},0,,,0\r\n");
            }

            // F record
            sb.Append(
                $"F,{N(bill.TotalAmount)},,,,,,{N(bill.TotalTax)},0,,,,,,,,,,,,,," +
                $"0,{N(bill.NetAmount)},\r\n");

            seq++;
        }
        return sb.ToString();
    }

    public async Task ExportAsync(IEnumerable<PurchaseBill> records, string outputPath, CancellationToken ct = default)
    {
        // Write beside the target and rename, so MARG never picks up a half-written file.
        var temp = outputPath + ".part";
        await File.WriteAllTextAsync(temp, Build(records), FileEncoding, ct);
        File.Move(temp, outputPath, overwrite: true);
    }

    private static string N(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The format has no quoting, so commas and line breaks inside text would shift every field.</summary>
    private static string Clean(string s) => s.Replace(',', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
