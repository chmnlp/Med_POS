using BillKro.Shared;
using ZXing;

namespace BillKro.Tests;

public class QrCodeTests
{
    public static TheoryData<string> Samples => new()
    {
        // Lot barcodes, item codes, manufacturer EANs and a supplier code with symbols.
        "29000123", "2912345678", "I00010", "8901234567890", "DL-512/a b~",
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Decodes_with_a_real_reader(string text)
    {
        // Render at 4 px per module with a 4-module quiet zone, then let ZXing (a different library) read it back.
        const int px = 4, quiet = 4;
        var matrix = QrCode.Matrix(text);
        var size = matrix.GetLength(0);
        var side = (size + 2 * quiet) * px;
        var image = Enumerable.Repeat((byte)255, side * side).ToArray();
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                if (matrix[y, x])
                    for (var dy = 0; dy < px; dy++)
                        Array.Fill(image, (byte)0, ((quiet + y) * px + dy) * side + (quiet + x) * px, px);

        var reader = new BarcodeReaderGeneric { Options = { PossibleFormats = [BarcodeFormat.QR_CODE], PureBarcode = true } };
        var result = reader.Decode(new RGBLuminanceSource(image, side, side, RGBLuminanceSource.BitmapFormat.Gray8));
        Assert.NotNull(result);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void Label_qr_holds_lot_code_batch_expiry_and_mrp_on_one_line()
    {
        var lot = new LabelDto { Barcode = "29000123", BatchNo = "PC24A/2", ExpiryDate = new DateOnly(2027, 3, 31), Mrp = 32.5m };
        Assert.Equal("29000123|B:PC24A/2|E:03/2027|MRP:32.50", QrCode.LabelText(lot));

        // Items without batches: code and MRP only.
        Assert.Equal("8901234567890|MRP:120.00", QrCode.LabelText(new LabelDto { Barcode = "8901234567890", Mrp = 120 }));
    }

    [Theory]
    [InlineData("29000123|B:PC24A/2|E:03/2027|MRP:32.50", "29000123")]
    [InlineData("  29000123 |B:X  ", "29000123")]
    [InlineData("8901234567890", "8901234567890")]
    [InlineData(" paracetamol ", "paracetamol")]
    [InlineData("|odd", "|odd")]
    public void Scans_keep_only_the_lot_code(string scanned, string expected) =>
        Assert.Equal(expected, QrCode.ScannedCode(scanned));

    [Fact]
    public void A_full_label_qr_decodes_to_its_text()
    {
        var text = QrCode.LabelText(new LabelDto
        {
            Barcode = "2912345678", BatchNo = "ABCDEFGHIJKLMNOPQRSTUVWXY/12", ExpiryDate = new DateOnly(2028, 12, 1), Mrp = 12345.67m,
        });
        Decodes_with_a_real_reader(text);
    }

    [Fact]
    public void Lot_codes_use_the_smallest_symbol_without_a_quiet_zone()
    {
        // Version 1 is 21×21 modules. With no quiet zone the top-left finder pattern starts at [0,0]: a dark
        // 7-module edge followed by its light separator.
        var m = QrCode.Matrix("29000123");
        Assert.Equal(21, m.GetLength(0));
        for (var i = 0; i < 7; i++) Assert.True(m[0, i] && m[i, 0] && m[0, 20 - i] && m[20 - i, 0]);
        Assert.False(m[0, 7] || m[7, 0] || m[0, 13] || m[13, 0]);
    }
}
