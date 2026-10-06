using System.Globalization;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LootTableProjectionCacheTests
{
    private static readonly Guid Session = Guid.NewGuid();
    private static readonly LootPriceSnapshot Prices = new("eu",
    [
        new("Apeiron Earring", 1_001, 0, LootPriceOrigin.LiveMarket, null, IsStale: true),
        new("Elion Follower's Helmet", 0, 100, LootPriceOrigin.FixedCatalog, null),
    ]);

    [Fact]
    public void AdvancingClockReusesRowsUntilAnActualFutureDropBecomesEligible()
    {
        var cache = new LootTableProjectionCache();
        var totals = new Dictionary<string, long> { ["Apeiron Earring"] = 2 };
        var drops = new List<SessionDropSample> { new(TimeSpan.FromSeconds(2), "Apeiron Earring", 1), new(TimeSpan.FromSeconds(10), "Apeiron Earring", 1) };
        var first = Update(cache, totals, drops, 5);
        Assert.Same(first, Update(cache, totals, drops, 9));
        Assert.Equal(TimeSpan.FromSeconds(2), first[0].LastDropElapsed);
        var atBoundary = Update(cache, totals, drops, 10);
        Assert.NotSame(first, atBoundary);
        Assert.Equal(TimeSpan.FromSeconds(10), atBoundary[0].LastDropElapsed);
        Assert.Equal(TimeSpan.FromSeconds(2), Update(cache, totals, drops, 3)[0].LastDropElapsed);
        Assert.Null(Update(cache, totals, drops, 1)[0].LastDropElapsed);
    }

    [Fact]
    public void MutableTotalsAndSameLengthDropCorrectionsCannotHideBehindReferences()
    {
        var cache = new LootTableProjectionCache();
        var totals = new Dictionary<string, long> { ["Apeiron Earring"] = 2 };
        var drops = new List<SessionDropSample> { new(TimeSpan.FromSeconds(2), "apeiron earring", 1) };
        var first = Update(cache, totals, drops);
        totals["Apeiron Earring"] = 3;
        drops[0] = new(TimeSpan.FromSeconds(4), "apeiron earring", 1);
        var next = Update(cache, totals, drops);
        Assert.Equal(2, first[0].Quantity);
        Assert.Equal(3, next[0].Quantity);
        Assert.Equal(TimeSpan.FromSeconds(4), next[0].LastDropElapsed);
        drops[0] = drops[0] with { Quantity = 0 };
        Assert.Null(Update(cache, totals, drops)[0].LastDropElapsed);
        totals["unknown"] = 1;
        Assert.Null(Assert.Single(Update(cache, totals, drops), row => row.Name == "unknown").Value);
    }

    [Theory]
    [InlineData("value", "", "en")]
    [InlineData("quantity", "", "de")]
    [InlineData("name", "", "de")]
    [InlineData("name", "", "en")]
    [InlineData("value", "Elion", "en")]
    [InlineData("quantity", "not present", "de")]
    public void CachedSortingAndSearchMatchThePreviousUncachedProjection(string sort, string query, string language)
    {
        var cache = new LootTableProjectionCache();
        var totals = new Dictionary<string, long>
        {
            ["Apeiron Earring"] = 2, ["Elion Follower's Helmet"] = 100, ["unknown"] = 1,
        };
        Update(cache, totals, [], sort: "value", query: "old query", language: "en");
        var rows = Update(cache, totals, [], sort: sort, query: query, language: language);
        var expected = totals.Where(pair => pair.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                ItemLocalizationCatalog.GermanNames.GetValueOrDefault(pair.Key, pair.Key).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(pair =>
            {
                var value = SilverValuation.Calculate(new Dictionary<string, long> { [pair.Key] = pair.Value }, Prices);
                return new LootTableRow(pair.Key, pair.Value, value.IsComplete ? value.AfterTax : null, value.IsStale, null);
            });
        var sorted = sort switch
        {
            "quantity" => expected.OrderByDescending(item => item.Quantity).ThenBy(item => item.Name),
            "name" => expected.OrderBy(item => ItemLocalizationCatalog.DisplayName(item.Name, language)),
            _ => expected.OrderByDescending(item => item.Value).ThenBy(item => item.Name),
        };
        Assert.Equal(sorted, rows);
        Assert.Same(rows, Update(cache, totals, [], sort: sort, query: query, language: language));
    }

    [Fact]
    public void TaxPricesAndHiddenOrSwitchedSessionUpdateWithoutChangingTheTotals()
    {
        var cache = new LootTableProjectionCache();
        var totals = new Dictionary<string, long> { ["Apeiron Earring"] = 2 };
        var drops = new[] { new SessionDropSample(TimeSpan.FromSeconds(2), "Apeiron Earring", 1) };
        var first = Update(cache, totals, drops);
        var tax = new SilverTaxOptions(ValuePack: true);
        var taxed = Update(cache, totals, drops, tax: tax);
        Assert.NotEqual(first[0].Value, taxed[0].Value);
        var prices = new LootPriceSnapshot("na", [new("Apeiron Earring", 0, 300, LootPriceOrigin.FixedCatalog, null)]);
        var repriced = Update(cache, totals, drops, prices: prices, tax: tax);
        Assert.Equal(600, repriced[0].Value);
        Assert.False(repriced[0].Stale);
        Assert.Null(cache.Update(totals, prices, tax, "", "value", "en", drops, TimeSpan.FromSeconds(5), null)[0].LastDropElapsed);
        var switched = cache.Update(totals, prices, tax, "", "value", "en", drops, TimeSpan.FromSeconds(5), Guid.NewGuid());
        Assert.Equal(TimeSpan.FromSeconds(2), switched[0].LastDropElapsed);
    }

    [Fact]
    public void OverflowAndZeroQuantityKeepTheExistingValuationSemantics()
    {
        var cache = new LootTableProjectionCache();
        var prices = new LootPriceSnapshot("eu", [new("overflow", 0, decimal.MaxValue, LootPriceOrigin.FixedCatalog, null)]);
        var totals = new Dictionary<string, long> { ["overflow"] = 2, ["missing but zero"] = 0 };
        var rows = Update(cache, totals, [], prices: prices);
        Assert.Null(Assert.Single(rows, row => row.Name == "overflow").Value);
        Assert.Equal(0, Assert.Single(rows, row => row.Name == "missing but zero").Value);
    }

    [Fact]
    public void CurrentCultureChangesInvalidateCultureSensitiveNameOrdering()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var cache = new LootTableProjectionCache();
            var totals = new Dictionary<string, long> { ["zebra"] = 1, ["\u00e4lg"] = 1 };
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var german = Update(cache, totals, [], sort: "name");
            Assert.Equal(totals.Keys.OrderBy(name => name), german.Select(row => row.Name));

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            var swedish = Update(cache, totals, [], sort: "name");

            Assert.NotSame(german, swedish);
            Assert.Equal(totals.Keys.OrderBy(name => name), swedish.Select(row => row.Name));
            Assert.NotEqual(german.Select(row => row.Name).ToArray(), swedish.Select(row => row.Name).ToArray());
            Assert.Same(swedish, Update(cache, totals, [], sort: "name"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static IReadOnlyList<LootTableRow> Update(LootTableProjectionCache cache,
        IReadOnlyDictionary<string, long> totals, IReadOnlyList<SessionDropSample> drops, double elapsed = 5,
        LootPriceSnapshot? prices = null, SilverTaxOptions? tax = null, string sort = "value", string query = "", string language = "en") =>
        cache.Update(totals, prices ?? Prices, tax ?? SilverTaxOptions.Default, query, sort, language,
            drops, TimeSpan.FromSeconds(elapsed), Session);
}
