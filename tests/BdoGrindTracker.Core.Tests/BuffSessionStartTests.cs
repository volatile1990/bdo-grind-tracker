using System.Text.Json;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.Core.Tests;

public sealed class BuffSessionStartTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly BuffDefinition First = new("first", "First", 101, TimeSpan.FromHours(1));
    private static readonly BuffDefinition Second = new("second", "Second", 102, TimeSpan.FromHours(1));
    private static readonly BuffPrice Price = new(1_200_000m, "eu", Start, false);

    [Fact]
    public void AllReadableInitialBuffsCountOnceOnlyAfterTwoMatchingFrames()
    {
        var ledger = Create();
        Assert.Empty(Apply(ledger, 0, Observe(First, 1800), Observe(Second, 900)).Consumptions);
        var confirmed = Apply(ledger, 10, Observe(First, 1790), Observe(Second, 890));
        var later = Apply(ledger, 20, Observe(First, 1780), Observe(Second, 880));

        Assert.Equal(2, confirmed.Consumptions.Count);
        Assert.Equal(new[] { First.Id, Second.Id }, confirmed.Consumptions.Select(item => item.BuffId));
        Assert.All(confirmed.Consumptions, item =>
        {
            Assert.True(item.IsSessionStart);
            Assert.Equal(Start, item.ConsumedAt);
            Assert.Equal(Price, item.Price);
        });
        Assert.Equal(2_400_000m, later.ConsumedCost);
        Assert.Equal(confirmed.Consumptions, later.Consumptions);
    }

    [Fact]
    public void InitiallyUnknownIdentityCountsOnceWhenItsOwnFirstCountdownIsConfirmed()
    {
        var ledger = Create();
        ledger.Apply([Observe(First, 1800)], Start, _ => Price, [Second.Id]);
        Apply(ledger, 10, Observe(First, 1790), Observe(Second, 900));
        var baseline = Apply(ledger, 20, Observe(First, 1780), Observe(Second, 890));

        Assert.Equal(2, baseline.Consumptions.Count);
        Assert.All(baseline.Consumptions, item => Assert.True(item.IsSessionStart));
        Assert.Equal(Start.AddSeconds(10), baseline.Consumptions.Single(item => item.BuffId == Second.Id).ConsumedAt);
        Assert.Equal(2, baseline.Active.Count);

        Apply(ledger, 30, Observe(First, 1770), Observe(Second, 3600));
        var renewed = Apply(ledger, 40, Observe(First, 1760), Observe(Second, 3590));
        Assert.Equal(3, renewed.Consumptions.Count);
        Assert.Equal(Second.Id, Assert.Single(renewed.Consumptions, item => !item.IsSessionStart).BuffId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialEmptyOrUnknownOnlyScanDoesNotPreventLaterInitialAccounting(bool unknownOnly)
    {
        var ledger = Create();
        ledger.Apply([], Start, _ => Price, unknownOnly ? [First.Id] : []);
        Apply(ledger, 10, Observe(First, 1800));

        var result = Apply(ledger, 20, Observe(First, 1790));

        Assert.True(Assert.Single(result.Consumptions).IsSessionStart);
        Assert.True(Assert.Single(result.Active).IsBaseline);
    }

    [Fact]
    public void InitiallyDuplicateIdentityWaitsUntilItsOwnReadableCountdownIsConfirmed()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 1800), Observe(First, 1800), Observe(Second, 900));
        Apply(ledger, 10, Observe(First, 1790), Observe(Second, 890));

        var result = Apply(ledger, 20, Observe(First, 1780), Observe(Second, 880));

        Assert.Equal(2, result.Consumptions.Count);
        Assert.Equal(new[] { First.Id, Second.Id }, result.Consumptions.Select(item => item.BuffId).Order());
        Assert.All(result.Consumptions, item => Assert.True(item.IsSessionStart));
    }

    [Fact]
    public void PauseBeforeOrAfterInitialConfirmationNeverDuplicatesAnInitialCharge()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 1800));
        ledger.BreakContinuity();
        Apply(ledger, 100, Observe(First, 1700), Observe(Second, 900));
        var initial = Apply(ledger, 110, Observe(First, 1690), Observe(Second, 890));
        ledger.BreakContinuity();
        Apply(ledger, 200, Observe(First, 1600), Observe(Second, 800));

        var result = Apply(ledger, 210, Observe(First, 1590), Observe(Second, 790));

        Assert.Equal(2, result.Consumptions.Count);
        Assert.All(result.Consumptions, item => Assert.True(item.IsSessionStart));
        Assert.Equal(initial.Consumptions, result.Consumptions);
        Assert.All(result.Usage, item => Assert.Equal(TimeSpan.FromSeconds(20), item.ObservedDuration));
    }

    [Fact]
    public void ConfirmedRefreshBeforeInitialBaselineClosesStartupWithoutChargingBoth()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 60));
        Apply(ledger, 10, Observe(First, 3600));
        var refreshed = Apply(ledger, 20, Observe(First, 3590));
        ledger.BreakContinuity();
        Apply(ledger, 30, Observe(First, 3580));

        var result = Apply(ledger, 40, Observe(First, 3570));

        Assert.False(Assert.Single(refreshed.Consumptions).IsSessionStart);
        Assert.Equal(Start.AddSeconds(10), Assert.Single(result.Consumptions).ConsumedAt);
        Assert.Equal(refreshed.Consumptions, result.Consumptions);
    }

    [Fact]
    public void InitialUnknownPriceIsNotRetroactivelyReplacedByLaterQuotes()
    {
        var ledger = Create();
        ledger.Apply([Observe(First, 1800)], Start, _ => null);
        Apply(ledger, 10, Observe(First, 1790));

        var result = Apply(ledger, 20, Observe(First, 1780));

        Assert.True(Assert.Single(result.Consumptions).IsSessionStart);
        Assert.Null(Assert.Single(result.Consumptions).Price);
        Assert.Null(result.ConsumedCost);
        Assert.Equal(Price, Assert.Single(result.Active).Price);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(result.Usage).UnpricedDuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoredHistoryOnlyClosesInitialAccountingForAlreadyBookedBuffs(bool withHistory)
    {
        var ledger = Create();
        var old = new BuffConsumption(First.Id, First.Name, First.MarketItemId, Start.AddDays(-1), Price with { UnitPrice = 7 });
        var history = withHistory ? new BuffLedgerSnapshot([old], [], []) : BuffLedgerSnapshot.Empty;
        ledger.Restore(history);
        Apply(ledger, 0, Observe(First, 1800), Observe(Second, 900));

        var result = Apply(ledger, 10, Observe(First, 1790), Observe(Second, 890));

        Assert.Equal(2, result.Consumptions.Count);
        if (withHistory)
        {
            Assert.Equal(old, result.Consumptions[0]);
            Assert.Equal(Second.Id, Assert.Single(result.Consumptions, item => item.IsSessionStart).BuffId);
        }
        else Assert.All(result.Consumptions, item => Assert.True(item.IsSessionStart));
        Assert.Equal(withHistory ? 1_200_007m : 2_400_000m, result.ConsumedCost);
    }

    [Fact]
    public void LegacyObservedBuffWithoutAConsumptionCanBeAccountedOnceAfterRestore()
    {
        var observed = new BuffUsage(First.Id, First.Name, First.MarketItemId,
            TimeSpan.FromMinutes(1), 20_000, TimeSpan.Zero);
        var active = new BuffActive(First.Id, First.Name, First.MarketItemId,
            TimeSpan.FromMinutes(15), Start, Price, true);
        var ledger = Create();
        ledger.Restore(new([], [observed], [active]));

        Assert.Empty(Apply(ledger, 100, Observe(First, 800)).Consumptions);
        var initial = Apply(ledger, 110, Observe(First, 790));
        var charge = Assert.Single(initial.Consumptions);
        Assert.True(charge.IsSessionStart);
        Assert.Equal(Start.AddSeconds(100), charge.ConsumedAt);
        Assert.Equal(Price, charge.Price);
        Assert.Equal(TimeSpan.FromSeconds(70), Assert.Single(initial.Usage).ObservedDuration);

        ledger.BreakContinuity();
        Apply(ledger, 120, Observe(First, 780));
        var paused = Apply(ledger, 130, Observe(First, 770));
        Assert.Equal(initial.Consumptions, paused.Consumptions);
        ledger.Restore(paused);
        Apply(ledger, 200, Observe(First, 700));
        var restored = Apply(ledger, 210, Observe(First, 690));
        Assert.Equal(initial.Consumptions, restored.Consumptions);
    }

    [Fact]
    public void EachLateBuffCountsOnlyAfterItsOwnTwoReadableFrames()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 1800));
        Apply(ledger, 10, Observe(First, 1790));
        var firstSeen = Apply(ledger, 120, Observe(First, 1680), Observe(Second, 900));
        Assert.Equal(First.Id, Assert.Single(firstSeen.Consumptions).BuffId);
        ledger.Apply([Observe(First, 1670)], Start.AddSeconds(130), _ => Price, [Second.Id]);
        Assert.Single(Apply(ledger, 140, Observe(First, 1660), Observe(Second, 880)).Consumptions);

        var confirmed = Apply(ledger, 150, Observe(First, 1650), Observe(Second, 870));

        Assert.Equal(2, confirmed.Consumptions.Count);
        Assert.Equal(Start.AddSeconds(140), confirmed.Consumptions.Single(item => item.BuffId == Second.Id).ConsumedAt);
        Assert.All(confirmed.Consumptions, item => Assert.True(item.IsSessionStart));
    }

    [Fact]
    public void StartMetadataRoundTripsWhileLegacyJsonDefaultsToAnOrdinaryHistoricalConsumption()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 1800));
        var original = Apply(ledger, 10, Observe(First, 1790));
        var restored = Create();
        restored.Restore(JsonSerializer.Deserialize<BuffLedgerSnapshot>(JsonSerializer.Serialize(original))!);
        Assert.True(Assert.Single(restored.Snapshot.Consumptions).IsSessionStart);
        Assert.Equal(original.Consumptions, restored.Snapshot.Consumptions);

        var legacy = JsonSerializer.Deserialize<BuffConsumption>("""
            {"BuffId":"first","Name":"First","MarketItemId":101,"ConsumedAt":"2026-09-20T12:00:00Z","Price":null}
            """)!;
        Assert.False(legacy.IsSessionStart);
    }

    [Fact]
    public void ResetAllowsEachBuffItsOwnInitialChargeInTheNewSession()
    {
        var ledger = Create();
        ledger.Restore(BuffLedgerSnapshot.Empty);
        ledger.Reset();
        Apply(ledger, 0, Observe(Second, 900));
        Apply(ledger, 10, Observe(First, 1800), Observe(Second, 890));

        var result = Apply(ledger, 20, Observe(First, 1790), Observe(Second, 880));

        Assert.Equal(2, result.Consumptions.Count);
        Assert.All(result.Consumptions, item => Assert.True(item.IsSessionStart));
    }

    private static BuffLedger Create() => new([First, Second]);
    private static BuffObservation Observe(BuffDefinition definition, int seconds) =>
        new(definition.Id, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1));
    private static BuffLedgerSnapshot Apply(BuffLedger ledger, int elapsedSeconds, params BuffObservation[] observations) =>
        ledger.Apply(observations, Start.AddSeconds(elapsedSeconds), _ => Price);
}
