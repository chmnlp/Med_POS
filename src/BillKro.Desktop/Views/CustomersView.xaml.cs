using System.Windows.Controls;
using BillKro.Desktop.ViewModels;

namespace BillKro.Desktop.Views;

public partial class CustomersView : UserControl
{
    public CustomersView()
    {
        InitializeComponent();
        CustomersViewModel? Vm() => DataContext as CustomersViewModel;
        ListShortcuts.Attach(this, SearchBox, ListGrid,
            () => Vm()?.NewCommand, () => Vm()?.EditCommand, () => Vm()?.DeactivateCommand, () => Vm()?.RefreshCommand);
    }
}
