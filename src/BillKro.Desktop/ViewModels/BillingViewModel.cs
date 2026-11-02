using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

/// <summary>Bill parked on the server; restored by item/batch ids so prices and stock are re-read on resume.</summary>
public record HeldBillPayload(int? CustomerId, string? WalkInName, string? WalkInPhone, string? DoctorName,
    string? PatientName, List<SaleLineRequest> Lines);

public partial class BillingViewModel : PageViewModel
{
    private static readonly Regex QtyPrefix = new(@"^(\d+(?:\.\d{1,3})?)\s*\*\s*(.+)$", RegexOptions.Compiled);

    private readonly ApiClient _api;
    private readonly Session _session;
    private readonly StoreContext _store;
    private readonly ClientSettings _settings;
    private readonly DialogService _dialogs;
    private readonly InvoicePrinter _printer;
    private CancellationTokenSource? _itemSearchDelay;
    private CancellationTokenSource? _customerSearchDelay;

    public BillingViewModel(ApiClient api, Session session, StoreContext store, ClientSettings settings,
        DialogService dialogs, InvoicePrinter printer)
    {
        _api = api;
        _session = session;
        _store = store;
        _settings = settings;
        _dialogs = dialogs;
        _printer = printer;
        Lines.CollectionChanged += (_, e) =>
        {
            foreach (BillLineViewModel l in e.NewItems ?? Array.Empty<BillLineViewModel>()) l.PropertyChanged += OnLineChanged;
            foreach (BillLineViewModel l in e.OldItems ?? Array.Empty<BillLineViewModel>()) l.PropertyChanged -= OnLineChanged;
            Recalculate();
        };
        _totals = BillCalculator.Calculate([], false);
    }

    public override string Title => "Billing";
    public bool CanEditRate => _session.CanManage;

    /// <summary>Asks the view to move focus (scan box or the quantity of the selected line).</summary>
    public event Action<string>? FocusRequested;

    // ------------------------------------------------------------ item entry

    [ObservableProperty] private string _scanText = "";
    [ObservableProperty] private bool _isItemSearchOpen;
    [ObservableProperty] private ItemDto? _selectedSearchResult;
    public ObservableCollection<ItemDto> SearchResults { get; } = [];

    [ObservableProperty] private string? _statusMessage;

    partial void OnScanTextChanged(string value)
    {
        _itemSearchDelay?.Cancel();
        var text = StripQtyPrefix(value).Text;
        // Numbers search from the first digit so item codes ("10" = I00010) show up as typed. A scanner sends the
        // whole barcode plus Enter well inside the search delay, so scans still go straight to Scan().
        if (text.Length < (text.All(char.IsDigit) ? 1 : 3))
        {
            IsItemSearchOpen = false;
            return;
        }
        var cts = _itemSearchDelay = new CancellationTokenSource();
        _ = SearchItemsAfterDelayAsync(text, cts.Token);
    }

    private async Task SearchItemsAfterDelayAsync(string text, CancellationToken ct)
    {
        try { await Task.Delay(250, ct); } catch (TaskCanceledException) { return; }
        PagedResult<ItemDto>? result = null;
        await RunAsync(async () => result = await _api.GetAsync<PagedResult<ItemDto>>(
            $"api/items?q={Uri.EscapeDataString(text)}&pageSize=20", ct));
        if (ct.IsCancellationRequested || result is null) return;

        SearchResults.Clear();
        foreach (var i in result.Items) SearchResults.Add(i);
        SelectedSearchResult = SearchResults.FirstOrDefault(i => i.StockQty > 0) ?? SearchResults.FirstOrDefault();
        IsItemSearchOpen = SearchResults.Count > 0;
    }

    /// <summary>Splits off a "3*" quantity prefix, and reduces a scanned QR label to its lot code.</summary>
    private static (decimal Qty, string Text) StripQtyPrefix(string input)
    {
        var text = QrCode.ScannedCode(input);
        var m = QtyPrefix.Match(text);
        return m.Success ? (decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value.Trim()) : (1m, text);
    }

