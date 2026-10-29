using MargPos.Shared;

namespace MargPos.Tests;

public class BatchDatesTests
{
    [Theory]
    [InlineData("03/27", 2027, 3, 31)]
    [InlineData("3/27", 2027, 3, 31)]
    [InlineData("02/28", 2028, 2, 29)] // leap year
    [InlineData("02-2027", 2027, 2, 28)]
    [InlineData("0327", 2027, 3, 31)]
    [InlineData("122026", 2026, 12, 31)]
    [InlineData("15-03-2027", 2027, 3, 15)]
    [InlineData("15/03/27", 2027, 3, 15)]
    [InlineData(" 11.2026 ", 2026, 11, 30)]
    public void Expiry_formats(string text, int y, int m, int d)
    {
        Assert.True(BatchDates.TryParseExpiry(text, out var date));
        Assert.Equal(new DateOnly(y, m, d), date);
    }

    [Fact]
    public void Mfg_month_year_is_first_of_month()
    {
        Assert.True(BatchDates.TryParseMfg("03/25", out var date));
        Assert.Equal(new DateOnly(2025, 3, 1), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_is_valid_and_null(string? text)
    {
        Assert.True(BatchDates.TryParseExpiry(text, out var date));
        Assert.Null(date);
    }

    [Theory]
    [InlineData("13/27")]
    [InlineData("00/27")]
    [InlineData("31-02-2027")]
    [InlineData("abc")]
    [InlineData("03/1999")]
    [InlineData("310327")]
    public void Invalid_dates_are_rejected(string text) =>
        Assert.False(BatchDates.TryParseExpiry(text, out _));
}
