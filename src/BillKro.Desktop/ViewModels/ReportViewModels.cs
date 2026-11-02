using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Desktop.Views;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

/// <summary>How many of one note / coin are in the drawer.</summary>
public partial class DenominationCount(int value) : ObservableObject
{
    public int Value { get; } = value;
    public string Label => Value >= 10 ? $"₹{Value} notes" : $"₹{Value} coins";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Amount))] private int _count;
    public decimal Amount => Value * Count;
}

/// <summary>
/// Counter closing. A cashier sees their own day and closes it with a drawer count; managers can also look at every
/// counter together and at past closings.
/// </summary>
public partial class DayEndViewModel : PageViewModel
{
    private readonly ApiClient _api;
    private readonly Session _session;
    private readonly DialogService _dialogs;
    private readonly InvoicePrinter _printer;

    public DayEndViewModel(ApiClient api, Session session, DialogService dialogs, InvoicePrinter printer)
    {
        _api = api;
        _session = session;
        _dialogs = dialogs;
        _printer = printer;
        foreach (var d in Shared.Denominations.All)
        {
            var c = new DenominationCount(d);
            c.PropertyChanged += OnCountChanged;
            Counts.Add(c);
        }
    }

    public override string Title => "Day end — close counter";
    public bool CanManage => _session.CanManage;

    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private bool _allCounters;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClosed), nameof(CanClose), nameof(ExpectedCash), nameof(Difference), nameof(DrawerStatus))]
    [NotifyCanExecuteChangedFor(nameof(CloseCounterCommand), nameof(PrintCommand))]
    private DayEndSummaryDto? _summary;

    public ObservableCollection<DenominationCount> Counts { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ExpectedCash), nameof(Difference), nameof(DrawerStatus))] private decimal _openingFloat;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Difference), nameof(DrawerStatus))] private decimal _countedCash;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private string? _statusMessage;

    public ObservableCollection<DayCloseDto> Closings { get; } = [];

    public bool IsClosed => Summary?.Closing is not null;
    public bool CanClose => Summary is { Closing: null } && !AllCounters;
    public decimal ExpectedCash => OpeningFloat + (Summary?.NetCash ?? 0);
    public decimal Difference => CountedCash - ExpectedCash;

    /// <summary>Not counted, Balanced, Short or Over; colours the tally card.</summary>
    public string DrawerStatus => CountedCash == 0 && ExpectedCash != 0
        ? "Not counted yet"
        : Difference switch { 0 => "Balanced", < 0 => "Short", _ => "Over" };

    partial void OnDateChanged(DateTime? value) => _ = ActivateAsync();
    partial void OnAllCountersChanged(bool value)
    {
        OnPropertyChanged(nameof(CanClose));
        CloseCounterCommand.NotifyCanExecuteChanged();
        _ = ActivateAsync();
    }

    private void OnCountChanged(object? sender, PropertyChangedEventArgs e) => CountedCash = Counts.Sum(c => c.Amount);

    public override async Task ActivateAsync()
    {
        if (Date is not { } d) return;
        var day = DateOnly.FromDateTime(d);
        var path = $"api/day-end/summary?date={day:yyyy-MM-dd}" + (AllCounters ? "&allCounters=true" : "");
        List<DayCloseDto>? closings = null;
        await RunAsync(async () =>
        {
            Summary = await _api.GetAsync<DayEndSummaryDto>(path);
            closings = await _api.GetAsync<List<DayCloseDto>>($"api/day-end/closings?from={day.AddDays(-14):yyyy-MM-dd}&to={day:yyyy-MM-dd}");
        });
        Closings.Clear();
        foreach (var c in closings ?? []) Closings.Add(c);
        if (Summary?.Closing is { } closed)
        {
            OpeningFloat = closed.OpeningFloat;
            CountedCash = closed.CountedCash;
            Note = closed.Note;
        }
    }

    [RelayCommand]
    private Task Refresh() => ActivateAsync();

    [RelayCommand(CanExecute = nameof(CanClose))]
    private async Task CloseCounter()
    {
        if (Summary is not { } s || Date is not { } d) return;
        var diff = Difference;
        var check = diff == 0 ? "Cash matches." : diff < 0 ? $"Cash is SHORT by ₹{-diff:N2}." : $"Cash is OVER by ₹{diff:N2}.";
        if (!_dialogs.Confirm($"{check}\n\nClose your counter for {d:dd-MM-yyyy}? This cannot be undone.")) return;

        var denominations = string.Join(",", Counts.Where(c => c.Count > 0).Select(c => $"{c.Value}x{c.Count}"));
        DayCloseDto? closed = null;
        if (!await RunAsync(async () => closed = await _api.PostAsync<DayCloseDto>("api/day-end/close",
                new DayCloseRequest(DateOnly.FromDateTime(d), OpeningFloat, CountedCash, denominations.Length == 0 ? null : denominations, Note))))
            return;
        StatusMessage = $"Counter closed. Expected ₹{closed!.ExpectedCash:N2}, counted ₹{closed.CountedCash:N2}, difference ₹{closed.Difference:N2}.";
        await ActivateAsync();
        Print();
    }

    private bool HasSummary() => Summary is not null;

    [RelayCommand(CanExecute = nameof(HasSummary))]
    private void Print()
    {
        if (Summary is not { } s) return;
        new InvoicePreviewWindow($"Day end {s.Date:dd-MM-yyyy} — {s.UserName}", _printer.BuildDayEnd(s),
            () => _printer.PrintDocument(_printer.BuildDayEnd(s), $"Day end {s.Date:dd-MM-yyyy}", wide: !_printer.IsThermalPaper))
        { Owner = DialogService.ActiveWindow() }.ShowDialog();
    }
}

