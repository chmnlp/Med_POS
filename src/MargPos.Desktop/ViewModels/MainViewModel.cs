using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MargPos.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MargPos.Desktop.ViewModels;

public record NavItem(string Title, string Glyph, Type PageType)
{
    /// <summary>Used as the accessible name of the sidebar entry.</summary>
    public override string ToString() => Title;
}

public partial class MainViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly ClientSettings _settings;
    private readonly Dictionary<Type, PageViewModel> _pages = [];

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private PageViewModel? _currentPage;

    public MainViewModel(IServiceProvider services, Session session, ClientSettings settings)
    {
        _services = services;
        _settings = settings;
        UserName = session.User!.FullName;
        UserRole = session.User.Role.ToString();

        Navigation =
        [
            new("Dashboard", "", typeof(DashboardViewModel)),
            new("Items & Stock", "", typeof(ItemsViewModel)),
            new("Customers", "", typeof(CustomersViewModel)),
            new("Suppliers", "", typeof(SuppliersViewModel)),
        ];
        if (session.IsAdmin)
            Navigation.Add(new("Users", "", typeof(UsersViewModel)));

        SelectedNav = Navigation[0];
    }

    public List<NavItem> Navigation { get; }
    public string UserName { get; }
    public string UserRole { get; }

    public event Action? SignOutRequested;
    public event Action? ChangePasswordRequested;

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;
        if (!_pages.TryGetValue(value.PageType, out var page))
            _pages[value.PageType] = page = (PageViewModel)_services.GetRequiredService(value.PageType);
        CurrentPage = page;
        _ = page.ActivateAsync();
    }

    [RelayCommand]
    private void SignOut() => SignOutRequested?.Invoke();

    [RelayCommand]
    private void ChangePassword() => ChangePasswordRequested?.Invoke();

    [RelayCommand]
    private void ToggleTheme()
    {
        var mode = Application.Current.ThemeMode;
        var dark = mode == ThemeMode.Dark || (mode == ThemeMode.System && IsSystemDark());
        _settings.Theme = dark ? "Light" : "Dark";
        _settings.Save();
        App.ApplyTheme(_settings.Theme);
    }

    private static bool IsSystemDark()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}

public partial class DashboardViewModel(ApiClient api) : PageViewModel
{
    public override string Title => "Dashboard";

    [ObservableProperty] private MargPos.Shared.DashboardSummary? _summary;

    public override async Task ActivateAsync() =>
        await RunAsync(async () => Summary = await api.GetAsync<MargPos.Shared.DashboardSummary>("api/dashboard/summary"));
}
