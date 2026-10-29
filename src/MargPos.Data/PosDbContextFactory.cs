using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MargPos.Data;

/// <summary>Used by EF Core tooling (dotnet ef migrations) at design time.</summary>
public class PosDbContextFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("MARGPOS_CONNECTION")
            ?? "Server=(local);Database=MargPos;Integrated Security=True;TrustServerCertificate=True;";
        var opts = new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlServer(conn, PosDbContextOptions.ConfigureSqlServer)
            .Options;
        return new PosDbContext(opts);
    }
}

public static class PosDbContextOptions
{
    public static void ConfigureSqlServer(Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder sql) =>
        sql.MigrationsHistoryTable("__EFMigrationsHistory", PosDbContext.Schema)
           .EnableRetryOnFailure();
}
