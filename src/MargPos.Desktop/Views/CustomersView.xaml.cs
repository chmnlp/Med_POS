using System.Windows.Controls;
using MargPos.Desktop.ViewModels;

namespace MargPos.Desktop.Views;

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
