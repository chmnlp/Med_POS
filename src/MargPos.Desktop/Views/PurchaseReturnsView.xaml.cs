using System.Windows.Controls;
using MargPos.Desktop.ViewModels;

namespace MargPos.Desktop.Views;

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
