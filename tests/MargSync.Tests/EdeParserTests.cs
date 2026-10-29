using FluentAssertions;
using MargSync.Parser;

namespace MargSync.Tests;

public class EdeParserTests
{
    private readonly string _sampleFile = Path.Combine(
        AppContext.BaseDirectory, "TestData", "sample_ede.csv");

    [Fact]
    public async Task ParseAsync_ParsesHeader_Correctly()
    {
        CreateSampleFile();
        var parser = new EdeParser();
        var bills = await parser.ParseAsync(_sampleFile);

        bills.Should().HaveCount(1);
        bills[0].BillNumber.Should().Be("5112009");
        bills[0].BillDate.Should().Be(new DateOnly(2009, 10, 30));
    }

    [Fact]
    public async Task ParseAsync_ParsesTransactionLines_Correctly()
    {
        CreateSampleFile();
        var parser = new EdeParser();
        var bills = await parser.ParseAsync(_sampleFile);

        var line = bills[0].Lines.First();
        line.ItemCode.Should().Be("AMLGLUCOTBXXXX01");
        line.Quantity.Should().Be(400m);
        line.MRP.Should().Be(24m);
        line.TaxRate.Should().Be(4m);
    }

    [Fact]
    public async Task ParseAsync_ParsesFooter_Correctly()
    {
        CreateSampleFile();
        var parser = new EdeParser();
        var bills = await parser.ParseAsync(_sampleFile);

        bills[0].TotalAmount.Should().Be(233515.99m);
    }

    [Fact]
    public void CanParse_ReturnsFalse_ForMasterFormat()
    {
        var parser = new EdeParser();
        var master = Path.GetTempFileName();
        File.WriteAllText(master, "C001001;DEMO;;\n");
        parser.CanParse(master).Should().BeFalse();
        File.Delete(master);
    }

    private void CreateSampleFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_sampleFile)!);
        if (File.Exists(_sampleFile)) return;
        File.WriteAllText(_sampleFile,
            "H,1,022IA10775,5112009,,30102009,DIRECT,,,6112009,HAND TO HAND DELIVERY,,,LUCKNOW,335,0,,,,,,,,,\n" +
            "T,AML,ADITYA MEDISALES LTD,,AMLGLUCOTBXXXX01,CONCOR  5 10T,S10,ADITYA MEDISALES LTD- DADRA,BS90799,31082012,1,0,4,18.502,16.537,,24,0,0.643,,400,0,,,0\n" +
            "F,233515.99,,,,,,10508.227,0,,,,,,,,,,,,,,-0.217,244024,\n");
    }
}
