using System.Windows;
using System.Windows.Media;
using BillKro.Shared;

namespace BillKro.Desktop.Views;

/// <summary>
/// Draws a Code 128 barcode filling its width. Bars are sized in whole printer dots (see <see cref="Dpi"/>),
/// because uneven bar widths on a 203/300 dpi label printer are the usual reason a label will not scan.
/// </summary>
public class BarcodeView : FrameworkElement
{
    private const int QuietModules = 10;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(BarcodeView), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DpiProperty = DependencyProperty.Register(
        nameof(Dpi), typeof(double), typeof(BarcodeView), new FrameworkPropertyMetadata(203.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Resolution of the printer the label is for.</summary>
    public double Dpi
    {
        get => (double)GetValue(DpiProperty);
        set => SetValue(DpiProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Value) || !Code128.CanEncode(Value) || ActualWidth <= 0 || ActualHeight <= 0) return;

        var modules = Code128.Modules(Value);
        var total = modules.Sum() + 2 * QuietModules;
        var dipPerDot = 96 / Dpi;
        var dotsPerModule = Math.Floor(ActualWidth / dipPerDot / total);
        var module = dotsPerModule >= 1 ? dotsPerModule * dipPerDot : ActualWidth / total;

        var x = (ActualWidth - total * module) / 2 + QuietModules * module;
        if (dotsPerModule >= 1) x = Math.Round(x / dipPerDot) * dipPerDot;
        for (var i = 0; i < modules.Count; i++)
        {
            var w = modules[i] * module;
            if (i % 2 == 0) dc.DrawRectangle(Brushes.Black, null, new Rect(x, 0, w, ActualHeight));
            x += w;
        }
    }
}
