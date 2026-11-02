using System.Globalization;
using System.IO;
using System.Text;

namespace BillKro.Desktop.Services;

/// <summary>Writes Excel-friendly CSV (UTF-8 with BOM so ₹ and Indian names open correctly).</summary>
public static class CsvWriter
{
    public static string Build(string[] headers, IEnumerable<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Cell)));
        foreach (var row in rows) sb.AppendLine(string.Join(",", row.Select(Cell)));
        return sb.ToString();
    }

    public static void Save(string path, string[] headers, IEnumerable<object?[]> rows) =>
        File.WriteAllText(path, Build(headers, rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private static string Cell(object? v)
    {
        var s = v switch
        {
            null => "",
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}