/// <summary>Sales summary and the GST (GSTR-1 working) report, with CSV export for the accountant.</summary>
public partial class SalesReportsViewModel(ApiClient api) : PageViewModel
{
    public override string Title => "Sales & GST reports";

    [ObservableProperty] private int _selectedTab;

    // Sales summary
    [ObservableProperty] private DateTime? _salesFrom = DateTime.Today;
    [ObservableProperty] private DateTime? _salesTo = DateTime.Today;
    [ObservableProperty] private SalesSummaryReport? _sales;

    // GST: default to the current month (a GSTR-1 period)
    [ObservableProperty] private DateTime? _gstFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _gstTo = DateTime.Today;
    [ObservableProperty] private GstReport? _gst;
    [ObservableProperty] private string? _statusMessage;

    partial void OnSelectedTabChanged(int value) => _ = ActivateAsync();
    partial void OnSalesFromChanged(DateTime? value) => _ = LoadSalesAsync();
    partial void OnSalesToChanged(DateTime? value) => _ = LoadSalesAsync();
    partial void OnGstFromChanged(DateTime? value) => _ = LoadGstAsync();
    partial void OnGstToChanged(DateTime? value) => _ = LoadGstAsync();

    public override Task ActivateAsync() => SelectedTab == 1 ? LoadGstAsync() : LoadSalesAsync();

    private async Task LoadSalesAsync()
    {
        if (SalesFrom is not { } f || SalesTo is not { } t) return;
        await RunAsync(async () => Sales = await api.GetAsync<SalesSummaryReport>($"api/reports/sales-summary?from={f:yyyy-MM-dd}&to={t:yyyy-MM-dd}"));
    }

    private async Task LoadGstAsync()
    {
        if (GstFrom is not { } f || GstTo is not { } t) return;
        await RunAsync(async () => Gst = await api.GetAsync<GstReport>($"api/reports/gst?from={f:yyyy-MM-dd}&to={t:yyyy-MM-dd}"));
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        var start = (GstFrom ?? DateTime.Today).AddMonths(-1);
        GstFrom = new DateTime(start.Year, start.Month, 1);
        GstTo = GstFrom.Value.AddMonths(1).AddDays(-1);
    }

    [RelayCommand]
    private Task Refresh() => ActivateAsync();

