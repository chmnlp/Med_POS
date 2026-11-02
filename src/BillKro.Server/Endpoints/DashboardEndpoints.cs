using System.Security.Claims;
using BillKro.Data;
using BillKro.Shared;
using Microsoft.EntityFrameworkCore;

namespace BillKro.Server.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboard(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/dashboard/summary", Summary);

    private static async Task<DashboardSummary> Summary(ClaimsPrincipal principal, PosDbContext db, IConfiguration config,
        TimeProvider clock, CancellationToken ct)
    {
        var nearExpiryDays = config.GetValue("Pos:NearExpiryDays", 90);
        var todayStart = clock.GetLocalNow().Date;
        var today = DateOnly.FromDateTime(todayStart);
        var cutoff = today.AddDays(nearExpiryDays);
        var todaysSales = db.SaleInvoices.Where(i => i.InvoiceDate >= todayStart && i.Status == InvoiceStatus.Completed);
        var receivables = await AccountEndpoints.ReceivablesAsync(db, today, ct);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthFrom = monthStart.ToDateTime(TimeOnly.MinValue);
        var monthSales = (await db.SaleInvoices.Where(i => i.InvoiceDate >= monthFrom && i.Status == InvoiceStatus.Completed)
                .SumAsync(i => (decimal?)i.Total, ct) ?? 0)
            - (await db.SaleReturns.Where(r => r.ReturnDate >= monthFrom && r.Invoice.Status == InvoiceStatus.Completed)
                .SumAsync(r => (decimal?)r.Total, ct) ?? 0);

        // Profit is commercially sensitive: managers only.
        var manager = principal.IsInRole(nameof(UserRole.Manager)) || principal.IsInRole(nameof(UserRole.Admin));
        decimal? todayProfit = manager ? (await StockEndpoints.ProfitAsync(db, today, today, null, ct)).Profit : null;
        decimal? monthProfit = manager ? (await StockEndpoints.ProfitAsync(db, monthStart, today, null, ct)).Profit : null;

        return new DashboardSummary(
            ActiveItems: await db.Items.CountAsync(i => i.IsActive, ct),
            LowStockItems: await db.Items.CountAsync(i => i.IsActive && i.ReorderLevel > 0
                && i.Batches.Sum(b => b.QtyOnHand) <= i.ReorderLevel, ct),
            NearExpiryBatches: await db.Batches.CountAsync(b => b.QtyOnHand > 0 && b.ExpiryDate != null
                && b.ExpiryDate <= cutoff && b.Item.IsActive, ct),
            Customers: await db.Customers.CountAsync(c => c.IsActive, ct),
            Suppliers: await db.Suppliers.CountAsync(s => s.IsActive, ct),
            TodayBills: await todaysSales.CountAsync(ct),
            TodaySales: await todaysSales.SumAsync(i => (decimal?)i.Total, ct) ?? 0,
            OverdueCustomers: receivables.Rows.Count(r => r.Overdue > 0),
            OverdueAmount: receivables.Overdue,
            MonthSales: monthSales,
            TodayProfit: todayProfit,
            MonthProfit: monthProfit);
    }
}
