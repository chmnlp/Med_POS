using System.Windows.Controls;
using BillKro.Desktop.ViewModels;

namespace BillKro.Desktop.Views;

public partial class SuppliersView : UserControl
{
    public SuppliersView()
    {
        InitializeComponent();
        SuppliersViewModel? Vm() => DataContext as SuppliersViewModel;
        ListShortcuts.Attach(this, SearchBox, ListGrid,
            () => Vm()?.NewCommand, () => Vm()?.EditCommand, () => Vm()?.DeactivateCommand, () => Vm()?.RefreshCommand);
    }
}
