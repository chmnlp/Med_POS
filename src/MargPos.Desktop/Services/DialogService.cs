using System.Windows;
using MargPos.Desktop.ViewModels;
using MargPos.Desktop.Views;

namespace MargPos.Desktop.Services;

public class DialogService
{
    /// <summary>Shows an editor dialog; returns true when the user saved.</summary>
    public bool ShowEditor(EditorViewModel editor)
    {
        var window = new EditorWindow(editor) { Owner = ActiveWindow() };
        return window.ShowDialog() == true;
    }

    public bool Confirm(string message, string title = "Please confirm") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Error(string message, string title = "MargPOS") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void Info(string message, string title = "MargPOS") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = ActiveWindow();
        return owner is null
            ? MessageBox.Show(message, title, buttons, image, MessageBoxResult.No)
            : MessageBox.Show(owner, message, title, buttons, image, MessageBoxResult.No);
    }

    /// <summary>The window to own a dialog: the active one, else the visible main window, else none.</summary>
    public static Window? ActiveWindow()
    {
        var windows = Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible).ToList();
        return windows.FirstOrDefault(w => w.IsActive)
            ?? (Application.Current.MainWindow is { IsVisible: true } main ? main : null);
    }
}
