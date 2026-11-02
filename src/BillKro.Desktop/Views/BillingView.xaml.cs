using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BillKro.Desktop.ViewModels;

namespace BillKro.Desktop.Views;

public partial class BillingView : UserControl
{
    private BillingViewModel? _vm;

    public BillingView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.FocusRequested -= OnFocusRequested;
            _vm = e.NewValue as BillingViewModel;
            if (_vm is not null) _vm.FocusRequested += OnFocusRequested;
        };
        Loaded += (_, _) => OnFocusRequested("scan");
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnFocusRequested(string target) =>
        // Defer until new rows have been generated.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            switch (target)
            {
                case "qty": FocusSelectedQty(); break;
                case "doctor": DoctorBox.Focus(); break;
                case "customer": CustomerBox.Focus(); break;
                default: ScanBox.Focus(); ScanBox.SelectAll(); break;
            }
        });

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        switch (e.Key)
        {
            case Key.F2: CommitFocused(); Run(_vm.NewBillCommand); break;
            case Key.F3: OnFocusRequested("scan"); break;
            case Key.F4: OnFocusRequested("qty"); break;
            case Key.F6:
                if (_vm.HasCustomer) Run(_vm.ClearCustomerCommand);
                OnFocusRequested("customer");
                break;
            case Key.F8: CommitFocused(); Run(_vm.HoldCommand); break;
            case Key.F9: Run(_vm.RecallCommand); break;
            case Key.F12: CommitFocused(); Run(_vm.PayCommand); break;
            case Key.Delete when LinesGrid.IsKeyboardFocusWithin && Keyboard.FocusedElement is not TextBox:
                Run(_vm.RemoveLineCommand);
                break;
            default: return;
        }
        e.Handled = true;
    }

    private static void Run(ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    /// <summary>Commits a half-typed cell before an action reads the bill.</summary>
    private static void CommitFocused()
    {
        if (Keyboard.FocusedElement is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void OnScanBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        switch (e.Key)
        {
            case Key.Down when _vm.IsItemSearchOpen: _vm.MoveSearchSelection(1); ItemList.ScrollIntoView(_vm.SelectedSearchResult); break;
            case Key.Up when _vm.IsItemSearchOpen: _vm.MoveSearchSelection(-1); ItemList.ScrollIntoView(_vm.SelectedSearchResult); break;
            case Key.Enter: Run(_vm.ScanCommand); break;
            case Key.Escape when _vm.IsItemSearchOpen: _vm.IsItemSearchOpen = false; break;
            case Key.Escape: _vm.ScanText = ""; break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnCustomerBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        switch (e.Key)
        {
            case Key.Down when _vm.IsCustomerSearchOpen: _vm.MoveCustomerSelection(1); break;
            case Key.Up when _vm.IsCustomerSearchOpen: _vm.MoveCustomerSelection(-1); break;
            case Key.Enter when _vm.IsCustomerSearchOpen: Run(_vm.PickCustomerCommand); break;
            case Key.Enter: OnFocusRequested("scan"); break;
            case Key.Escape: _vm.CustomerQuery = ""; OnFocusRequested("scan"); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnItemPicked(object sender, MouseButtonEventArgs e)
    {
        if (_vm is not null && ListPick.IsRowClick(sender, e)) Run(_vm.PickSearchResultCommand);
    }

    private void OnCustomerPicked(object sender, MouseButtonEventArgs e)
    {
        if (_vm is not null && ListPick.IsRowClick(sender, e)) Run(_vm.PickCustomerCommand);
    }

    private void OnCellKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        OnFocusRequested("scan");
        e.Handled = true;
    }

    private void OnCellFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var box = (TextBox)sender;
        if (_vm is not null && box.DataContext is BillLineViewModel line) _vm.SelectedLine = line;
        box.SelectAll();
    }

    private void OnRemoveLine(object sender, RoutedEventArgs e)
    {
        if (_vm is null || ((FrameworkElement)sender).DataContext is not BillLineViewModel line) return;
        _vm.SelectedLine = line;
        Run(_vm.RemoveLineCommand);
    }

    private void FocusSelectedQty()
    {
        if (_vm?.SelectedLine is not { } line) return;
        LinesGrid.ScrollIntoView(line);
        LinesGrid.UpdateLayout();
        if (LinesGrid.ItemContainerGenerator.ContainerFromItem(line) is DataGridRow row
            && FindDescendant<TextBox>(row, t => Equals(t.Tag, "Qty")) is { } qty)
        {
            qty.Focus();
            qty.SelectAll();
        }
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && match(t)) return t;
            if (FindDescendant(child, match) is { } found) return found;
        }
        return null;
    }
}
