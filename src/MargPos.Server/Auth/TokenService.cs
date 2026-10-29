using System.Security.Claims;
using MargPos.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MargPos.Server.Auth;

public static class PosClaims
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Role = "role";
    public const string Stamp = "stamp";

    /// <summary>Present when the user must change their password before doing anything else.</summary>
    public const string MustChangePassword = "mcp";

    public static int GetUserId(this ClaimsPrincipal p) => int.Parse(p.FindFirstValue(UserId)!);
}

public class TokenService(JwtSettings settings)
{
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var expires = DateTime.UtcNow.Add(settings.Lifetime);
        var claims = new List<Claim>
        {
            new(PosClaims.UserId, user.Id.ToString()),
            new(PosClaims.Name, user.Username),
            new(PosClaims.Role, user.Role.ToString()),
            new(PosClaims.Stamp, user.SecurityStamp),
        };
        if (user.MustChangePassword)
            claims.Add(new Claim(PosClaims.MustChangePassword, "1"));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            Issuer = JwtSettings.Issuer,
            Audience = JwtSettings.Audience,
            SigningCredentials = new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }
}

public class PasswordService
{
    public const int MinLength = 6;
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    /// <summary>Verifies and transparently upgrades the hash if the algorithm parameters changed.</summary>
    public bool Verify(User user, string password)
    {
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = _hasher.HashPassword(user, password);
        return result != PasswordVerificationResult.Failed;
    }

    public static string? Validate(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinLength
            ? $"Password must be at least {MinLength} characters."
            : null;
}
