using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MargSync.Data;

/// <summary>
/// Used by EF Core tooling (dotnet ef migrations) at design time.
/// </summary>
public class MargSyncDbContextFactory : IDesignTimeDbContextFactory<MargSyncDbContext>
{
    public MargSyncDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<MargSyncDbContext>()
            .UseSqlServer("Server=(local);Database=MargSync;Integrated Security=True;TrustServerCertificate=True;")
            .Options;
        return new MargSyncDbContext(opts);
    }
}
