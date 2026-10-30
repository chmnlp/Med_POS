using MargPos.Shared;
using ZXing;

namespace MargPos.Tests;

public class Code128Tests
{
    [Fact]
    public void Every_symbol_has_eleven_modules_and_an_even_bar_count()
    {
        Assert.Equal(107, Code128.Patterns.Count);
        Assert.Equal(107, Code128.Patterns.Distinct().Count());
        foreach (var p in Code128.Patterns.Take(106))
        {
            var widths = p.Select(c => c - '0').ToArray();
            Assert.Equal(11, widths.Sum());
            Assert.Equal(0, (widths[0] + widths[2] + widths[4]) % 2);
        }
        Assert.Equal(13, Code128.Patterns[106].Sum(c => c - '0'));
    }

    public static TheoryData<string> Samples => new()
    {
        // Lot barcodes (set C), item codes and supplier barcodes with letters/symbols (set B), odd-length numbers.
        "29000123", "8901234567890", "890123456789", "I00010", "DL-512/a b~",
        // Every set B character and every set C digit pair, so each data pattern in the table is read once.
        new string(Enumerable.Range(' ', 95).Select(c => (char)c).ToArray()),
        string.Concat(Enumerable.Range(0, 100).Select(n => n.ToString("D2"))),
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Decodes_with_a_real_reader(string text)
    {
        // Render one barcode row at 3 px per module with 10-module quiet zones, then let ZXing read it back.
        const int px = 3, height = 40;
        var modules = Code128.Modules(text);
        var width = (modules.Sum() + 20) * px;
        var image = Enumerable.Repeat((byte)255, width * height).ToArray();
        var x = 10 * px;
        for (var i = 0; i < modules.Count; i++)
        {
            var w = modules[i] * px;
            if (i % 2 == 0)
                for (var y = 0; y < height; y++) Array.Fill(image, (byte)0, y * width + x, w);
            x += w;
        }

        var reader = new BarcodeReaderGeneric
        {
            Options = { PossibleFormats = [BarcodeFormat.CODE_128], PureBarcode = true },
        };
        var result = reader.Decode(new RGBLuminanceSource(image, width, height, RGBLuminanceSource.BitmapFormat.Gray8));
        Assert.NotNull(result);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void Even_length_numbers_use_the_compact_digit_set()
    {
        Assert.Equal(105, Code128.Symbols("29000123")[0]);
        Assert.Equal(104, Code128.Symbols("2900012")[0]);
        Assert.Equal(104, Code128.Symbols("I00010")[0]);
        Assert.Throws<ArgumentException>(() => Code128.Symbols("₹10"));
    }
}
