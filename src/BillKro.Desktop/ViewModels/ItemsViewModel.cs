using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

public partial class ItemsViewModel(ApiClient api, Session session, DialogService dialogs, LabelPrinter labels, ClientSettings settings)
    : PagedListViewModel<ItemDto>
{
    private int _batchLoadVersion;

    public override string Title => "Items & Stock";
    public bool CanManage => session.CanManage;

    [ObservableProperty] private bool _lowStockOnly;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintLabelsCommand))]
    private BatchDto? _selectedBatch;

    public ObservableCollection<BatchDto> Batches { get; } = [];

    public bool ShowBatches => Selected?.IsBatchTracked == true;

    protected override Task<PagedResult<ItemDto>> FetchAsync(string? query, int page, int pageSize, bool includeInactive) =>
        api.GetAsync<PagedResult<ItemDto>>(Query("api/items",
            ("q", query), ("page", page), ("pageSize", pageSize),
            ("includeInactive", includeInactive), ("lowStock", LowStockOnly)));

    protected override object KeyOf(ItemDto item) => item.Id;

    partial void OnLowStockOnlyChanged(bool value) => _ = ReloadFromFirstPageAsync();

    protected override void OnSelectionChanged(ItemDto? item)
    {
        OnPropertyChanged(nameof(ShowBatches));
        EditCommand.NotifyCanExecuteChanged();
        DeactivateCommand.NotifyCanExecuteChanged();
        AddBatchCommand.NotifyCanExecuteChanged();
        AdjustStockCommand.NotifyCanExecuteChanged();
        HistoryCommand.NotifyCanExecuteChanged();
        PrintLabelsCommand.NotifyCanExecuteChanged();
        _ = LoadBatchesAsync(item);
    }

    private async Task LoadBatchesAsync(ItemDto? item)
    {
        var version = ++_batchLoadVersion;
        Batches.Clear();
        SelectedBatch = null;
        if (item is not { IsBatchTracked: true }) return;

        List<BatchDto>? batches = null;
        await RunAsync(async () => batches = await api.GetAsync<List<BatchDto>>($"api/items/{item.Id}/batches"));
        if (batches is null || version != _batchLoadVersion) return;
        foreach (var b in batches) Batches.Add(b);
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task New()
    {
        var editor = new ItemEditorViewModel(api, null);
        if (!dialogs.ShowEditor(editor)) return;
        await LoadAsync();
        Selected = Items.FirstOrDefault(i => i.Id == editor.Result?.Id) ?? Selected;
    }

    private bool CanEditSelected() => CanManage && Selected is not null;

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private async Task Edit()
    {
        if (Selected is null) return;
        if (dialogs.ShowEditor(new ItemEditorViewModel(api, Selected))) await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private async Task Deactivate()
    {
        if (Selected is not { } item) return;
        if (!item.IsActive)
        {
            dialogs.Info($"'{item.Name}' is already inactive. Edit it to reactivate.");
            return;
        }
        if (!dialogs.Confirm($"Deactivate '{item.Name}'? It will no longer appear in billing or search.")) return;
        if (await RunAsync(() => api.DeleteAsync($"api/items/{item.Id}"))) await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private async Task AdjustStock()
    {
        if (Selected is not { } item) return;
        var editor = new StockAdjustViewModel(api, item);
        if (!dialogs.ShowEditor(editor) || editor.Result is not { } done) return;
        StatusMessage = $"{done.AdjustmentNo}: {done.ItemName} batch {done.BatchNo} {done.QtyChange:+0.###;-0.###}.";
        await ReloadWithBatchesAsync();
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void History()
    {
        if (Selected is { } item) dialogs.ShowEditor(new StockHistoryViewModel(api, item));
    }

    [ObservableProperty] private string? _statusMessage;

    private bool CanAddBatch() => CanManage && Selected?.IsBatchTracked == true;

    [RelayCommand(CanExecute = nameof(CanAddBatch))]
    private async Task AddBatch()
    {
        if (Selected is not { } item) return;
        if (dialogs.ShowEditor(new BatchEditorViewModel(api, item, null))) await ReloadWithBatchesAsync();
    }

    /// <summary>
    /// Item rows are records compared by value, so a batch edit that leaves the item unchanged
    /// would not re-trigger selection; reload the batches explicitly.
    /// </summary>
    private async Task ReloadWithBatchesAsync()
    {
        await LoadAsync();
        await LoadBatchesAsync(Selected);
    }

    /// <summary>Labels for the selected batch, or for all of the item's stock when no batch is selected.</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private async Task PrintLabels()
    {
        if (Selected is not { } item) return;
        var request = SelectedBatch is { } batch && item.IsBatchTracked
            ? new LabelRequest { BatchIds = [batch.Id] }
            : new LabelRequest { ItemIds = [item.Id] };
        var title = SelectedBatch is { } b && item.IsBatchTracked ? $"Print labels — {item.Name} batch {b.BatchNo}" : $"Print labels — {item.Name}";
        dialogs.ShowEditor(new LabelPrintViewModel(api, labels, settings, title, request, l => l.QtyOnHand));
        // Labels may have given lots their barcodes.
        await LoadBatchesAsync(Selected);
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task SetBarcodes()
    {
        var setup = new BarcodeSetupViewModel(api);
        dialogs.ShowEditor(setup);
        if (setup.SavedCount > 0) await LoadAsync();
    }

    private bool CanEditBatch() => CanManage && SelectedBatch is not null;

    [RelayCommand(CanExecute = nameof(CanEditBatch))]
    private async Task EditBatch()
    {
        if (Selected is not { } item || SelectedBatch is not { } batch) return;
        if (dialogs.ShowEditor(new BatchEditorViewModel(api, item, batch))) await ReloadWithBatchesAsync();
    }
}
