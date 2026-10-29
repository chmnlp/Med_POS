using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MargPos.Desktop;

/// <summary>Visible when the value is a non-empty string (or any non-null object); otherwise collapsed.</summary>
public class HasValueToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value is string s ? s.Length > 0 : value is not null;
        return has ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Maps a batch expiry date to "Expired", "Near expiry" or an empty string.</summary>
public class ExpiryStatusConverter : IValueConverter
{
    public const int NearExpiryDays = 90;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateOnly expiry) return "";
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (expiry <= today) return "Expired";
        return expiry <= today.AddDays(NearExpiryDays) ? "Near expiry" : "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
