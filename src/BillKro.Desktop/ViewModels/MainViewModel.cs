using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace BillKro.Desktop.ViewModels;

public record NavItem(string Title, string Glyph, Type PageType, string Group, string Description)
{
    /// <summary>Ctrl+number shortcut shown next to the first nine entries.</summary>
    public string? Shortcut { get; init; }

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
    [ObservableProperty] private string _clockText = "";
    [ObservableProperty] private bool _isDark = BrandTheme.IsDark;
    [ObservableProperty] private bool _isQuickJumpOpen;
    [ObservableProperty] private string _quickJumpText = "";
    [ObservableProperty] private NavItem? _quickJumpSelected;

    public MainViewModel(IServiceProvider services, Session session, ClientSettings settings, NavigationService navigation)
    {
        _services = services;
        _settings = settings;
        UserName = session.User!.FullName;
        UserRole = session.User.Role.ToString();
        Initials = InitialsOf(UserName);

        const string counter = "Counter", insights = "Insights", stock = "Stock", money = "Money & parties", system = "System";
        Navigation =
        [
            new("Billing", "", typeof(BillingViewModel), counter, "Scan, bill and collect payment"),
            new("Sales history", "", typeof(SalesViewModel), counter, "Find, reprint, return or cancel past bills"),
            new("Day end", "", typeof(DayEndViewModel), counter, "Count the drawer and close your counter"),
            new("Dashboard", "", typeof(DashboardViewModel), insights, "How the store is doing today"),
        ];
        if (session.CanManage)
        {
            Navigation.Add(new("Sales & GST", "", typeof(SalesReportsViewModel), insights, "Sales summary, profit and GSTR-1 working"));
            Navigation.Add(new("Stock reports", "", typeof(ReportsViewModel), insights, "Expiry, reorder and stock valuation"));
        }
        Navigation.Add(new("Items & Stock", "", typeof(ItemsViewModel), stock, "Item master, batches and stock levels"));
        if (session.CanManage)
        {
            Navigation.Add(new("Purchases", "", typeof(PurchasesViewModel), stock, "Enter or import supplier bills"));
            Navigation.Add(new("Supplier returns", "", typeof(PurchaseReturnsViewModel), stock, "Expiry and breakage returns, debit notes"));
        }
        Navigation.Add(new("Accounts", "", typeof(AccountsViewModel), money, "Receivables, payables, receipts and payments"));
        Navigation.Add(new("Customers", "", typeof(CustomersViewModel), money, "Customer master and credit terms"));
        Navigation.Add(new("Suppliers", "", typeof(SuppliersViewModel), money, "Supplier master and EDE codes"));
        if (session.CanManage)
            Navigation.Add(new("Data Exchange", "", typeof(ErpLinkViewModel), system, "Send purchases to the ERP, import masters"));
        if (session.IsAdmin)
            Navigation.Add(new("Users", "", typeof(UsersViewModel), system, "Cashier, manager and admin accounts"));
        Navigation.Add(new("Settings", "", typeof(SettingsViewModel), system, "Store profile, printing and billing rules"));

        for (var i = 0; i < Math.Min(9, Navigation.Count); i++)
            Navigation[i] = Navigation[i] with { Shortcut = $"Ctrl+{i + 1}" };

        navigation.Handler = Navigate;

        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        clock.Tick += (_, _) => UpdateClock();
        clock.Start();
        UpdateClock();

        SelectedNav = Navigation[0];
    }

    public List<NavItem> Navigation { get; }
    public string UserName { get; }
    public string UserRole { get; }
    public string Initials { get; }

    public IEnumerable<NavItem> QuickJumpResults
    {
        get
        {
            var q = QuickJumpText.Trim();
            if (q.Length == 0) return Navigation;

            // Best first: title starts with it, a word in the title starts with it, then description matches.
            static bool WordStarts(string text, string q) =>
                text.Split(' ', '&', '-').Any(w => w.StartsWith(q, StringComparison.OrdinalIgnoreCase));
            return Navigation
                .Select(n => (Item: n, Rank:
                    n.Title.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0
                    : WordStarts(n.Title, q) ? 1
                    : n.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ? 2
                    : WordStarts(n.Description, q) || WordStarts(n.Group, q) ? 3
                    : -1))
                .Where(x => x.Rank >= 0)
                .OrderBy(x => x.Rank)
                .Select(x => x.Item)
                .ToList();
        }
    }

    public event Action? SignOutRequested;
    public event Action? ChangePasswordRequested;

    public static string InitialsOf(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    private void UpdateClock() => ClockText = DateTime.Now.ToString("ddd d MMM  ·  h:mm tt");

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;
        if (!_pages.TryGetValue(value.PageType, out var page))
            _pages[value.PageType] = page = (PageViewModel)_services.GetRequiredService(value.PageType);
        CurrentPage = page;
        _ = page.ActivateAsync();
    }

