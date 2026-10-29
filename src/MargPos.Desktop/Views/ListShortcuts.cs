using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MargPos.Desktop.Views;

/// <summary>
/// Keyboard handling shared by list pages: F2/Insert new, Enter edit, Delete deactivate, F5 refresh,
/// Ctrl+F search, and Enter/Down from the search box jumps into the grid.
/// </summary>
public static class ListShortcuts
{
    public static void Attach(UserControl view, TextBox search, DataGrid grid,
        Func<ICommand?> newCommand, Func<ICommand?> editCommand, Func<ICommand?> deleteCommand, Func<ICommand?> refreshCommand)
    {
        view.PreviewKeyDown += (_, e) =>
        {
            var inGrid = grid.IsKeyboardFocusWithin;
            e.Handled = e.Key switch
            {
                Key.F when Keyboard.Modifiers == ModifierKeys.Control => FocusSearch(search),
                Key.F2 or Key.Insert => Run(newCommand()),
                Key.F5 => Run(refreshCommand()),
                Key.Enter when inGrid => Run(editCommand()),
                Key.Delete when inGrid => Run(deleteCommand()),
                Key.Enter or Key.Down when search.IsKeyboardFocused => FocusGrid(grid),
                _ => false,
            };
        };

        grid.MouseDoubleClick += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) is DataGridRow)
                e.Handled = Run(editCommand());
        };

        view.Loaded += (_, _) => FocusSearch(search);
    }

    private static bool Run(ICommand? command)
    {
        if (command?.CanExecute(null) == true) command.Execute(null);
        return command is not null;
    }

    private static bool FocusSearch(TextBox search)
    {
        search.Focus();
        search.SelectAll();
        return true;
    }

    private static bool FocusGrid(DataGrid grid)
    {
        if (grid.Items.Count == 0) return true;
        grid.SelectedItem ??= grid.Items[0];
        grid.ScrollIntoView(grid.SelectedItem);
        grid.UpdateLayout();
        if (grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem) is DataGridRow row)
            row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        else
            grid.Focus();
        return true;
    }
}
