using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MargPos.Desktop.ViewModels;

namespace MargPos.Desktop.Views.Forms;

public partial class BarcodeSetupForm : UserControl
{
    private BarcodeSetupViewModel? _vm;

    public BarcodeSetupForm()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.FocusScan -= FocusScan;
            _vm = e.NewValue as BarcodeSetupViewModel;
            if (_vm is not null) _vm.FocusScan += FocusScan;
        };
        Loaded += (_, _) => FocusScan();
    }

    private void FocusScan() => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
    {
        ScanBox.Focus();
        ScanBox.SelectAll();
        if (_vm?.Selected is { } item) ItemList.ScrollIntoView(item);
    });

    /// <summary>The scanner's Enter saves this barcode instead of closing the dialog.</summary>
    private void OnScanKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null) return;
        e.Handled = true;
        _vm.SaveBarcodeCommand.Execute(null);
    }
}
