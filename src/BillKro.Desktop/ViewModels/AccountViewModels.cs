using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Desktop.Views;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

/// <summary>Receivables and payables with ageing, plus recent receipts and payments.</summary>
public partial class AccountsViewModel(ApiClient api, Session session, StoreContext store, DialogService dialogs,
    InvoicePrinter printer) : PageViewModel
{
    public override string Title => "Accounts";
    public bool CanManage => session.CanManage;

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string? _statusMessage;

    public ObservableCollection<AgeingRow> Receivables { get; } = [];
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ReceiveCommand), nameof(CustomerStatementCommand), nameof(CopyReminderCommand))]
    private AgeingRow? _selectedReceivable;
    [ObservableProperty] private AgeingReport? _receivableReport;

    public ObservableCollection<AgeingRow> Payables { get; } = [];
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(PayCommand), nameof(SupplierStatementCommand))]
    private AgeingRow? _selectedPayable;
    [ObservableProperty] private AgeingReport? _payableReport;

    public ObservableCollection<ReceiptDto> Receipts { get; } = [];
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CancelReceiptCommand), nameof(PrintReceiptCommand))]
    private ReceiptDto? _selectedReceipt;

    public ObservableCollection<SupplierPaymentDto> Payments { get; } = [];
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(CancelPaymentCommand))]
    private SupplierPaymentDto? _selectedPayment;

    partial void OnSelectedTabChanged(int value) => _ = ActivateAsync();

    public override async Task ActivateAsync()
    {
        switch (SelectedTab)
        {
            case 1 when CanManage:
                await Load(async () => PayableReport = await api.GetAsync<AgeingReport>("api/reports/payables"), () => Fill(Payables, PayableReport!.Rows));
                break;
            case 2:
                PagedResult<ReceiptDto>? r = null;
                await Load(async () => r = await api.GetAsync<PagedResult<ReceiptDto>>("api/receipts?pageSize=100"), () => Fill(Receipts, r!.Items));
                break;
            case 3 when CanManage:
                PagedResult<SupplierPaymentDto>? p = null;
                await Load(async () => p = await api.GetAsync<PagedResult<SupplierPaymentDto>>("api/supplier-payments?pageSize=100"), () => Fill(Payments, p!.Items));
                break;
            default:
                await Load(async () => ReceivableReport = await api.GetAsync<AgeingReport>("api/reports/receivables"), () => Fill(Receivables, ReceivableReport!.Rows));
                break;
        }
    }

    private async Task Load(Func<Task> fetch, Action fill)
    {
        if (await RunAsync(fetch)) fill();
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> rows)
    {
        target.Clear();
        foreach (var r in rows) target.Add(r);
    }

    [RelayCommand]
    private Task Refresh() => ActivateAsync();

    // ------------------------------------------------------------ receivables

    private bool HasReceivable() => SelectedReceivable is not null;

    [RelayCommand(CanExecute = nameof(HasReceivable))]
    private async Task Receive()
    {
        if (SelectedReceivable is not { } row) return;
        if (await ReceivePaymentAsync(api, dialogs, printer, row.PartyId, row.Name) is { } receipt)
        {
            StatusMessage = $"{receipt.ReceiptNo}: ₹{receipt.Amount:N2} received from {receipt.CustomerName}. Balance ₹{receipt.BalanceAfter:N2}.";
            await ActivateAsync();
        }
    }

    /// <summary>Shared by Accounts and Customers: take a payment, then offer the printed receipt.</summary>
    public static async Task<ReceiptDto?> ReceivePaymentAsync(ApiClient api, DialogService dialogs, InvoicePrinter printer,
        int customerId, string customerName)
    {
        CustomerBalanceDto? balance = null;
        try { balance = await api.GetAsync<CustomerBalanceDto>($"api/customers/{customerId}/balance"); }
        catch (ApiException ex) { dialogs.Error(ex.Message); return null; }

        var editor = new ReceiptEditorViewModel(api, customerId, customerName, balance.Outstanding);
        if (!dialogs.ShowEditor(editor) || editor.Result is not { } receipt) return null;
        new InvoicePreviewWindow($"Receipt {receipt.ReceiptNo}", printer.BuildReceipt(receipt),
            () => printer.PrintDocument(printer.BuildReceipt(receipt), $"Receipt {receipt.ReceiptNo}", wide: !printer.IsThermalPaper))
        { Owner = DialogService.ActiveWindow() }.ShowDialog();
        return receipt;
    }

    [RelayCommand(CanExecute = nameof(HasReceivable))]
    private void CustomerStatement()
    {
        if (SelectedReceivable is { } row)
            dialogs.ShowEditor(new StatementViewModel(api, printer, $"api/customers/{row.PartyId}/statement", row.Name, isCustomer: true));
    }

    /// <summary>Puts a polite payment reminder on the clipboard, ready to paste into WhatsApp or SMS.</summary>
    [RelayCommand(CanExecute = nameof(HasReceivable))]
    private void CopyReminder()
    {
        if (SelectedReceivable is not { } row) return;
        var s = store.Settings;
        var overdue = row.Overdue > 0 ? $"₹{row.Overdue:N2} of which is overdue" : "not yet overdue";
        var text = $"Dear {row.Name}, your account with {s.StoreName} shows ₹{row.Outstanding:N2} due ({overdue}). " +
                   "Kindly arrange the payment at your earliest convenience. Thank you." +
                   (s.Phone is null ? "" : $" – {s.StoreName}, {s.Phone}");
        Clipboard.SetText(text);
        StatusMessage = $"Reminder for {row.Name} copied" + (row.Phone is null ? "." : $" — paste it into WhatsApp/SMS to {row.Phone}.");
    }

    // ------------------------------------------------------------ payables

    private bool HasPayable() => CanManage && SelectedPayable is not null;

    [RelayCommand(CanExecute = nameof(HasPayable))]
    private async Task Pay()
    {
        if (SelectedPayable is not { } row) return;
        var editor = new SupplierPaymentEditorViewModel(api, row.PartyId, row.Name, row.Outstanding);
        if (!dialogs.ShowEditor(editor) || editor.Result is not { } payment) return;
        StatusMessage = $"{payment.PaymentNo}: ₹{payment.Amount:N2} paid to {payment.SupplierName}.";
        await ActivateAsync();
    }

    [RelayCommand(CanExecute = nameof(HasPayable))]
    private void SupplierStatement()
    {
        if (SelectedPayable is { } row)
            dialogs.ShowEditor(new StatementViewModel(api, printer, $"api/suppliers/{row.PartyId}/statement", row.Name, isCustomer: false));
    }

    // ------------------------------------------------------------ receipts / payments

    private bool CanCancelReceipt() => CanManage && SelectedReceipt is { Status: AccountEntryStatus.Active };

    [RelayCommand(CanExecute = nameof(CanCancelReceipt))]
    private async Task CancelReceipt()
    {
        if (SelectedReceipt is not { } r) return;
        if (dialogs.ShowEditor(new ReasonViewModel($"Cancel receipt {r.ReceiptNo}", "Cancel receipt",
                $"₹{r.Amount:N2} from {r.CustomerName} will be added back to what they owe.",
                reason => api.PostAsync<ReceiptDto>($"api/receipts/{r.Id}/cancel", new CancelEntryRequest(reason)))))
            await ActivateAsync();
    }

    private bool HasReceipt() => SelectedReceipt is not null;

    [RelayCommand(CanExecute = nameof(HasReceipt))]
    private async Task PrintReceipt()
    {
        if (SelectedReceipt is not { } r) return;
        ReceiptDto? full = null;
        if (!await RunAsync(async () => full = await api.GetAsync<ReceiptDto>($"api/receipts/{r.Id}"))) return;
        new InvoicePreviewWindow($"Receipt {full!.ReceiptNo}", printer.BuildReceipt(full),
            () => printer.PrintDocument(printer.BuildReceipt(full), $"Receipt {full.ReceiptNo}", wide: !printer.IsThermalPaper))
        { Owner = DialogService.ActiveWindow() }.ShowDialog();
    }

    private bool CanCancelPayment() => CanManage && SelectedPayment is { Status: AccountEntryStatus.Active };

    [RelayCommand(CanExecute = nameof(CanCancelPayment))]
    private async Task CancelPayment()
    {
        if (SelectedPayment is not { } p) return;
        if (dialogs.ShowEditor(new ReasonViewModel($"Cancel payment {p.PaymentNo}", "Cancel payment",
                $"₹{p.Amount:N2} to {p.SupplierName} will be added back to what we owe.",
                reason => api.PostAsync<SupplierPaymentDto>($"api/supplier-payments/{p.Id}/cancel", new CancelEntryRequest(reason)))))
            await ActivateAsync();
    }
}

