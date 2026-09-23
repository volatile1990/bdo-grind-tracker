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
    public void AllInitiallyActiveBuffsRemainUnchargedEvenWithFullTimers()
    {
        var ledger = Create();
        Assert.Empty(Apply(ledger, 0, Observe(First, 3600), Observe(Second, 900)).Consumptions);
        var confirmed = Apply(ledger, 10, Observe(First, 3590), Observe(Second, 890));
        var later = Apply(ledger, 20, Observe(First, 3580), Observe(Second, 880));

        Assert.Empty(confirmed.Consumptions);
        Assert.Equal(2, confirmed.Active.Count);
        Assert.All(confirmed.Active, item => Assert.True(item.IsBaseline));
        Assert.Empty(later.Consumptions);
        Assert.Equal(0m, later.ConsumedCost);
        Assert.All(later.Usage, item => Assert.Equal(TimeSpan.FromSeconds(20), item.ObservedDuration));
    }

    [Fact]
    public void InitiallyUnknownIdentityIsNotChargedWhenItsTimerBecomesReadable()
    {
        var ledger = Create();
        ledger.Apply([Observe(First, 1800)], Start, _ => Price, [Second.Id]);
        Apply(ledger, 10, Observe(First, 1790), Observe(Second, 3600));
        var baseline = Apply(ledger, 20, Observe(First, 1780), Observe(Second, 3590));
        Assert.Empty(baseline.Consumptions);
        Assert.Equal(2, baseline.Active.Count);

        var renewed = Apply(ledger, 30, Observe(First, 1770), Observe(Second, 3600));
        Assert.Equal(Second.Id, Assert.Single(renewed.Consumptions).BuffId);
        Assert.False(Assert.Single(renewed.Consumptions).IsSessionStart);
    }

    [Fact]
    public void ReadableEmptyBaselineAllowsANewApplicationToCountAfterConfirmation()
    {
        var ledger = Create();
        Apply(ledger, 0);
        Assert.Empty(Apply(ledger, 10, Observe(First, 3590)).Consumptions);
        var result = Apply(ledger, 20, Observe(First, 3580));

        var consumption = Assert.Single(result.Consumptions);
        Assert.Equal(Start.AddSeconds(10), consumption.ConsumedAt);
        Assert.False(consumption.IsSessionStart);
        Assert.Equal(Price, consumption.Price);
        Assert.False(Assert.Single(result.Active).IsBaseline);
        Assert.Equal(result.Consumptions, Apply(ledger, 30, Observe(First, 3570)).Consumptions);
    }

    [Fact]
    public void NewBuffDuringGrindCountsWhileInitialBuffStaysUncharged()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 1800));
        Apply(ledger, 10, Observe(First, 1790), Observe(Second, 3600));
        var result = Apply(ledger, 20, Observe(First, 1780), Observe(Second, 3590));

        Assert.Equal(Second.Id, Assert.Single(result.Consumptions).BuffId);
        Assert.True(result.Active.Single(item => item.BuffId == First.Id).IsBaseline);
        Assert.False(result.Active.Single(item => item.BuffId == Second.Id).IsBaseline);
    }

    [Fact]
    public void LatePartialFirstReadingCannotProveANewApplication()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 1800));
        Apply(ledger, 10, Observe(First, 1790), Observe(Second, 900));
        var result = Apply(ledger, 20, Observe(First, 1780), Observe(Second, 890));

        Assert.Empty(result.Consumptions);
        Assert.All(result.Active, item => Assert.True(item.IsBaseline));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    public void AmbiguousInitialIdentityDoesNotBecomeANewApplicationOnceReadable(string ambiguity)
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 3600), Observe(First, ambiguity == "duplicate" ? 3600 : 7200));
        Apply(ledger, 10, Observe(First, 3590));
        var result = Apply(ledger, 20, Observe(First, 3580));

        Assert.Empty(result.Consumptions);
        Assert.True(Assert.Single(result.Active).IsBaseline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewBuffAfterPauseOrLongCaptureGapNeedsAFreshBaseline(bool explicitPause)
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 1800));
        if (explicitPause) ledger.BreakContinuity();
        var elapsed = explicitPause ? 10 : 100;
        Apply(ledger, elapsed, Observe(First, 1800 - elapsed), Observe(Second, 3600));
        var result = Apply(ledger, elapsed + 10, Observe(First, 1790 - elapsed), Observe(Second, 3590));

        Assert.Empty(result.Consumptions);
        Assert.All(result.Active, item => Assert.True(item.IsBaseline));
    }

    [Fact]
    public void PauseBeforeOrAfterInitialConfirmationNeverChargesTheBaseline()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 1800));
        ledger.BreakContinuity();
        Apply(ledger, 100, Observe(First, 1700), Observe(Second, 900));
        Apply(ledger, 110, Observe(First, 1690), Observe(Second, 890));
        ledger.BreakContinuity();
        Apply(ledger, 200, Observe(First, 1600), Observe(Second, 800));
        var result = Apply(ledger, 210, Observe(First, 1590), Observe(Second, 790));

        Assert.Empty(result.Consumptions);
        Assert.All(result.Usage, item => Assert.Equal(TimeSpan.FromSeconds(20), item.ObservedDuration));
    }

    [Fact]
    public void RefreshBeforeBaselineConfirmationCountsOnlyTheNewUse()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 60));
        var refreshed = Apply(ledger, 10, Observe(First, 3600));
        Apply(ledger, 20, Observe(First, 3590));
        ledger.BreakContinuity();
        Apply(ledger, 30, Observe(First, 3580));
        var result = Apply(ledger, 40, Observe(First, 3570));

        Assert.False(Assert.Single(refreshed.Consumptions).IsSessionStart);
        Assert.Equal(Start.AddSeconds(10), Assert.Single(result.Consumptions).ConsumedAt);
        Assert.Equal(refreshed.Consumptions, result.Consumptions);
    }

    [Fact]
    public void UnknownPriceOfNewApplicationIsNotRetroactivelyReplaced()
    {
        var ledger = Create();
        Apply(ledger, 0);
        ledger.Apply([Observe(First, 3600)], Start.AddSeconds(10), _ => null);
        Apply(ledger, 20, Observe(First, 3590));
        var result = Apply(ledger, 30, Observe(First, 3580));

        Assert.False(Assert.Single(result.Consumptions).IsSessionStart);
        Assert.Null(Assert.Single(result.Consumptions).Price);
        Assert.Null(result.ConsumedCost);
        Assert.Equal(Price, Assert.Single(result.Active).Price);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(result.Usage).UnpricedDuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreKeepsHistoricalChargesWithoutChargingCurrentBuffs(bool withHistory)
    {
        var ledger = Create();
        var old = new BuffConsumption(First.Id, First.Name, First.MarketItemId, Start.AddDays(-1), Price with { UnitPrice = 7 })
            { IsSessionStart = true };
        ledger.Restore(withHistory ? new([old], [], []) : BuffLedgerSnapshot.Empty);
        Apply(ledger, 0, Observe(First, 1800), Observe(Second, 3600));
        var result = Apply(ledger, 10, Observe(First, 1790), Observe(Second, 3590));

        if (withHistory) Assert.Equal(old, Assert.Single(result.Consumptions));
        else Assert.Empty(result.Consumptions);
        Assert.Equal(withHistory ? 7m : 0m, result.ConsumedCost);
    }

    [Fact]
    public void RestoredUnchargedBaselineRemainsUnchargedUntilATimerIncrease()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 900));
        var saved = Apply(ledger, 10, Observe(First, 890));
        ledger.Restore(JsonSerializer.Deserialize<BuffLedgerSnapshot>(JsonSerializer.Serialize(saved))!);
        Apply(ledger, 100, Observe(First, 800));
        var resumed = Apply(ledger, 110, Observe(First, 790));
        Assert.Empty(resumed.Consumptions);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(resumed.Usage).ObservedDuration);

        var renewed = Apply(ledger, 120, Observe(First, 3600));
        Assert.Equal(Start.AddSeconds(120), Assert.Single(renewed.Consumptions).ConsumedAt);
    }

    [Fact]
    public void HistoricalStartMetadataRoundTripsAndLegacyJsonRemainsReadable()
    {
        var historical = new BuffConsumption(First.Id, First.Name, First.MarketItemId, Start, Price) { IsSessionStart = true };
        var original = new BuffLedgerSnapshot([historical], [], []);
        var restored = Create();
        restored.Restore(JsonSerializer.Deserialize<BuffLedgerSnapshot>(JsonSerializer.Serialize(original))!);
        Assert.Equal(historical, Assert.Single(restored.Snapshot.Consumptions));

        var legacy = JsonSerializer.Deserialize<BuffConsumption>("""
            {"BuffId":"first","Name":"First","MarketItemId":101,"ConsumedAt":"2026-09-20T12:00:00Z","Price":null}
            """)!;
        Assert.False(legacy.IsSessionStart);
    }

    [Fact]
    public void ResetStartsANewUnchargedBaseline()
    {
        var ledger = Create();
        Apply(ledger, 0, Observe(First, 60));
        Assert.Single(Apply(ledger, 10, Observe(First, 3600)).Consumptions);
        ledger.Reset();
        Apply(ledger, 20, Observe(First, 3590), Observe(Second, 3600));
        var result = Apply(ledger, 30, Observe(First, 3580), Observe(Second, 3590));

        Assert.Empty(result.Consumptions);
        Assert.All(result.Active, item => Assert.True(item.IsBaseline));
    }

    private static BuffLedger Create() => new([First, Second]);
    private static BuffObservation Observe(BuffDefinition definition, int seconds) =>
        new(definition.Id, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1));
    private static BuffLedgerSnapshot Apply(BuffLedger ledger, int elapsedSeconds, params BuffObservation[] observations) =>
        ledger.Apply(observations, Start.AddSeconds(elapsedSeconds), _ => Price);
}
