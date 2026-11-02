using System.Security.Claims;
using BillKro.Data;
using BillKro.Server.Auth;
using BillKro.Server.Infrastructure;
using BillKro.Shared;
using Microsoft.EntityFrameworkCore;

namespace BillKro.Server.Endpoints;

public static class AuthEndpoints
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");
        g.MapPost("/login", Login).AllowAnonymous();
        g.MapPost("/change-password", ChangePassword).RequireAuthorization(Policies.SignedIn);
        g.MapGet("/me", Me).RequireAuthorization(Policies.SignedIn);
    }

    private static async Task<IResult> Login(LoginRequest req, PosDbContext db, PasswordService passwords,
        TokenService tokens, CancellationToken ct)
    {
        const string invalid = "Invalid username or password.";
        var username = req.Username?.Trim() ?? "";
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null || !user.IsActive)
            return Results.Problem(invalid, statusCode: StatusCodes.Status401Unauthorized);

        if (user.LockoutEndUtc > DateTime.UtcNow)
        {
            var minutes = (int)Math.Ceiling((user.LockoutEndUtc.Value - DateTime.UtcNow).TotalMinutes);
            return Results.Problem($"Account locked after too many failed attempts. Try again in {minutes} minute(s).",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!passwords.Verify(user, req.Password ?? ""))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockoutEndUtc = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync(ct);
            return Results.Problem(invalid, statusCode: StatusCodes.Status401Unauthorized);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync(ct);

        var (token, expires) = tokens.Create(user);
        return Results.Ok(new LoginResponse(token, expires, user.ToDto(), user.MustChangePassword));
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest req, ClaimsPrincipal principal,
        PosDbContext db, PasswordService passwords, TokenService tokens, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([principal.GetUserId()], ct);
        if (user is null) return DbHelpers.NotFound("User");

        var errors = new Errors();
        if (!passwords.Verify(user, req.CurrentPassword ?? ""))
            errors.Add(nameof(req.CurrentPassword), "Current password is incorrect.");
        if (PasswordService.Validate(req.NewPassword) is { } msg)
            errors.Add(nameof(req.NewPassword), msg);
        else if (req.NewPassword == req.CurrentPassword)
            errors.Add(nameof(req.NewPassword), "New password must be different from the current one.");
        if (errors.Any) return errors.ToResult();

        user.PasswordHash = passwords.Hash(user, req.NewPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);

        // Old token is now revoked by the stamp change; hand back a fresh one.
        var (token, expires) = tokens.Create(user);
        return Results.Ok(new LoginResponse(token, expires, user.ToDto(), false));
    }

    private static async Task<IResult> Me(ClaimsPrincipal principal, PosDbContext db, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([principal.GetUserId()], ct);
        return user is null ? DbHelpers.NotFound("User") : Results.Ok(user.ToDto());
    }

    public static UserDto ToDto(this User u) => new(u.Id, u.Username, u.FullName, u.Role, u.IsActive);
}
