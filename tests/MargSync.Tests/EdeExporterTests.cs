using System.Text;
using FluentAssertions;
using MargSync.Core.Models;
using MargSync.Parser;
using MargSync.Parser.Exporters;

namespace MargSync.Tests;

public class EdeExporterTests
{
    private static PurchaseBill Sample() => new()
    {
        BillNumber = "INV/77", CompanyCode = "ADM", SupplierName = "Aditya Medisales, Pune", BillDate = new DateOnly(2026, 10, 29),
        TotalAmount = 1120m, TotalTax = 120m, NetAmount = 1120m,
        Lines =
        [
            new PurchaseBillLine
            {
                ItemCode = "I00042", ItemName = "Paracetamol 650, Tab", PackForm = "15 TAB", Manufacturer = "Micro Labs",
                BatchNo = "PC24A", ExpiryDate = new DateOnly(2028, 3, 31), Quantity = 100, FreeQuantity = 10, TaxRate = 12,
                PurchaseRate = 10, CostRate = 9.0909m, MRP = 33.5m, Discount = 2.5m, TaxAmount = 1.2m,
            },
        ],
    };

    [Fact]
    public async Task Exported_file_reads_back_with_the_parser()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ede-{Guid.NewGuid():N}.csv");
        try
        {
            await new EdeExporter().ExportAsync([Sample()], path);
            var bills = await new EdeParser().ParseAsync(path);

            var bill = bills.Should().ContainSingle().Subject;
            bill.BillNumber.Should().Be("INV/77");
            bill.BillDate.Should().Be(new DateOnly(2026, 10, 29));
            bill.TotalAmount.Should().Be(1120m);
            bill.NetAmount.Should().Be(1120m);

            var line = bill.Lines.Should().ContainSingle().Subject;
            line.ItemCode.Should().Be("I00042");
            line.ItemName.Should().Be("Paracetamol 650  Tab");   // commas would break the format
            line.BatchNo.Should().Be("PC24A");
            line.ExpiryDate.Should().Be(new DateOnly(2028, 3, 31));
            line.Quantity.Should().Be(100m);
            line.FreeQuantity.Should().Be(10m);
            line.TaxRate.Should().Be(12m);
            line.PurchaseRate.Should().Be(10m);
            line.MRP.Should().Be(33.5m);
            line.Discount.Should().Be(2.5m);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task File_has_no_byte_order_mark_and_no_partial_file_left()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ede-{Guid.NewGuid():N}.csv");
        try
        {
            await new EdeExporter().ExportAsync([Sample()], path);
            var bytes = await File.ReadAllBytesAsync(path);
            bytes[0].Should().Be((byte)'H');
            File.Exists(path + ".part").Should().BeFalse();
            Encoding.ASCII.GetString(bytes).Should().Contain("\r\n");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
