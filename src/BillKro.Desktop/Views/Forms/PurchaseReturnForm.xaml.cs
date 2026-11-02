using System.Windows.Controls;
using System.Windows.Input;
using BillKro.Desktop.ViewModels;

namespace BillKro.Desktop.Views.Forms;

public partial class PurchaseReturnForm : UserControl
{
    public PurchaseReturnForm() => InitializeComponent();

    private PurchaseReturnEditorViewModel? Vm => DataContext as PurchaseReturnEditorViewModel;

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        switch (e.Key)
        {
            case Key.Down when vm.IsItemSearchOpen: vm.MoveSearchSelection(1); break;
            case Key.Up when vm.IsItemSearchOpen: vm.MoveSearchSelection(-1); break;
            case Key.Escape when vm.IsItemSearchOpen: vm.IsItemSearchOpen = false; break;
            // Enter picks the item rather than triggering the dialog's Save.
            case Key.Enter: vm.PickItemCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnItemPicked(object sender, MouseButtonEventArgs e)
    {
        if (ListPick.IsRowClick(sender, e)) Vm?.PickItemCommand.Execute(null);
    }
}
