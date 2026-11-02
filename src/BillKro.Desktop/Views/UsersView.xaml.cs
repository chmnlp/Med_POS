using System.Windows.Controls;
using System.Windows.Input;
using BillKro.Desktop.ViewModels;

namespace BillKro.Desktop.Views;

public partial class UsersView : UserControl
{
    public UsersView() => InitializeComponent();

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is UsersViewModel { EditCommand: var cmd } && cmd.CanExecute(null)) cmd.Execute(null);
    }
}
