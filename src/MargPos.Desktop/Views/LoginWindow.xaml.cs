using System.Windows;
using MargPos.Desktop.ViewModels;

namespace MargPos.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.SignedIn += () => DialogResult = true;

        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(vm.Username)) UsernameBox.Focus();
            else PasswordBox.Focus();
        };
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        await _vm.SignInAsync(PasswordBox.Password);
        if (_vm.ErrorMessage is null) return;

        // Connection problems are most often a wrong server address; surface that field.
        if (_vm.ErrorMessage.StartsWith("Cannot reach") || _vm.ErrorMessage.StartsWith("Server address"))
            ServerExpander.IsExpanded = true;
        PasswordBox.Focus();
        PasswordBox.SelectAll();
    }
}
