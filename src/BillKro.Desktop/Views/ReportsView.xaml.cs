using System.Windows.Controls;
using System.Windows.Input;
using BillKro.Desktop.ViewModels;

namespace BillKro.Desktop.Views;

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();

    private void OnValuationSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ReportsViewModel vm) return;
        ValuationSearch.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        vm.LoadValuationCommand.Execute(null);
    }
}
