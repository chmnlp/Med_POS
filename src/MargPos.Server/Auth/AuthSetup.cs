using MargPos.Data;
using MargPos.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace MargPos.Server.Auth;

public static class Policies
{
    /// <summary>Signed in, even if a password change is pending. Only for change-password and "me".</summary>
    public const string SignedIn = nameof(SignedIn);
    public const string Manager = nameof(Manager);
    public const string Admin = nameof(Admin);
}

public static class AuthSetup
{
    public static IServiceCollection AddPosAuth(this IServiceCollection services, JwtSettings jwt)
    {
        services.AddSingleton(jwt);
        services.AddSingleton<TokenService>();
        services.AddSingleton<PasswordService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = JwtSettings.Issuer,
                    ValidAudience = JwtSettings.Audience,
                    IssuerSigningKey = jwt.SigningKey,
                    NameClaimType = PosClaims.Name,
                    RoleClaimType = PosClaims.Role,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
                o.Events = new JwtBearerEvents { OnTokenValidated = RejectRevokedSessions };
            });

        services.AddAuthorization(o =>
        {
            // Everything requires a full session unless it opts out with AllowAnonymous or SignedIn.
            var fullSession = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => !ctx.User.HasClaim(c => c.Type == PosClaims.MustChangePassword))
                .Build();
            o.DefaultPolicy = fullSession;
            o.FallbackPolicy = fullSession;

            o.AddPolicy(Policies.SignedIn, p => p.RequireAuthenticatedUser());
            o.AddPolicy(Policies.Manager, p => p.Combine(fullSession)
                .RequireRole(nameof(UserRole.Manager), nameof(UserRole.Admin)));
            o.AddPolicy(Policies.Admin, p => p.Combine(fullSession)
                .RequireRole(nameof(UserRole.Admin)));
        });

        return services;
    }

    /// <summary>Fails tokens for users who were deactivated or whose password changed since issue.</summary>
    private static async Task RejectRevokedSessions(TokenValidatedContext ctx)
    {
        var principal = ctx.Principal!;
        if (!int.TryParse(principal.FindFirst(PosClaims.UserId)?.Value, out var userId))
        {
            ctx.Fail("Invalid token.");
            return;
        }

        var db = ctx.HttpContext.RequestServices.GetRequiredService<PosDbContext>();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.SecurityStamp })
            .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);

        if (user is null || !user.IsActive || user.SecurityStamp != principal.FindFirst(PosClaims.Stamp)?.Value)
            ctx.Fail("Session is no longer valid.");
    }
}
