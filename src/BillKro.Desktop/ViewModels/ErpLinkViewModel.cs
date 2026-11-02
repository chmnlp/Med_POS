using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

/// <summary>
/// Exchange with MARG ERP: purchases go to MARG's Method folder automatically, masters come in from MARG's
/// export file, and what MARG cannot import (sales, debit notes, cancellations) is listed for keying in.
/// </summary>
public partial class ErpLinkViewModel(ApiClient api, Session session, DialogService dialogs) : PageViewModel
{
    public override string Title => "Data Exchange";
    public bool IsAdmin => session.IsAdmin;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private ErpLinkStatusDto? _status;

    // Settings (admin).
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string? _methodFolder;
    [ObservableProperty] private string _companyCode = "001";
    [ObservableProperty] private DateTime? _sendFrom;
    [ObservableProperty] private int _pollMinutes = 5;
    [ObservableProperty] private string? _folderCheck;

    public ObservableCollection<ErpExportDto> Exports { get; } = [];
    public ObservableCollection<ErpManualTask> ManualTasks { get; } = [];

    // Master import.
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ApplyMasterCommand))] private string? _masterFile;
    [ObservableProperty] private ErpMasterPreview? _masterPreview;
    [ObservableProperty] private ErpPartyImport _partyImport = ErpPartyImport.Skip;
    [ObservableProperty] private string? _partyTypeFilter;
    public static ErpPartyImport[] PartyImports { get; } = Enum.GetValues<ErpPartyImport>();
    public ObservableCollection<ErpMasterItemPreview> PreviewItems { get; } = [];
    public ObservableCollection<ErpMasterPartyPreview> PreviewParties { get; } = [];

    // Sales register.
    [ObservableProperty] private DateTime? _registerFrom = DateTime.Today;
    [ObservableProperty] private DateTime? _registerTo = DateTime.Today;

    public string LastRunText => Status switch
    {
        null => "",
        { LastRunAt: null } => "Not run yet.",
        { LastError: { } e } s => $"Last run {s.LastRunAt:dd-MM-yyyy HH:mm}: {e}",
        var s => $"Last run {s.LastRunAt:dd-MM-yyyy HH:mm}: OK.",
    };

    public string PendingText => Status is null ? ""
        : Status.Settings.Enabled
            ? $"{Status.PendingPurchases} purchase(s) waiting to be sent. Sending is ON (every {Status.Settings.PollMinutes} min)."
            : $"{Status.PendingPurchases} purchase(s) waiting. Automatic sending is OFF.";

    partial void OnStatusChanged(ErpLinkStatusDto? value)
    {
        OnPropertyChanged(nameof(LastRunText));
        OnPropertyChanged(nameof(PendingText));
    }

    public override async Task ActivateAsync()
    {
        ErpLinkStatusDto? status = null;
        if (!await RunAsync(async () => status = await api.GetAsync<ErpLinkStatusDto>("api/erp/status"))) return;
        Status = status;
        var s = status!.Settings;
        Enabled = s.Enabled;
        MethodFolder = s.MethodFolder;
        CompanyCode = s.CompanyCode;
        SendFrom = s.SendFrom?.ToDateTime(TimeOnly.MinValue);
        PollMinutes = s.PollMinutes;
        Exports.Clear();
        foreach (var x in status.RecentExports) Exports.Add(x);
        ManualTasks.Clear();
        foreach (var t in status.ManualTasks) ManualTasks.Add(t);
    }

    [RelayCommand]
    private Task Refresh() => ActivateAsync();

    // ---------------------------------------------------------------- purchases out

    [RelayCommand]
    private async Task SendNow()
    {
        StatusMessage = null;
        ErpSendResult? result = null;
        if (!await RunAsync(async () => result = await api.PostAsync<ErpSendResult>("api/erp/send-now", new { }))) return;
        if (result!.Error is { } error) ErrorMessage = error;
        StatusMessage = $"Sent {result.Sent} purchase(s) to the ERP" + (result.Failed > 0 ? $", {result.Failed} failed." : ".");
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        StatusMessage = null;
        var dto = new ErpLinkSettingsDto
        {
            Enabled = Enabled, MethodFolder = MethodFolder, CompanyCode = CompanyCode,
            SendFrom = SendFrom is { } d ? DateOnly.FromDateTime(d) : null, PollMinutes = PollMinutes,
        };
        if (!await RunAsync(() => api.PutAsync<ErpLinkSettingsDto>("api/erp/settings", dto))) return;
        StatusMessage = "Data Exchange settings saved.";
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task TestFolder()
    {
        ErpFolderCheck? check = null;
        if (await RunAsync(async () => check = await api.PostAsync<ErpFolderCheck>("api/erp/test-folder", new { methodFolder = MethodFolder })))
            FolderCheck = (check!.Ok ? "✔ " : "✖ ") + check.Message;
    }

    [RelayCommand]
    private async Task MarkDone(ErpManualTask? task)
    {
        if (task is null) return;
        if (!dialogs.Confirm($"Have you entered {task.DocumentNo} in the ERP?\n\n{task.Action}")) return;
        if (await RunAsync(() => api.PostAsync($"api/erp/manual/{task.Kind}/{task.DocumentId}/done", new { })))
            ManualTasks.Remove(task);
    }

    // ---------------------------------------------------------------- masters in

    [RelayCommand]
    private async Task ChooseMasterFile()
    {
        var path = dialogs.OpenFile("Choose the ERP's master export file", "ERP export (*.csv;*.txt)|*.csv;*.txt|All files|*.*");
        if (path is null) return;
        MasterFile = path;
        MasterPreview = null;
        PreviewItems.Clear();
        PreviewParties.Clear();
        ErpMasterPreview? preview = null;
        if (!await RunAsync(async () => preview = await api.UploadAsync<ErpMasterPreview>("api/erp/master/preview", path))) return;
        MasterPreview = preview;
        foreach (var i in preview!.Items) PreviewItems.Add(i);
        foreach (var p in preview.Parties) PreviewParties.Add(p);
    }

    private bool CanApplyMaster() => MasterFile is not null;

    [RelayCommand(CanExecute = nameof(CanApplyMaster))]
    private async Task ApplyMaster()
    {
        if (MasterFile is not { } path || MasterPreview is not { } preview) return;
        if (!File.Exists(path))
        {
            ErrorMessage = "The chosen file is no longer there. Choose it again.";
            return;
        }
        var parties = PartyImport switch
        {
            ErpPartyImport.Customers => $"\n• parties as customers{(string.IsNullOrWhiteSpace(PartyTypeFilter) ? "" : $" (type {PartyTypeFilter.Trim()} only)")}",
            ErpPartyImport.Suppliers => $"\n• parties as suppliers{(string.IsNullOrWhiteSpace(PartyTypeFilter) ? "" : $" (type {PartyTypeFilter.Trim()} only)")}",
            _ => "",
        };
        if (!dialogs.Confirm($"Import from the ERP:\n• {preview.NewItems} new item(s) with their opening stock\n" +
                             $"• {preview.Items.Count - preview.NewItems} existing item row(s): names and prices only, stock is not changed{parties}\n\nContinue?"))
            return;

        var query = $"api/erp/master/apply?parties={PartyImport}"
            + (string.IsNullOrWhiteSpace(PartyTypeFilter) ? "" : $"&partyType={Uri.EscapeDataString(PartyTypeFilter.Trim())}");
        ErpMasterImportResult? result = null;
        if (!await RunAsync(async () => result = await api.UploadAsync<ErpMasterImportResult>(query, path))) return;
        var r = result!;
        StatusMessage = $"Imported: {r.ItemsCreated} new item(s), {r.ItemsUpdated} updated, {r.LotsCreated} lot(s); " +
                        $"{r.PartiesCreated} new part(ies), {r.PartiesUpdated} updated.";
        if (r.Warnings.Count > 0)
            dialogs.Info(string.Join("\n", r.Warnings.Take(30)) + (r.Warnings.Count > 30 ? $"\n… and {r.Warnings.Count - 30} more." : ""),
                "Import notes");
        MasterFile = null;
        MasterPreview = null;
        PreviewItems.Clear();
        PreviewParties.Clear();
    }

    // ---------------------------------------------------------------- sales out

    [RelayCommand]
    private async Task ExportSalesRegister()
    {
        if (RegisterFrom is not { } from || RegisterTo is not { } to) return;
        List<SalesRegisterRow>? rows = null;
        if (!await RunAsync(async () => rows = await api.GetAsync<List<SalesRegisterRow>>(
                $"api/erp/sales-register?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}")))
            return;
        if (rows!.Count == 0)
        {
            StatusMessage = "No sales in this period.";
            return;
        }
        var path = dialogs.SaveFile("Save sales register", "CSV (*.csv)|*.csv", $"sales-register-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        if (path is null) return;
        CsvWriter.Save(path,
            ["Invoice no", "Date", "Customer", "GSTIN", "Item code", "Item", "HSN", "Batch", "Expiry", "Qty", "Rate", "Disc %", "GST %",
             "Taxable", "GST", "Amount", "Paid by"],
            rows.Select(r => new object?[] { r.InvoiceNo, r.InvoiceDate.ToString("dd-MM-yyyy HH:mm"), r.Customer, r.CustomerGstin, r.ItemCode,
                r.ItemName, r.Hsn, r.BatchNo, r.Expiry, r.Qty, r.Rate, r.DiscountPercent, r.GstRate, r.Taxable, r.Tax, r.Amount, r.PaymentModes }));
        StatusMessage = $"Saved {rows.Count} sale line(s) from {rows.Select(r => r.InvoiceNo).Distinct().Count()} invoice(s) to {Path.GetFileName(path)}.";
    }
}
