using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BillKro.Desktop.Services;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>Runs an API call, showing busy state and turning <see cref="ApiException"/> into <see cref="ErrorMessage"/>.</summary>
    protected async Task<bool> RunAsync(Func<Task> action)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await action();
            return true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>A page shown in the main window's content area.</summary>
public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }

    /// <summary>Called each time the page is navigated to.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;
}

/// <summary>Searchable, paged list backed by a server endpoint returning <see cref="PagedResult{T}"/>.</summary>
public abstract partial class PagedListViewModel<T> : PageViewModel where T : class
{
    public const int PageSize = 50;

    private CancellationTokenSource? _debounce;
    private int _loadVersion;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _includeInactive;
    [ObservableProperty] private T? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand), nameof(PreviousPageCommand))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfo))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand), nameof(PreviousPageCommand))]
    private int _total;

    public ObservableCollection<T> Items { get; } = [];

    private bool _loadedOnce;

    /// <summary>True once a load finished with no records; drives the empty-state placeholder.</summary>
    public bool ShowEmpty => _loadedOnce && !IsBusy && Total == 0;

    /// <summary>True while the user is searching, so the placeholder can say "no matches" instead of "nothing yet".</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsBusy) or nameof(Total)) base.OnPropertyChanged(nameof(ShowEmpty));
        else if (e.PropertyName is nameof(SearchText)) base.OnPropertyChanged(nameof(IsSearching));
    }

    public string PageInfo => Total == 0
        ? "No records"
        : $"{(Page - 1) * PageSize + 1}–{Math.Min(Page * PageSize, Total)} of {Total}";

    protected abstract Task<PagedResult<T>> FetchAsync(string? query, int page, int pageSize, bool includeInactive);

    public override Task ActivateAsync() => LoadAsync();

    partial void OnSelectedChanged(T? value) => OnSelectionChanged(value);

    protected virtual void OnSelectionChanged(T? item) { }

    partial void OnSearchTextChanged(string value) => DebouncedReload();
    partial void OnIncludeInactiveChanged(bool value) => _ = ReloadFromFirstPageAsync();

    protected Task ReloadFromFirstPageAsync()
    {
        Page = 1;
        return LoadAsync();
    }

    private async void DebouncedReload()
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        await ReloadFromFirstPageAsync();
    }

    /// <summary>Reloads the current page, keeping the selection when the record is still present.</summary>
    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        var selectedKey = Selected is null ? null : KeyOf(Selected);
        PagedResult<T>? result = null;

        await RunAsync(async () =>
            result = await FetchAsync(string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                Page, PageSize, IncludeInactive));

        // A newer search started while this one was in flight; drop the stale result.
        if (result is null || version != _loadVersion) return;

        Items.Clear();
        foreach (var item in result.Items) Items.Add(item);
        _loadedOnce = true;
        Total = result.Total;
        OnPropertyChanged(nameof(ShowEmpty));
        Selected = selectedKey is null ? Items.FirstOrDefault() : Items.FirstOrDefault(i => Equals(KeyOf(i), selectedKey)) ?? Items.FirstOrDefault();
    }

    /// <summary>Identity used to restore the selection after a reload.</summary>
    protected abstract object KeyOf(T item);

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextPage()
    {
        Page++;
        return LoadAsync();
    }

    private bool CanGoNext() => Page * PageSize < Total;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousPage()
    {
        Page--;
        return LoadAsync();
    }

    private bool CanGoPrevious() => Page > 1;

    protected static string Query(string path, params (string Key, object? Value)[] args)
    {
        var parts = args.Where(a => a.Value is not null && a.Value is not false)
            .Select(a => $"{a.Key}={Uri.EscapeDataString(a.Value is bool ? "true" : a.Value!.ToString()!)}");
        return $"{path}?{string.Join("&", parts)}";
    }
}

/// <summary>Base for create/edit dialogs. The dialog closes when <see cref="SaveCommand"/> succeeds.</summary>
public abstract partial class EditorViewModel : ViewModelBase
{
    public abstract string Title { get; }

    /// <summary>Segoe Fluent Icons glyph shown beside the dialog title.</summary>
    public virtual string Glyph => "";

    /// <summary>Caption of the dialog's main button.</summary>
    public virtual string SaveText => "Save";

    /// <summary>Read-only dialogs show a single Close button.</summary>
    public virtual bool ShowCancel => true;

    /// <summary>Raised after a successful save.</summary>
    public event Action? Saved;

    protected abstract Task SaveCoreAsync();

    [RelayCommand]
    private async Task Save()
    {
        if (IsBusy) return;
        if (await RunAsync(SaveCoreAsync)) Saved?.Invoke();
    }
}
