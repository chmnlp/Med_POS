using System.Globalization;
using QRCoder;

namespace BillKro.Shared;

/// <summary>
/// QR codes for lot labels. The QR starts with the lot's barcode text, then the batch, expiry and MRP for anyone
/// reading it with a phone: <c>29000123|B:PC24A/2|E:03/2027|MRP:32.50</c>. A counter scanner types all of it, so
/// every scan box keeps only the part before the first "|" (see <see cref="ScannedCode"/>) and bills that lot.
/// </summary>
public static class QrCode
{
    public const char Separator = '|';

    /// <summary>
    /// What goes inside a label's QR. One line on purpose: a line break would reach the app as Enter mid-scan.
    /// Items without batches only add the MRP.
    /// </summary>
    public static string LabelText(LabelDto label)
    {
        var parts = new List<string> { label.Barcode };
        if (!string.IsNullOrEmpty(label.BatchNo)) parts.Add($"B:{label.BatchNo}");
        if (label.ExpiryDate is { } exp) parts.Add($"E:{exp.ToString("MM'/'yyyy", CultureInfo.InvariantCulture)}");
        if (label.Mrp > 0) parts.Add($"MRP:{label.Mrp.ToString("0.00", CultureInfo.InvariantCulture)}");
        return string.Join(Separator, parts);
    }

    /// <summary>
    /// The code to look up from what a scanner typed: the text before the first "|" of one of our QR labels,
    /// otherwise the text itself (barcodes, item codes, names).
    /// </summary>
    public static string ScannedCode(string scanned)
    {
        var text = scanned.Trim();
        var cut = text.IndexOf(Separator);
        return cut > 0 ? text[..cut].Trim() : text;
    }

    /// <summary>Quiet zone QRCoder adds around the symbol (modules per side); stripped so the caller draws its own.</summary>
    private const int LibraryQuietZone = 4;

    /// <summary>
    /// Dark (true) / light modules of the QR symbol, without a quiet zone. Error correction M (~15%) survives
    /// scuffed or partly smudged labels; a full label QR (about 40 characters) fits a 29×29 or 33×33 symbol.
    /// </summary>
    public static bool[,] Matrix(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        using var data = QRCodeGenerator.GenerateQrCode(value, QRCodeGenerator.ECCLevel.M);
        var rows = data.ModuleMatrix;
        var size = rows.Count - 2 * LibraryQuietZone;
        var matrix = new bool[size, size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                matrix[y, x] = rows[y + LibraryQuietZone][x + LibraryQuietZone];
        return matrix;
    }
}