public static class PaymentModes
{
    public static IReadOnlyList<AccountPaymentMode> All { get; } = Enum.GetValues<AccountPaymentMode>();
}

public partial class ReceiptEditorViewModel(ApiClient api, int customerId, string customerName, decimal outstanding) : EditorViewModel
{
    public override string Glyph => "\uE8C7";

    public override string Title => $"Receive payment — {customerName}";
    public override string SaveText => "Save receipt";
    public decimal Outstanding { get; } = outstanding;
    public ReceiptDto? Result { get; private set; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(After))] private decimal _amount = Math.Max(0, outstanding);
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NeedsReference))] private AccountPaymentMode _mode = AccountPaymentMode.Cash;
    [ObservableProperty] private string? _reference;
    [ObservableProperty] private string? _note;

    public decimal After => Outstanding - Amount;
    public bool NeedsReference => Mode is AccountPaymentMode.Cheque or AccountPaymentMode.BankTransfer;

    protected override async Task SaveCoreAsync() =>
        Result = await api.PostAsync<ReceiptDto>("api/receipts", new ReceiptRequest(customerId, Amount, Mode, Reference, Note));
}

public partial class SupplierPaymentEditorViewModel(ApiClient api, int supplierId, string supplierName, decimal outstanding) : EditorViewModel
{
    public override string Glyph => "\uE8C7";

