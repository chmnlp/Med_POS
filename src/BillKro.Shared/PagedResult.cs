namespace BillKro.Shared;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record DashboardSummary(int ActiveItems, int LowStockItems, int NearExpiryBatches, int Customers, int Suppliers,
    int TodayBills = 0, decimal TodaySales = 0, int OverdueCustomers = 0, decimal OverdueAmount = 0,
    decimal MonthSales = 0, decimal? TodayProfit = null, decimal? MonthProfit = null)
{
    /// <summary>Profit figures are only sent to managers.</summary>
    public bool ShowsProfit => TodayProfit is not null;
}
