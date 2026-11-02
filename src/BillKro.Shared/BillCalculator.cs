namespace BillKro.Shared;

/// <summary>One bill line as entered: rate is GST-inclusive (MRP-based retail pricing).</summary>
public record BillLineInput(decimal Qty, decimal Rate, decimal DiscountPercent, decimal GstRate);

public record BillLineResult(decimal Gross, decimal Discount, decimal Amount, decimal Taxable, decimal Tax);

/// <summary>Tax summary per GST rate, as printed on the invoice.</summary>
public record GstSlab(decimal GstRate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst);

public record BillTotals(
    IReadOnlyList<BillLineResult> Lines,
    IReadOnlyList<GstSlab> Slabs,
    decimal Gross,
    decimal Discount,
    decimal Taxable,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal RoundOff,
    decimal Total)
{
    public decimal Tax => Cgst + Sgst + Igst;
}

/// <summary>
/// The single source of truth for bill maths, used by the billing screen for live totals and by the
/// server for the stored invoice. All amounts are rounded to paise per line; the bill total is
/// rounded to the nearest rupee with the difference shown as round-off.
/// </summary>
public static class BillCalculator
{
    public static BillLineResult CalculateLine(BillLineInput line)
    {
        var gross = Round2(line.Qty * line.Rate);
        var discount = Round2(gross * line.DiscountPercent / 100m);
        var amount = gross - discount;
        var taxable = Round2(amount * 100m / (100m + line.GstRate));
        return new BillLineResult(gross, discount, amount, taxable, amount - taxable);
    }

    public static BillTotals Calculate(IReadOnlyList<BillLineInput> lines, bool interState)
    {
        var results = lines.Select(CalculateLine).ToList();
        var slabs = BuildSlabs(lines.Zip(results, (l, r) => (l.GstRate, r.Taxable, r.Tax)), interState);
        var net = results.Sum(r => r.Amount);
        var total = Math.Round(net, 0, MidpointRounding.AwayFromZero);

        return new BillTotals(
            results,
            slabs,
            Gross: results.Sum(r => r.Gross),
            Discount: results.Sum(r => r.Discount),
            Taxable: slabs.Sum(s => s.Taxable),
            Cgst: slabs.Sum(s => s.Cgst),
            Sgst: slabs.Sum(s => s.Sgst),
            Igst: slabs.Sum(s => s.Igst),
            RoundOff: total - net,
            Total: total);
    }

    /// <summary>Groups line taxes by GST rate and splits each into CGST+SGST (intra-state) or IGST.</summary>
    public static List<GstSlab> BuildSlabs(IEnumerable<(decimal GstRate, decimal Taxable, decimal Tax)> lines, bool interState) =>
        lines.GroupBy(x => x.GstRate)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var taxable = g.Sum(x => x.Taxable);
                var tax = g.Sum(x => x.Tax);
                if (interState) return new GstSlab(g.Key, taxable, 0, 0, tax);
                var cgst = Round2(tax / 2m);
                return new GstSlab(g.Key, taxable, cgst, tax - cgst, 0);
            })
            .ToList();

    /// <summary>True when the buyer's GSTIN is registered in a different state from the store.</summary>
    public static bool IsInterState(string? storeStateCode, string? customerGstin) =>
        !string.IsNullOrWhiteSpace(storeStateCode)
        && customerGstin is { Length: >= 2 }
        && customerGstin[..2] != storeStateCode;

    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public static class Pricing
{
    /// <summary>
    /// Default selling rate for a customer type from a lot's price list. Hospital falls back to wholesale, and
    /// wholesale to retail, when not set (0). Never above MRP.
    /// </summary>
    public static decimal DefaultRate(CustomerType type, decimal mrp, decimal retail, decimal wholesale, decimal hospital)
    {
        var rate = type switch
        {
            CustomerType.Hospital when hospital > 0 => hospital,
            CustomerType.Hospital or CustomerType.Wholesale when wholesale > 0 => wholesale,
            _ => retail,
        };
        return mrp > 0 ? Math.Min(rate, mrp) : rate;
    }

    /// <summary>Lot rate if set, else the item's rate (lots created before per-lot rates existed have 0).</summary>
    public static decimal LotOrItem(decimal lotRate, decimal itemRate) => lotRate > 0 ? lotRate : itemRate;

    /// <summary>
    /// GST-inclusive selling rate that earns <paramref name="marginPercent"/> on the landed cost (ex GST):
    /// cost × (1 + margin) × (1 + GST), rounded to paise and capped at MRP.
    /// </summary>
    public static decimal FromMargin(decimal landedCost, decimal gstRate, decimal marginPercent, decimal mrp)
    {
        var rate = BillCalculator.Round2(landedCost * (100m + marginPercent) / 100m * (100m + gstRate) / 100m);
        return mrp > 0 ? Math.Min(rate, mrp) : rate;
    }

    /// <summary>Margin earned on cost by a GST-inclusive selling rate (for display).</summary>
    public static decimal? MarginOnCost(decimal sellingRate, decimal gstRate, decimal landedCost) =>
        landedCost <= 0 ? null : Math.Round((sellingRate * 100m / (100m + gstRate) - landedCost) / landedCost * 100m, 2);
}