    public override string Title => $"Pay supplier — {supplierName}";
    public override string SaveText => "Save payment";
    public decimal Outstanding { get; } = outstanding;
    public SupplierPaymentDto? Result { get; private set; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(After))] private decimal _amount = Math.Max(0, outstanding);
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NeedsReference))] private AccountPaymentMode _mode = AccountPaymentMode.BankTransfer;
    [ObservableProperty] private string? _reference;
    [ObservableProperty] private string? _note;

    public decimal After => Outstanding - Amount;
    public bool NeedsReference => Mode is AccountPaymentMode.Cheque or AccountPaymentMode.BankTransfer;

    protected override async Task SaveCoreAsync() =>
        Result = await api.PostAsync<SupplierPaymentDto>("api/supplier-payments", new SupplierPaymentRequest(supplierId, Amount, Mode, Reference, Note));
}

/// <summary>Read-only account statement for a date range, printable on A4.</summary>
public partial class StatementViewModel : EditorViewModel
{
    public override string Glyph => "\uE8A5";

    private readonly ApiClient _api;
    private readonly InvoicePrinter _printer;
    private readonly string _path;

    public StatementViewModel(ApiClient api, InvoicePrinter printer, string path, string name, bool isCustomer)
    {
        _api = api;
        _printer = printer;
        _path = path;
        PartyName = name;
        IsCustomer = isCustomer;
        var today = DateTime.Today;
        _fromDate = new DateTime(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1);
        _toDate = today;
        _ = LoadAsync();
    }

    public string PartyName { get; }
    public bool IsCustomer { get; }
    public string BalanceMeaning => IsCustomer ? "Balance = what the customer owes us" : "Balance = what we owe the supplier";
    public override string Title => $"Statement — {PartyName}";
    public override string SaveText => "Close";
    public override bool ShowCancel => false;

    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;
    [ObservableProperty] private StatementDto? _statement;

    partial void OnFromDateChanged(DateTime? value) => _ = LoadAsync();
    partial void OnToDateChanged(DateTime? value) => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (FromDate is not { } f || ToDate is not { } t) return;
        await RunAsync(async () => Statement = await _api.GetAsync<StatementDto>($"{_path}?from={f:yyyy-MM-dd}&to={t:yyyy-MM-dd}"));
    }

    [RelayCommand]
    private void Print()
    {
        if (Statement is not { } s) return;
        new InvoicePreviewWindow($"Statement — {PartyName}", _printer.BuildStatement(s, IsCustomer),
            () => _printer.PrintDocument(_printer.BuildStatement(s, IsCustomer), $"Statement {PartyName}", wide: true))
        { Owner = DialogService.ActiveWindow() }.ShowDialog();
    }

    protected override Task SaveCoreAsync() => Task.CompletedTask;
}

/// <summary>Picks one of an invoice's credit notes to view / print.</summary>
public partial class CreditNotePickerViewModel(IReadOnlyList<SaleReturnSummaryDto> returns) : EditorViewModel
{
    public override string Glyph => "\uE8A5";

    public override string Title => "Credit notes for this bill";
    public override string SaveText => "View";
    public IReadOnlyList<SaleReturnSummaryDto> Returns { get; } = returns;

    [ObservableProperty] private SaleReturnSummaryDto? _selected = returns.LastOrDefault();

    protected override Task SaveCoreAsync() =>
        Selected is null ? throw new ApiException("Choose a credit note.") : Task.CompletedTask;
}
