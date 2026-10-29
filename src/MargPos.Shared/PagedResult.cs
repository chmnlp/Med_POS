namespace MargPos.Shared;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record DashboardSummary(int ActiveItems, int LowStockItems, int NearExpiryBatches, int Customers, int Suppliers);
