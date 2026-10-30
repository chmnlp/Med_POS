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

public class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Readable name for an account payment mode (UPI, Bank transfer).</summary>
public class PaymentModeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MargPos.Shared.AccountPaymentMode m ? MargPos.Shared.PaymentModeText.Of(m) : value?.ToString() ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Blank for zero, otherwise a money amount; for debit / credit columns.</summary>
public class ZeroBlankConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal d && d != 0 ? d.ToString("N2", culture) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class UpperCaseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpper(culture) ?? "";

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