    private bool Navigate(Type pageType)
    {
        var target = Navigation.FirstOrDefault(n => n.PageType == pageType);
        if (target is null) return false;
        SelectedNav = target;
        return true;
    }

    /// <summary>Ctrl+1..9 jumps straight to a sidebar entry.</summary>
    public void GoToIndex(int index)
    {
        if (index >= 0 && index < Navigation.Count) SelectedNav = Navigation[index];
    }

    partial void OnQuickJumpTextChanged(string value)
    {
        OnPropertyChanged(nameof(QuickJumpResults));
        QuickJumpSelected = QuickJumpResults.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenQuickJump()
    {
        QuickJumpText = "";
        QuickJumpSelected = SelectedNav;
        IsQuickJumpOpen = true;
    }

    public void ConfirmQuickJump()
    {
        if (QuickJumpSelected is { } target) SelectedNav = target;
        IsQuickJumpOpen = false;
    }

    [RelayCommand]
    private void SignOut() => SignOutRequested?.Invoke();

    [RelayCommand]
    private void ChangePassword() => ChangePasswordRequested?.Invoke();

    [RelayCommand]
    private void ToggleTheme()
    {
        _settings.Theme = BrandTheme.IsDark ? "Light" : "Dark";
        _settings.Save();
        App.ApplyTheme(_settings.Theme);
        IsDark = BrandTheme.IsDark;
    }
}

/// <summary>A dashboard tile that needs someone's attention; clicking it opens the page that deals with it.</summary>
public record AttentionItem(string Glyph, string Title, string Detail, string Tone, IRelayCommand Open);

public partial class DashboardViewModel(ApiClient api, Session session, NavigationService navigation) : PageViewModel
{
    public override string Title => "Dashboard";

    [ObservableProperty] private DashboardSummary? _summary;

    public string Greeting => DateTime.Now.Hour switch
    {
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening",
    };

    public string FirstName => session.User?.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    public string TodayText => DateTime.Today.ToString("dddd, d MMMM yyyy");
    public bool CanManage => session.CanManage;

    /// <summary>Average bill value today, or null before the first bill.</summary>
    public decimal? AverageBill => Summary is { TodayBills: > 0 } s ? Math.Round(s.TodaySales / s.TodayBills, 0) : null;

    /// <summary>Gross margin this month as a percentage of sales (managers only).</summary>
    public decimal? MonthMarginPercent => Summary is { MonthProfit: { } p, MonthSales: > 0 } s ? Math.Round(p * 100 / s.MonthSales, 1) : null;

    /// <summary>Share of the month's sales made today, drawn as a bar under today's figure.</summary>
    public double TodayShareOfMonth => Summary is { MonthSales: > 0 } s ? (double)Math.Clamp(s.TodaySales / s.MonthSales, 0, 1) : 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllClear))]
    private List<AttentionItem> _attention = [];

    public bool AllClear => Summary is not null && Attention.Count == 0;

    partial void OnSummaryChanged(DashboardSummary? value)
    {
        var attention = new List<AttentionItem>();
        if (value is not null)
        {
            if (value.OverdueCustomers > 0)
                attention.Add(new("", $"₹ {value.OverdueAmount:N0} overdue",
                    $"{value.OverdueCustomers} customer(s) past their credit days — send reminders", "Critical", GoAccountsCommand));
            if (value.NearExpiryBatches > 0)
                attention.Add(new("", $"{value.NearExpiryBatches} batch(es) expiring soon",
                    "In stock and expiring within 90 days — sell first or return to supplier", "Caution",
                    session.CanManage ? GoStockReportsCommand : GoItemsCommand));
            if (value.LowStockItems > 0)
                attention.Add(new("", $"{value.LowStockItems} item(s) running low",
                    "At or below reorder level — time to order", "Caution",
                    session.CanManage ? GoStockReportsCommand : GoItemsCommand));
        }
        Attention = attention;
        OnPropertyChanged(nameof(AverageBill));
        OnPropertyChanged(nameof(MonthMarginPercent));
        OnPropertyChanged(nameof(TodayShareOfMonth));
    }

    public override async Task ActivateAsync()
    {
        OnPropertyChanged(nameof(Greeting));
        await RunAsync(async () => Summary = await api.GetAsync<DashboardSummary>("api/dashboard/summary"));
    }

    [RelayCommand] private void GoBilling() => navigation.GoTo<BillingViewModel>();
    [RelayCommand] private void GoPurchases() => navigation.GoTo<PurchasesViewModel>();
    [RelayCommand] private void GoAccounts() => navigation.GoTo<AccountsViewModel>();
    [RelayCommand] private void GoDayEnd() => navigation.GoTo<DayEndViewModel>();
    [RelayCommand] private void GoItems() => navigation.GoTo<ItemsViewModel>();
    [RelayCommand] private void GoStockReports() => navigation.GoTo<ReportsViewModel>();
    [RelayCommand] private void GoSalesReports() => navigation.GoTo<SalesReportsViewModel>();
}
