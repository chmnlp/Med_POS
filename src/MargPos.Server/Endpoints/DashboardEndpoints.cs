using MargPos.Data;
using MargPos.Shared;
using Microsoft.EntityFrameworkCore;

namespace MargPos.Server.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboard(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/dashboard/summary", Summary);

    private static async Task<DashboardSummary> Summary(PosDbContext db, IConfiguration config, CancellationToken ct)
    {
        var nearExpiryDays = config.GetValue("Pos:NearExpiryDays", 90);
        var cutoff = DateOnly.FromDateTime(DateTime.Today).AddDays(nearExpiryDays);

        return new DashboardSummary(
            ActiveItems: await db.Items.CountAsync(i => i.IsActive, ct),
            LowStockItems: await db.Items.CountAsync(i => i.IsActive && i.ReorderLevel > 0
                && i.Batches.Sum(b => b.QtyOnHand) <= i.ReorderLevel, ct),
            NearExpiryBatches: await db.Batches.CountAsync(b => b.QtyOnHand > 0 && b.ExpiryDate != null
                && b.ExpiryDate <= cutoff && b.Item.IsActive, ct),
            Customers: await db.Customers.CountAsync(c => c.IsActive, ct),
            Suppliers: await db.Suppliers.CountAsync(s => s.IsActive, ct));
    }
}
