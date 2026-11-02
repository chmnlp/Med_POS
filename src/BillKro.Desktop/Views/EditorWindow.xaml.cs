using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BillKro.Desktop.ViewModels;

namespace BillKro.Desktop.Views;

/// <summary>Dialog shell for any <see cref="EditorViewModel"/>; the form comes from the DataTemplates in App.xaml.</summary>
public partial class EditorWindow : Window
{
    private readonly EditorViewModel _vm;

    public EditorWindow(EditorViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.Saved += () => DialogResult = true;
        Loaded += (_, _) => MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void OnSave(object sender, RoutedEventArgs e) => TrySave();

    /// <summary>Commits the field being edited, blocks on invalid input, then runs the editor's save.</summary>
    public void TrySave()
    {
        // Enter triggers the default button without moving focus, so commit the field being typed in.
        if (Keyboard.FocusedElement is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        if (HasBindingErrors(this))
        {
            _vm.ErrorMessage = "Some fields have invalid values (highlighted in red). Numbers must use digits only, e.g. 125.50";
            return;
        }
        _vm.SaveCommand.Execute(null);
    }

    private static bool HasBindingErrors(DependencyObject node)
    {
        if (Validation.GetHasError(node)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (HasBindingErrors(VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }
}
