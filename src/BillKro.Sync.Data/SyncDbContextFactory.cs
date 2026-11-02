using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BillKro.Sync.Data;

/// <summary>
/// Used by EF Core tooling (dotnet ef migrations) at design time.
/// </summary>
public class SyncDbContextFactory : IDesignTimeDbContextFactory<SyncDbContext>
{
    public SyncDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<SyncDbContext>()
            .UseSqlServer("Server=(local);Database=BillKroSync;Integrated Security=True;TrustServerCertificate=True;")
            .Options;
        return new SyncDbContext(opts);
    }
}
