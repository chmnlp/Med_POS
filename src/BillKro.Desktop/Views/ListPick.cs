using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BillKro.Desktop.Views;

/// <summary>Single-click picking for search pop-up lists.</summary>
public static class ListPick
{
    /// <summary>
    /// True when the click landed on a row (not the scrollbar or padding); that row is made the selection first.
    /// </summary>
    public static bool IsRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || e.OriginalSource is not DependencyObject source) return false;
        if (ItemsControl.ContainerFromElement(list, source) is not ListBoxItem row) return false;
        list.SelectedItem = list.ItemContainerGenerator.ItemFromContainer(row);
        return true;
    }
}
