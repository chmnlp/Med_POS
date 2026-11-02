using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

public record ReasonChoice(AdjustmentReason Reason, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Records a stock correction on one batch. Removing reasons take a quantity to remove; a count correction takes the
/// quantity actually counted and works out the difference.
/// </summary>
public partial class StockAdjustViewModel : EditorViewModel
{
    public override string Glyph => "\uE9D5";

    private readonly ApiClient _api;

    public StockAdjustViewModel(ApiClient api, ItemDto item)
    {
        _api = api;
        Item = item;
        _reason = Reasons[0];
        _ = LoadAsync();
    }

    public ItemDto Item { get; }
    public override string Title => $"Adjust stock — {Item.Name}";
    public override string SaveText => "Save adjustment";
    public StockAdjustmentDto? Result { get; private set; }

    public static IReadOnlyList<ReasonChoice> Reasons { get; } =
    [
        new(AdjustmentReason.Damaged, "Damaged / broken"),
        new(AdjustmentReason.Expired, "Expired — write off"),
        new(AdjustmentReason.Lost, "Lost / missing"),
        new(AdjustmentReason.CountCorrection, "Physical count correction"),
        new(AdjustmentReason.Other, "Other (explain in note)"),
    ];

    public ObservableCollection<BatchDto> Batches { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private BatchDto? _batch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCount), nameof(IsOther), nameof(QuantityLabel), nameof(Preview))]
    private ReasonChoice _reason;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Preview))] private decimal _quantity;
    [ObservableProperty] private string? _note;

    public bool IsCount => Reason.Reason == AdjustmentReason.CountCorrection;
    public bool IsOther => Reason.Reason == AdjustmentReason.Other;

    public string QuantityLabel => Reason.Reason switch
    {
        AdjustmentReason.CountCorrection => "Quantity actually counted",
        AdjustmentReason.Other => "Change (+ to add, − to remove)",
        _ => "Quantity to remove",
    };

    private decimal Change => Reason.Reason switch
    {
        AdjustmentReason.CountCorrection => Quantity - (Batch?.QtyOnHand ?? 0),
        AdjustmentReason.Other => Quantity,
        _ => -Math.Abs(Quantity),
    };

    public string Preview => Batch is null ? "" :
        $"In stock: {Batch.QtyOnHand:0.###}  →  after: {Batch.QtyOnHand + Change:0.###}  ({Change:+0.###;-0.###;0})";

    private async Task LoadAsync()
    {
        List<BatchDto>? batches = null;
        await RunAsync(async () => batches = await _api.GetAsync<List<BatchDto>>($"api/items/{Item.Id}/batches?includeEmpty=true"));
        foreach (var b in batches ?? []) Batches.Add(b);
        Batch = Batches.FirstOrDefault(b => b.QtyOnHand > 0) ?? Batches.FirstOrDefault();
    }

    protected override async Task SaveCoreAsync()
    {
        if (Batch is null) throw new ApiException("Choose the batch.");
        if (Change == 0) throw new ApiException(IsCount ? "The counted quantity matches the stock — nothing to adjust." : "Enter a quantity.");
        Result = await _api.PostAsync<StockAdjustmentDto>("api/stock/adjustments",
            new StockAdjustmentRequest(Batch.Id, Change, Reason.Reason, Note));
    }
}

/// <summary>Read-only stock ledger for one item, newest first.</summary>
public partial class StockHistoryViewModel : EditorViewModel
{
    public override string Glyph => "\uE81C";

    public StockHistoryViewModel(ApiClient api, ItemDto item)
    {
        Item = item;
        _ = LoadAsync(api);
    }

    public ItemDto Item { get; }
    public override string Title => $"Stock history — {Item.Name}";
    public override string SaveText => "Close";
    public override bool ShowCancel => false;

    public ObservableCollection<StockMovementDto> Movements { get; } = [];
    [ObservableProperty] private string? _summary;

    private async Task LoadAsync(ApiClient api)
    {
        PagedResult<StockMovementDto>? page = null;
        await RunAsync(async () => page = await api.GetAsync<PagedResult<StockMovementDto>>($"api/items/{Item.Id}/movements?pageSize=200"));
        if (page is null) return;
        foreach (var m in page.Items) Movements.Add(m);
        Summary = page.Total > page.Items.Count
            ? $"Latest {page.Items.Count} of {page.Total} movements. Current stock {Item.StockQty:0.###}."
            : $"{page.Total} movement(s). Current stock {Item.StockQty:0.###}.";
    }

    protected override Task SaveCoreAsync() => Task.CompletedTask;
}

public partial class ReportsViewModel(ApiClient api, DialogService dialogs) : PageViewModel
{
    public override string Title => "Stock reports";

    public static IReadOnlyList<int> ExpiryWindows { get; } = [30, 60, 90, 180, 365];

    [ObservableProperty] private int _selectedTab;

    // Expiry
    public ObservableCollection<ExpiryReportRow> ExpiryRows { get; } = [];
    [ObservableProperty] private int _expiryDays = 90;
    [ObservableProperty] private bool _includeExpired = true;
    [ObservableProperty] private string? _expirySummary;

    // Reorder
    public ObservableCollection<ReorderReportRow> ReorderRows { get; } = [];
    [ObservableProperty] private string? _reorderSummary;

    // Valuation
    public ObservableCollection<StockValuationRow> ValuationRows { get; } = [];
    [ObservableProperty] private string _valuationSearch = "";
    [ObservableProperty] private StockValuationReport? _valuation;
    [ObservableProperty] private int _valuationPage = 1;

