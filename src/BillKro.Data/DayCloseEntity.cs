namespace BillKro.Data;

/// <summary>A cashier's counter closing for one day: expected cash vs what was counted in the drawer.</summary>
public class DayClose
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime ClosedAt { get; set; }
    public decimal OpeningFloat { get; set; }
    public decimal NetCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal CountedCash { get; set; }

    /// <summary>Note/coin counts as "500x3,100x12".</summary>
    public string? Denominations { get; set; }
    public string? Note { get; set; }
    public int Bills { get; set; }
    public decimal SalesTotal { get; set; }
}