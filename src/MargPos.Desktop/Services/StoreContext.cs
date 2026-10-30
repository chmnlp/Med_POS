using MargPos.Shared;

namespace MargPos.Desktop.Services;

/// <summary>Store profile shared by billing (tax state, discount limit) and printing.</summary>
public class StoreContext(ApiClient api)
{
    public StoreSettingsDto Settings { get; private set; } = new();

    public async Task RefreshAsync() => Settings = await api.GetAsync<StoreSettingsDto>("api/settings");

    public void Update(StoreSettingsDto settings) => Settings = settings;
}
