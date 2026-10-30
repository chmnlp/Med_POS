using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using MargPos.Desktop.Services;
using MargPos.Desktop.ViewModels;
using MargPos.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MargPos.Desktop;

public partial class App : Application
{
    private ServiceProvider _services = null!;
    private Session _session = null!;
    private bool _signingOut;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // Indian number grouping (1,23,450.00) and dd-MM-yyyy dates in all bindings.
        var india = new CultureInfo("en-IN");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = india;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(india.IetfLanguageTag)));

        var settings = ClientSettings.Load();
        ApplyTheme(settings.Theme);

        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton<Session>();
        services.AddSingleton<ApiClient>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<StoreContext>();
        services.AddSingleton<InvoicePrinter>();
        services.AddSingleton<LabelPrinter>();
        services.AddTransient<BillingViewModel>();
        services.AddTransient<SalesViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PurchasesViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<PurchaseReturnsViewModel>();
        services.AddTransient<AccountsViewModel>();
        services.AddTransient<DayEndViewModel>();
        services.AddTransient<MargLinkViewModel>();
        services.AddTransient<SalesReportsViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<ChangePasswordViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ItemsViewModel>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<SuppliersViewModel>();
        services.AddTransient<UsersViewModel>();
        services.AddSingleton<NavigationService>();
        _services = services.BuildServiceProvider();

        _session = _services.GetRequiredService<Session>();
        _services.GetRequiredService<ApiClient>().SessionRejected += () => Dispatcher.InvokeAsync(OnSessionRejected);

        try
        {
            ShowLogin();
        }
        catch (Exception ex)
        {
            // Without this, a startup failure leaves a windowless process running (ShutdownMode is explicit).
            ClientLog.Error("startup", ex);
            MessageBox.Show($"MargPOS could not start: {ex.Message}\n\nDetails: {ClientLog.FilePath}", "MargPOS",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public static void ApplyTheme(string theme) => BrandTheme.Apply(theme);

    private void ShowLogin()
    {
        while (true)
        {
            var login = new LoginWindow(_services.GetRequiredService<LoginViewModel>());
            var signedIn = login.ShowDialog() == true;
            MainWindow = null; // WPF made the (now closed) login window the main window.
            if (!signedIn)
            {
                Shutdown();
                return;
            }

            if (_session.MustChangePassword && !ShowChangePassword(forced: true))
            {
                _session.Clear();
                continue;
            }
            break;
        }

        var vm = _services.GetRequiredService<MainViewModel>();
        vm.SignOutRequested += SignOut;
        vm.ChangePasswordRequested += () => ShowChangePassword(forced: false);

        var main = new MainWindow(vm);
        MainWindow = main;
        main.Closed += (_, _) =>
        {
            if (!_signingOut) Shutdown();
        };
        main.Show();
    }

    private bool ShowChangePassword(bool forced)
    {
        var vm = _services.GetRequiredService<ChangePasswordViewModel>();
        vm.IsForced = forced;
        var window = new ChangePasswordWindow(vm) { Owner = DialogService.ActiveWindow() };
        return window.ShowDialog() == true;
    }

    private void SignOut()
    {
        _session.Clear();
        _signingOut = true;
        MainWindow?.Close();
        _signingOut = false;
        MainWindow = null;
        ShowLogin();
    }

    private void OnSessionRejected()
    {
        // Several in-flight requests can fail together; only react once.
        if (!_session.IsSignedIn || MainWindow is null) return;
        _session.Clear();
        _services.GetRequiredService<DialogService>()
            .Info("Your session has ended (expired, password changed, or account disabled). Please sign in again.");
        SignOut();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ClientLog.Error("ui", e.Exception);
        MessageBox.Show($"{e.Exception.Message}\n\nDetails were saved to {ClientLog.FilePath}",
            "MargPOS — unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
