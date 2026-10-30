using System.Windows;
using System.Windows.Documents;
using MargPos.Desktop.Services;
using MargPos.Shared;

namespace MargPos.Desktop.Views;

/// <summary>On-screen preview of a printable document (invoice, debit note) with a Print button.</summary>
public partial class InvoicePreviewWindow : Window
{
    private readonly Action _print;

    public InvoicePreviewWindow(InvoicePrinter printer, SaleInvoiceDto invoice)
        : this($"Invoice {invoice.InvoiceNo}", printer.Build(invoice), () => printer.Print(invoice)) { }

    public InvoicePreviewWindow(string title, FlowDocument document, Action print)
    {
        InitializeComponent();
        _print = print;
        Title = title;
        Viewer.Width = document.PageWidth + 40;
        Viewer.Document = document;
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        try
        {
            _print();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ClientLog.Error("print", ex);
            MessageBox.Show(this, $"Printing failed: {ex.Message}", "MargPOS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}