using System.Globalization;

namespace MargPos.Shared;

/// <summary>
/// Parses batch dates the way they are printed on Indian pharma packs: month/year (03/27, 03/2027, 0327)
/// or a full day-month-year date (31-03-2027, 31/03/27).
/// </summary>
public static class BatchDates
{
    private static readonly char[] Separators = ['/', '-', '.', ' '];

    /// <summary>Month/year means the last day of that month (stock is sellable through it).</summary>
    public static bool TryParseExpiry(string? text, out DateOnly? date) => TryParse(text, endOfMonth: true, out date);

    /// <summary>Month/year means the first day of that month.</summary>
    public static bool TryParseMfg(string? text, out DateOnly? date) => TryParse(text, endOfMonth: false, out date);

    /// <summary>A full day-month-year date such as an invoice date (31-03-2027, 31/3/27, 31032027).</summary>
    public static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Length == 8 && t.All(char.IsAsciiDigit)) t = $"{t[..2]}-{t[2..4]}-{t[4..]}";
        if (!TryParse(t, endOfMonth: false, out var parsed) || parsed is null) return false;
        if (t.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Length != 3) return false;
        date = parsed.Value;
        return true;
    }

    public static string Format(DateOnly? date) => date?.ToString("MM/yyyy", CultureInfo.InvariantCulture) ?? "";

    private static bool TryParse(string? text, bool endOfMonth, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var parts = text.Trim().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && parts[0].Length is 4 or 6 && parts[0].All(char.IsAsciiDigit))
            parts = [parts[0][..2], parts[0][2..]]; // 0327 or 032027

        if (!parts.All(p => p.All(char.IsAsciiDigit))) return false;
        var nums = parts.Select(int.Parse).ToArray();

        switch (nums.Length)
        {
            case 2 when TryYear(nums[1], out var year) && nums[0] is >= 1 and <= 12:
                date = new DateOnly(year, nums[0], endOfMonth ? DateTime.DaysInMonth(year, nums[0]) : 1);
                return true;
            case 3 when TryYear(nums[2], out var year) && nums[1] is >= 1 and <= 12
                        && nums[0] >= 1 && nums[0] <= DateTime.DaysInMonth(year, nums[1]):
                date = new DateOnly(year, nums[1], nums[0]);
                return true;
            default:
                return false;
        }
    }

    private static bool TryYear(int value, out int year)
    {
        year = value < 100 ? 2000 + value : value;
        return year is >= 2000 and <= 2099;
    }
}
