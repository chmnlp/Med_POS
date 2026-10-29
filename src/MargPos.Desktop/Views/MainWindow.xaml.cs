using System.Windows;
using MargPos.Desktop.ViewModels;

namespace MargPos.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
