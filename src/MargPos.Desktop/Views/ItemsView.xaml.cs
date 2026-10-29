using System.Windows.Controls;
using MargPos.Desktop.ViewModels;

namespace MargPos.Desktop.Views;

public partial class ItemsView : UserControl
{
    public ItemsView()
    {
        InitializeComponent();
        ItemsViewModel? Vm() => DataContext as ItemsViewModel;
        ListShortcuts.Attach(this, SearchBox, ItemGrid,
            () => Vm()?.NewCommand, () => Vm()?.EditCommand, () => Vm()?.DeactivateCommand, () => Vm()?.RefreshCommand);

        BatchGrid.MouseDoubleClick += (_, _) =>
        {
            if (Vm()?.EditBatchCommand is { } cmd && cmd.CanExecute(null)) cmd.Execute(null);
        };
    }
}
