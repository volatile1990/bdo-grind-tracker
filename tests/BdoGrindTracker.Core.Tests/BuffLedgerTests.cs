using System.Text.Json;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.Core.Tests;

public sealed class BuffLedgerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly BuffDefinition Buff = new("draught", "Draught", 123, TimeSpan.FromMinutes(60));
    private static readonly BuffPrice Price = new(360_000m, "EU", Start, false);

    [Fact]
    public void ExistingBuffNeedsConfirmationAndOnlyChargesObservedRunningTime()
    {
        var ledger = CreateLedger();
        var initial = Apply(ledger, 0, 1800);
        Assert.Empty(initial.Active);
        Assert.Empty(initial.Consumptions);
        Assert.Empty(initial.Usage);

        var confirmed = Apply(ledger, 10, 1790);
        Assert.True(Assert.Single(confirmed.Active).IsBaseline);
        Assert.Empty(confirmed.Consumptions);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(confirmed.Usage).ObservedDuration);
        Assert.Equal(1000m, confirmed.ProratedCost);
        Assert.Equal(0m, confirmed.ConsumedCost);
    }

    [Fact]
    public void NewlyAppearingBuffBooksOneItemAfterTwoReadings()
    {
        var ledger = CreateLedger();
        ApplyEmpty(ledger, 0);
        Assert.Empty(Apply(ledger, 10, 3600).Consumptions);
        var confirmed = Apply(ledger, 20, 3590);
        Assert.False(Assert.Single(confirmed.Active).IsBaseline);
        var consumption = Assert.Single(confirmed.Consumptions);
        Assert.Equal(Start.AddSeconds(10), consumption.ConsumedAt);
        Assert.Equal(360_000m, consumption.Cost);
        Assert.Equal(1000m, confirmed.ProratedCost);

        Assert.Single(Apply(ledger, 30, 3580).Consumptions);
    }

    [Fact]
    public void RecastRequiresTwoConsistentReadingsAndCapturesNewPrice()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        Apply(ledger, 10, 110);
        var newPrice = Price with { UnitPrice = 720_000m, IsStale = true };
        Assert.Empty(Apply(ledger, 20, 3600, newPrice).Consumptions);
        var result = Apply(ledger, 30, 3590, Price);
        Assert.Equal(newPrice, Assert.Single(result.Consumptions).Price);
        Assert.Equal(3000m, result.ProratedCost);
        Assert.Equal(720_000m, result.ConsumedCost);
    }

    [Fact]
    public void MinuteTimerRoundingAndJitterDoNotCountAsRecasts()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 1800, precisionSeconds: 60);
        Apply(ledger, 10, 1800, precisionSeconds: 60);
        Apply(ledger, 20, 1740, precisionSeconds: 60);
        var result = Apply(ledger, 30, 1800, precisionSeconds: 60);
        Assert.Empty(result.Consumptions);
        // The backwards timer jump is an unconfirmed candidate, so this ambiguous interval is not charged.
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void RoundedConstantTimerAccruesElapsedTimeWithoutExtraPurchases()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 1800, precisionSeconds: 60);
        Apply(ledger, 10, 1800, precisionSeconds: 60);
        Apply(ledger, 20, 1800, precisionSeconds: 60);
        var result = Apply(ledger, 30, 1800, precisionSeconds: 60);
        Assert.Empty(result.Consumptions);
        Assert.Equal(3000m, result.ProratedCost);
    }

    [Fact]
    public void SingleTimerSpikeDoesNotConsumeOrChargeAmbiguousIntervals()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        Apply(ledger, 10, 110);
        Apply(ledger, 20, 3600);
        Apply(ledger, 30, 90);
        var result = Apply(ledger, 40, 80);
        Assert.Empty(result.Consumptions);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void MissingObservationRequiresReconfirmationAndDoesNotChargeGap()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 1800);
        Apply(ledger, 10, 1790);
        Assert.Empty(ApplyEmpty(ledger, 20).Active);
        Apply(ledger, 30, 1770);
        var result = Apply(ledger, 40, 1760);
        Assert.Empty(result.Consumptions);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void MissingInitialConfirmationDoesNotTurnSessionStartBuffIntoConsumption()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 3600);
        ApplyEmpty(ledger, 10);
        Apply(ledger, 20, 3580);
        var result = Apply(ledger, 30, 3570);
        Assert.Empty(result.Consumptions);
        Assert.True(Assert.Single(result.Active).IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void InitialBaselineEvidenceStillAllowsClearlyRenewedTimerAfterMissingReading()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 60);
        ApplyEmpty(ledger, 10);
        Apply(ledger, 20, 3600);
        var result = Apply(ledger, 30, 3590);
        Assert.Single(result.Consumptions);
        Assert.False(Assert.Single(result.Active).IsBaseline);
    }

    [Fact]
    public void ReappearanceWithClearlyRenewedTimerCountsAsConsumption()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 60);
        Apply(ledger, 10, 50);
        ApplyEmpty(ledger, 20);
        Apply(ledger, 30, 3600);
        var result = Apply(ledger, 40, 3590);
        Assert.Single(result.Consumptions);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void LongCaptureGapStartsBaselineWithoutChargingOfflineTime()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        Apply(ledger, 10, 110);
        Apply(ledger, 300, 3600);
        var result = Apply(ledger, 310, 3590);
        Assert.Empty(result.Consumptions);
        Assert.True(Assert.Single(result.Active).IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void ExplicitBreakPreventsChargingPauseOrInferringUnseenConsumption()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        Apply(ledger, 10, 110);
        ledger.BreakContinuity();
        Assert.Empty(ledger.Snapshot.Active);
        Apply(ledger, 20, 3600);
        var result = Apply(ledger, 30, 3590);
        Assert.Empty(result.Consumptions);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void MissingPriceRemainsUnknownForPastConsumptionAndPastRunningTime()
    {
        var ledger = CreateLedger();
        ApplyEmpty(ledger, 0);
        ledger.Apply([Observation(3600)], Start.AddSeconds(10), _ => null);
        var result = Apply(ledger, 20, 3590);
        Assert.Null(result.ConsumedCost);
        Assert.Null(result.ProratedCost);
        Assert.Equal(0m, result.KnownConsumedCost);
        Assert.Equal(0m, result.KnownProratedCost);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(result.Usage).UnpricedDuration);
        Assert.Null(Assert.Single(result.Consumptions).Price);
        Assert.Equal(Price, Assert.Single(result.Active).Price);

        var later = Apply(ledger, 30, 3580);
        Assert.Null(later.ConsumedCost);
        Assert.Null(later.ProratedCost);
        Assert.Equal(1000m, later.KnownProratedCost);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(later.Usage).UnpricedDuration);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(later.Usage).ObservedDuration);
    }

    [Fact]
    public void PricesAreCapturedPerCycleAndNeverRetroactivelyRevalued()
    {
        var ledger = CreateLedger();
        ApplyEmpty(ledger, 0);
        Apply(ledger, 10, 3600);
        Apply(ledger, 20, 3590, Price with { UnitPrice = 1 });
        var result = Apply(ledger, 30, 3580, Price with { UnitPrice = 2 });
        Assert.Equal(360_000m, Assert.Single(result.Consumptions).Cost);
        Assert.Equal(2000m, result.ProratedCost);
    }

    [Fact]
    public void LateFirstDetectionWithPartialTimerIsBaseline()
    {
        var ledger = CreateLedger();
        ApplyEmpty(ledger, 0);
        Apply(ledger, 10, 1800);
        var result = Apply(ledger, 20, 1790);
        Assert.Empty(result.Consumptions);
        Assert.True(Assert.Single(result.Active).IsBaseline);
    }

    [Fact]
    public void DuplicateAndOutOfOrderFramesCannotConfirmOrCharge()
    {
        var ledger = CreateLedger();
        Apply(ledger, 10, 1800);
        Assert.Empty(Apply(ledger, 10, 1800).Active);
        Assert.Empty(Apply(ledger, 0, 1810).Active);
        var result = Apply(ledger, 20, 1790);
        Assert.Equal(1000m, result.ProratedCost);
    }

    [Fact]
    public void DuplicateBuffIdentitiesAndInvalidObservationsAreNotCounted()
    {
        var ledger = CreateLedger();
        ApplyEmpty(ledger, 0);
        var invalid = new[] { Observation(3600), Observation(3600), new BuffObservation("unknown", TimeSpan.FromMinutes(60), TimeSpan.FromSeconds(1)) };
        ledger.Apply(invalid, Start.AddSeconds(10), _ => Price);
        var result = ledger.Apply(invalid, Start.AddSeconds(20), _ => Price);
        Assert.Empty(result.Active);
        Assert.Empty(result.Consumptions);
        Assert.Empty(result.Usage);
    }

    [Fact]
    public void RestoreRoundTripsTotalsAndDoesNotResumePersistedTimers()
    {
        var ledger = CreateLedger();
        ApplyEmpty(ledger, 0);
        Apply(ledger, 10, 3600);
        var snapshot = Apply(ledger, 20, 3590);
        var serialized = JsonSerializer.Serialize(snapshot);
        var restored = CreateLedger();
        restored.Restore(JsonSerializer.Deserialize<BuffLedgerSnapshot>(serialized)!);
        Assert.Empty(restored.Snapshot.Active);
        Assert.Equal(snapshot.ConsumedCost, restored.Snapshot.ConsumedCost);
        Assert.Equal(snapshot.ProratedCost, restored.Snapshot.ProratedCost);
        Apply(restored, 300, 3300);
        var result = Apply(restored, 310, 3290);
        Assert.Single(result.Consumptions);
        Assert.Equal(2000m, result.ProratedCost);
        Assert.Equal(1000m, snapshot.ProratedCost);
    }

    [Theory]
    [InlineData("null-list")]
    [InlineData("negative-price")]
    [InlineData("unknown-id")]
    [InlineData("negative-duration")]
    [InlineData("negative-cost")]
    [InlineData("duplicate-usage")]
    [InlineData("unpriced-exceeds-total")]
    [InlineData("invalid-market-id")]
    public void InvalidRestoreIsRejectedWithoutChangingLiveLedger(string invalidCase)
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 1800);
        var before = Apply(ledger, 10, 1790);
        var usage = Assert.Single(before.Usage);
        var invalid = invalidCase switch
        {
            "null-list" => before with { Consumptions = null! },
            "negative-price" => before with { Consumptions = [new(Buff.Id, Buff.Name, Buff.MarketItemId, Start, Price with { UnitPrice = -1 })] },
            "unknown-id" => before with { Usage = [usage with { BuffId = "unknown" }] },
            "negative-duration" => before with { Usage = [usage with { ObservedDuration = TimeSpan.FromSeconds(-1) }] },
            "negative-cost" => before with { Usage = [usage with { KnownProratedCost = -1 }] },
            "duplicate-usage" => before with { Usage = [usage, usage] },
            "unpriced-exceeds-total" => before with { Usage = [usage with { UnpricedDuration = TimeSpan.FromHours(1) }] },
            "invalid-market-id" => before with { Usage = [usage with { MarketItemId = -1 }] },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
        };
        Assert.Throws<ArgumentException>(() => ledger.Restore(invalid));
        Assert.Equal(before.ProratedCost, ledger.Snapshot.ProratedCost);
        Assert.Single(ledger.Snapshot.Active);
        Assert.Equal(2000m, Apply(ledger, 20, 1780).ProratedCost);
    }

    [Fact]
    public void CachedPriceProvenanceSurvivesBuffDisappearanceAndRestore()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 1800, Price with { IsStale = true });
        Apply(ledger, 10, 1790);
        var snapshot = ApplyEmpty(ledger, 20);
        Assert.True(snapshot.HasStalePrices);
        Assert.Empty(snapshot.Active);
        Assert.Empty(snapshot.Consumptions);
        var restored = CreateLedger();
        restored.Restore(snapshot);
        Assert.True(restored.Snapshot.HasStalePrices);
    }

    [Fact]
    public void ResetClearsHistoryAndStartsNewBaseline()
    {
        var ledger = CreateLedger();
        ApplyEmpty(ledger, 0);
        Apply(ledger, 10, 3600);
        Apply(ledger, 20, 3590);
        ledger.Reset();
        Assert.Empty(ledger.Snapshot.Active);
        Assert.Empty(ledger.Snapshot.Consumptions);
        Assert.Empty(ledger.Snapshot.Usage);
        Apply(ledger, 30, 3580);
        Assert.Empty(Apply(ledger, 40, 3570).Consumptions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartyBuffsOnlyTrackCostsWhenOwnConsumptionWasConfigured(bool ownConsumption)
    {
        var party = Buff with { RequiresConsumptionConfirmation = true };
        var ledger = new BuffLedger([party]);
        ApplyEmpty(ledger, 0);
        BuffObservation Reading(int seconds) => Observation(seconds) with
        { ConsumptionAttributionConfirmed = ownConsumption };
        ledger.Apply([Reading(3600)], Start.AddSeconds(10), _ => Price);
        var snapshot = ledger.Apply([Reading(3590)], Start.AddSeconds(20), _ => Price);
        if (ownConsumption)
        {
            Assert.Equal(Price.UnitPrice, Assert.Single(snapshot.Consumptions).Cost);
            Assert.Equal(1000m, snapshot.ProratedCost);
        }
        else
        {
            Assert.Empty(snapshot.Consumptions);
            Assert.Empty(snapshot.Usage);
            Assert.Empty(snapshot.Active);
        }
    }

    [Fact]
    public void NpcPriceProvenanceAndFullPurchaseCostSurviveRoundTrip()
    {
        var ledger = CreateLedger();
        var npc = Price with { Source = BuffPriceSource.FixedNpc, Region = "NPC", FetchedAt = null };
        ApplyEmpty(ledger, 0);
        Apply(ledger, 10, 3600, npc);
        var snapshot = Apply(ledger, 20, 3590, npc);
        var restored = CreateLedger();
        restored.Restore(JsonSerializer.Deserialize<BuffLedgerSnapshot>(JsonSerializer.Serialize(snapshot))!);
        Assert.Equal(npc, Assert.Single(restored.Snapshot.Consumptions).Price);
        Assert.Equal(npc.UnitPrice, restored.Snapshot.ConsumedCost);
        Assert.Equal(1000m, restored.Snapshot.ProratedCost);
    }

    private static BuffLedger CreateLedger() => new([Buff]);

    private static BuffObservation Observation(int remainingSeconds, int precisionSeconds = 1) =>
        new(Buff.Id, TimeSpan.FromSeconds(remainingSeconds), TimeSpan.FromSeconds(precisionSeconds));

    private static BuffLedgerSnapshot Apply(BuffLedger ledger, int elapsedSeconds, int remainingSeconds,
        BuffPrice? price = null, int precisionSeconds = 1) =>
        ledger.Apply([Observation(remainingSeconds, precisionSeconds)], Start.AddSeconds(elapsedSeconds), _ => price ?? Price);

    private static BuffLedgerSnapshot ApplyEmpty(BuffLedger ledger, int elapsedSeconds) =>
        ledger.Apply([], Start.AddSeconds(elapsedSeconds), _ => Price);
}
