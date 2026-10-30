using System.Windows.Controls;
using MargPos.Desktop.ViewModels;

namespace MargPos.Desktop.Views;

public partial class SalesView : UserControl
{
    public SalesView()
    {
        InitializeComponent();
        SalesViewModel? Vm() => DataContext as SalesViewModel;
        // Enter/double-click opens the bill; there is no "new" here and Delete does nothing.
        ListShortcuts.Attach(this, SearchBox, ListGrid,
            () => null, () => Vm()?.ViewCommand, () => null, () => Vm()?.RefreshCommand);
    }
}
