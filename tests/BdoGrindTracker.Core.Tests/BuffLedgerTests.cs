using System.Text.Json;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.Core.Tests;

public sealed class BuffLedgerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly BuffDefinition Buff = new("draught", "Draught", 123, TimeSpan.FromMinutes(60));
    private static readonly BuffPrice Price = new(360_000m, "EU", Start, false);

    [Fact]
    public void ExistingBuffNeedsConfirmationAndOnlyAccruesObservedRunningTime()
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
    public void FirstSeenFullBuffCountsOnceAfterItsOwnConfirmation()
    {
        var ledger = CreateLedger();
        ApplyEmpty(ledger, 0);
        Assert.Empty(Apply(ledger, 10, 3600).Consumptions);
        var confirmed = Apply(ledger, 20, 3590);
        Assert.False(Assert.Single(confirmed.Active).IsBaseline);
        var consumption = Assert.Single(confirmed.Consumptions);
        Assert.False(consumption.IsSessionStart);
        Assert.Equal(Start.AddSeconds(10), consumption.ConsumedAt);
        Assert.Equal(1000m, confirmed.ProratedCost);

        Assert.Equal(consumption, Assert.Single(Apply(ledger, 30, 3580).Consumptions));
    }

    [Fact]
    public void RecastCountsImmediatelyAndCapturesNewPrice()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        Apply(ledger, 10, 110);
        var newPrice = Price with { UnitPrice = 720_000m, IsStale = true };
        Assert.Single(Apply(ledger, 20, 3600, newPrice).Consumptions);
        var result = Apply(ledger, 30, 3590, Price);
        var consumption = Assert.Single(result.Consumptions);
        Assert.False(consumption.IsSessionStart);
        Assert.Equal(newPrice, consumption.Price);
        Assert.Equal(4000m, result.ProratedCost);
        Assert.Equal(720_000m, result.ConsumedCost);
    }

    [Fact]
    public void AnyReadMinuteTimerIncreaseCountsAsARecast()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 1800, precisionSeconds: 60);
        Apply(ledger, 10, 1800, precisionSeconds: 60);
        Apply(ledger, 20, 1740, precisionSeconds: 60);
        var result = Apply(ledger, 30, 1800, precisionSeconds: 60);
        Assert.Single(result.Consumptions);
        Assert.Single(result.Consumptions, item => !item.IsSessionStart);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(result.Usage).ObservedDuration);
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
    public void LaterMinuteTimerDecreaseDoesNotUndoAnAlreadyReadIncrease()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 1800, precisionSeconds: 60);
        Apply(ledger, 10, 1800, precisionSeconds: 60);
        Apply(ledger, 20, 1740, precisionSeconds: 60);
        Apply(ledger, 30, 1800, precisionSeconds: 60);
        var decreased = Apply(ledger, 40, 1740, precisionSeconds: 60);
        Assert.Single(decreased.Consumptions);
        Assert.Single(decreased.Active);
        var recovered = Apply(ledger, 50, 1740, precisionSeconds: 60);
        Assert.Single(recovered.Consumptions);
        Assert.False(Assert.Single(recovered.Active).IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(50), Assert.Single(recovered.Usage).ObservedDuration);
    }

    [Fact]
    public void IncreaseBeforeInitialConfirmationCountsOnlyTheNewUse()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 1740, precisionSeconds: 60);
        Apply(ledger, 10, 1800, precisionSeconds: 60);
        Apply(ledger, 20, 1740, precisionSeconds: 60);
        var result = Apply(ledger, 30, 1740, precisionSeconds: 60);
        Assert.False(Assert.Single(result.Consumptions).IsSessionStart);
        Assert.False(Assert.Single(result.Active).IsBaseline);
    }

    [Fact]
    public void ClearlyRenewedMinuteTimerStillBooksEveryConfirmedConsumptionExactlyOnce()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 1800, precisionSeconds: 60);
        Apply(ledger, 10, 1800, precisionSeconds: 60);
        Apply(ledger, 20, 3600, precisionSeconds: 60);
        Apply(ledger, 30, 3600, precisionSeconds: 60);
        Apply(ledger, 40, 3540, precisionSeconds: 60);
        Apply(ledger, 50, 3540, precisionSeconds: 60);
        Apply(ledger, 60, 3600, precisionSeconds: 60);
        var result = Apply(ledger, 70, 3600, precisionSeconds: 60);
        Assert.Equal(2, result.Consumptions.Count);
        Assert.All(result.Consumptions, item => Assert.False(item.IsSessionStart));
        Assert.Equal(720_000m, result.ConsumedCost);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneUnreadableBuffCanConfirmARefreshWhileOtherBuffsKeepTheirContinuity(bool recoveredBeforeDelivery)
    {
        var second = Buff with { Id = "perfume", Name = "Perfume" };
        var ledger = new BuffLedger([Buff, second]);
        BuffObservation Other(int seconds) => Observation(seconds) with { BuffId = second.Id };
        ledger.Apply([Observation(120), Other(600)], Start, _ => Price);
        ledger.Apply([Observation(110), Other(590)], Start.AddSeconds(10), _ => Price);
        var scan = recoveredBeforeDelivery ? new[] { Observation(3600), Other(580) } : [Other(580)];
        ledger.Apply(scan, Start.AddSeconds(20), _ => Price, [Buff.Id]);
        ledger.Apply([Observation(3590), Other(570)], Start.AddSeconds(30), _ => Price);
        var result = ledger.Apply([Observation(3580), Other(560)], Start.AddSeconds(40), _ => Price);
        Assert.Single(result.Consumptions);
        Assert.Equal(Buff.Id, Assert.Single(result.Consumptions, item => !item.IsSessionStart).BuffId);
        Assert.Equal(2, result.Active.Count);
        Assert.False(result.Active.Single(item => item.BuffId == Buff.Id).IsBaseline);
        Assert.True(result.Active.Single(item => item.BuffId == second.Id).IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(recoveredBeforeDelivery ? 30 : 20), result.Usage.Single(item => item.BuffId == Buff.Id).ObservedDuration);
        Assert.Equal(TimeSpan.FromSeconds(40), result.Usage.Single(item => item.BuffId == second.Id).ObservedDuration);
    }

    [Fact]
    public void DelayedUnreadableScanCannotDiscardAnAlreadyConfirmedBuff()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 1800);
        Apply(ledger, 10, 1790);
        var ignored = ledger.Apply([], Start.AddSeconds(5), _ => Price, [Buff.Id]);
        Assert.Single(ignored.Active);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(Apply(ledger, 20, 1780).Usage).ObservedDuration);
    }

    [Fact]
    public void HigherReadTimerIsBookedEvenWhenALaterTimerDrops()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        Apply(ledger, 10, 110);
        Apply(ledger, 20, 3600);
        Apply(ledger, 30, 90);
        var result = Apply(ledger, 40, 80);
        Assert.Single(result.Consumptions);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(result.Usage).ObservedDuration);
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

    [Theory]
    [InlineData(20)]
    [InlineData(600)]
    public void MissingIconRetainsItsLastReadTimerAcrossShortAndLongGaps(int lastMissingAt)
    {
        var second = Buff with { Id = "perfume", Name = "Perfume" };
        var ledger = new BuffLedger([Buff, second]);
        BuffObservation Other(int elapsed) => Observation(3000 - elapsed) with { BuffId = second.Id };
        ledger.Apply([Observation(600), Other(0)], Start, _ => Price);
        ledger.Apply([Observation(590), Other(10)], Start.AddSeconds(10), _ => Price);
        for (var elapsed = 20; elapsed <= lastMissingAt; elapsed += 10)
            ledger.Apply([Other(elapsed)], Start.AddSeconds(elapsed), _ => Price);
        ledger.Apply([Observation(3600), Other(lastMissingAt + 10)], Start.AddSeconds(lastMissingAt + 10), _ => Price);
        var state = ledger.Apply([Observation(3590), Other(lastMissingAt + 20)], Start.AddSeconds(lastMissingAt + 20), _ => Price);

        Assert.Single(state.Consumptions);
        Assert.Equal(Buff.Id, Assert.Single(state.Consumptions, item => !item.IsSessionStart).BuffId);
        Assert.Equal(2, state.Active.Count);
        Assert.False(state.Active.Single(item => item.BuffId == Buff.Id).IsBaseline);
        Assert.True(state.Active.Single(item => item.BuffId == second.Id).IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(20), state.Usage.Single(item => item.BuffId == Buff.Id).ObservedDuration);
        Assert.Equal(TimeSpan.FromSeconds(lastMissingAt + 20), state.Usage.Single(item => item.BuffId == second.Id).ObservedDuration);
    }

    [Fact]
    public void DelayedFirstIconDetectionCountsOnceAndLaterIncreaseAddsOne()
    {
        var second = Buff with { Id = "perfume", Name = "Perfume" };
        var ledger = new BuffLedger([Buff, second]);
        BuffObservation Other(int remaining) => Observation(remaining) with { BuffId = second.Id };
        ledger.Apply([Other(600)], Start, _ => Price);
        ledger.Apply([Other(590), Observation(3590, 60)], Start.AddSeconds(10), _ => Price);
        var confirmed = ledger.Apply([Other(580), Observation(3580, 60)], Start.AddSeconds(20), _ => Price);
        Assert.Equal(Buff.Id, Assert.Single(confirmed.Consumptions).BuffId);
        Assert.False(Assert.Single(confirmed.Consumptions).IsSessionStart);
        Assert.False(confirmed.Active.Single(item => item.BuffId == Buff.Id).IsBaseline);

        // A later continuously observed, clearly renewed countdown still counts once.
        ledger.Apply([Other(570), Observation(1800, 60)], Start.AddSeconds(30), _ => Price);
        ledger.Apply([Other(560), Observation(1790, 60)], Start.AddSeconds(40), _ => Price);
        ledger.Apply([Other(550), Observation(3600, 60)], Start.AddSeconds(50), _ => Price);
        var renewed = ledger.Apply([Other(540), Observation(3590, 60)], Start.AddSeconds(60), _ => Price);
        Assert.Equal(2, renewed.Consumptions.Count);
        Assert.All(renewed.Consumptions, item =>
        {
            Assert.Equal(Buff.Id, item.BuffId);
            Assert.False(item.IsSessionStart);
        });
    }

    [Fact]
    public void MissingInitialConfirmationStillLeavesTheInitialBuffUncounted()
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
    public void HigherTimerAfterAnUnconfirmedInitialReadingCountsOneUse()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 60);
        ApplyEmpty(ledger, 10);
        Apply(ledger, 20, 3600);
        var result = Apply(ledger, 30, 3590);
        Assert.False(Assert.Single(result.Consumptions).IsSessionStart);
        Assert.False(Assert.Single(result.Active).IsBaseline);
    }

    [Fact]
    public void ReappearanceWithHigherTimerCountsOneNewUse()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 60);
        Apply(ledger, 10, 50);
        ApplyEmpty(ledger, 20);
        Apply(ledger, 30, 3600);
        var result = Apply(ledger, 40, 3590);
        Assert.Single(result.Consumptions);
        Assert.False(Assert.Single(result.Active).IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void LongCaptureGapRetainsTimerComparisonWithoutChargingOfflineTime()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        Apply(ledger, 10, 110);
        Apply(ledger, 300, 3600);
        var result = Apply(ledger, 310, 3590);
        Assert.Single(result.Consumptions);
        Assert.False(Assert.Single(result.Active).IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void ExplicitBreakRetainsTimerComparisonButDoesNotChargeThePause()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        Apply(ledger, 10, 110);
        ledger.BreakContinuity();
        Assert.Empty(ledger.Snapshot.Active);
        Apply(ledger, 20, 3600);
        var result = Apply(ledger, 30, 3590);
        Assert.Single(result.Consumptions);
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void MissingPriceRemainsUnknownForPastConsumptionAndPastRunningTime()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 60);
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
        Apply(ledger, 0, 60);
        Apply(ledger, 10, 3600);
        Apply(ledger, 20, 3590, Price with { UnitPrice = 1 });
        var result = Apply(ledger, 30, 3580, Price with { UnitPrice = 2 });
        Assert.Equal(360_000m, Assert.Single(result.Consumptions).Cost);
        Assert.Equal(2000m, result.ProratedCost);
    }

    [Fact]
    public void LateFirstDetectionWithPartialTimerRemainsAnUncountedBaseline()
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
        Assert.Empty(result.Consumptions);
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
        Apply(ledger, 0, 60);
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
    public void ResetClearsHistoryAndStartsANewUncountedBaseline()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 60);
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
        BuffObservation Reading(int seconds) => Observation(seconds) with
        { ConsumptionAttributionConfirmed = ownConsumption };
        ledger.Apply([Reading(60)], Start, _ => Price);
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
        Apply(ledger, 0, 60, npc);
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
