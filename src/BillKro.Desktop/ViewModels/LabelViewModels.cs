using System.Collections.ObjectModel;
using System.Printing;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

public partial class LabelLineViewModel(LabelDto label, int copies) : ObservableObject
{
    public LabelDto Label { get; } = label;

    [ObservableProperty] private int _copies = copies;
}

/// <summary>Barcode stickers for items, batches or a received purchase, with a live preview at the label size.</summary>
public partial class LabelPrintViewModel : EditorViewModel
{
    public override string Glyph => "\uE8EC";
    public override string SaveText => "Print";

    private readonly LabelPrinter _printer;
    private readonly ClientSettings _settings;
    private readonly string _title;

    /// <param name="copiesFor">Starting number of copies per label, e.g. the stock in hand or the quantity received.</param>
    public LabelPrintViewModel(ApiClient api, LabelPrinter printer, ClientSettings settings, string title,
        LabelRequest request, Func<LabelDto, decimal> copiesFor)
    {
        _printer = printer;
        _settings = settings;
        _title = title;
        _widthMm = settings.LabelWidthMm;
        _heightMm = settings.LabelHeightMm;
        _labelsAcross = Math.Clamp(settings.LabelsAcross, 1, 3);
        _showStoreName = settings.LabelShowStoreName;
        _showSaleRate = settings.LabelShowSaleRate;
        _useQr = settings.LabelUseQr;
        _ = LoadAsync(api, request, copiesFor);
    }

    public override string Title => _title;

    public ObservableCollection<LabelLineViewModel> Lines { get; } = [];
    public static IReadOnlyList<int> AcrossChoices { get; } = [1, 2, 3];

    [ObservableProperty] private LabelLineViewModel? _selectedLine;
    [ObservableProperty] private FrameworkElement? _preview;
    [ObservableProperty] private double _widthMm;
    [ObservableProperty] private double _heightMm;
    [ObservableProperty] private int _labelsAcross;
    [ObservableProperty] private bool _showStoreName;
    [ObservableProperty] private bool _showSaleRate;
    [ObservableProperty] private bool _useQr;

    public int TotalLabels => Lines.Sum(l => Math.Max(l.Copies, 0));
    public string PrinterText => _printer.PrinterName ?? "Not chosen yet — you will be asked when printing";

    private async Task LoadAsync(ApiClient api, LabelRequest request, Func<LabelDto, decimal> copiesFor)
    {
        List<LabelDto>? labels = null;
        await RunAsync(async () => labels = await api.PostAsync<List<LabelDto>>("api/labels", request));
        if (labels is null) return;
        foreach (var l in labels)
        {
            var line = new LabelLineViewModel(l, (int)Math.Clamp(Math.Ceiling(copiesFor(l)), 1, 999));
            line.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TotalLabels));
            Lines.Add(line);
        }
        if (Lines.Count == 0) ErrorMessage = "Nothing to label: there is no stock in hand for this selection.";
        SelectedLine = Lines.FirstOrDefault();
        OnPropertyChanged(nameof(TotalLabels));
    }

    partial void OnSelectedLineChanged(LabelLineViewModel? value) => RefreshPreview();
    partial void OnWidthMmChanged(double value) => RefreshPreview();
    partial void OnHeightMmChanged(double value) => RefreshPreview();
    partial void OnShowStoreNameChanged(bool value) => RefreshPreview();
    partial void OnShowSaleRateChanged(bool value) => RefreshPreview();
    partial void OnUseQrChanged(bool value) => RefreshPreview();

    /// <summary>The preview is drawn by the printer's own layout code, so it matches the sticker.</summary>
    private void RefreshPreview()
    {
        if (!SizeIsValid()) return;
        ApplySettings();
        Preview = SelectedLine is { } line ? _printer.BuildLabel(line.Label) : null;
    }

    private bool SizeIsValid() => WidthMm is >= 20 and <= 150 && HeightMm is >= 10 and <= 150;

    private void ApplySettings()
    {
        _settings.LabelWidthMm = WidthMm;
        _settings.LabelHeightMm = HeightMm;
        _settings.LabelsAcross = LabelsAcross;
        _settings.LabelShowStoreName = ShowStoreName;
        _settings.LabelShowSaleRate = ShowSaleRate;
        _settings.LabelUseQr = UseQr;
    }

    [RelayCommand]
    private void ChoosePrinter()
    {
        if (_printer.ChoosePrinter()) OnPropertyChanged(nameof(PrinterText));
    }

    [RelayCommand]
    private void OneEach()
    {
        foreach (var l in Lines) l.Copies = 1;
    }

    protected override Task SaveCoreAsync()
    {
        if (!SizeIsValid()) throw new ApiException("Label size must be 20–150 mm wide and 10–150 mm high.");
        var picks = Lines.Where(l => l.Copies > 0).Select(l => (l.Label, l.Copies)).ToList();
        if (picks.Count == 0) throw new ApiException("Set at least one label to print.");
        if (TotalLabels > 5000) throw new ApiException("Print at most 5000 labels at a time.");

        ApplySettings();
        _settings.Save();
        try
        {
            if (!_printer.Print(picks)) throw new ApiException("Choose the label printer to print.");
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException)
        {
            ClientLog.Error("labels", ex);
            throw new ApiException($"The label printer reported a problem: {ex.Message}");
        }
        return Task.CompletedTask;
    }
}

