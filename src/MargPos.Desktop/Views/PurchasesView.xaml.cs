using System.Windows.Controls;
using MargPos.Desktop.ViewModels;

namespace MargPos.Desktop.Views;

public partial class PurchasesView : UserControl
{
    public PurchasesView()
    {
        InitializeComponent();
        PurchasesViewModel? Vm() => DataContext as PurchasesViewModel;
        ListShortcuts.Attach(this, SearchBox, ListGrid,
            () => Vm()?.NewCommand, () => Vm()?.ViewCommand, () => null, () => Vm()?.RefreshCommand);
    }
}