    /// <summary>Writes one CSV per section into a folder the user picks.</summary>
    [RelayCommand]
    private void Export()
    {
        var folderDialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder for the report files" };
        var owner = DialogService.ActiveWindow();
        if ((owner is null ? folderDialog.ShowDialog() : folderDialog.ShowDialog(owner)) != true) return;
        var folder = folderDialog.FolderName;
        var written = new List<string>();
        void Save(string name, string[] headers, IEnumerable<object?[]> rows)
        {
            var path = Path.Combine(folder, name);
            CsvWriter.Save(path, headers, rows);
            written.Add(name);
        }

        if (SelectedTab == 1 && Gst is { } g)
        {
            var p = $"{g.From:yyyyMMdd}-{g.To:yyyyMMdd}";
            Save($"gst-b2b-{p}.csv", ["GSTIN", "Receiver", "Invoice no", "Invoice date", "Invoice value", "Place of supply", "Rate", "Taxable", "IGST", "CGST", "SGST"],
                g.B2B.Select(r => new object?[] { r.Gstin, r.Name, r.InvoiceNo, r.Date, r.InvoiceValue, r.PlaceOfSupply, r.Rate, r.Taxable, r.Igst, r.Cgst, r.Sgst }));
            Save($"gst-b2cs-{p}.csv", ["Place of supply", "Rate", "Taxable", "IGST", "CGST", "SGST"],
                g.B2CS.Select(r => new object?[] { r.PlaceOfSupply, r.Rate, r.Taxable, r.Igst, r.Cgst, r.Sgst }));
            Save($"gst-hsn-{p}.csv", ["HSN", "UQC", "Qty", "Rate", "Taxable", "IGST", "CGST", "SGST", "Total"],
                g.Hsn.Select(r => new object?[] { r.Hsn, r.Uqc, r.Qty, r.Rate, r.Taxable, r.Igst, r.Cgst, r.Sgst, r.Total }));
            Save($"gst-credit-notes-{p}.csv", ["GSTIN", "Party", "Note no", "Note date", "Invoice no", "Invoice date", "Rate", "Taxable", "IGST", "CGST", "SGST"],
                g.CreditNotes.Select(r => new object?[] { r.Gstin, r.Name, r.NoteNo, r.NoteDate, r.InvoiceNo, r.InvoiceDate, r.Rate, r.Taxable, r.Igst, r.Cgst, r.Sgst }));
            Save($"gst-documents-{p}.csv", ["Nature", "From", "To", "Total", "Cancelled"],
                g.Documents.Select(r => new object?[] { r.Nature, r.From, r.To, r.Total, r.Cancelled }));
            Save($"gst-purchases-itc-{p}.csv", ["Supplier GSTIN", "Supplier", "Document", "Date", "Type", "Taxable", "IGST", "CGST", "SGST"],
                g.Purchases.Select(r => new object?[] { r.SupplierGstin, r.SupplierName, r.Document, r.Date, r.Type, r.Taxable, r.Igst, r.Cgst, r.Sgst }));
        }
        else if (Sales is { } s)
        {
            var p = $"{s.From:yyyyMMdd}-{s.To:yyyyMMdd}";
            Save($"sales-by-day-{p}.csv", ["Date", "Bills", "Sales", "Returns", "Net"],
                s.ByDay.Select(d => new object?[] { d.Date, d.Bills, d.Sales, d.Returns, d.Net }));
            Save($"sales-by-mode-{p}.csv", ["Mode", "Payments", "Amount"], s.ByMode.Select(m => new object?[] { m.Mode, m.Count, m.Amount }));
            Save($"sales-by-cashier-{p}.csv", ["Cashier", "Bills", "Sales"], s.ByCashier.Select(c => new object?[] { c.Name, c.Bills, c.Sales }));
            Save($"sales-top-items-{p}.csv", ["Item", "Qty", "Amount"], s.TopItems.Select(t => new object?[] { t.ItemName, t.Qty, t.Amount }));
        }
        StatusMessage = written.Count == 0 ? "Nothing to export yet." : $"Saved {written.Count} file(s) to {folder}.";
    }
}
