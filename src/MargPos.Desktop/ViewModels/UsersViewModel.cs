using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MargPos.Desktop.Services;
using MargPos.Shared;

namespace MargPos.Desktop.ViewModels;

public partial class UsersViewModel(ApiClient api, DialogService dialogs) : PageViewModel
{
    public override string Title => "Users";

    public ObservableCollection<UserDto> Users { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(ResetPasswordCommand))]
    private UserDto? _selected;

    public override async Task ActivateAsync()
    {
        var selectedId = Selected?.Id;
        List<UserDto>? users = null;
        if (!await RunAsync(async () => users = await api.GetAsync<List<UserDto>>("api/users"))) return;

        Users.Clear();
        foreach (var u in users!) Users.Add(u);
        Selected = Users.FirstOrDefault(u => u.Id == selectedId) ?? Users.FirstOrDefault();
    }

    [RelayCommand]
    private Task Refresh() => ActivateAsync();

    [RelayCommand]
    private async Task New()
    {
        if (dialogs.ShowEditor(new UserEditorViewModel(api, null))) await ActivateAsync();
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Edit()
    {
        if (Selected is null) return;
        if (dialogs.ShowEditor(new UserEditorViewModel(api, Selected))) await ActivateAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ResetPassword()
    {
        if (Selected is not { } user) return;
        if (dialogs.ShowEditor(new ResetPasswordViewModel(api, user)))
            dialogs.Info($"Password reset. {user.FullName} must choose a new password at next sign-in, and any open session has been signed out.");
    }
}