/// <summary>Scan barcodes into items one after another: pick an item, scan its pack, the next item comes up.</summary>
public partial class BarcodeSetupViewModel : EditorViewModel
{
    public override string Glyph => "\uED14";
    public override string Title => "Set item barcodes";
    public override string SaveText => "Done";
    public override bool ShowCancel => false;

    private readonly ApiClient _api;
    private CancellationTokenSource? _searchDelay;

    public BarcodeSetupViewModel(ApiClient api)
    {
        _api = api;
        _ = LoadAsync();
    }

    public ObservableCollection<ItemDto> Items { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _includeWithBarcode;
    [ObservableProperty] private ItemDto? _selected;
    [ObservableProperty] private string _scanText = "";
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int _savedCount;
    [ObservableProperty] private int _total;

    /// <summary>Raised when the scan box should take focus again.</summary>
    public event Action? FocusScan;

    partial void OnSearchTextChanged(string value)
    {
        _searchDelay?.Cancel();
        var cts = _searchDelay = new CancellationTokenSource();
        _ = Task.Delay(250, cts.Token).ContinueWith(_ => LoadAsync(), cts.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
    }

    partial void OnIncludeWithBarcodeChanged(bool value) => _ = LoadAsync();

    private async Task LoadAsync()
    {
        PagedResult<ItemDto>? page = null;
        var q = string.IsNullOrWhiteSpace(SearchText) ? "" : $"&q={Uri.EscapeDataString(SearchText.Trim())}";
        await RunAsync(async () => page = await _api.GetAsync<PagedResult<ItemDto>>(
            $"api/items?pageSize=200&missingBarcode={(!IncludeWithBarcode).ToString().ToLowerInvariant()}{q}"));
        if (page is null) return;
        Items.Clear();
        foreach (var i in page.Items) Items.Add(i);
        Total = page.Total;
        Selected = Items.FirstOrDefault();
    }

    partial void OnSelectedChanged(ItemDto? value)
    {
        ScanText = value?.Barcode ?? "";
        FocusScan?.Invoke();
    }

    /// <summary>Saves the scanned barcode on the selected item and moves to the next one.</summary>
    [RelayCommand]
    private async Task SaveBarcode()
    {
        if (Selected is not { } item) return;
        // Scanning one of our own QR labels by mistake gives its lot code, which the server then rejects as taken.
        var code = QrCode.ScannedCode(ScanText);
        if (code.Length == 0)
        {
            StatusMessage = "Scan the pack's barcode first, or click Skip.";
            return;
        }

        ItemDto? saved = null;
        StatusMessage = null;
        if (!await RunAsync(async () => saved = await _api.PutAsync<ItemDto>($"api/items/{item.Id}/barcode", new SetBarcodeRequest(code))))
        {
            ScanText = "";
            FocusScan?.Invoke();
            return;
        }

        SavedCount++;
        StatusMessage = $"Saved {saved!.Barcode} on {saved.Name}.";
        var index = Items.IndexOf(item);
        if (IncludeWithBarcode)
        {
            Items[index] = saved;
            Selected = index + 1 < Items.Count ? Items[index + 1] : saved;
        }
        else
        {
            Items.RemoveAt(index);
            Total--;
            Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
        }
        FocusScan?.Invoke();
    }

    [RelayCommand]
    private void Skip()
    {
        if (Selected is not { } item) return;
        var index = Items.IndexOf(item);
        Selected = index + 1 < Items.Count ? Items[index + 1] : Items.FirstOrDefault();
    }

    protected override Task SaveCoreAsync() => Task.CompletedTask;
}
