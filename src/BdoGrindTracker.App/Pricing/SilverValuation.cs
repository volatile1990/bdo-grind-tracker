namespace BdoGrindTracker.App.Pricing;

/// <summary>
/// Companion's Central Market return settings. Bonuses increase the 65% base
/// proceeds, not the pre-tax selling price. NPC sales never use this factor.
/// </summary>
internal sealed record SilverTaxOptions
{
    public SilverTaxOptions(bool ValuePack = false, bool MerchantRing = false, int FamilyFame = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(FamilyFame);
        this.ValuePack = ValuePack;
        this.MerchantRing = MerchantRing;
        this.FamilyFame = FamilyFame;
    }

    public bool ValuePack { get; }
    public bool MerchantRing { get; }
    public int FamilyFame { get; }
    public decimal FamilyFameBonus => FamilyFame switch
    {
        >= 7000 => 0.015m,
        >= 4000 => 0.010m,
        >= 1000 => 0.005m,
        _ => 0m,
    };

    public decimal MarketReturnRate => 0.65m *
        (1m + (ValuePack ? 0.30m : 0m) + (MerchantRing ? 0.05m : 0m) + FamilyFameBonus);

    public static SilverTaxOptions Default { get; } = new();
}

/// <summary>
/// The known subtotal, not an invented zero for items whose price is missing.
/// Completeness and staleness must accompany the values in the dashboard.
/// </summary>
internal sealed record SilverValuationResult(
    decimal BeforeTax,
    decimal AfterTax,
    int ValuedItemCount,
    IReadOnlyList<string> MissingItems,
    IReadOnlyList<string> OverflowItems,
    bool IsStale)
{
    public bool IsComplete => MissingItems.Count == 0 && OverflowItems.Count == 0;
    public bool HasKnownValue => ValuedItemCount > 0;
}

/// <summary>
/// A pure projection of the already-counted item totals. It never feeds back
/// into OCR, reconciliation, timing, or the session's quantity ledger.
/// </summary>
internal static class SilverValuation
{
    public static SilverValuationResult Calculate(
        IReadOnlyDictionary<string, long> totals,
        LootPriceSnapshot prices,
        SilverTaxOptions? tax = null)
    {
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(prices);
        tax ??= SilverTaxOptions.Default;

        decimal beforeTax = 0;
        decimal afterTax = 0;
        var valuedItemCount = 0;
        var missing = new List<string>();
        var overflow = new List<string>();
        var isStale = false;
        foreach (var (itemName, quantity) in totals)
        {
            // A reconciled-away item needs neither a price nor a warning.
            if (quantity == 0)
            {
                continue;
            }

            if (!prices.TryGetQuote(itemName, out var quote))
            {
                missing.Add(itemName);
                continue;
            }

            try
            {
                // Native Companion truncates the taxed unit price before
                // multiplying by quantity (0x1401B37F4..0x1401B382C). Do not
                // truncate a line's or session's total instead. Decimal avoids
                // binary floating-point drift while retaining this rounding.
                var unitBeforeTax = decimal.Truncate(quote.UnitPrice);
                var unitAfterTax = decimal.Truncate(
                    quote.TaxableUnitPrice * tax.MarketReturnRate + quote.UntaxedUnitPrice);
                var nextBeforeTax = checked(beforeTax + unitBeforeTax * quantity);
                var nextAfterTax = checked(afterTax + unitAfterTax * quantity);
                beforeTax = nextBeforeTax;
                afterTax = nextAfterTax;
                valuedItemCount++;
                isStale |= quote.IsStale;
            }
            catch (OverflowException)
            {
                // An invalid/extreme external quote must never crash tracking
                // or silently turn an unrepresentable amount into zero.
                overflow.Add(itemName);
            }
        }

        missing.Sort(StringComparer.Ordinal);
        overflow.Sort(StringComparer.Ordinal);
        return new SilverValuationResult(beforeTax, afterTax, valuedItemCount,
            missing.AsReadOnly(), overflow.AsReadOnly(), isStale);
    }
}
