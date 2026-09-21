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
    [InlineData("tent-body-enhancement", 1, 300, 10_000_000)]
    [InlineData("tent-turning-gates", 280, 300, 2_000_000)]
    [InlineData("tent-turning-gates", 160, 300, 2_000_000)]
    [InlineData("tent-turning-gates", 1, 300, 2_000_000)]
    [InlineData("tent-adventures-boon", 160, 300, 12_000_000)]
    [InlineData("tent-adventures-boon", 110, 300, 12_000_000)]
    [InlineData("tent-adventures-boon", 1, 300, 12_000_000)]
    public void BoonAndVillaAlwaysSelectTheMaximumPurchaseAndItsNpcPrice(
        string family, int remainingMinutes, int purchasedMinutes, int price)
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        BuffObservation[] observations = [new("automatic-" + family,
            TimeSpan.FromMinutes(remainingMinutes), TimeSpan.FromMinutes(1))];

        ledger.Apply(observations, at, Price);
        var confirmed = ledger.Apply(observations, at.AddSeconds(10), Price);

        var active = Assert.Single(confirmed.Active);
        Assert.Equal($"{family}-{purchasedMinutes}", active.BuffId);
        Assert.Equal(price, active.Price!.UnitPrice);
        Assert.Equal(BuffPriceSource.FixedNpc, active.Price.Source);
        var initial = Assert.Single(confirmed.Consumptions);
        Assert.True(initial.IsSessionStart);
        Assert.Equal(active.BuffId, initial.BuffId);
        Assert.Equal(price, initial.Cost);
        Assert.Equal(price * 10m / (purchasedMinutes * 60), Assert.Single(confirmed.Usage).ProratedCost);
    }

    [Theory]
    [InlineData("tent-body-enhancement", 10_000_000)]
    [InlineData("tent-turning-gates", 2_000_000)]
    [InlineData("tent-adventures-boon", 12_000_000)]
    public void DifferentStartingAndRenewalDurationsStackInOneConsumptionTile(string family, int price)
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        BuffObservation[] Reading(int minutes) => [new("automatic-" + family,
            TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(1))];
        ledger.Apply(Reading(20), at, Price);
        ledger.Apply(Reading(20), at.AddSeconds(10), Price);
        ledger.Apply(Reading(160), at.AddSeconds(20), Price);
        var result = ledger.Apply(Reading(280), at.AddSeconds(30), Price);

        Assert.Equal(3, result.Consumptions.Count);
        Assert.Single(result.Consumptions, item => item.IsSessionStart);
        Assert.All(result.Consumptions, item => Assert.Equal(family + "-300", item.BuffId));
        var tile = Assert.Single(BdoGrindTracker.App.Components.ConsumablesPresentation.Create(result, "en").Items);
        Assert.Equal(family + "-300", tile.Id);
        Assert.Equal(3, tile.Count);
        Assert.Equal(price * 3m, tile.KnownCost);
    }

    [Theory]
    [InlineData("tent-body-enhancement", 10_000_000)]
    [InlineData("tent-turning-gates", 2_000_000)]
    [InlineData("tent-adventures-boon", 12_000_000)]
    public void RestoringAShorterPurchaseKeepsItsRecordedCostAndOnlyPricesTheNextRenewalAtMaximumDuration(
        string family, int renewalPrice)
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

        var renewed = ledger.Apply(Reading(160), at.AddSeconds(20), Price);
        Assert.Equal(previous, renewed.Consumptions[0]);
        var purchase = Assert.Single(renewed.Consumptions, item => !item.IsSessionStart);
        Assert.Equal(family + "-300", purchase.BuffId);
        Assert.Equal(renewalPrice, purchase.Cost);
        Assert.Equal(123_456m + renewalPrice, renewed.ConsumedCost);
    }

    [Fact]
    public void CountdownAcrossAPurchaseBoundaryKeepsTheInitiallyAssignedDurationAndPrice()
    {
        var ledger = CreateLedger();
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        BuffObservation[] Reading(int minutes) => [new("automatic-tent-body-enhancement",
            TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(1))];

        ledger.Apply(Reading(181), at, Price);
        ledger.Apply(Reading(181), at.AddSeconds(10), Price);
        var result = ledger.Apply(Reading(180), at.AddSeconds(20), Price);

        Assert.Equal("tent-body-enhancement-300", Assert.Single(result.Active).BuffId);
        Assert.Equal(10_000_000m, Assert.Single(result.Active).Price!.UnitPrice);
        Assert.Equal("tent-body-enhancement-300", Assert.Single(result.Usage).BuffId);
        var initial = Assert.Single(result.Consumptions);
        Assert.True(initial.IsSessionStart);
        Assert.Equal("tent-body-enhancement-300", initial.BuffId);
        Assert.Equal(10_000_000m, initial.Cost);
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

        ledger.Apply(readings, at, Price);
        var result = ledger.Apply(readings, at.AddSeconds(10), Price);

        Assert.Equal(2, result.Active.Count);
        Assert.All(result.Active, item => Assert.Null(item.Price));
        Assert.All(result.Active, item => Assert.StartsWith("automatic-", item.BuffId));
        Assert.Equal(2, result.Consumptions.Count);
        Assert.All(result.Consumptions, item =>
        {
            Assert.True(item.IsSessionStart);
            Assert.Null(item.Cost);
        });
        Assert.Null(result.ConsumedCost);
    }

    private static BuffLedger CreateLedger() => new(BuffPriceCatalog.HistoryDefinitions
        .Concat(AutomaticBuffCatalog.Default.HistoricalGroupDefinitions));
    private static BuffPrice? Price(BuffDefinition definition) => BuffPriceCatalog.GetPrice(definition, new("eu", []));
}
