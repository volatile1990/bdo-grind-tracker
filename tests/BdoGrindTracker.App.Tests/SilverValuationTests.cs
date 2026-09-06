using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Tests;

public sealed class SilverValuationTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "0")]
    [InlineData(1000, "0.005")]
    [InlineData(3999, "0.005")]
    [InlineData(4000, "0.010")]
    [InlineData(6999, "0.010")]
    [InlineData(7000, "0.015")]
    [InlineData(int.MaxValue, "0.015")]
    public void FamilyFameUsesCompanionThresholds(int fame, string bonus)
    {
        var tax = new SilverTaxOptions(FamilyFame: fame);
        Assert.Equal(Parse(bonus), tax.FamilyFameBonus);
    }

    [Theory]
    [InlineData(false, false, 0, "0.65")]
    [InlineData(true, false, 0, "0.845")]
    [InlineData(false, true, 0, "0.6825")]
    [InlineData(true, true, 0, "0.8775")]
    [InlineData(false, false, 7000, "0.65975")]
    [InlineData(true, false, 7000, "0.85475")]
    [InlineData(true, true, 7000, "0.88725")]
    public void BonusesMultiplyTheSixtyFivePercentBase(bool valuePack, bool ring, int fame, string rate)
    {
        Assert.Equal(Parse(rate), new SilverTaxOptions(valuePack, ring, fame).MarketReturnRate);
    }

    [Fact]
    public void NegativeFamilyFameIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SilverTaxOptions(FamilyFame: -1));

    [Fact]
    public void DefaultsDoNotAssumeUserOwnsPaidPackOrMerchantRing()
    {
        Assert.False(SilverTaxOptions.Default.ValuePack);
        Assert.False(SilverTaxOptions.Default.MerchantRing);
        Assert.Equal(0, SilverTaxOptions.Default.FamilyFame);
    }

    [Fact]
    public void EmptySessionHasCompleteZeroValue()
    {
        var result = SilverValuation.Calculate(new Dictionary<string, long>(), Prices());
        Assert.Equal(0, result.BeforeTax);
        Assert.Equal(0, result.AfterTax);
        Assert.True(result.IsComplete);
        Assert.False(result.HasKnownValue);
        Assert.False(result.IsStale);
    }

    [Fact]
    public void MarketAndNpcSalesAreValuedSeparately()
    {
        var totals = Totals(("Black Stone", 10), ("Black Crystal Fragment", 100));
        var prices = Prices(Market("Black Stone", 129_000), Fixed("Black Crystal Fragment", 160_539));
        var result = SilverValuation.Calculate(totals, prices, new SilverTaxOptions(ValuePack: true));
        Assert.Equal(17_343_900m, result.BeforeTax);
        Assert.Equal(17_143_950m, result.AfterTax);
        Assert.Equal(2, result.ValuedItemCount);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void NpcValueDoesNotChangeWhenTaxOptionsChange()
    {
        var totals = Totals(("Trash", 1582));
        var prices = Prices(Fixed("Trash", 160_539));
        var withoutBonuses = SilverValuation.Calculate(totals, prices);
        var withBonuses = SilverValuation.Calculate(totals, prices, new SilverTaxOptions(true, true, 9000));
        Assert.Equal(253_972_698m, withoutBonuses.BeforeTax);
        Assert.Equal(withoutBonuses.BeforeTax, withoutBonuses.AfterTax);
        Assert.Equal(withoutBonuses.AfterTax, withBonuses.AfterTax);
    }

    [Fact]
    public void TaxedUnitPriceIsTruncatedBeforeQuantityMultiplication()
    {
        var result = SilverValuation.Calculate(Totals(("Market", 3)), Prices(Market("Market", 3)));
        Assert.Equal(9, result.BeforeTax);
        Assert.Equal(3, result.AfterTax); // 3 * truncate(3 * .65), not truncate(9 * .65).
    }

    [Fact]
    public void DecimalTaxArithmeticDoesNotLoseOneSilverAtIntegerBoundary()
    {
        var result = SilverValuation.Calculate(Totals(("Market", 1)), Prices(Market("Market", 100_000)),
            new SilverTaxOptions(true, false, 7000));
        Assert.Equal(85_475m, result.AfterTax);
    }

    [Fact]
    public void DustUsesCompanionStoredUnitValueAndNonzeroTaxFlag()
    {
        // Companion's cache stores (885000 - 129000) / 5 = 151200 and tax=2.
        // Native valuation treats every nonzero tax flag as taxed, with no
        // special deduction of the Black Stone cost after applying tax.
        var result = SilverValuation.Calculate(Totals(("Ancient Spirit Dust", 5)),
            Prices(Market("Ancient Spirit Dust", 151_200)), new SilverTaxOptions(ValuePack: true));
        Assert.Equal(756_000m, result.BeforeTax);
        Assert.Equal(638_820m, result.AfterTax);
    }

    [Fact]
    public void MissingPriceDoesNotMasqueradeAsCompleteZero()
    {
        var result = SilverValuation.Calculate(Totals(("Unknown", 2)), Prices());
        Assert.False(result.IsComplete);
        Assert.False(result.HasKnownValue);
        Assert.Equal(["Unknown"], result.MissingItems);
    }

    [Fact]
    public void PartialValuesReportMissingItemsWithoutLosingKnownSubtotal()
    {
        var result = SilverValuation.Calculate(Totals(("Trash", 3), ("Unknown", 1)), Prices(Fixed("Trash", 100)));
        Assert.Equal(300, result.BeforeTax);
        Assert.Equal(300, result.AfterTax);
        Assert.True(result.HasKnownValue);
        Assert.Equal(1, result.ValuedItemCount);
        Assert.False(result.IsComplete);
        Assert.Equal(["Unknown"], result.MissingItems);
    }

    [Fact]
    public void KnownZeroPriceIsDifferentFromMissingPrice()
    {
        var result = SilverValuation.Calculate(Totals(("Bound item", 2)), Prices(Fixed("Bound item", 0)));
        Assert.True(result.IsComplete);
        Assert.True(result.HasKnownValue);
        Assert.Equal(0, result.BeforeTax);
        Assert.Empty(result.MissingItems);
    }

    [Fact]
    public void ReconciledAwayMissingItemNeedsNoWarning()
    {
        var result = SilverValuation.Calculate(Totals(("Unknown", 0)), Prices());
        Assert.True(result.IsComplete);
        Assert.False(result.HasKnownValue);
    }

    [Fact]
    public void SignedCorrectionsAreNotTurnedIntoAdditionalPositiveValue()
    {
        var prices = Prices(Market("Market", 3), Fixed("Trash", 100));
        var result = SilverValuation.Calculate(Totals(("Market", -3), ("Trash", 3)), prices);
        Assert.Equal(291, result.BeforeTax);
        Assert.Equal(297, result.AfterTax);
    }

    [Fact]
    public void RecalculationUsesCorrectedTotalsAndDoesNotAccumulateValuesAgain()
    {
        var prices = Prices(Fixed("BON Origin Shard", 15_000_000));
        var totals = Totals(("BON Origin Shard", 3));
        Assert.Equal(45_000_000m, SilverValuation.Calculate(totals, prices).BeforeTax);
        totals["BON Origin Shard"] = 1;
        Assert.Equal(15_000_000m, SilverValuation.Calculate(totals, prices).BeforeTax);
        Assert.Equal(15_000_000m, SilverValuation.Calculate(totals, prices).BeforeTax);
        Assert.Equal(1, totals["BON Origin Shard"]);
    }

    [Fact]
    public void UnusedStaleMarketPricesDoNotMakeNpcOnlySessionStale()
    {
        var result = SilverValuation.Calculate(Totals(("Trash", 1)),
            Prices(Fixed("Trash", 10), Market("Unused", 100) with { IsStale = true }));
        Assert.False(result.IsStale);
    }

    [Fact]
    public void UsedStalePriceIsClearlyMarkedButStillSuppliesSubtotal()
    {
        var result = SilverValuation.Calculate(Totals(("Market", 1)),
            Prices(Market("Market", 100) with { IsStale = true }));
        Assert.True(result.IsStale);
        Assert.True(result.IsComplete);
        Assert.Equal(100, result.BeforeTax);
    }

    [Fact]
    public void ZeroQuantityWithStalePriceDoesNotMakeSessionStale()
    {
        var result = SilverValuation.Calculate(Totals(("Market", 0)),
            Prices(Market("Market", 100) with { IsStale = true }));
        Assert.False(result.IsStale);
    }

    [Fact]
    public void ExtremeQuoteCannotOverflowOrCorruptOtherLine()
    {
        var result = SilverValuation.Calculate(Totals(("Huge", long.MaxValue), ("Trash", 3)),
            Prices(Market("Huge", long.MaxValue), Fixed("Trash", 100)));
        Assert.Equal(300, result.BeforeTax);
        Assert.Equal(300, result.AfterTax);
        Assert.Equal(1, result.ValuedItemCount);
        Assert.False(result.IsComplete);
        Assert.Equal(["Huge"], result.OverflowItems);
    }

    [Fact]
    public void SubtotalOverflowDoesNotApplyOnlyHalfOfTheLine()
    {
        var result = SilverValuation.Calculate(Totals(("Large", 1), ("Extra", 1)),
            Prices(Fixed("Large", decimal.MaxValue), Fixed("Extra", 1)));
        Assert.Equal(decimal.MaxValue, result.BeforeTax);
        Assert.Equal(decimal.MaxValue, result.AfterTax);
        Assert.Equal(["Extra"], result.OverflowItems);
    }

    [Fact]
    public void SignedMinimumQuantityDoesNotOverflowThroughAbsoluteValue()
    {
        var result = SilverValuation.Calculate(Totals(("Trash", long.MinValue)), Prices(Fixed("Trash", 1)));
        Assert.Equal((decimal)long.MinValue, result.BeforeTax);
        Assert.Equal((decimal)long.MinValue, result.AfterTax);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void MissingItemNamesAreStableAndReadOnly()
    {
        var result = SilverValuation.Calculate(Totals(("Zulu", 1), ("Alpha", 1)), Prices());
        Assert.Equal(["Alpha", "Zulu"], result.MissingItems);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.MissingItems).Add("Changed"));
    }

    [Fact]
    public void PriceSnapshotsAreReadOnlyAndCalculationDoesNotMutateInput()
    {
        var original = new List<LootPriceQuote> { Fixed("Trash", 5) };
        var snapshot = new LootPriceSnapshot("eu", original);
        original.Clear();
        var totals = Totals(("Trash", 2));
        var result = SilverValuation.Calculate(totals, snapshot);
        Assert.Equal(10, result.BeforeTax);
        Assert.Single(totals);
        Assert.Single(snapshot.Quotes);
        Assert.Equal(2, totals["Trash"]);
    }

    [Fact]
    public void NullInputsFailClearly()
    {
        Assert.Throws<ArgumentNullException>(() => SilverValuation.Calculate(null!, Prices()));
        Assert.Throws<ArgumentNullException>(() => SilverValuation.Calculate(Totals(), null!));
    }

    private static Dictionary<string, long> Totals(params (string Name, long Quantity)[] items) =>
        items.ToDictionary(static item => item.Name, static item => item.Quantity);

    private static LootPriceSnapshot Prices(params LootPriceQuote[] quotes) => new("eu", quotes);

    private static LootPriceQuote Market(string name, decimal price) =>
        new(name, price, 0, LootPriceOrigin.LiveMarket, DateTimeOffset.Parse("2026-09-05T12:00:00Z"));

    private static LootPriceQuote Fixed(string name, decimal price) =>
        new(name, 0, price, LootPriceOrigin.FixedCatalog, null);

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
