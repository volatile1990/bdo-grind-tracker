using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class HistoryPresentationCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(2));
    private static readonly LootSpotPresentation Profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.MagaiaId);
    private static readonly LootPriceSnapshot Prices = new("eu",
    [
        new(Profile.TrashItemName, 0, 100, LootPriceOrigin.FixedCatalog, null),
        new("Apeiron Earring", 1_001, 0, LootPriceOrigin.LiveMarket, Now),
    ]);

    [Fact]
    public void UnchangedMutableInputsReuseValuationGroupingMetricsAndChart()
    {
        var history = new List<LootHistoryEntry> { Entry(10), Entry(20, startedAt: Now.AddHours(-2)) };
        var cache = new HistoryPresentationCache();
        var first = Update(cache, history);
        var metrics = first.Metrics(Profile);
        var chart = first.Chart(Profile);

        var second = Update(cache, history, now: Now.AddSeconds(20));

        Assert.Same(first, second);
        Assert.Same(first.Sessions, second.Sessions);
        Assert.Same(first.ForSpot(Profile.SpotId), second.ForSpot(Profile.SpotId));
        Assert.Same(metrics, second.Metrics(Profile));
        Assert.Same(chart, second.Chart(Profile));
        Assert.Equal(HistoryPresentation.CalculateMetrics(Profile, first.ForSpot(Profile.SpotId)), metrics);
        Assert.Equal(HistoryPresentation.BuildChartData(Profile, first.Sessions).CumulativeSilver, chart.CumulativeSilver);
    }

    [Fact]
    public void InPlaceTotalsChangesInvalidateWithoutMutatingThePreviousProjection()
    {
        var entry = Entry(10);
        var history = new List<LootHistoryEntry> { entry };
        var cache = new HistoryPresentationCache();
        var first = Update(cache, history);
        var originalMetrics = first.Metrics(Profile);
        entry.Totals[Profile.TrashItemName] = 20;
        entry.Totals["unknown"] = 1;

        var next = Update(cache, history);

        Assert.NotSame(first, next);
        Assert.Equal(10, first.Sessions[0].Totals[Profile.TrashItemName]);
        Assert.Equal(20, next.Sessions[0].Totals[Profile.TrashItemName]);
        Assert.False(next.Sessions[0].SilverIsComplete);
        Assert.NotEqual(originalMetrics, next.Metrics(Profile));
        entry.Totals.Remove("unknown");
        Assert.True(Update(cache, history).Sessions[0].SilverIsComplete);
    }

    [Fact]
    public void PriceTaxAndMetadataChangesAreReflectedInTheCurrentProjection()
    {
        var entry = Entry(10);
        entry.Totals["Apeiron Earring"] = 2;
        var history = new List<LootHistoryEntry> { entry };
        var cache = new HistoryPresentationCache();
        var first = Update(cache, history);
        var tax = new SilverTaxOptions(ValuePack: true, MerchantRing: true, FamilyFame: 7_000);
        var taxed = Update(cache, history, tax: tax);
        Assert.NotEqual(first.Sessions[0].SilverAfterTax, taxed.Sessions[0].SilverAfterTax);
        var prices = new LootPriceSnapshot("na", [new(Profile.TrashItemName, 0, 200, LootPriceOrigin.FixedCatalog, null)]);
        var repriced = Update(cache, history, prices: prices, tax: tax);
        Assert.Equal(2_000, repriced.Sessions[0].SilverAfterTax);
        Assert.False(repriced.Sessions[0].SilverIsComplete);
        history[0] = entry with { CharacterClass = "Ranger", GarmothUploadedAt = Now, Duration = TimeSpan.FromMinutes(30) };
        var metadata = Update(cache, history, prices: prices, tax: tax);
        Assert.Equal("Ranger", metadata.Sessions[0].CharacterClass);
        Assert.Equal(Now, metadata.Sessions[0].GarmothUploadedAt);
        Assert.Equal(TimeSpan.FromMinutes(30), metadata.Sessions[0].Duration);
    }

    [Fact]
    public void DateWindowKeepsExactInclusiveBoundaryAndSupportsClockRewind()
    {
        var boundary = Entry(10, startedAt: Now.AddDays(-7));
        var cache = new HistoryPresentationCache();
        Assert.Single(Update(cache, [boundary], days: 7).Sessions);
        Assert.Empty(Update(cache, [boundary], days: 7, now: Now.AddTicks(1)).Sessions);
        Assert.Single(Update(cache, [boundary], days: 7, now: Now.AddTicks(-1)).Sessions);
    }

    [Fact]
    public void FiltersApplyBeforeValuationAndCacheRetainsOnlyTheCurrentView()
    {
        var selected = Entry(10);
        var otherClass = Entry(20) with { CharacterClass = "Ranger" };
        var old = Entry(30, startedAt: Now.AddDays(-8));
        var otherSpot = Entry(40) with { SpotId = LootSpotCatalog.AphrodonId };
        var history = new List<LootHistoryEntry> { selected, otherClass, old, otherSpot };
        var cache = new HistoryPresentationCache();
        Assert.Equal(4, Update(cache, history).Sessions.Length);
        var spot = cache.Update(history, Prices, SilverTaxOptions.Default, Profile.SpotId, 7,
            "Drakania", "query ignored on a spot page", "en", Now);
        Assert.Equal(selected.SessionId, Assert.Single(spot.Sessions).SessionId);
        Assert.Equal(1, cache.CachedEntryCount);
        var searched = cache.Update(history, Prices, SilverTaxOptions.Default, null, 0, "",
            Presentation.SpotName(Profile.SpotId, "en"), "en", Now);
        Assert.Equal(3, searched.Sessions.Length);
        history.Clear();
        Assert.Empty(Update(cache, history).Sessions);
        Assert.Equal(0, cache.CachedEntryCount);
    }

    [Fact]
    public void AbsentLegacyCollectionsDoNotChangeHistoryValuation()
    {
        var entry = Entry(10) with
        {
            Rotations = null!, RotationTimeline = null!, ManualLootItems = null!,
            GarmothPendingCorrectionIntervals = null!,
        };
        var projection = Update(new HistoryPresentationCache(), [entry]);
        Assert.Equal(1_000, Assert.Single(projection.Sessions).SilverAfterTax);
        Assert.Equal(HistoryPresentation.CalculateMetrics(Profile, projection.Sessions), projection.Metrics(Profile));
    }

    [Fact]
    public void MetricTiesPreserveOverviewAndDetailInputOrderSeparately()
    {
        var history = Enumerable.Range(0, 6).Select(index =>
        {
            var entry = Entry(10, startedAt: Now.AddHours(index - 10));
            entry.Totals["Apeiron Earring"] = index;
            return entry with { UpdatedAt = Now };
        }).ToArray();
        var projection = Update(new HistoryPresentationCache(), history);
        var overview = projection.Metrics(Profile);
        var detail = projection.Metrics(Profile, chronological: true);

        Assert.Equal(HistoryPresentation.CalculateMetrics(Profile, projection.ForSpot(Profile.SpotId)), overview);
        Assert.Equal(HistoryPresentation.CalculateMetrics(Profile, projection.Sessions), detail);
        Assert.NotEqual(overview.BestFiveHourAverageSilverPerHour, detail.BestFiveHourAverageSilverPerHour);
    }

    [Fact]
    public void EntryBudgetReturnsAllSessionsAndMetricsWithoutRetainingTheOversizedView()
    {
        var history = Enumerable.Range(0, HistoryPresentationCache.MaximumCachedEntries + 1)
            .Select(index => Entry(index + 1, Now.AddMinutes(-index)) with
            {
                CharacterClass = index == 0 ? "Ranger" : "Drakania",
            }).ToArray();
        var cache = new HistoryPresentationCache();

        var oversized = Update(cache, history);

        Assert.Equal(history.Length, oversized.Sessions.Length);
        Assert.Equal(history.OrderByDescending(entry => entry.StartedAt).Select(entry => entry.SessionId),
            oversized.Sessions.Select(entry => entry.SessionId));
        Assert.Equal(history.Sum(entry => entry.Totals[Profile.TrashItemName]) * 100m,
            oversized.Metrics(Profile).TotalSilver);
        Assert.Equal(HistoryPresentation.CalculateMetrics(Profile, oversized.ForSpot(Profile.SpotId)),
            oversized.Metrics(Profile));
        Assert.Equal(HistoryPresentation.BuildChartData(Profile, oversized.Sessions).CumulativeSilver,
            oversized.Chart(Profile).CumulativeSilver);
        Assert.Equal(0, cache.CachedEntryCount);
        Assert.Equal(0, cache.EstimatedRetainedBytes);
        Assert.NotSame(oversized, Update(cache, history));
        Assert.All(history, entry => Assert.Equal(0, entry.SilverAfterTax));

        var reduced = cache.Update(history, Prices, SilverTaxOptions.Default, null, 0, "Ranger", "", "de", Now);
        Assert.Single(reduced.Sessions);
        Assert.Equal(1, cache.CachedEntryCount);
        Assert.InRange(cache.EstimatedRetainedBytes, 1, HistoryPresentationCache.MaximumEstimatedRetainedBytes);
        Assert.Same(reduced, cache.Update(history, Prices, SilverTaxOptions.Default, null, 0, "Ranger", "", "de", Now));
    }

    [Fact]
    public void EstimatedByteBudgetIncludesLargeTotalsKeysAndSharedSourceJournals()
    {
        var largeKey = new string('x', (int)(HistoryPresentationCache.MaximumEstimatedRetainedBytes / 2));
        var largeTotals = Entry(10);
        largeTotals.Totals[largeKey] = 1;
        var largeJournal = Entry(20) with
        {
            DropHistory = Enumerable.Repeat(new SessionDropSample(TimeSpan.Zero, Profile.TrashItemName, 1),
                (int)(HistoryPresentationCache.MaximumEstimatedRetainedBytes / 128) + 1).ToArray(),
        };
        var cache = new HistoryPresentationCache();

        foreach (var source in new[] { largeTotals, largeJournal })
        {
            var projection = Update(cache, [source]);
            var session = Assert.Single(projection.Sessions);
            var valuation = SilverValuation.Calculate(source.Totals, Prices);
            Assert.Equal(source.SessionId, session.SessionId);
            Assert.Equal(source.Totals, session.Totals);
            Assert.Equal(valuation.AfterTax, session.SilverAfterTax);
            Assert.Equal(valuation.IsComplete, session.SilverIsComplete);
            Assert.Equal(HistoryPresentation.CalculateMetrics(Profile, projection.Sessions), projection.Metrics(Profile));
            Assert.Same(source.DropHistory, session.DropHistory);
            Assert.Equal(0, cache.CachedEntryCount);
            Assert.Equal(0, cache.EstimatedRetainedBytes);
        }
    }

    private static HistoryProjection Update(HistoryPresentationCache cache, IReadOnlyList<LootHistoryEntry> history,
        LootPriceSnapshot? prices = null, SilverTaxOptions? tax = null, int days = 0, DateTimeOffset? now = null) =>
        cache.Update(history, prices ?? Prices, tax ?? SilverTaxOptions.Default, null, days, "", "", "de", now ?? Now);

    private static LootHistoryEntry Entry(long trash, DateTimeOffset? startedAt = null) => new()
    {
        SessionId = Guid.NewGuid(), StartedAt = startedAt ?? Now.AddHours(-1), UpdatedAt = Now,
        Duration = TimeSpan.FromHours(1), SpotId = Profile.SpotId, CharacterClass = "Drakania",
        Totals = new(StringComparer.OrdinalIgnoreCase) { [Profile.TrashItemName] = trash },
        SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false,
    };
}
