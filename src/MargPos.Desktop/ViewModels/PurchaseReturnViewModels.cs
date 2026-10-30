using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MargPos.Desktop.Services;
using MargPos.Desktop.Views;
using MargPos.Shared;

namespace MargPos.Desktop.ViewModels;

public partial class PurchaseReturnsViewModel(ApiClient api, DialogService dialogs, InvoicePrinter printer)
    : PagedListViewModel<PurchaseReturnSummaryDto>
{
    public override string Title => "Supplier returns (debit notes)";

    [ObservableProperty] private DateTime? _fromDate = DateTime.Today.AddDays(-30);
    [ObservableProperty] private DateTime? _toDate = DateTime.Today;

    partial void OnFromDateChanged(DateTime? value) => _ = ReloadFromFirstPageAsync();
    partial void OnToDateChanged(DateTime? value) => _ = ReloadFromFirstPageAsync();

    protected override Task<PagedResult<PurchaseReturnSummaryDto>> FetchAsync(string? query, int page, int pageSize, bool includeInactive) =>
        api.GetAsync<PagedResult<PurchaseReturnSummaryDto>>(Query("api/purchase-returns",
            ("page", page), ("pageSize", pageSize),
            ("from", FromDate?.ToString("yyyy-MM-dd")), ("to", ToDate?.ToString("yyyy-MM-dd"))));

    protected override object KeyOf(PurchaseReturnSummaryDto item) => item.Id;

    protected override void OnSelectionChanged(PurchaseReturnSummaryDto? item) => ViewCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task New()
    {
        var editor = new PurchaseReturnEditorViewModel(api, null);
        if (!dialogs.ShowEditor(editor) || editor.Result is not { } saved) return;
        await LoadAsync();
        ShowDebitNote(saved);
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task View()
    {
        if (Selected is null) return;
        PurchaseReturnDto? ret = null;
        if (await RunAsync(async () => ret = await api.GetAsync<PurchaseReturnDto>($"api/purchase-returns/{Selected.Id}")))
            ShowDebitNote(ret!);
    }

    private void ShowDebitNote(PurchaseReturnDto ret) =>
        new InvoicePreviewWindow($"Debit note {ret.ReturnNo}", printer.BuildDebitNote(ret),
            () => printer.PrintDocument(printer.BuildDebitNote(ret), $"Debit note {ret.ReturnNo}", wide: true))
        { Owner = DialogService.ActiveWindow() }.ShowDialog();
}

/// <summary>One lot being returned; quantity and credit rate are editable in the grid.</summary>
public partial class ReturnEntryLine : ObservableObject
{
    public int BatchId { get; init; }
    public int? PurchaseLineId { get; init; }
    public string ItemName { get; init; } = "";
    public string BatchNo { get; init; } = "";
    public DateOnly? ExpiryDate { get; init; }
    public decimal GstRate { get; init; }

    /// <summary>Most that can go back: stock on hand, and (against a purchase) what is left of that line.</summary>
    public decimal MaxQty { get; init; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Amount), nameof(IsOver))] private decimal _qty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Amount))] private decimal _rate;

    public decimal Amount => PurchaseCalculator.CalculateLine(Qty, Rate, 0, GstRate).Amount;
    public bool IsOver => Qty > MaxQty;
}

/// <summary>
/// Raises a debit note. Free-standing (typical expiry return: load the supplier's expired / near-expiry lots, or add
/// lots one by one) or against one purchase (quantities limited to what that purchase brought in).
/// </summary>
public partial class PurchaseReturnEditorViewModel : EditorViewModel
{
    public override string Glyph => "\uE7A7";

    private readonly ApiClient _api;
    private readonly PurchaseSummaryDto? _purchase;
    private CancellationTokenSource? _searchDelay;

    public PurchaseReturnEditorViewModel(ApiClient api, PurchaseSummaryDto? againstPurchase)
    {
        _api = api;
        _purchase = againstPurchase;
        Lines.CollectionChanged += (_, e) =>
        {
            foreach (ReturnEntryLine l in e.NewItems ?? Array.Empty<ReturnEntryLine>()) l.PropertyChanged += OnLineChanged;
            RefreshTotals();
        };
        _ = LoadAsync();
    }