    /// <summary>Enter in the scan box: exact barcode/code first, else name search. "3*code" adds 3.</summary>
    [RelayCommand]
    private async Task Scan()
    {
        _itemSearchDelay?.Cancel();
        var (qty, text) = StripQtyPrefix(ScanText);
        if (text.Length == 0) return;

        if (IsItemSearchOpen && SelectedSearchResult is { } picked)
        {
            await AddItemAsync(picked, qty);
            return;
        }

        ItemDto? item = null;
        try
        {
            var scan = await _api.GetAsync<ScanResultDto>($"api/items/scan/{Uri.EscapeDataString(text)}");
            if (scan.BatchId is { } batchId)
            {
                await AddItemAsync(scan.Item, qty, (batchId, scan.BatchNo!));
                return;
            }
            item = scan.Item;
        }
        catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            // Not a code; fall back to name search.
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        if (item is null)
        {
            PagedResult<ItemDto>? found = null;
            if (!await RunAsync(async () => found = await _api.GetAsync<PagedResult<ItemDto>>(
                    $"api/items?q={Uri.EscapeDataString(text)}&pageSize=20"))) return;
            if (found!.Items.Count == 0)
            {
                ErrorMessage = $"No item matches \"{text}\".";
                return;
            }
            if (found.Items.Count > 1)
            {
                SearchResults.Clear();
                foreach (var i in found.Items) SearchResults.Add(i);
                SelectedSearchResult = SearchResults[0];
                IsItemSearchOpen = true;
                return;
            }
            item = found.Items[0];
        }
        await AddItemAsync(item, qty);
    }

    [RelayCommand]
    private Task PickSearchResult() => SelectedSearchResult is { } item ? AddItemAsync(item, StripQtyPrefix(ScanText).Qty) : Task.CompletedTask;

    public void MoveSearchSelection(int delta)
    {
        if (SearchResults.Count == 0) return;
        var index = SelectedSearchResult is null ? -1 : SearchResults.IndexOf(SelectedSearchResult);
        SelectedSearchResult = SearchResults[Math.Clamp(index + delta, 0, SearchResults.Count - 1)];
    }

