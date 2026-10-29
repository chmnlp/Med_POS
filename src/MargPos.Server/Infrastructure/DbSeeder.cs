using MargPos.Data;
using MargPos.Server.Auth;
using MargPos.Shared;
using Microsoft.EntityFrameworkCore;

namespace MargPos.Server.Infrastructure;

public static class DbSeeder
{
    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "admin@123";

    /// <summary>Applies migrations and creates the first admin on an empty database.</summary>
    public static async Task InitialiseAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder));

        await db.Database.MigrateAsync(ct);

        if (await db.Users.AnyAsync(ct)) return;

        var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();
        var admin = new User
        {
            Username = DefaultAdminUsername,
            FullName = "Administrator",
            Role = UserRole.Admin,
            MustChangePassword = true,
        };
        admin.PasswordHash = passwords.Hash(admin, DefaultAdminPassword);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);

        logger.LogWarning("Created default admin '{User}'. The password must be changed at first sign-in.",
            DefaultAdminUsername);
    }
}
