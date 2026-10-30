using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MargPos.Desktop.ViewModels;

namespace MargPos.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Ctrl+K opens quick jump; Ctrl+1..9 opens the matching sidebar entry.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.K)
        {
            _vm.OpenQuickJumpCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key is >= Key.D1 and <= Key.D9 or >= Key.NumPad1 and <= Key.NumPad9)
        {
            _vm.GoToIndex(e.Key >= Key.NumPad1 ? e.Key - Key.NumPad1 : e.Key - Key.D1);
            e.Handled = true;
        }
    }

    private void OnQuickJumpOpened(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => QuickJumpBox.Focus());

    private void OnQuickJumpKeyDown(object sender, KeyEventArgs e)
    {
        var results = _vm.QuickJumpResults.ToList();
        var index = _vm.QuickJumpSelected is null ? -1 : results.IndexOf(_vm.QuickJumpSelected);
        switch (e.Key)
        {
            case Key.Down when results.Count > 0: _vm.QuickJumpSelected = results[Math.Min(index + 1, results.Count - 1)]; break;
            case Key.Up when results.Count > 0: _vm.QuickJumpSelected = results[Math.Max(index - 1, 0)]; break;
            case Key.Enter: _vm.ConfirmQuickJump(); break;
            case Key.Escape: _vm.IsQuickJumpOpen = false; break;
            default: return;
        }
        if (_vm.QuickJumpSelected is not null) QuickJumpList.ScrollIntoView(_vm.QuickJumpSelected);
        e.Handled = true;
    }

    private void OnQuickJumpClicked(object sender, MouseButtonEventArgs e) => _vm.ConfirmQuickJump();
}
