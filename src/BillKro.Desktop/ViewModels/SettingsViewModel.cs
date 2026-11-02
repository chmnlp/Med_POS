using System.Printing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Desktop.Views;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

/// <summary>Store profile (admins) and this counter's printing preferences (everyone).</summary>
public partial class SettingsViewModel(ApiClient api, Session session, StoreContext store, ClientSettings settings,
    InvoicePrinter printer) : PageViewModel
{
    public override string Title => "Settings";
    public bool IsAdmin => session.IsAdmin;

    public static IReadOnlyList<string> PaperSizes { get; } = ["Thermal80", "A4"];
    public List<string> Printers { get; } = [];

    [ObservableProperty] private string _storeName = "";
    [ObservableProperty] private string? _address;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _gstin;
    [ObservableProperty] private string? _drugLicenseNo;
    [ObservableProperty] private string? _stateCode;
    [ObservableProperty] private string _invoicePrefix = "S";
    [ObservableProperty] private string? _invoiceFooter;
    [ObservableProperty] private decimal _maxCashierDiscountPercent;

    [ObservableProperty] private string _counterName = settings.CounterName;
    [ObservableProperty] private string? _printerName = settings.PrinterName;
    [ObservableProperty] private string _paperSize = settings.PaperSize;
    [ObservableProperty] private bool _autoPrint = settings.AutoPrint;
    [ObservableProperty] private string? _savedMessage;

    public override async Task ActivateAsync()
    {
        if (Printers.Count == 0)
        {
            try
            {
                using var server = new LocalPrintServer();
                Printers.AddRange(server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections])
                    .Select(q => q.FullName).Order());
                OnPropertyChanged(nameof(Printers));
            }
            catch (PrintSystemException ex)
            {
                ClientLog.Error("printers", ex);
            }
        }

        if (!await RunAsync(store.RefreshAsync)) return;
        var s = store.Settings;
        StoreName = s.StoreName;
        Address = s.Address;
        Phone = s.Phone;
        Email = s.Email;
        Gstin = s.Gstin;
        DrugLicenseNo = s.DrugLicenseNo;
        StateCode = s.StateCode;
        InvoicePrefix = s.InvoicePrefix;
        InvoiceFooter = s.InvoiceFooter;
        MaxCashierDiscountPercent = s.MaxCashierDiscountPercent;
    }

    [RelayCommand]
    private async Task SaveStore()
    {
        SavedMessage = null;
        var ok = await RunAsync(async () => store.Update(await api.PutAsync<StoreSettingsDto>("api/settings", new StoreSettingsDto
        {
            StoreName = StoreName, Address = Address, Phone = Phone, Email = Email, Gstin = Gstin,
            DrugLicenseNo = DrugLicenseNo, StateCode = StateCode, InvoicePrefix = InvoicePrefix,
            InvoiceFooter = InvoiceFooter, MaxCashierDiscountPercent = MaxCashierDiscountPercent,
        })));
        if (ok)
        {
            StateCode = store.Settings.StateCode;
            SavedMessage = "Store details saved.";
        }
    }

    [RelayCommand]
    private void SaveCounter()
    {
        settings.CounterName = string.IsNullOrWhiteSpace(CounterName) ? Environment.MachineName : CounterName.Trim();
        settings.PrinterName = string.IsNullOrWhiteSpace(PrinterName) ? null : PrinterName;
        settings.PaperSize = PaperSize;
        settings.AutoPrint = AutoPrint;
        settings.Save();
        SavedMessage = "Counter settings saved on this PC.";
    }

    /// <summary>Shows a sample bill in the current paper format so the layout can be checked without selling.</summary>
    [RelayCommand]
    private void PreviewSample()
    {
        SaveCounter();
        new InvoicePreviewWindow(printer, InvoicePrinter.Sample()) { Owner = DialogService.ActiveWindow() }.ShowDialog();
    }
}
