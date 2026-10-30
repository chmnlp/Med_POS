using MargPos.Shared;

namespace MargPos.Tests;

public class BillCalculatorTests
{
    [Fact]
    public void Inclusive_rate_is_split_into_taxable_and_tax()
    {
        // 2 strips at 112 incl. 12% GST: 224 = 200 taxable + 24 tax.
        var line = BillCalculator.CalculateLine(new BillLineInput(2, 112, 0, 12));
        Assert.Equal(224m, line.Gross);
        Assert.Equal(224m, line.Amount);
        Assert.Equal(200m, line.Taxable);
        Assert.Equal(24m, line.Tax);
    }

    [Fact]
    public void Discount_is_applied_before_tax_split()
    {
        // 100 at 10% off = 90 incl. 5% GST → taxable 85.71, tax 4.29.
        var line = BillCalculator.CalculateLine(new BillLineInput(1, 100, 10, 5));
        Assert.Equal(10m, line.Discount);
        Assert.Equal(90m, line.Amount);
        Assert.Equal(85.71m, line.Taxable);
        Assert.Equal(4.29m, line.Tax);
    }

    [Fact]
    public void Intra_state_bill_splits_cgst_and_sgst_per_slab_and_rounds_total()
    {
        var totals = BillCalculator.Calculate(
        [
            new BillLineInput(1, 33.50m, 0, 5),
            new BillLineInput(3, 12.25m, 0, 5),
            new BillLineInput(1, 118m, 0, 18),
        ], interState: false);

        // 33.50 + 36.75 + 118 = 188.25 → 188, round-off −0.25
        Assert.Equal(188m, totals.Total);
        Assert.Equal(-0.25m, totals.RoundOff);
        Assert.Equal(2, totals.Slabs.Count);

        var five = totals.Slabs[0];
        Assert.Equal(5m, five.GstRate);
        Assert.Equal(five.Cgst + five.Sgst, totals.Lines[0].Tax + totals.Lines[1].Tax);
        Assert.Equal(0m, totals.Igst);

        // Every paisa is accounted for.
        Assert.Equal(totals.Total, totals.Taxable + totals.Tax + totals.RoundOff);
    }

    [Fact]
    public void Inter_state_bill_uses_igst()
    {
        var totals = BillCalculator.Calculate([new BillLineInput(1, 118m, 0, 18)], interState: true);
        Assert.Equal(18m, totals.Igst);
        Assert.Equal(0m, totals.Cgst + totals.Sgst);
    }

    [Theory]
    [InlineData("27", "29ABCDE1234F1Z5", true)]
    [InlineData("27", "27ABCDE1234F1Z5", false)]
    [InlineData("27", null, false)]
    [InlineData(null, "29ABCDE1234F1Z5", false)]
    public void Inter_state_is_decided_by_gstin_state_code(string? store, string? customer, bool expected) =>
        Assert.Equal(expected, BillCalculator.IsInterState(store, customer));

    [Theory]
    [InlineData(CustomerType.Retail, 33.5, 31, 29, 27, 31)]
    [InlineData(CustomerType.Wholesale, 33.5, 31, 29, 27, 29)]
    [InlineData(CustomerType.Hospital, 33.5, 31, 29, 27, 27)]
    [InlineData(CustomerType.Hospital, 33.5, 31, 29, 0, 29)]    // no hospital rate → wholesale
    [InlineData(CustomerType.Wholesale, 33.5, 31, 0, 0, 31)]    // no wholesale rate → retail
    [InlineData(CustomerType.Hospital, 33.5, 31, 40, 0, 33.5)]  // capped at MRP
    public void Default_rate_by_customer_type(CustomerType type, decimal mrp, decimal retail, decimal wholesale,
        decimal hospital, decimal expected) =>
        Assert.Equal(expected, Pricing.DefaultRate(type, mrp, retail, wholesale, hospital));

    [Theory]
    [InlineData(50, 12, 20, 100, 67.20)]   // 50 × 1.2 × 1.12
    [InlineData(50, 12, 300, 100, 100)]    // capped at MRP
    [InlineData(19.5455, 5, 25, 0, 25.65)] // no MRP cap
    public void Rate_from_margin_on_landed_cost(decimal cost, decimal gst, decimal margin, decimal mrp, decimal expected) =>
        Assert.Equal(expected, Pricing.FromMargin(cost, gst, margin, mrp));
}
