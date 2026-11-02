using System.Windows.Controls;
using BillKro.Desktop.ViewModels;

namespace BillKro.Desktop.Views;

public partial class PurchaseReturnsView : UserControl
{
    public PurchaseReturnsView()
    {
        InitializeComponent();
        PurchaseReturnsViewModel? Vm() => DataContext as PurchaseReturnsViewModel;
        ListShortcuts.Attach(this, SearchBox, ListGrid,
            () => Vm()?.NewCommand, () => Vm()?.ViewCommand, () => null, () => Vm()?.RefreshCommand);
    }
}
