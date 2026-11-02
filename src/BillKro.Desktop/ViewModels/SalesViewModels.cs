using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Desktop.Views;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

public partial class SalesViewModel(ApiClient api, Session session, DialogService dialogs, InvoicePrinter printer)
    : PagedListViewModel<SaleSummaryDto>
{
    public override string Title => "Sales history";
    public bool CanManage => session.CanManage;

    [ObservableProperty] private DateTime? _fromDate = DateTime.Today;
    [ObservableProperty] private DateTime? _toDate = DateTime.Today;

    partial void OnFromDateChanged(DateTime? value) => _ = ReloadFromFirstPageAsync();
    partial void OnToDateChanged(DateTime? value) => _ = ReloadFromFirstPageAsync();

    protected override Task<PagedResult<SaleSummaryDto>> FetchAsync(string? query, int page, int pageSize, bool includeInactive) =>
        api.GetAsync<PagedResult<SaleSummaryDto>>(Query("api/sales",
            ("q", query), ("page", page), ("pageSize", pageSize),
            ("from", FromDate?.ToString("yyyy-MM-dd")), ("to", ToDate?.ToString("yyyy-MM-dd"))));

    protected override object KeyOf(SaleSummaryDto item) => item.Id;

    protected override void OnSelectionChanged(SaleSummaryDto? item)
    {
        ViewCommand.NotifyCanExecuteChanged();
        PrintCommand.NotifyCanExecuteChanged();
        ReturnCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        CreditNotesCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Today()
    {
        FromDate = DateTime.Today;
        ToDate = DateTime.Today;
    }

    private async Task<SaleInvoiceDto?> LoadSelectedAsync()
    {
        if (Selected is null) return null;
        SaleInvoiceDto? invoice = null;
        await RunAsync(async () => invoice = await api.GetAsync<SaleInvoiceDto>($"api/sales/{Selected.Id}"));
        return invoice;
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task View()
    {
        if (await LoadSelectedAsync() is not { } invoice) return;
        new InvoicePreviewWindow(printer, invoice) { Owner = DialogService.ActiveWindow() }.ShowDialog();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Print()
    {
        if (await LoadSelectedAsync() is not { } invoice) return;
        try
        {
            printer.Print(invoice);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ClientLog.Error("print", ex);
            dialogs.Error($"Printing failed: {ex.Message}");
        }
    }

    private bool CanReturn() => Selected is { Status: InvoiceStatus.Completed };

    [RelayCommand(CanExecute = nameof(CanReturn))]
    private async Task Return()
    {
        if (await LoadSelectedAsync() is not { } invoice) return;
        if (invoice.Lines.All(l => l.Qty <= l.ReturnedQty))
        {
            dialogs.Info("Everything on this bill has already been returned.");
            return;
        }
        var editor = new SaleReturnViewModel(api, invoice);
        if (!dialogs.ShowEditor(editor) || editor.Result is not { } ret) return;

        dialogs.Info(ret.RefundMode == RefundMode.Cash
            ? $"Credit note {ret.ReturnNo}: refund ₹{ret.Total:N2} in cash."
            : $"Credit note {ret.ReturnNo}: ₹{ret.Total:N2} credited to the customer's account.");
        await LoadAsync();
        await ShowCreditNoteAsync(ret.Id);
    }

    private bool CanShowCreditNotes() => Selected is { HasReturns: true };

    [RelayCommand(CanExecute = nameof(CanShowCreditNotes))]
    private async Task CreditNotes()
    {
        if (await LoadSelectedAsync() is not { } invoice || invoice.Returns.Count == 0) return;
        var pick = invoice.Returns.Count == 1 ? invoice.Returns[0] : null;
        if (pick is null)
        {
            var picker = new CreditNotePickerViewModel(invoice.Returns);
            if (!dialogs.ShowEditor(picker) || picker.Selected is not { } chosen) return;
            pick = chosen;
        }
        await ShowCreditNoteAsync(pick.Id);
    }

    private async Task ShowCreditNoteAsync(int returnId)
    {
        CreditNoteDto? note = null;
        if (!await RunAsync(async () => note = await api.GetAsync<CreditNoteDto>($"api/sale-returns/{returnId}"))) return;
        new InvoicePreviewWindow($"Credit note {note!.ReturnNo}", printer.BuildCreditNote(note),
            () => printer.PrintDocument(printer.BuildCreditNote(note), $"Credit note {note.ReturnNo}", wide: !printer.IsThermalPaper))
        { Owner = DialogService.ActiveWindow() }.ShowDialog();
    }

    private bool CanCancel() => CanManage && Selected is { Status: InvoiceStatus.Completed, HasReturns: false };

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task Cancel()
    {
        if (Selected is not { } sale) return;
        if (dialogs.ShowEditor(new CancelSaleViewModel(api, sale))) await LoadAsync();
    }
}

public partial class ReturnLineViewModel(SaleLineDto line) : ObservableObject
{
    public SaleLineDto Line { get; } = line;
    public decimal Remaining => Line.Qty - Line.ReturnedQty;

    [ObservableProperty] private decimal _returnQty;
}

public partial class SaleReturnViewModel : EditorViewModel
{
    public override string Glyph => "\uE7A7";

    private readonly ApiClient _api;
    private readonly SaleInvoiceDto _invoice;

    public SaleReturnViewModel(ApiClient api, SaleInvoiceDto invoice)
    {
        _api = api;
        _invoice = invoice;
        Lines = new(invoice.Lines.Where(l => l.Qty > l.ReturnedQty).Select(l => new ReturnLineViewModel(l)));
        foreach (var l in Lines) l.PropertyChanged += (_, _) => OnPropertyChanged(nameof(EstimatedRefund));

        // Credit bills are settled against the account; walk-in bills can only be refunded in cash.
        RefundModes = invoice.BalanceDue > 0 ? [RefundMode.Account]
            : invoice.CustomerId is null ? [RefundMode.Cash]
            : [RefundMode.Cash, RefundMode.Account];
        _refundMode = RefundModes[0];
    }

    public override string Title => $"Return items — {_invoice.InvoiceNo}";
    public override string SaveText => "Save return";

    public ObservableCollection<ReturnLineViewModel> Lines { get; }
    public IReadOnlyList<RefundMode> RefundModes { get; }
    public SaleReturnDto? Result { get; private set; }

    [ObservableProperty] private RefundMode _refundMode;
    [ObservableProperty] private string? _reason;

    public decimal EstimatedRefund => Math.Round(
        Lines.Where(l => l.ReturnQty > 0).Sum(l => BillCalculator.Round2(l.Line.Amount * l.ReturnQty / l.Line.Qty)),
        0, MidpointRounding.AwayFromZero);

    protected override async Task SaveCoreAsync()
    {
        var chosen = Lines.Where(l => l.ReturnQty > 0).ToList();
        if (chosen.Count == 0) throw new ApiException("Enter a return quantity for at least one item.");
        if (chosen.FirstOrDefault(l => l.ReturnQty > l.Remaining) is { } over)
            throw new ApiException($"Only {over.Remaining:0.###} of {over.Line.ItemName} can be returned.");

        Result = await _api.PostAsync<SaleReturnDto>($"api/sales/{_invoice.Id}/returns", new SaleReturnRequest(
            chosen.Select(l => new ReturnLineRequest(l.Line.Id, l.ReturnQty)).ToList(), RefundMode, Reason));
    }
}

public partial class CancelSaleViewModel(ApiClient api, SaleSummaryDto sale) : EditorViewModel
{
    public override string Glyph => "\uE711";

    public override string Title => $"Cancel invoice {sale.InvoiceNo}";
    public override string SaveText => "Cancel invoice";
    public string Warning => $"₹{sale.Total:N2} — stock will be put back. This cannot be undone.";

    [ObservableProperty] private string _reason = "";

    protected override Task SaveCoreAsync() =>
        api.PostAsync<SaleInvoiceDto>($"api/sales/{sale.Id}/cancel", new CancelSaleRequest(Reason));
}
