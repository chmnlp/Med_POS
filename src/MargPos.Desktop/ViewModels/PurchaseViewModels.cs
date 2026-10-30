using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MargPos.Desktop.Services;
using MargPos.Shared;

namespace MargPos.Desktop.ViewModels;

public partial class PurchasesViewModel(ApiClient api, DialogService dialogs, InvoicePrinter printer, LabelPrinter labels,
    ClientSettings settings) : PagedListViewModel<PurchaseSummaryDto>
{
    public override string Title => "Purchases";

    [ObservableProperty] private DateTime? _fromDate = DateTime.Today.AddDays(-30);
    [ObservableProperty] private DateTime? _toDate = DateTime.Today;

    partial void OnFromDateChanged(DateTime? value) => _ = ReloadFromFirstPageAsync();
    partial void OnToDateChanged(DateTime? value) => _ = ReloadFromFirstPageAsync();

    protected override Task<PagedResult<PurchaseSummaryDto>> FetchAsync(string? query, int page, int pageSize, bool includeInactive) =>
        api.GetAsync<PagedResult<PurchaseSummaryDto>>(Query("api/purchases",
            ("q", query), ("page", page), ("pageSize", pageSize),
            ("from", FromDate?.ToString("yyyy-MM-dd")), ("to", ToDate?.ToString("yyyy-MM-dd"))));

    protected override object KeyOf(PurchaseSummaryDto item) => item.Id;

    protected override void OnSelectionChanged(PurchaseSummaryDto? item)
    {
        ViewCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ReturnToSupplierCommand.NotifyCanExecuteChanged();
        PrintLabelsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task ReturnToSupplier()
    {
        if (Selected is not { } p) return;
        var editor = new PurchaseReturnEditorViewModel(api, p);
        if (!dialogs.ShowEditor(editor) || editor.Result is not { } ret) return;
        await LoadAsync();
        new Views.InvoicePreviewWindow($"Debit note {ret.ReturnNo}", printer.BuildDebitNote(ret),
            () => printer.PrintDocument(printer.BuildDebitNote(ret), $"Debit note {ret.ReturnNo}", wide: true))
        { Owner = DialogService.ActiveWindow() }.ShowDialog();
    }

    [RelayCommand]
    private async Task New()
    {
        var editor = new PurchaseEditorViewModel(api, dialogs);
        if (!dialogs.ShowEditor(editor) || editor.Result is not { } saved) return;
        await LoadAsync();
        if (dialogs.Confirm($"Purchase {saved.PurchaseNo} saved: {saved.Lines.Count} line(s), ₹{saved.Total:N2}. Stock has been updated.\n\n" +
                            "Print barcode labels for the goods received now?", "Purchase saved"))
            ShowLabels(saved);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task PrintLabels()
    {
        if (Selected is null) return;
        PurchaseDto? purchase = null;
        if (await RunAsync(async () => purchase = await api.GetAsync<PurchaseDto>($"api/purchases/{Selected.Id}")))
            ShowLabels(purchase!);
    }

    /// <summary>One label per unit received (paid and free) on each line, by default.</summary>
    private void ShowLabels(PurchaseDto purchase)
    {
        var received = purchase.Lines.GroupBy(l => l.BatchId).ToDictionary(g => g.Key, g => g.Sum(l => l.Qty + l.FreeQty));
        dialogs.ShowEditor(new LabelPrintViewModel(api, labels, settings, $"Print labels — purchase {purchase.PurchaseNo}",
            new LabelRequest { BatchIds = [.. received.Keys] }, l => received.GetValueOrDefault(l.BatchId)));
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task View()
    {
        if (Selected is null) return;
        PurchaseDto? purchase = null;
        if (await RunAsync(async () => purchase = await api.GetAsync<PurchaseDto>($"api/purchases/{Selected.Id}")))
            dialogs.ShowEditor(new PurchaseDetailViewModel(purchase!));
    }

    private bool CanCancel() => Selected is { Status: PurchaseStatus.Completed };

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task Cancel()
    {
        if (Selected is not { } p) return;
        if (dialogs.ShowEditor(new ReasonViewModel($"Cancel purchase {p.PurchaseNo}", "Cancel purchase",
                $"₹{p.Total:N2} from {p.SupplierName}. The received stock will be removed again.",
                reason => api.PostAsync<PurchaseDto>($"api/purchases/{p.Id}/cancel", new CancelPurchaseRequest(reason)))))
            await LoadAsync();
    }
}

/// <summary>Asks for a reason and runs an action with it (cancellations).</summary>
public partial class ReasonViewModel(string title, string saveText, string warning, Func<string, Task> action) : EditorViewModel
{
    public override string Glyph => "\uE7BA";

    public override string Title => title;
    public override string SaveText => saveText;
    public string Warning => warning;

    [ObservableProperty] private string _reason = "";

    protected override Task SaveCoreAsync() =>
        string.IsNullOrWhiteSpace(Reason) ? throw new ApiException("Enter a reason.") : action(Reason.Trim());
}

public partial class PurchaseLineViewModel : ObservableObject
{
    public ItemDto? Item { get; init; }

    /// <summary>Item name as it appears in an imported file (shown while the line is unmatched).</summary>
    public string? FileItemName { get; init; }
    public string? SupplierItemCode { get; init; }
    public string? BatchNo { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public decimal Qty { get; init; }
    public decimal FreeQty { get; init; }
    public decimal Rate { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal GstRate { get; init; }
    public decimal Mrp { get; init; }
    public decimal SaleRate { get; init; }
    public decimal WholesaleRate { get; init; }
    public decimal HospitalRate { get; init; }

    /// <summary>Set when the sale rate was worked out from this margin; sent so the server records it.</summary>
    public decimal? MarginPercent { get; init; }

    public bool IsMatched => Item is not null;
    public decimal LandingCost => PurchaseCalculator.LandingCost(Calc.Taxable, Qty, FreeQty);
    public string DisplayName => Item?.Name ?? $"Not matched: {FileItemName}";
    public PurchaseCalculator.Line Calc => PurchaseCalculator.CalculateLine(Qty, Rate, DiscountPercent, GstRate);

    public PurchaseLineRequest ToRequest() => new()
    {
        ItemId = Item!.Id, BatchNo = BatchNo, ExpiryDate = ExpiryDate, Qty = Qty, FreeQty = FreeQty, Rate = Rate,
        DiscountPercent = DiscountPercent, GstRate = GstRate, Mrp = Mrp, SupplierItemCode = SupplierItemCode,
        SaleRate = MarginPercent is null ? SaleRate : 0, MarginPercent = MarginPercent,
        WholesaleRate = WholesaleRate, HospitalRate = HospitalRate,
    };
}

/// <summary>
/// Goods-received entry. Lines are built in the entry row (item search, batch, expiry, quantities, rates) and added
/// to the list; imported EDE lines arrive pre-filled and unmatched lines are fixed by editing them.
/// </summary>
public partial class PurchaseEditorViewModel : EditorViewModel
{
    public override string Glyph => "\uE7B8";

    private readonly ApiClient _api;
    private readonly DialogService _dialogs;
    private CancellationTokenSource? _searchDelay;

    public PurchaseEditorViewModel(ApiClient api, DialogService dialogs)
    {
        _api = api;
        _dialogs = dialogs;
        Lines.CollectionChanged += (_, _) => RefreshTotals();
        Warnings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasWarnings));
        _ = LoadSuppliersAsync();
    }

    public override string Title => "Receive goods (purchase entry)";
    public override string SaveText => "Save purchase";
    public PurchaseDto? Result { get; private set; }

    /// <summary>Lets the view move focus back to the item search after a line is added.</summary>
    public event Action? LineAdded;

    // ------------------------------------------------------------ header

    public ObservableCollection<SupplierDto> Suppliers { get; } = [];
    [ObservableProperty] private SupplierDto? _supplier;
    [ObservableProperty] private string _supplierInvoiceNo = "";
    [ObservableProperty] private string _invoiceDateText = DateTime.Today.ToString("dd-MM-yyyy");
    [ObservableProperty] private string? _notes;
    public ObservableCollection<string> Warnings { get; } = [];
    public bool HasWarnings => Warnings.Count > 0;
    [ObservableProperty] private decimal? _fileTotal;

    private async Task LoadSuppliersAsync()
    {
        PagedResult<SupplierDto>? result = null;
        await RunAsync(async () => result = await _api.GetAsync<PagedResult<SupplierDto>>("api/suppliers?pageSize=200"));
        foreach (var s in result?.Items ?? []) Suppliers.Add(s);
    }

    [RelayCommand]
    private async Task ImportEde()
    {
        if (Supplier is null)
        {
            ErrorMessage = "Choose the supplier first, so their item codes can be matched.";
            return;
        }
        if (Lines.Count > 0 && !_dialogs.Confirm("Replace the lines already entered with the file's lines?")) return;
        var path = _dialogs.OpenFile("Open distributor EDE bill", "EDE / CSV files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*");
        if (path is null) return;

        EdeImportDto? draft = null;
        if (!await RunAsync(async () => draft = await _api.UploadAsync<EdeImportDto>($"api/purchases/import-ede?supplierId={Supplier.Id}", path)))
            return;

        Lines.Clear();
        Warnings.Clear();
        SupplierInvoiceNo = draft!.SupplierInvoiceNo;
        if (draft.SupplierInvoiceDate is { } d) InvoiceDateText = d.ToString("dd-MM-yyyy");
        FileTotal = draft.FileTotal > 0 ? draft.FileTotal : null;
        foreach (var w in draft.Warnings) Warnings.Add(w);
        foreach (var l in draft.Lines)
            Lines.Add(new PurchaseLineViewModel
            {
                Item = l.MatchedItem, FileItemName = $"{l.ItemName} {l.Pack}".Trim(), SupplierItemCode = l.SupplierItemCode,
                BatchNo = l.BatchNo, ExpiryDate = l.ExpiryDate, Qty = l.Qty, FreeQty = l.FreeQty, Rate = l.Rate,
                DiscountPercent = l.DiscountPercent, GstRate = l.GstRate, Mrp = l.Mrp,
                SaleRate = l.MatchedItem is { } m && m.SaleRate > 0 && m.SaleRate <= l.Mrp ? m.SaleRate : l.Mrp,
            });
        if (Lines.FirstOrDefault(l => !l.IsMatched) is { } first) EditLine(first);
    }

    // ------------------------------------------------------------ entry row

    [ObservableProperty] private string _itemQuery = "";
    [ObservableProperty] private bool _isItemSearchOpen;
    [ObservableProperty] private ItemDto? _selectedSearchResult;
    public ObservableCollection<ItemDto> SearchResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntryIsBatchTracked), nameof(EntryHeading))]
    private ItemDto? _entryItem;

    [ObservableProperty] private string? _entryFileName;
    [ObservableProperty] private string? _entrySupplierCode;
    [ObservableProperty] private string? _entryBatch;
    [ObservableProperty] private string? _entryExpiry;
    [ObservableProperty] private decimal _entryQty;
    [ObservableProperty] private decimal _entryFree;
    [ObservableProperty] private decimal _entryRate;
    [ObservableProperty] private decimal _entryDiscount;
    [ObservableProperty] private decimal _entryGst;
    [ObservableProperty] private decimal _entryMrp;
    [ObservableProperty] private decimal _entrySale;
    [ObservableProperty] private decimal _entryMargin;
    [ObservableProperty] private decimal _entryWholesale;
    [ObservableProperty] private decimal _entryHospital;
    [ObservableProperty] private string? _entryCostInfo;
    private bool _saleFromMargin;
    private bool _settingSale;

    private decimal EntryLanding =>
        PurchaseCalculator.LandingCost(PurchaseCalculator.CalculateLine(EntryQty, EntryRate, EntryDiscount, EntryGst).Taxable, EntryQty, EntryFree);

    /// <summary>Re-prices the sale rate from the margin and refreshes the landed-cost hint.</summary>
    private void RefreshEntryPricing()
    {
        if (EntryItem is { } item && EntryMargin > 0 && EntryQty > 0)
        {
            _settingSale = true;
            EntrySale = Pricing.FromMargin(EntryLanding, item.GstRate, EntryMargin, EntryMrp);
            _settingSale = false;
            _saleFromMargin = true;
        }
        var landing = EntryLanding;
        var earned = EntryItem is { } i ? Pricing.MarginOnCost(EntrySale, i.GstRate, landing) : null;
        EntryCostInfo = EntryQty > 0 && landing > 0
            ? $"Landed cost ₹{landing:0.00##}/unit" + (earned is { } m ? $" · sale rate earns {m:0.#}% on cost" : "")
            : null;
    }

    partial void OnEntryMarginChanged(decimal value)
    {
        if (value <= 0) _saleFromMargin = false;
        RefreshEntryPricing();
    }

    partial void OnEntrySaleChanged(decimal value)
    {
        if (!_settingSale) _saleFromMargin = false;
        RefreshEntryPricing();
    }

    partial void OnEntryQtyChanged(decimal value) => RefreshEntryPricing();
    partial void OnEntryFreeChanged(decimal value) => RefreshEntryPricing();
    partial void OnEntryRateChanged(decimal value) => RefreshEntryPricing();
    partial void OnEntryDiscountChanged(decimal value) => RefreshEntryPricing();
    partial void OnEntryMrpChanged(decimal value) => RefreshEntryPricing();
    partial void OnEntryGstChanged(decimal value) => RefreshEntryPricing();

    public bool EntryIsBatchTracked => EntryItem?.IsBatchTracked ?? true;
    public string EntryHeading => EntryItem is { } i
        ? $"{i.Name} · {i.Pack} · stock {i.StockQty:0.###}"
        : EntryFileName is { } f ? $"Match this line from the file: {f}" : "Search an item to add";

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
        foreach (var i in result.Items) SearchResults.Add(i);
        SelectedSearchResult = SearchResults.FirstOrDefault();
        IsItemSearchOpen = SearchResults.Count > 0;
    }

    public void MoveSearchSelection(int delta)
    {
        if (SearchResults.Count == 0) return;
        var index = SelectedSearchResult is null ? -1 : SearchResults.IndexOf(SelectedSearchResult);
        SelectedSearchResult = SearchResults[Math.Clamp(index + delta, 0, SearchResults.Count - 1)];
    }

    /// <summary>Picks the highlighted search result into the entry row, keeping values already filled from a file.</summary>
    [RelayCommand]
    private void PickItem()
    {
        if (SelectedSearchResult is not { } item) return;
        _searchDelay?.Cancel();
        IsItemSearchOpen = false;
        ItemQuery = "";
        var fromFile = EntryFileName is not null;
        EntryItem = item;
        if (!fromFile)
        {
            EntryRate = item.PurchaseRate;
            EntryGst = item.GstRate;
            EntryMrp = item.Mrp;
            EntrySale = item.SaleRate;
            EntryQty = 0;
            EntryFree = 0;
            EntryDiscount = 0;
        }
        else if (EntrySale == 0 || EntrySale > EntryMrp) EntrySale = item.SaleRate <= EntryMrp ? item.SaleRate : EntryMrp;
        EntryWholesale = item.WholesaleMarginPercent > 0 ? 0 : item.WholesaleRate;
        EntryHospital = item.HospitalRate;
        EntryMargin = item.MarginPercent;
        if (!item.IsBatchTracked)
        {
            EntryBatch = null;
            EntryExpiry = null;
        }
    }

    [RelayCommand]
    private void AddLine()
    {
        ErrorMessage = null;
        if (EntryItem is not { } item)
        {
            ErrorMessage = "Search and pick the item first.";
            return;
        }
        DateOnly? expiry = null;
        if (item.IsBatchTracked)
        {
            if (string.IsNullOrWhiteSpace(EntryBatch)) { ErrorMessage = "Enter the batch number."; return; }
            if (!BatchDates.TryParseExpiry(EntryExpiry, out expiry) || expiry is null)
            {
                ErrorMessage = "Enter the expiry as printed on the pack, e.g. 03/27.";
                return;
            }
        }
        if (EntryQty <= 0) { ErrorMessage = "Enter the quantity received."; return; }
        if (EntryMrp > 0 && EntrySale > EntryMrp) { ErrorMessage = "Selling rate cannot be above MRP."; return; }

        Lines.Add(new PurchaseLineViewModel
        {
            Item = item, SupplierItemCode = EntrySupplierCode, BatchNo = item.IsBatchTracked ? EntryBatch!.Trim().ToUpperInvariant() : null,
            ExpiryDate = expiry, Qty = EntryQty, FreeQty = EntryFree, Rate = EntryRate, DiscountPercent = EntryDiscount,
            GstRate = EntryGst, Mrp = EntryMrp, SaleRate = EntrySale,
            WholesaleRate = EntryWholesale, HospitalRate = EntryHospital,
            MarginPercent = _saleFromMargin && EntryMargin > 0 ? EntryMargin : null,
        });
        ClearEntry();
        if (Lines.FirstOrDefault(l => !l.IsMatched) is { } next) EditLine(next);
        LineAdded?.Invoke();
    }

    private void ClearEntry()
    {
        EntryItem = null;
        EntryFileName = EntrySupplierCode = EntryBatch = EntryExpiry = null;
        EntryQty = EntryFree = EntryRate = EntryDiscount = EntryGst = EntryMrp = EntrySale = 0;
        EntryMargin = EntryWholesale = EntryHospital = 0;
        _saleFromMargin = false;
        EntryCostInfo = null;
    }

    // ------------------------------------------------------------ lines

    public ObservableCollection<PurchaseLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditLineCommand), nameof(RemoveLineCommand))]
    private PurchaseLineViewModel? _selectedLine;

    private bool HasSelectedLine() => SelectedLine is not null;

    /// <summary>Moves a line back into the entry row for changes (or to match an imported line to an item).</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedLine))]
    private void EditLine(PurchaseLineViewModel? line = null)
    {
        line ??= SelectedLine;
        if (line is null) return;
        Lines.Remove(line);
        EntryItem = line.Item;
        EntryFileName = line.IsMatched ? null : line.FileItemName;
        EntrySupplierCode = line.SupplierItemCode;
        EntryBatch = line.BatchNo;
        EntryExpiry = line.ExpiryDate?.ToString("MM/yy", System.Globalization.CultureInfo.InvariantCulture);
        EntryQty = line.Qty;
        EntryFree = line.FreeQty;
        EntryRate = line.Rate;
        EntryDiscount = line.DiscountPercent;
        EntryGst = line.GstRate;
        EntryMrp = line.Mrp;
        EntrySale = line.SaleRate;
        EntryWholesale = line.WholesaleRate;
        EntryHospital = line.HospitalRate;
        EntryMargin = line.MarginPercent ?? 0;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedLine))]
    private void RemoveLine()
    {
        if (SelectedLine is { } line) Lines.Remove(line);
    }

    [ObservableProperty] private decimal _taxable;
    [ObservableProperty] private decimal _tax;
    [ObservableProperty] private decimal _roundOff;
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private string? _totalCheck;

    private void RefreshTotals()
    {
        var t = PurchaseCalculator.Totals(Lines.Select(l => l.Calc));
        Taxable = t.Taxable;
        Tax = t.Tax;
        RoundOff = t.RoundOff;
        Total = t.Total;
        TotalCheck = FileTotal is { } f && Lines.All(l => l.IsMatched) && Math.Abs(f - Total) > 1
            ? $"Bill total in the file is ₹{f:N2} — check rates and quantities (difference ₹{Total - f:N2})."
            : null;
    }

    partial void OnFileTotalChanged(decimal? value) => RefreshTotals();

    protected override async Task SaveCoreAsync()
    {
        if (Supplier is null) throw new ApiException("Choose the supplier.");
        if (string.IsNullOrWhiteSpace(SupplierInvoiceNo)) throw new ApiException("Enter the supplier's invoice number.");
        if (!BatchDates.TryParseDate(InvoiceDateText, out var invoiceDate)) throw new ApiException("Enter the invoice date as dd-mm-yyyy.");
        if (EntryItem is not null || EntryFileName is not null)
            throw new ApiException("There is a line in the entry row. Add it (Add line) or clear it before saving.");
        if (Lines.Count == 0) throw new ApiException("Add at least one item.");
        if (Lines.Any(l => !l.IsMatched)) throw new ApiException("Some imported lines are not matched to items yet.");

        Result = await _api.PostAsync<PurchaseDto>("api/purchases", new PurchaseRequest
        {
            SupplierId = Supplier.Id, SupplierInvoiceNo = SupplierInvoiceNo.Trim(), SupplierInvoiceDate = invoiceDate,
            Notes = Notes, Lines = Lines.Select(l => l.ToRequest()).ToList(),
        });
    }
}

/// <summary>Read-only view of a saved purchase.</summary>
public class PurchaseDetailViewModel(PurchaseDto purchase) : EditorViewModel
{
    public override string Glyph => "\uE7B8";

    public PurchaseDto Purchase { get; } = purchase;
    public override string Title => $"Purchase {Purchase.PurchaseNo}" + (Purchase.Status == PurchaseStatus.Cancelled ? " (cancelled)" : "");
    public override string SaveText => "Close";
    public override bool ShowCancel => false;
    protected override Task SaveCoreAsync() => Task.CompletedTask;
}
