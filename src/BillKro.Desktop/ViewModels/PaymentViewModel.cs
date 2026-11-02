using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

/// <summary>Settles the bill: cash by default, or split across UPI, card and customer credit.</summary>
public partial class PaymentViewModel : EditorViewModel
{
    public override string Glyph => "\uE8C7";

    private readonly Func<List<PaymentRequest>, Task<SaleInvoiceDto>> _submit;

    public PaymentViewModel(decimal total, CustomerDto? customer, CustomerBalanceDto? balance,
        Func<List<PaymentRequest>, Task<SaleInvoiceDto>> submit)
    {
        Total = total;
        _submit = submit;
        CreditAvailable = balance?.Available ?? 0;
        CanUseCredit = customer is not null && CreditAvailable > 0;
        CreditHint = customer is null ? "Select a customer to sell on credit."
            : CanUseCredit ? $"Up to ₹{CreditAvailable:N2} available for {customer.Name}."
            : $"{customer.Name} has no credit available.";
        _cash = total;
        _tendered = total;
    }

    public override string Title => $"Payment — ₹{Total:N2}";
    public override string SaveText => "Complete sale";

    public decimal Total { get; }
    public bool CanUseCredit { get; }
    public decimal CreditAvailable { get; }
    public string CreditHint { get; }
    public SaleInvoiceDto? Result { get; private set; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Remaining), nameof(Change))] private decimal _cash;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Remaining))] private decimal _upi;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Remaining))] private decimal _card;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Remaining))] private decimal _credit;
    [ObservableProperty] private string? _upiReference;
    [ObservableProperty] private string? _cardReference;

    /// <summary>Cash handed over by the customer, for working out change.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Change))] private decimal _tendered;

    public decimal Remaining => Total - Cash - Upi - Card - Credit;
    public decimal Change => Math.Max(0, Tendered - Cash);

    [RelayCommand] private void AllCash() => SetSplit(cash: Total);
    [RelayCommand] private void AllUpi() => SetSplit(upi: Total);
    [RelayCommand] private void AllCard() => SetSplit(card: Total);

    [RelayCommand(CanExecute = nameof(CanUseCredit))]
    private void AllCredit() => SetSplit(credit: Total);

    private void SetSplit(decimal cash = 0, decimal upi = 0, decimal card = 0, decimal credit = 0)
    {
        Cash = cash;
        Upi = upi;
        Card = card;
        Credit = credit;
        Tendered = cash;
    }

    protected override async Task SaveCoreAsync()
    {
        if (new[] { Cash, Upi, Card, Credit }.Any(a => a < 0)) throw new ApiException("Amounts cannot be negative.");
        if (Remaining != 0)
            throw new ApiException(Remaining > 0
                ? $"₹{Remaining:N2} still to be paid. Payments must add up to ₹{Total:N2}."
                : $"Payments are ₹{-Remaining:N2} more than the bill.");
        if (Credit > 0 && !CanUseCredit) throw new ApiException(CreditHint);
        if (Cash > 0 && Tendered < Cash) throw new ApiException($"Cash received (₹{Tendered:N2}) is less than the cash amount (₹{Cash:N2}).");

        var payments = new List<PaymentRequest>();
        if (Cash > 0) payments.Add(new PaymentRequest(PaymentMode.Cash, Cash));
        if (Upi > 0) payments.Add(new PaymentRequest(PaymentMode.Upi, Upi, UpiReference));
        if (Card > 0) payments.Add(new PaymentRequest(PaymentMode.Card, Card, CardReference));
        if (Credit > 0) payments.Add(new PaymentRequest(PaymentMode.Credit, Credit));
        Result = await _submit(payments);
    }
}

/// <summary>Picks a held bill to resume.</summary>
public partial class HeldBillsViewModel : EditorViewModel
{
    public override string Glyph => "\uE768";

    public HeldBillsViewModel(ApiClient api)
    {
        _ = LoadAsync(api);
    }

    public override string Title => "Resume a held bill";
    public override string SaveText => "Resume";

    public ObservableCollection<HeldBillDto> Bills { get; } = [];

    [ObservableProperty] private HeldBillDto? _selected;

    private async Task LoadAsync(ApiClient api)
    {
        List<HeldBillDto>? bills = null;
        await RunAsync(async () => bills = await api.GetAsync<List<HeldBillDto>>("api/held-bills"));
        foreach (var b in bills ?? []) Bills.Add(b);
        Selected = Bills.FirstOrDefault();
        if (bills is { Count: 0 }) ErrorMessage = "There are no held bills.";
    }

    protected override Task SaveCoreAsync() =>
        Selected is null ? throw new ApiException("Choose a bill to resume.") : Task.CompletedTask;
}
