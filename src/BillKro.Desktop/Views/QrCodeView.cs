using System.Windows;
using System.Windows.Media;
using BillKro.Shared;

namespace BillKro.Desktop.Views;

/// <summary>
/// Draws a QR code as large as fits (square, centred). Like <see cref="BarcodeView"/>, modules are sized in whole
/// printer dots (see <see cref="Dpi"/>) so every square prints the same size on a 203/300 dpi label printer.
/// </summary>
public class QrCodeView : FrameworkElement
{
    /// <summary>Light border around the symbol, in modules. The spec asks for 4; 2 is plenty on a white label.</summary>
    private const int QuietModules = 2;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(QrCodeView), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DpiProperty = DependencyProperty.Register(
        nameof(Dpi), typeof(double), typeof(QrCodeView), new FrameworkPropertyMetadata(203.0, FrameworkPropertyMetadataOptions.AffectsRender));

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
        var side = Math.Min(ActualWidth, ActualHeight);
        if (string.IsNullOrEmpty(Value) || side <= 0) return;

        var matrix = QrCode.Matrix(Value);
        var size = matrix.GetLength(0);
        var total = size + 2 * QuietModules;
        var dipPerDot = 96 / Dpi;
        var dotsPerModule = Math.Floor(side / dipPerDot / total);
        var module = dotsPerModule >= 1 ? dotsPerModule * dipPerDot : side / total;

        var symbol = size * module;
        var left = (ActualWidth - symbol) / 2;
        var top = (ActualHeight - symbol) / 2;
        if (dotsPerModule >= 1)
        {
            left = Math.Round(left / dipPerDot) * dipPerDot;
            top = Math.Round(top / dipPerDot) * dipPerDot;
        }

        // One geometry for all dark squares: prints as a single shape, with no hairline gaps between neighbours.
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    if (!matrix[y, x]) continue;
                    var px = left + x * module;
                    var py = top + y * module;
                    ctx.BeginFigure(new Point(px, py), isFilled: true, isClosed: true);
                    ctx.PolyLineTo([new Point(px + module, py), new Point(px + module, py + module), new Point(px, py + module)],
                        isStroked: false, isSmoothJoin: false);
                }
        geometry.Freeze();
        dc.DrawGeometry(Brushes.Black, null, geometry);
    }
}
