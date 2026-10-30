using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MargPos.Desktop.Views;
using MargPos.Shared;

namespace MargPos.Desktop.Services;

/// <summary>
/// Barcode stickers for a label printer (TSC, Zebra, TVS, Argox… through their Windows driver). Each printed page
/// is one row of labels on the roll, so the page size is the label size (times the labels across).
/// </summary>
public class LabelPrinter(ClientSettings settings, StoreContext store)
{
    private const double DipPerMm = 96 / 25.4;
    private const double DefaultDpi = 203; // most thermal label printers

    public string? PrinterName => settings.LabelPrinterName;

    /// <summary>Lets the user pick the label printer; remembered on this PC.</summary>
    public bool ChoosePrinter()
    {
        var dialog = new PrintDialog();
        if (InvoicePrinter.FindQueue(settings.LabelPrinterName) is { } queue) dialog.PrintQueue = queue;
        if (dialog.ShowDialog() != true) return false;
        settings.LabelPrinterName = dialog.PrintQueue.FullName;
        settings.Save();
        return true;
    }

    /// <summary>Prints the labels, each repeated its number of copies. False when no printer was chosen.</summary>
    public bool Print(IReadOnlyList<(LabelDto Label, int Copies)> labels)
    {
        if (InvoicePrinter.FindQueue(settings.LabelPrinterName) is null && !ChoosePrinter()) return false;

        var dialog = new PrintDialog { PrintQueue = InvoicePrinter.FindQueue(settings.LabelPrinterName)! };
        var across = Math.Clamp(settings.LabelsAcross, 1, 3);
        var labelWidth = settings.LabelWidthMm * DipPerMm;
        var gap = settings.LabelGapMm * DipPerMm;
        var pageWidth = across * labelWidth + (across - 1) * gap;
        var pageHeight = settings.LabelHeightMm * DipPerMm;

        var ticket = dialog.PrintTicket;
        ticket.PageMediaSize = new PageMediaSize(pageWidth, pageHeight);
        ticket.PageOrientation = PageOrientation.Portrait;
        var dpi = ticket.PageResolution?.X is int x and > 0 ? x : DefaultDpi;

        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);
        var queue = labels.SelectMany(l => Enumerable.Repeat(l.Label, l.Copies)).ToList();
        for (var start = 0; start < queue.Count; start += across)
        {
            var page = new FixedPage { Width = pageWidth, Height = pageHeight, Background = Brushes.White };
            for (var i = 0; i < across && start + i < queue.Count; i++)
            {
                var label = BuildLabel(queue[start + i], dpi);
                FixedPage.SetLeft(label, i * (labelWidth + gap));
                page.Children.Add(label);
            }
            page.Measure(new Size(pageWidth, pageHeight));
            page.Arrange(new Rect(0, 0, pageWidth, pageHeight));
            doc.Pages.Add(new PageContent { Child = page });
        }

        dialog.PrintDocument(doc.DocumentPaginator, queue.Count == 1 ? $"Label {queue[0].ItemName}" : $"{queue.Count} labels");
        return true;
    }

    /// <summary>One label at the configured size: store, item, barcode, code, MRP and lot details.</summary>
    public FrameworkElement BuildLabel(LabelDto label, double dpi = DefaultDpi)
    {
        var width = settings.LabelWidthMm * DipPerMm;
        var height = settings.LabelHeightMm * DipPerMm;
        var scale = Math.Clamp(Math.Min(settings.LabelHeightMm / 25, settings.LabelWidthMm / 40), 0.8, 2);
        var small = 7.5 * scale;

        var grid = new Grid();
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        if (settings.LabelShowStoreName && settings.LabelHeightMm >= 20 && !string.IsNullOrWhiteSpace(store.Settings.StoreName))
            Add(grid, 0, Text(store.Settings.StoreName, small, FontWeights.Normal, TextAlignment.Center));

        var name = label.Pack is null ? label.ItemName : $"{label.ItemName} {label.Pack}";
        Add(grid, 1, Text(name, 9.5 * scale, FontWeights.Bold, TextAlignment.Center));
        Add(grid, 2, new BarcodeView { Value = label.Barcode, Dpi = dpi, Margin = new Thickness(0, 1.5, 0, 0.5) });
        Add(grid, 3, Text(label.Barcode, small, FontWeights.Normal, TextAlignment.Center));

        var price = $"MRP ₹{label.Mrp:0.00}";
        // Our rate only fits beside MRP and the lot details from about 40 mm wide.
        if (settings.LabelShowSaleRate && settings.LabelWidthMm >= 40 && label.SaleRate > 0 && label.SaleRate < label.Mrp)
            price += $"  Rate ₹{label.SaleRate:0.00}";
        var lot = string.Join("  ", new[]
        {
            label.BatchNo is null ? null : $"B:{label.BatchNo}",
            label.ExpiryDate is { } exp ? $"E:{exp:MM'/'yy}" : null, // as printed on packs, whatever the PC's date separator
        }.Where(s => s is not null));

        var bottom = new DockPanel { LastChildFill = true };
        var lotText = Text(lot, small, FontWeights.Normal, TextAlignment.Right);
        DockPanel.SetDock(lotText, Dock.Right);
        bottom.Children.Add(lotText);
        bottom.Children.Add(Text(price, 9 * scale, FontWeights.Bold, TextAlignment.Left));
        Add(grid, 4, bottom);

        return new Border
        {
            Width = width,
            Height = height,
            Padding = new Thickness(1.5 * DipPerMm, 1 * DipPerMm, 1.5 * DipPerMm, 1 * DipPerMm),
            Background = Brushes.White,
            Child = grid,
        };
    }

    private static void Add(Grid grid, int row, UIElement child)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private static TextBlock Text(string text, double size, FontWeight weight, TextAlignment align) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = size,
        FontWeight = weight,
        Foreground = Brushes.Black,
        TextAlignment = align,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        LineHeight = size * 1.15,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };
}