    public override string Title => _purchase is null ? "Return goods to supplier" : $"Return against {_purchase.PurchaseNo}";
    public override string SaveText => "Save debit note";
    public bool IsAgainstPurchase => _purchase is not null;
    public bool IsFreeStanding => _purchase is null;
    public PurchaseReturnDto? Result { get; private set; }

    public ObservableCollection<SupplierDto> Suppliers { get; } = [];
    [ObservableProperty] private SupplierDto? _supplier;
    [ObservableProperty] private string? _reason = "Expiry";
    public static IReadOnlyList<int> ExpiryWindows { get; } = [0, 30, 60, 90];
    [ObservableProperty] private int _expiryDays = 30;

    public ObservableCollection<ReturnEntryLine> Lines { get; } = [];
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RemoveLineCommand))] private ReturnEntryLine? _selectedLine;

    [ObservableProperty] private decimal _taxable;
    [ObservableProperty] private decimal _tax;
    [ObservableProperty] private decimal _total;

    private async Task LoadAsync()
    {
        PagedResult<SupplierDto>? suppliers = null;
        List<ReturnablePurchaseLineDto>? returnable = null;
        await RunAsync(async () =>
        {
            suppliers = await _api.GetAsync<PagedResult<SupplierDto>>("api/suppliers?pageSize=200&includeInactive=true");
            if (_purchase is not null)
                returnable = await _api.GetAsync<List<ReturnablePurchaseLineDto>>($"api/purchases/{_purchase.Id}/returnable");
        });
        foreach (var s in suppliers?.Items ?? []) Suppliers.Add(s);
        if (_purchase is null) return;

        Supplier = Suppliers.FirstOrDefault(s => s.Name == _purchase.SupplierName);
        Reason = null;
        foreach (var r in returnable ?? [])
        {
            var max = Math.Min(r.Received - r.AlreadyReturned, r.InStock);
            if (max <= 0) continue;
            Lines.Add(new ReturnEntryLine
            {
                BatchId = r.BatchId, PurchaseLineId = r.PurchaseLineId, ItemName = r.ItemName, BatchNo = r.BatchNo,
                ExpiryDate = r.ExpiryDate, GstRate = r.GstRate, MaxQty = max, Rate = r.LandingCost,
            });
        }
        if (Lines.Count == 0) ErrorMessage = "Nothing from this purchase is left to return (sold or already returned).";
    }

    /// <summary>Adds every lot bought from this supplier that is expired or expires within the window.</summary>
    [RelayCommand]
    private async Task LoadExpiry()
    {
        if (Supplier is null)
        {
            ErrorMessage = "Choose the supplier first.";
            return;
        }
        List<ReturnCandidateDto>? found = null;
        if (!await RunAsync(async () => found = await _api.GetAsync<List<ReturnCandidateDto>>(
                $"api/purchase-returns/candidates?supplierId={Supplier.Id}&days={ExpiryDays}"))) return;

        var added = 0;
        foreach (var c in found!.Where(c => Lines.All(l => l.BatchId != c.BatchId)))
        {
            Lines.Add(new ReturnEntryLine
            {
                BatchId = c.BatchId, ItemName = $"{c.ItemName} {c.Pack}".Trim(), BatchNo = c.BatchNo, ExpiryDate = c.ExpiryDate,
                GstRate = c.GstRate, MaxQty = c.QtyOnHand, Qty = c.QtyOnHand, Rate = c.LandingCost,
            });
            added++;
        }
        ErrorMessage = added == 0 ? $"No stock from {Supplier.Name} expiring within {ExpiryDays} days." : null;
    }

    // ------------------------------------------------------------ add one lot

    [ObservableProperty] private string _itemQuery = "";
    [ObservableProperty] private bool _isItemSearchOpen;
    [ObservableProperty] private ItemDto? _selectedSearchResult;
    public ObservableCollection<ItemDto> SearchResults { get; } = [];
    public ObservableCollection<BatchDto> EntryLots { get; } = [];
    [ObservableProperty] private ItemDto? _entryItem;
    [ObservableProperty] private BatchDto? _entryLot;

    partial void OnItemQueryChanged(string value)
    {
        _searchDelay?.Cancel();
        if (value.Trim().Length < 2)
        {
            IsItemSearchOpen = false;
            return;
        }
        var cts = _searchDelay = new CancellationTokenSource();
        _ = SearchAsync(value.Trim(), cts.Token);
    }

    private async Task SearchAsync(string text, CancellationToken ct)
    {
        try { await Task.Delay(250, ct); } catch (TaskCanceledException) { return; }
        PagedResult<ItemDto>? result = null;
        await RunAsync(async () => result = await _api.GetAsync<PagedResult<ItemDto>>($"api/items?q={Uri.EscapeDataString(text)}&pageSize=20", ct));
        if (ct.IsCancellationRequested || result is null) return;
        SearchResults.Clear();
        foreach (var i in result.Items.Where(i => i.StockQty > 0)) SearchResults.Add(i);
        SelectedSearchResult = SearchResults.FirstOrDefault();
        IsItemSearchOpen = SearchResults.Count > 0;
    }

    public void MoveSearchSelection(int delta)
    {
        if (SearchResults.Count == 0) return;
        var index = SelectedSearchResult is null ? -1 : SearchResults.IndexOf(SelectedSearchResult);
        SelectedSearchResult = SearchResults[Math.Clamp(index + delta, 0, SearchResults.Count - 1)];
    }

    [RelayCommand]
    private async Task PickItem()
    {
        if (SelectedSearchResult is not { } item) return;
        _searchDelay?.Cancel();
        IsItemSearchOpen = false;
        ItemQuery = "";
        EntryItem = item;
        EntryLots.Clear();
        List<BatchDto>? lots = null;
        await RunAsync(async () => lots = await _api.GetAsync<List<BatchDto>>($"api/items/{item.Id}/batches"));
        foreach (var b in lots ?? []) EntryLots.Add(b);   // in stock, nearest expiry first; expired lots included
        EntryLot = EntryLots.FirstOrDefault();
    }

    [RelayCommand]
    private void AddLot()
    {
        if (EntryItem is not { } item || EntryLot is not { } lot)
        {
            ErrorMessage = "Search an item and choose the lot.";
            return;
        }
        if (Lines.Any(l => l.BatchId == lot.Id))
        {
            ErrorMessage = $"Lot {lot.BatchNo} is already on this return.";
            return;
        }
        Lines.Add(new ReturnEntryLine
        {
            BatchId = lot.Id, ItemName = $"{item.Name} {item.Pack}".Trim(), BatchNo = lot.BatchNo, ExpiryDate = lot.ExpiryDate,
            GstRate = item.GstRate, MaxQty = lot.QtyOnHand, Qty = lot.QtyOnHand, Rate = lot.PurchaseRate,
        });
        EntryItem = null;
        EntryLot = null;
        EntryLots.Clear();
        ErrorMessage = null;
    }

    private bool HasSelectedLine() => SelectedLine is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedLine))]
    private void RemoveLine()
    {
        if (SelectedLine is { } l) Lines.Remove(l);
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e) => RefreshTotals();

    private void RefreshTotals()
    {
        var t = PurchaseCalculator.Totals(Lines.Where(l => l.Qty > 0)
            .Select(l => PurchaseCalculator.CalculateLine(l.Qty, l.Rate, 0, l.GstRate)));
        Taxable = t.Taxable;
        Tax = t.Tax;
        Total = t.Total;
    }

    protected override async Task SaveCoreAsync()
    {
        if (Supplier is null) throw new ApiException("Choose the supplier.");
        var lines = Lines.Where(l => l.Qty > 0).ToList();
        if (lines.Count == 0) throw new ApiException("Enter a quantity for at least one lot.");
        if (lines.FirstOrDefault(l => l.IsOver) is { } over)
            throw new ApiException($"At most {over.MaxQty:0.###} of {over.ItemName} ({over.BatchNo}) can be returned.");

        Result = await _api.PostAsync<PurchaseReturnDto>("api/purchase-returns", new PurchaseReturnRequest
        {
            SupplierId = Supplier.Id, PurchaseId = _purchase?.Id, Reason = Reason,
            Lines = lines.Select(l => new PurchaseReturnLineRequest
            {
                BatchId = l.BatchId, Qty = l.Qty, Rate = l.Rate, GstRate = l.GstRate, PurchaseLineId = l.PurchaseLineId,
            }).ToList(),
        });
    }
}
