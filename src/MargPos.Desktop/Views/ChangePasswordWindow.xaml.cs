using System.Windows;
using MargPos.Desktop.ViewModels;

namespace MargPos.Desktop.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly ChangePasswordViewModel _vm;

    public ChangePasswordWindow(ChangePasswordViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.Changed += () => DialogResult = true;
        if (vm.IsForced) CancelButton.Content = "Sign out";
        Loaded += (_, _) => CurrentBox.Focus();
    }

    private async void OnSubmit(object sender, RoutedEventArgs e) =>
        await _vm.SubmitAsync(CurrentBox.Password, NewBox.Password, ConfirmBox.Password);
}
