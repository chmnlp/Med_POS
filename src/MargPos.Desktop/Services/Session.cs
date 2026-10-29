using MargPos.Shared;

namespace MargPos.Desktop.Services;

public class Session
{
    public string? Token { get; private set; }
    public UserDto? User { get; private set; }
    public bool MustChangePassword { get; private set; }

    public bool IsSignedIn => Token is not null;
    public bool IsAdmin => User?.Role == UserRole.Admin;
    public bool CanManage => User?.Role is UserRole.Manager or UserRole.Admin;

    public void Start(LoginResponse login)
    {
        Token = login.Token;
        User = login.User;
        MustChangePassword = login.MustChangePassword;
    }

    public void Clear()
    {
        Token = null;
        User = null;
        MustChangePassword = false;
    }
}
