using System.Security.Claims;
using BillKro.Data;
using BillKro.Server.Auth;
using BillKro.Server.Infrastructure;
using BillKro.Shared;
using Microsoft.EntityFrameworkCore;

namespace BillKro.Server.Endpoints;

public static class UserEndpoints
{
    public static void MapUsers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/users").RequireAuthorization(Policies.Admin);
        g.MapGet("/", List);
        g.MapPost("/", Create);
        g.MapPut("/{id:int}", Update);
        g.MapPost("/{id:int}/reset-password", ResetPassword);
    }

    private static async Task<List<UserDto>> List(PosDbContext db, CancellationToken ct) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Username)
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsActive))
            .ToListAsync(ct);

    private static async Task<IResult> Create(UserCreateRequest req, PosDbContext db, PasswordService passwords,
        CancellationToken ct)
    {
        var username = req.Username?.Trim() ?? "";
        var errors = new Errors()
            .Required(nameof(req.Username), username, 50)
            .Required(nameof(req.FullName), req.FullName, 100);
        if (PasswordService.Validate(req.Password) is { } msg) errors.Add(nameof(req.Password), msg);
        if (!Enum.IsDefined(req.Role)) errors.Add(nameof(req.Role), "Unknown role.");
        if (errors.Any) return errors.ToResult();

        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            return DbHelpers.Conflict($"Username '{username}' is already taken.");

        // New users pick their own password on first sign-in.
        var user = new User { Username = username, FullName = req.FullName.Trim(), Role = req.Role, MustChangePassword = true };
        user.PasswordHash = passwords.Hash(user, req.Password);
        db.Users.Add(user);
        if (await db.SaveOrConflictAsync($"Username '{username}' is already taken.", ct) is { } conflict) return conflict;

        return Results.Created($"/api/users/{user.Id}", user.ToDto());
    }

    private static async Task<IResult> Update(int id, UserUpdateRequest req, ClaimsPrincipal principal,
        PosDbContext db, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return DbHelpers.NotFound("User");

        var errors = new Errors().Required(nameof(req.FullName), req.FullName, 100);
        if (!Enum.IsDefined(req.Role)) errors.Add(nameof(req.Role), "Unknown role.");
        if (errors.Any) return errors.ToResult();

        if (id == principal.GetUserId() && (!req.IsActive || req.Role != UserRole.Admin))
            return DbHelpers.Conflict("You cannot deactivate or demote your own account.");

        var losingAdmin = user.Role == UserRole.Admin && user.IsActive && (req.Role != UserRole.Admin || !req.IsActive);
        if (losingAdmin && !await db.Users.AnyAsync(u => u.Id != id && u.Role == UserRole.Admin && u.IsActive, ct))
            return DbHelpers.Conflict("At least one active admin is required.");

        if ((user.IsActive && !req.IsActive) || user.Role != req.Role)
            user.SecurityStamp = Guid.NewGuid().ToString("N");

        user.FullName = req.FullName.Trim();
        user.Role = req.Role;
        user.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
        return Results.Ok(user.ToDto());
    }

    private static async Task<IResult> ResetPassword(int id, ResetPasswordRequest req, PosDbContext db,
        PasswordService passwords, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return DbHelpers.NotFound("User");
        if (PasswordService.Validate(req.NewPassword) is { } msg)
            return new Errors().Add(nameof(req.NewPassword), msg).ToResult();

        user.PasswordHash = passwords.Hash(user, req.NewPassword);
        user.MustChangePassword = true;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
