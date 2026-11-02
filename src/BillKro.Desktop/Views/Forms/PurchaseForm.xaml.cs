using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BillKro.Desktop.ViewModels;

namespace BillKro.Desktop.Views.Forms;

public partial class PurchaseForm : UserControl
{
    private PurchaseEditorViewModel? _vm;

    public PurchaseForm()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.LineAdded -= FocusSearch;
            _vm = e.NewValue as PurchaseEditorViewModel;
            if (_vm is not null) _vm.LineAdded += FocusSearch;
        };
        Loaded += (_, _) => SupplierBox.Focus();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void FocusSearch() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ItemSearchBox.Focus());

    /// <summary>Ctrl+S saves; Enter in single-line boxes moves to the next box instead of triggering Save.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            (Window.GetWindow(this) as EditorWindow)?.TrySave();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !EntryPanel.IsKeyboardFocusWithin
                 && Keyboard.FocusedElement is TextBox { AcceptsReturn: false } box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void OnEntryKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (Keyboard.FocusedElement == ItemSearchBox)
        {
            switch (e.Key)
            {
                case Key.Down when _vm.IsItemSearchOpen: _vm.MoveSearchSelection(1); break;
                case Key.Up when _vm.IsItemSearchOpen: _vm.MoveSearchSelection(-1); break;
                case Key.Escape when _vm.IsItemSearchOpen: _vm.IsItemSearchOpen = false; break;
                case Key.Enter when _vm.IsItemSearchOpen:
                    _vm.PickItemCommand.Execute(null);
                    (_vm.EntryIsBatchTracked ? BatchBox : QtyBox).Focus();
                    break;
                case Key.Enter when _vm.EntryItem is not null: (_vm.EntryIsBatchTracked ? BatchBox : QtyBox).Focus(); break;
                default: return;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (box == SaleBox || box == MarginBox || box == WholesaleBox || box == HospitalBox) _vm.AddLineCommand.Execute(null);
            else box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void OnItemPicked(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null || !ListPick.IsRowClick(sender, e)) return;
        _vm.PickItemCommand.Execute(null);
        (_vm.EntryIsBatchTracked ? BatchBox : QtyBox).Focus();
    }

    private void OnLineDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.EditLineCommand.CanExecute(null) == true)
        {
            _vm.EditLineCommand.Execute(null);
            ItemSearchBox.Focus();
        }
    }
}
