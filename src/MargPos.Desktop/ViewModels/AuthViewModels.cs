using CommunityToolkit.Mvvm.ComponentModel;
using MargPos.Desktop.Services;
using MargPos.Shared;

namespace MargPos.Desktop.ViewModels;

public partial class LoginViewModel(ApiClient api, Session session, ClientSettings settings) : ViewModelBase
{
    [ObservableProperty] private string _serverUrl = settings.ServerUrl;
    [ObservableProperty] private string _username = settings.LastUsername ?? "";

    public event Action? SignedIn;

    /// <remarks>The password comes from a PasswordBox, which cannot be data-bound.</remarks>
    public async Task SignInAsync(string password)
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(password))
        {
            ErrorMessage = "Enter your username and password.";
            return;
        }

        var ok = await RunAsync(async () =>
        {
            api.SetServer(ServerUrl);
            var login = await api.PostAsync<LoginResponse>("api/auth/login", new LoginRequest(Username.Trim(), password));
            session.Start(login);
        });
        if (!ok) return;

        settings.ServerUrl = api.ServerUri!.ToString().TrimEnd('/');
        settings.LastUsername = Username.Trim();
        settings.Save();
        SignedIn?.Invoke();
    }
}

public partial class ChangePasswordViewModel(ApiClient api, Session session) : ViewModelBase
{
    /// <summary>True on first sign-in, when the user cannot skip the change.</summary>
    public bool IsForced { get; set; }

    public string Heading => IsForced
        ? "Choose a new password to continue"
        : "Change your password";

    public event Action? Changed;

    public async Task SubmitAsync(string current, string next, string confirm)
    {
        if (IsBusy) return;
        if (next != confirm)
        {
            ErrorMessage = "The new passwords do not match.";
            return;
        }

        var ok = await RunAsync(async () =>
        {
            var fresh = await api.PostAsync<LoginResponse>("api/auth/change-password", new ChangePasswordRequest(current, next));
            session.Start(fresh);
        });
        if (ok) Changed?.Invoke();
    }
}
