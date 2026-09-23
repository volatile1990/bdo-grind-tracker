using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class AutomaticBuffDurationPricingTests
{
    [Theory]
    [InlineData("tent-body-enhancement", 280, 300, 10_000_000)]
    [InlineData("tent-body-enhancement", 160, 300, 10_000_000)]
    [InlineData("tent-body-enhancement", 120, 300, 10_000_000)]
    [InlineData("tent-body-enhancement", 91, 300, 10_000_000)]
    [InlineData("tent-body-enhancement", 90, 300, 10_000_000)]
    [InlineData("tent-body-enhancement", 61, 300, 10_000_000)]
    [InlineData("tent-body-enhancement", 60, 300, 10_000_000)]
    [InlineData("tent-turning-gates", 280, 300, 2_000_000)]
    [InlineData("tent-turning-gates", 160, 180, 900_000)]
    [InlineData("tent-turning-gates", 110, 120, 450_000)]
    [InlineData("tent-turning-gates", 80, 90, 300_000)]
    [InlineData("tent-turning-gates", 50, 60, 200_000)]
    [InlineData("tent-adventures-boon", 160, 300, 12_000_000)]
    [InlineData("tent-adventures-boon", 110, 300, 12_000_000)]
    [InlineData("tent-adventures-boon", 50, 300, 12_000_000)]
    public void RenewalsUseFixedFiveHourBoonAndBodyVariantsWhileTurningGatesRetainsDurationPricing(
        string family, int remainingMinutes, int purchasedMinutes, int price)
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        BuffObservation[] observations = [new("automatic-" + family,
            TimeSpan.FromMinutes(remainingMinutes), TimeSpan.FromMinutes(1))];

        ledger.Apply([new("automatic-" + family, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1))], at, Price);
        var renewed = ledger.Apply(observations, at.AddSeconds(10), Price);
        var confirmed = ledger.Apply(observations, at.AddSeconds(20), Price);

        var active = Assert.Single(confirmed.Active);
        Assert.Equal($"{family}-{purchasedMinutes}", active.BuffId);
        Assert.Equal(price, active.Price!.UnitPrice);
        Assert.Equal(BuffPriceSource.FixedNpc, active.Price.Source);
        var purchase = Assert.Single(confirmed.Consumptions);
        Assert.False(purchase.IsSessionStart);
        Assert.Equal(active.BuffId, purchase.BuffId);
        Assert.Equal(price, purchase.Cost);
        Assert.Equal(renewed.Consumptions, confirmed.Consumptions);
        Assert.Equal(price * 10m / (purchasedMinutes * 60), Assert.Single(confirmed.Usage).ProratedCost);
    }

    [Theory]
    [InlineData("tent-body-enhancement", 1, 300, 10_000_000)]
    [InlineData("tent-body-enhancement", 2, 300, 10_000_000)]
    [InlineData("tent-body-enhancement", 4, 300, 10_000_000)]
    [InlineData("tent-body-enhancement", 5, 300, 10_000_000)]
    [InlineData("tent-turning-gates", 2, 180, 900_000)]
    [InlineData("tent-turning-gates", 4, 300, 2_000_000)]
    [InlineData("tent-adventures-boon", 1, 300, 12_000_000)]
    [InlineData("tent-adventures-boon", 4, 300, 12_000_000)]
    [InlineData("tent-adventures-boon", 5, 300, 12_000_000)]
    public void FlooredHourRenewalRespectsFixedFiveHourVariantsAndTurningGatesDurationPricing(
        string family, int remainingHours, int purchasedMinutes, int price)
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        ledger.Apply([new("automatic-" + family, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(1))], at, Price);
        BuffObservation[] observations = [new("automatic-" + family,
            TimeSpan.FromHours(remainingHours), TimeSpan.FromHours(1))];
        ledger.Apply(observations, at.AddSeconds(10), Price);

        var result = ledger.Apply(observations, at.AddSeconds(20), Price);

        var consumption = Assert.Single(result.Consumptions);
        Assert.Equal(family + "-" + purchasedMinutes, consumption.BuffId);
        Assert.Equal(price, consumption.Cost);
        Assert.False(consumption.IsSessionStart);
        Assert.Equal(consumption.BuffId, Assert.Single(result.Active).BuffId);
    }

    [Theory]
    [InlineData("tent-turning-gates", false)]
    [InlineData("tent-turning-gates", true)]
    public void AmbiguousOneHourTurningGatesApplicationCountsWithoutAnArbitraryDurationOrPrice(string family, bool renewal)
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        ledger.Apply(renewal ? [new("automatic-" + family, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(1))] : [], at, Price);
        BuffObservation[] observations = [new("automatic-" + family, TimeSpan.FromHours(1), TimeSpan.FromHours(1))];
        ledger.Apply(observations, at.AddSeconds(10), Price);

        var result = ledger.Apply(observations, at.AddSeconds(20), Price);

        var consumption = Assert.Single(result.Consumptions);
        Assert.Equal("automatic-" + family, consumption.BuffId);
        Assert.Null(consumption.Cost);
        Assert.False(consumption.IsSessionStart);
        var tile = Assert.Single(BdoGrindTracker.App.Components.ConsumablesPresentation.Create(result, "en").Items);
        Assert.Equal(consumption.BuffId, tile.Id);
        Assert.Contains("variant unknown", tile.Name);
        Assert.Equal(1, tile.UnpricedCount);
    }

    [Theory]
    [InlineData("tent-turning-gates", 450_000, 2_000_000)]
    public void InitialTurningGatesBuffIsExcludedAndRenewalDurationsHaveSeparateConsumptionTiles(
        string family, int shortPrice, int longPrice)
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        BuffObservation[] Reading(int minutes) => [new("automatic-" + family,
            TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(1))];
        ledger.Apply(Reading(20), at, Price);
        ledger.Apply(Reading(20), at.AddSeconds(10), Price);
        ledger.Apply(Reading(110), at.AddSeconds(20), Price);
        var result = ledger.Apply(Reading(280), at.AddSeconds(30), Price);

        Assert.Equal(2, result.Consumptions.Count);
        Assert.All(result.Consumptions, item => Assert.False(item.IsSessionStart));
        var tiles = BdoGrindTracker.App.Components.ConsumablesPresentation.Create(result, "en").Items;
        Assert.Equal(2, tiles.Count);
        Assert.All(tiles, tile => Assert.Equal(1, tile.Count));
        Assert.Equal(shortPrice, Assert.Single(tiles, tile => tile.Id == family + "-120").KnownCost);
        Assert.Equal(longPrice, Assert.Single(tiles, tile => tile.Id == family + "-300").KnownCost);
        Assert.Equal(shortPrice + longPrice, result.ConsumedCost);
    }

    [Theory]
    [InlineData("tent-body-enhancement", 60, 1)]
    [InlineData("tent-body-enhancement", 90, 1)]
    [InlineData("tent-body-enhancement", 120, 1)]
    [InlineData("tent-body-enhancement", 180, 1)]
    [InlineData("tent-body-enhancement", 60, 60)]
    [InlineData("tent-adventures-boon", 60, 1)]
    [InlineData("tent-adventures-boon", 120, 1)]
    [InlineData("tent-adventures-boon", 60, 60)]
    public void FirstAppearanceAtAShorterDurationThresholdDoesNotInventAPurchase(
        string family, int remainingMinutes, int precisionMinutes)
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-23T12:00:00Z");
        BuffObservation[] readings = [new("automatic-" + family,
            TimeSpan.FromMinutes(remainingMinutes), TimeSpan.FromMinutes(precisionMinutes))];
        ledger.Apply([], at, Price);
        ledger.Apply(readings, at.AddSeconds(10), Price);

        var result = ledger.Apply(readings, at.AddSeconds(20), Price);

        Assert.Empty(result.Consumptions);
        var active = Assert.Single(result.Active);
        Assert.Equal(family + "-300", active.BuffId);
        Assert.True(active.IsBaseline);
    }

    [Theory]
    [InlineData("tent-body-enhancement", 10_000_000)]
    [InlineData("tent-adventures-boon", 12_000_000)]
    public void CountdownAcrossEveryShorterThresholdKeepsOneFiveHourPurchaseUntilATrueRefresh(
        string family, int price)
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-23T12:00:00Z");
        BuffObservation[] Reading(int minutes, int precisionMinutes) => [new("automatic-" + family,
            TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(precisionMinutes))];
        ledger.Apply([], at, Price);
        ledger.Apply(Reading(240, 60), at.AddSeconds(10), Price);
        var applied = ledger.Apply(Reading(240, 60), at.AddSeconds(20), Price);
        Assert.Equal(family + "-300", Assert.Single(applied.Consumptions).BuffId);

        foreach (var (elapsedMinutes, remainingMinutes, precisionMinutes) in new[]
        {
            (60, 180, 60), (120, 120, 60), (180, 60, 60),
            (181, 119, 1), (209, 91, 1), (210, 90, 1), (211, 89, 1),
            (239, 61, 1), (240, 60, 1), (241, 59, 1),
        })
        {
            var observedAt = at.AddMinutes(elapsedMinutes);
            var reading = Reading(remainingMinutes, precisionMinutes);
            ledger.Apply(reading, observedAt, Price);
            var countedDown = ledger.Apply(reading, observedAt.AddSeconds(10), Price);

            Assert.Equal(applied.Consumptions, countedDown.Consumptions);
            Assert.Equal(family + "-300", Assert.Single(countedDown.Active).BuffId);
            Assert.Equal(price, countedDown.ConsumedCost);
        }

        ledger.Apply(Reading(240, 60), at.AddMinutes(242), Price);
        var refreshed = ledger.Apply(Reading(240, 60), at.AddMinutes(242).AddSeconds(10), Price);

        Assert.Equal(2, refreshed.Consumptions.Count);
        Assert.All(refreshed.Consumptions, purchase =>
        {
            Assert.Equal(family + "-300", purchase.BuffId);
            Assert.Equal(price, purchase.Cost);
            Assert.False(purchase.IsSessionStart);
        });
        var tile = Assert.Single(BdoGrindTracker.App.Components.ConsumablesPresentation.Create(refreshed, "en").Items);
        Assert.Equal(2, tile.Count);
        Assert.Equal(price * 2m, tile.KnownCost);
    }

    [Theory]
    [InlineData("tent-body-enhancement", 300, 10_000_000)]
    [InlineData("tent-turning-gates", 120, 450_000)]
    [InlineData("tent-adventures-boon", 300, 12_000_000)]
    public void RestoringAShorterPurchaseKeepsItsRecordedCostAndUsesCurrentRecognitionForTheNextRenewal(
        string family, int renewalMinutes, int renewalPrice)
    {
        var ledger = CreateLedger();
        var old = BuffPriceCatalog.HistoryDefinitions.Single(item => item.Id == family + "-60");
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        var oldPrice = new BuffPrice(123_456m, "eu", at.AddHours(-1), true) { Source = BuffPriceSource.FixedNpc };
        var previous = new BuffConsumption(old.Id, old.Name, old.MarketItemId, at.AddMinutes(-30), oldPrice)
            { IsSessionStart = true };
        ledger.Restore(new([previous], [],
            [new(old.Id, old.Name, old.MarketItemId, TimeSpan.FromMinutes(30), at.AddSeconds(-10), oldPrice, true)]));
        BuffObservation[] Reading(int minutes) => [new("automatic-" + family,
            TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(1))];

        ledger.Apply(Reading(29), at, Price);
        var resumed = ledger.Apply(Reading(29), at.AddSeconds(10), Price);
        Assert.Equal(previous, Assert.Single(resumed.Consumptions));
        Assert.Equal(123_456m, resumed.ConsumedCost);

        var renewed = ledger.Apply(Reading(110), at.AddSeconds(20), Price);
        Assert.Equal(previous, renewed.Consumptions[0]);
        var purchase = Assert.Single(renewed.Consumptions, item => !item.IsSessionStart);
        Assert.Equal(family + "-" + renewalMinutes, purchase.BuffId);
        Assert.Equal(renewalPrice, purchase.Cost);
        Assert.Equal(123_456m + renewalPrice, renewed.ConsumedCost);
    }

    [Fact]
    public void CountdownAcrossAPurchaseBoundaryKeepsTheRenewalDurationAndPrice()
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        BuffObservation[] Reading(int minutes) => [new("automatic-tent-body-enhancement",
            TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(1))];

        ledger.Apply(Reading(20), at, Price);
        ledger.Apply(Reading(181), at.AddSeconds(10), Price);
        var result = ledger.Apply(Reading(180), at.AddSeconds(20), Price);

        Assert.Equal("tent-body-enhancement-300", Assert.Single(result.Active).BuffId);
        Assert.Equal(10_000_000m, Assert.Single(result.Active).Price!.UnitPrice);
        Assert.Equal("tent-body-enhancement-300", Assert.Single(result.Usage).BuffId);
        var purchase = Assert.Single(result.Consumptions);
        Assert.False(purchase.IsSessionStart);
        Assert.Equal("tent-body-enhancement-300", purchase.BuffId);
        Assert.Equal(10_000_000m, purchase.Cost);
    }

    [Fact]
    public void EqualDurationLuckAndPerfumeVariantsDoNotAcquireAnArbitraryPrice()
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        BuffObservation[] readings = [
            new("automatic-tent-adventurers-luck", TimeSpan.FromMinutes(51), TimeSpan.FromMinutes(1)),
            new("automatic-perfume-of-courage", TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(1)),
        ];

        ledger.Apply(readings.Select(item => item with { Remaining = TimeSpan.FromMinutes(1) }), at, Price);
        var result = ledger.Apply(readings, at.AddSeconds(10), Price);

        Assert.Equal(2, result.Active.Count);
        Assert.All(result.Active, item => Assert.Null(item.Price));
        Assert.All(result.Active, item => Assert.StartsWith("automatic-", item.BuffId));
        Assert.Equal(2, result.Consumptions.Count);
        Assert.All(result.Consumptions, item =>
        {
            Assert.False(item.IsSessionStart);
            Assert.Null(item.Cost);
        });
        Assert.Null(result.ConsumedCost);
    }

    private static BuffLedger CreateLedger() => new(BuffPriceCatalog.HistoryDefinitions
        .Concat(AutomaticBuffCatalog.Default.HistoricalGroupDefinitions));
    private static BuffPrice? Price(BuffDefinition definition) => BuffPriceCatalog.GetPrice(definition, new("eu", []));
}
