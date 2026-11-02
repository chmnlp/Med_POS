using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

public partial class CustomersViewModel(ApiClient api, Session session, DialogService dialogs, InvoicePrinter printer)
    : PagedListViewModel<CustomerDto>
{
    public override string Title => "Customers";

    /// <summary>Any signed-in user can add and edit customers; only managers deactivate.</summary>
    public bool CanManage => session.CanManage;

    protected override Task<PagedResult<CustomerDto>> FetchAsync(string? query, int page, int pageSize, bool includeInactive) =>
        api.GetAsync<PagedResult<CustomerDto>>(Query("api/customers",
            ("q", query), ("page", page), ("pageSize", pageSize), ("includeInactive", includeInactive)));

    protected override object KeyOf(CustomerDto item) => item.Id;

    protected override void OnSelectionChanged(CustomerDto? item)
    {
        EditCommand.NotifyCanExecuteChanged();
        DeactivateCommand.NotifyCanExecuteChanged();
        ReceivePaymentCommand.NotifyCanExecuteChanged();
        StatementCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ReceivePayment()
    {
        if (Selected is { } c) await AccountsViewModel.ReceivePaymentAsync(api, dialogs, printer, c.Id, c.Name);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Statement()
    {
        if (Selected is { } c)
            dialogs.ShowEditor(new StatementViewModel(api, printer, $"api/customers/{c.Id}/statement", c.Name, isCustomer: true));
    }

    [RelayCommand]
    private async Task New()
    {
        var editor = new CustomerEditorViewModel(api, null);
        if (!dialogs.ShowEditor(editor)) return;
        await LoadAsync();
        Selected = Items.FirstOrDefault(i => i.Id == editor.Result?.Id) ?? Selected;
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Edit()
    {
        if (Selected is null) return;
        if (dialogs.ShowEditor(new CustomerEditorViewModel(api, Selected))) await LoadAsync();
    }

    private bool CanDeactivate() => CanManage && Selected is { IsActive: true };

    [RelayCommand(CanExecute = nameof(CanDeactivate))]
    private async Task Deactivate()
    {
        if (Selected is not { } c || !dialogs.Confirm($"Deactivate customer '{c.Name}'?")) return;
        if (await RunAsync(() => api.DeleteAsync($"api/customers/{c.Id}"))) await LoadAsync();
    }
}

public partial class SuppliersViewModel(ApiClient api, Session session, DialogService dialogs)
    : PagedListViewModel<SupplierDto>
{
    public override string Title => "Suppliers";
    public bool CanManage => session.CanManage;

    protected override Task<PagedResult<SupplierDto>> FetchAsync(string? query, int page, int pageSize, bool includeInactive) =>
        api.GetAsync<PagedResult<SupplierDto>>(Query("api/suppliers",
            ("q", query), ("page", page), ("pageSize", pageSize), ("includeInactive", includeInactive)));

    protected override object KeyOf(SupplierDto item) => item.Id;

    protected override void OnSelectionChanged(SupplierDto? item)
    {
        EditCommand.NotifyCanExecuteChanged();
        DeactivateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task New()
    {
        var editor = new SupplierEditorViewModel(api, null);
        if (!dialogs.ShowEditor(editor)) return;
        await LoadAsync();
        Selected = Items.FirstOrDefault(i => i.Id == editor.Result?.Id) ?? Selected;
    }

    private bool CanEditSelected() => CanManage && Selected is not null;

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private async Task Edit()
    {
        if (Selected is null) return;
        if (dialogs.ShowEditor(new SupplierEditorViewModel(api, Selected))) await LoadAsync();
    }

    private bool CanDeactivate() => CanManage && Selected is { IsActive: true };

    [RelayCommand(CanExecute = nameof(CanDeactivate))]
    private async Task Deactivate()
    {
        if (Selected is not { } s || !dialogs.Confirm($"Deactivate supplier '{s.Name}'?")) return;
        if (await RunAsync(() => api.DeleteAsync($"api/suppliers/{s.Id}"))) await LoadAsync();
    }
}
