using FluentAssertions;
using BillKro.Sync.Parser;

namespace BillKro.Sync.Tests;

public class MasterDataParserTests
{
    private readonly string _sampleFile = Path.Combine(
        AppContext.BaseDirectory, "TestData", "sample_master.csv");

    [Fact]
    public async Task ParseAsync_ParsesCompany()
    {
        CreateSampleFile();
        var parser = new MasterDataParser();
        var result = await parser.ParseAsync(_sampleFile);

        result.Company.Should().NotBeNull();
        result.Company!.Name.Should().Be("DEMO PHARMA DISTRIBUTORS");
    }

    [Fact]
    public async Task ParseAsync_ParsesParty()
    {
        CreateSampleFile();
        var parser = new MasterDataParser();
        var result = await parser.ParseAsync(_sampleFile);

        result.Parties.Should().HaveCountGreaterThan(0);
        result.Parties[0].Name.Should().Be("AHUJA MEDICINE CENTRE");
        result.Parties[0].Type.Should().Be("S");
    }

    [Fact]
    public async Task ParseAsync_ParsesItems()
    {
        CreateSampleFile();
        var parser = new MasterDataParser();
        var result = await parser.ParseAsync(_sampleFile);

        result.Items.Should().HaveCountGreaterThan(0);
        result.Items[0].Name.Should().Be("CONCOR  5 10T");
        result.Items[0].PurchaseRate.Should().Be(32.83m);
    }

    private void CreateSampleFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_sampleFile)!);
        if (File.Exists(_sampleFile)) return;
        // Minimal valid master data extract from master.csv
        File.WriteAllLines(_sampleFile, [
            "C001001;DEMO PHARMA DISTRIBUTORS;;1234, Software Market, New Delhi - 72;",
            "H001AHUJA MEDICINE CENTRE;S;0033;31-03-2004;;  -  -    ;0.00;0.00;98378.67;;0.00;;3935.15;0.00;SZTZOZ;0.00;#29;0.00;#30;0.00;;0.00;0.18;102314.00;0.00;SZTZEX;;0.00;Discount;0.00;108215.58;;0.00;98378.67;;0.00;0.00",
            "H002;0.00;0.00;STACVT;0.00;0.00;;0.00;0.00;0;;;;  -  -    ;;;31-03-2004;0;;;;;BANK OF BARODA;;;;N;Z;;;;;;;0.00;61408.44;A;;;;;R8;A12;1;M;0;0.000;;; 24.00",
            "H0030NN  0Y  98378.67     Z 102478.60  10247.86      0.00      0.00      0.00;;0.00;0.00;0.00;;0.00;0.00;  0.00;0.00;SZHZCX;0.00;;;;;",
            "T00010014007;CONCOR  5 10T;;0033;31-03-2004;32.83000;T;120.000000;12.000000;0.00;0.00;0.000;0.00;32.8300;32.8300;2.00;0.00;292000;;  -  -    ;  -  -    ;G;;;4.00000;0.00000;0.0000;0.00;0.00;F;78.79;0.000;0.00;28.4106;0.000;0.000",
            "T0001002;O;0.00;154.43;0.00;0.00;0.00;0.00;0.00;0.00;0.00;0.00;3860.81;$40;A;R8;A12;;;;     9;0;4246.890;0.000;0.000;0.000;  0.00      E M    #      32.83A1        0;;  -  -    ;9;;;;",
        ]);
    }
}