    // Profit
    public static IReadOnlyList<ProfitGrouping> ProfitGroupings { get; } = Enum.GetValues<ProfitGrouping>();
    public ObservableCollection<ProfitRow> ProfitRows { get; } = [];
    [ObservableProperty] private DateTime? _profitFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _profitTo = DateTime.Today;
    [ObservableProperty] private ProfitGrouping _profitGrouping = ProfitGrouping.Day;
    [ObservableProperty] private ProfitReport? _profit;

    partial void OnProfitFromChanged(DateTime? value) => _ = LoadProfitAsync();
    partial void OnProfitToChanged(DateTime? value) => _ = LoadProfitAsync();
    partial void OnProfitGroupingChanged(ProfitGrouping value) => _ = LoadProfitAsync();

    private async Task LoadProfitAsync()
    {
        if (ProfitFrom is not { } from || ProfitTo is not { } to) return;
        ProfitReport? report = null;
        if (!await RunAsync(async () => report = await api.GetAsync<ProfitReport>(
                $"api/reports/profit?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&groupBy={ProfitGrouping}"))) return;
        Profit = report;
        ProfitRows.Clear();
        foreach (var r in report!.Rows) ProfitRows.Add(r);
    }

    partial void OnExpiryDaysChanged(int value) => _ = LoadExpiryAsync();
    partial void OnIncludeExpiredChanged(bool value) => _ = LoadExpiryAsync();
    partial void OnSelectedTabChanged(int value) => _ = ActivateAsync();

    public override Task ActivateAsync() => SelectedTab switch
    {
        1 => LoadReorderAsync(),
        2 => LoadValuationAsync(),
        3 => LoadProfitAsync(),
        _ => LoadExpiryAsync(),
    };

    private async Task LoadExpiryAsync()
    {
        List<ExpiryReportRow>? rows = null;
        if (!await RunAsync(async () => rows = await api.GetAsync<List<ExpiryReportRow>>(
                $"api/reports/expiry?days={ExpiryDays}&includeExpired={IncludeExpired.ToString().ToLowerInvariant()}"))) return;
        ExpiryRows.Clear();
        foreach (var r in rows!) ExpiryRows.Add(r);
        var expired = rows.Count(r => r.DaysLeft <= 0);
        ExpirySummary = $"{rows.Count} batch(es) · {expired} already expired · value at cost ₹{rows.Sum(r => r.PurchaseValue):N2}";
    }

    private async Task LoadReorderAsync()
    {
        List<ReorderReportRow>? rows = null;
        if (!await RunAsync(async () => rows = await api.GetAsync<List<ReorderReportRow>>("api/reports/reorder"))) return;
        ReorderRows.Clear();
        foreach (var r in rows!) ReorderRows.Add(r);
        ReorderSummary = rows.Count == 0
            ? "Nothing to reorder. (Items need a reorder level to appear here.)"
            : $"{rows.Count} item(s) at or below reorder level · suggested order fills up to twice the reorder level";
    }

    [RelayCommand]
    private async Task LoadValuationAsync()
    {
        StockValuationReport? report = null;
        var q = string.IsNullOrWhiteSpace(ValuationSearch) ? "" : $"&q={Uri.EscapeDataString(ValuationSearch.Trim())}";
        if (!await RunAsync(async () => report = await api.GetAsync<StockValuationReport>(
                $"api/reports/stock-valuation?page={ValuationPage}&pageSize=200{q}"))) return;
        Valuation = report;
        ValuationRows.Clear();
        foreach (var r in report!.Rows) ValuationRows.Add(r);
    }

    [RelayCommand]
    private Task Refresh() => ActivateAsync();

    [RelayCommand]
    private void Export()
    {
        var (name, csv) = SelectedTab switch
        {
            1 => ("reorder", CsvWriter.Build(["Code", "Item", "Company", "Stock", "Reorder level", "Suggested qty", "Last supplier", "Last rate"],
                ReorderRows.Select(r => new object?[] { r.ItemCode, r.ItemName, r.Manufacturer, r.Stock, r.ReorderLevel, r.SuggestedQty, r.LastSupplier, r.LastPurchaseRate }))),
            2 => ("stock-valuation", CsvWriter.Build(["Code", "Item", "Company", "Qty", "Value at cost", "Value at MRP"],
                ValuationRows.Select(r => new object?[] { r.ItemCode, r.ItemName, r.Manufacturer, r.Qty, r.PurchaseValue, r.MrpValue }))),
            3 => ("profit", CsvWriter.Build([ProfitGrouping == ProfitGrouping.Item ? "Item" : "Day", "Qty", "Sales (ex GST)", "Cost", "Profit", "Margin %"],
                ProfitRows.Select(r => new object?[] { r.Label, r.Qty, r.Revenue, r.Cost, r.Profit, r.MarginPercent }))),
            _ => ("expiry", CsvWriter.Build(["Code", "Item", "Company", "Batch", "Expiry", "Days left", "Qty", "Cost rate", "MRP", "Value at cost"],
                ExpiryRows.Select(r => new object?[] { r.ItemCode, r.ItemName, r.Manufacturer, r.BatchNo, r.ExpiryDate.ToString("dd-MM-yyyy"), r.DaysLeft, r.Qty, r.PurchaseRate, r.Mrp, r.PurchaseValue }))),
        };
        var path = dialogs.SaveFile("Export report", "CSV (Excel) files (*.csv)|*.csv", $"{name}-{DateTime.Today:yyyy-MM-dd}.csv");
        if (path is null) return;
        File.WriteAllText(path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        dialogs.Info($"Saved {Path.GetFileName(path)}.");
    }
}