    /// <param name="scannedLot">A batch label that was scanned: that lot is billed first instead of the nearest expiry.</param>
    public async Task AddItemAsync(ItemDto item, decimal qty, (int Id, string BatchNo)? scannedLot = null)
    {
        IsItemSearchOpen = false;
        ErrorMessage = null;
        StatusMessage = null;

        List<SellableBatchDto>? batches = null;
        if (!await RunAsync(async () => batches = await _api.GetAsync<List<SellableBatchDto>>($"api/items/{item.Id}/sellable-batches")))
            return;
        if (batches!.Count == 0)
        {
            ErrorMessage = $"{item.Name} is out of stock (or only expired stock is left).";
            return;
        }

        ScanText = "";

        // Allocate nearest-expiry first (FEFO), splitting across batches when one batch is not enough,
        // and topping up a line that already uses the batch. A scanned lot label goes ahead of the others.
        var usedOnBill = Lines.GroupBy(l => l.Batch.Id).ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));
        var remaining = qty;
        var nearExpiry = DateOnly.FromDateTime(DateTime.Today).AddDays(ExpiryStatusConverter.NearExpiryDays);
        var notes = new List<string>();
        var order = batches;
        if (scannedLot is { } lot)
        {
            if (batches.Any(b => b.Id == lot.Id)) order = [.. batches.Where(b => b.Id == lot.Id), .. batches.Where(b => b.Id != lot.Id)];
            else notes.Add($"scanned batch {lot.BatchNo} is expired or out of stock, so the nearest-expiry batch was used");
        }
        foreach (var batch in order)
        {
            var free = batch.QtyOnHand - usedOnBill.GetValueOrDefault(batch.Id);
            if (free <= 0) continue;
            var take = Math.Min(free, remaining);

            var line = Lines.FirstOrDefault(l => l.Item.Id == item.Id && l.Batch.Id == batch.Id);
            if (line is not null) line.Qty += take;
            else
            {
                line = new BillLineViewModel(item, batches, batch, take, b => DefaultRate(item, b));
                Lines.Add(line);
            }
            SelectedLine = line;
            if (batch.ExpiryDate is { } exp && exp <= nearExpiry)
                notes.Add($"batch {batch.BatchNo} expires {exp:MM/yyyy}");

            remaining -= take;
            if (remaining == 0) break;
        }

        if (remaining == qty)
            ErrorMessage = $"All stock of {item.Name} is already on this bill.";
        else if (remaining > 0)
            ErrorMessage = $"Only {qty - remaining:0.###} of {item.Name} in stock — added what is available.";
        if (notes.Count > 0) StatusMessage = $"Note: {item.Name} {string.Join(", ", notes)}.";
        FocusRequested?.Invoke("scan");
    }

    /// <summary>The lot's price for the current customer's type (lot rates already fall back to the item's).</summary>
    private decimal DefaultRate(ItemDto item, SellableBatchDto batch) => batch.RateFor(Customer?.Type ?? CustomerType.Retail);

    // ------------------------------------------------------------ lines and totals

    public ObservableCollection<BillLineViewModel> Lines { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveLineCommand))]
    private BillLineViewModel? _selectedLine;

    [ObservableProperty] private BillTotals _totals;
    [ObservableProperty] private bool _isInterState;

    public int ItemCount => Lines.Count;
    public bool IsEmpty => Lines.Count == 0;
    public bool NeedsPrescription => Lines.Any(l => l.Item.Schedule is DrugSchedule.H1 or DrugSchedule.X);
    public bool HasScheduledDrugs => Lines.Any(l => l.IsScheduled);
    public bool ShowPrescription => HasScheduledDrugs || !string.IsNullOrEmpty(DoctorName) || !string.IsNullOrEmpty(PatientName);

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BillLineViewModel.Qty) or nameof(BillLineViewModel.Rate)
            or nameof(BillLineViewModel.DiscountPercent) or nameof(BillLineViewModel.Batch))
            Recalculate();
    }

    private void Recalculate()
    {
        foreach (var line in Lines)
            line.Available = line.Batch.QtyOnHand - Lines.Where(o => o != line && o.Batch.Id == line.Batch.Id).Sum(o => o.Qty);

        IsInterState = BillCalculator.IsInterState(_store.Settings.StateCode, Customer?.Gstin);
        Totals = BillCalculator.Calculate(
            Lines.Select(l => new BillLineInput(l.Qty, l.Rate, l.DiscountPercent, l.Item.GstRate)).ToList(), IsInterState);
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].LineNo = i + 1;
            Lines[i].Amount = Totals.Lines[i].Amount;
        }

        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(NeedsPrescription));
        OnPropertyChanged(nameof(HasScheduledDrugs));
        OnPropertyChanged(nameof(ShowPrescription));
        PayCommand.NotifyCanExecuteChanged();
        HoldCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveLine))]
    private void RemoveLine()
    {
        if (SelectedLine is not { } line) return;
        var index = Lines.IndexOf(line);
        Lines.Remove(line);
        SelectedLine = Lines.Count == 0 ? null : Lines[Math.Min(index, Lines.Count - 1)];
        FocusRequested?.Invoke("scan");
    }

    private bool CanRemoveLine() => SelectedLine is not null;

    // ------------------------------------------------------------ customer

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer), nameof(CustomerSummary))]
    private CustomerDto? _customer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomerSummary))]
    private CustomerBalanceDto? _balance;

    [ObservableProperty] private string _customerQuery = "";
    [ObservableProperty] private bool _isCustomerSearchOpen;
    [ObservableProperty] private CustomerDto? _selectedCustomerResult;
    public ObservableCollection<CustomerDto> CustomerResults { get; } = [];

    [ObservableProperty] private string? _walkInName;
    [ObservableProperty] private string? _walkInPhone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPrescription))]
    private string? _doctorName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPrescription))]
    private string? _patientName;

    public bool HasCustomer => Customer is not null;

    public string CustomerSummary => Customer is null
        ? ""
        : $"{Customer.Type} · {Customer.Phone ?? "no phone"}"
          + (Balance is { CreditLimit: > 0 } b ? $" · Due ₹{b.Outstanding:N2} of ₹{b.CreditLimit:N2} limit" : " · Cash only");

    partial void OnCustomerQueryChanged(string value)
    {
        _customerSearchDelay?.Cancel();
        if (value.Trim().Length < 2)
        {
            IsCustomerSearchOpen = false;
            return;
        }
        var cts = _customerSearchDelay = new CancellationTokenSource();
        _ = SearchCustomersAfterDelayAsync(value.Trim(), cts.Token);
    }

    private async Task SearchCustomersAfterDelayAsync(string text, CancellationToken ct)
    {
        try { await Task.Delay(250, ct); } catch (TaskCanceledException) { return; }
        PagedResult<CustomerDto>? result = null;
        await RunAsync(async () => result = await _api.GetAsync<PagedResult<CustomerDto>>(
            $"api/customers?q={Uri.EscapeDataString(text)}&pageSize=15", ct));
        if (ct.IsCancellationRequested || result is null) return;

        CustomerResults.Clear();
        foreach (var c in result.Items) CustomerResults.Add(c);
        SelectedCustomerResult = CustomerResults.FirstOrDefault();
        IsCustomerSearchOpen = CustomerResults.Count > 0;
    }

    public void MoveCustomerSelection(int delta)
    {
        if (CustomerResults.Count == 0) return;
        var index = SelectedCustomerResult is null ? -1 : CustomerResults.IndexOf(SelectedCustomerResult);
        SelectedCustomerResult = CustomerResults[Math.Clamp(index + delta, 0, CustomerResults.Count - 1)];
    }

    [RelayCommand]
    private Task PickCustomer() => SelectedCustomerResult is { } c ? SetCustomerAsync(c) : Task.CompletedTask;

    public async Task SetCustomerAsync(CustomerDto? customer)
    {
        _customerSearchDelay?.Cancel();
        IsCustomerSearchOpen = false;
        CustomerQuery = "";
        Customer = customer;
        Balance = null;
        foreach (var line in Lines) line.Reprice(); // price list follows the customer type
        Recalculate();
        if (customer is not null)
            await RunAsync(async () => Balance = await _api.GetAsync<CustomerBalanceDto>($"api/customers/{customer.Id}/balance"));
        FocusRequested?.Invoke("scan");
    }

    [RelayCommand]
    private Task ClearCustomer() => SetCustomerAsync(null);

    [RelayCommand]
    private async Task NewCustomer()
    {
        var editor = new CustomerEditorViewModel(_api, null) { Name = CustomerQuery.Trim() };
        if (_dialogs.ShowEditor(editor) && editor.Result is { } created) await SetCustomerAsync(created);
    }

    // ------------------------------------------------------------ bill actions

    [RelayCommand]
    private async Task NewBill()
    {
        if (Lines.Count > 0 && !_dialogs.Confirm("Discard the current bill?")) return;
        await ResetAsync();
    }

    private async Task ResetAsync()
    {
        Lines.Clear();
        ScanText = "";
        DoctorName = PatientName = WalkInName = WalkInPhone = null;
        ErrorMessage = null;
        await SetCustomerAsync(null);
    }

    private bool HasLines() => Lines.Count > 0;

    [RelayCommand(CanExecute = nameof(HasLines))]
    private async Task Pay()
    {
        ErrorMessage = null;
        if (Lines.FirstOrDefault(l => l.Qty <= 0) is { } zero)
        {
            ErrorMessage = $"Enter a quantity for {zero.Item.Name}.";
            return;
        }
        if (Lines.FirstOrDefault(l => l.IsOverStock) is { } over)
        {
            ErrorMessage = $"Only {over.Available:0.###} of {over.Item.Name} (batch {over.Batch.BatchNo}) is in stock.";
            return;
        }
        var maxDiscount = _store.Settings.MaxCashierDiscountPercent;
        if (!_session.CanManage && Lines.FirstOrDefault(l => l.DiscountPercent > maxDiscount) is { } disc)
        {
            ErrorMessage = $"Discount on {disc.Item.Name} is above your {maxDiscount:0.##}% limit.";
            return;
        }
        if (NeedsPrescription && (string.IsNullOrWhiteSpace(DoctorName) || string.IsNullOrWhiteSpace(PatientName)))
        {
            ErrorMessage = "Schedule H1/X medicines on this bill: enter the doctor and patient names.";
            FocusRequested?.Invoke("doctor");
            return;
        }

        var payment = new PaymentViewModel(Totals.Total, Customer, Balance, payments => _api.PostAsync<SaleInvoiceDto>(
            "api/sales", new SaleRequest
            {
                CustomerId = Customer?.Id,
                CustomerName = Customer is null ? WalkInName : null,
                CustomerPhone = Customer is null ? WalkInPhone : null,
                DoctorName = DoctorName,
                PatientName = PatientName,
                Lines = Lines.Select(l => l.ToRequest()).ToList(),
                Payments = payments,
            }));
        if (!_dialogs.ShowEditor(payment) || payment.Result is not { } invoice)
        {
            FocusRequested?.Invoke("scan");
            return;
        }

        await ResetAsync();
        var change = payment.Change > 0 ? $" · Change ₹{payment.Change:N2}" : "";
        StatusMessage = $"Saved {invoice.InvoiceNo} · ₹{invoice.Total:N2}{change}";

        if (_settings.AutoPrint)
        {
            try
            {
                _printer.Print(invoice);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ClientLog.Error("print", ex);
                _dialogs.Error($"The sale was saved, but printing failed: {ex.Message}\nReprint it from Sales history.");
            }
        }
        FocusRequested?.Invoke("scan");
    }

    [RelayCommand(CanExecute = nameof(HasLines))]
    private async Task Hold()
    {
        var who = Customer?.Name ?? WalkInName ?? "Walk-in";
        var label = $"{_settings.CounterName} · {who} · {Lines.Count} item(s) · ₹{Totals.Total:N0}";
        var payload = JsonSerializer.Serialize(new HeldBillPayload(Customer?.Id, WalkInName, WalkInPhone, DoctorName,
            PatientName, Lines.Select(l => l.ToRequest()).ToList()), PosJson.Options);

        if (!await RunAsync(() => _api.PostAsync("api/held-bills", new HoldBillRequest(label, payload)))) return;
        await ResetAsync();
        StatusMessage = $"Bill held: {who}. Press F9 to resume it from any counter.";
    }

    [RelayCommand]
    private async Task Recall()
    {
        if (Lines.Count > 0)
        {
            ErrorMessage = "Finish, hold (F8) or clear (F2) the current bill before resuming another.";
            return;
        }

        var picker = new HeldBillsViewModel(_api);
        if (!_dialogs.ShowEditor(picker) || picker.Selected is not { } held) return;

        // Claim it first so two counters cannot resume the same bill.
        try
        {
            await _api.DeleteAsync($"api/held-bills/{held.Id}");
        }
        catch (ApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            ErrorMessage = "That bill was already resumed at another counter.";
            return;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        var payload = JsonSerializer.Deserialize<HeldBillPayload>(held.Payload, PosJson.Options)!;
        var problems = new List<string>();
        await RunAsync(async () =>
        {
            if (payload.CustomerId is { } cid)
                await SetCustomerAsync(await _api.GetAsync<CustomerDto>($"api/customers/{cid}"));
            WalkInName = payload.WalkInName;
            WalkInPhone = payload.WalkInPhone;
            DoctorName = payload.DoctorName;
            PatientName = payload.PatientName;

            foreach (var saved in payload.Lines)
            {
                ItemDto item;
                List<SellableBatchDto> batches;
                try
                {
                    item = await _api.GetAsync<ItemDto>($"api/items/{saved.ItemId}");
                    batches = await _api.GetAsync<List<SellableBatchDto>>($"api/items/{saved.ItemId}/sellable-batches");
                }
                catch (ApiException)
                {
                    problems.Add($"item #{saved.ItemId} is no longer available");
                    continue;
                }
                if (batches.Count == 0)
                {
                    problems.Add($"{item.Name} is out of stock");
                    continue;
                }
                var batch = batches.FirstOrDefault(b => b.Id == saved.BatchId);
                if (batch is null) problems.Add($"{item.Name}: original batch unavailable, using {batches[0].BatchNo}");
                var line = new BillLineViewModel(item, batches, batch ?? batches[0], saved.Qty, b => DefaultRate(item, b))
                {
                    DiscountPercent = saved.DiscountPercent,
                };
                if (CanEditRate && batch is not null) line.Rate = saved.Rate;
                Lines.Add(line);
            }
        });

        StatusMessage = problems.Count == 0 ? "Held bill resumed." : "Resumed with changes: " + string.Join("; ", problems) + ".";
        SelectedLine = Lines.FirstOrDefault();
        FocusRequested?.Invoke("scan");
    }

    public override async Task ActivateAsync()
    {
        await RunAsync(_store.RefreshAsync);
        Recalculate();
        FocusRequested?.Invoke("scan");
    }
}
