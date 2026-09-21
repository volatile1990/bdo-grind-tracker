using System.Text.Json;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.Core.Tests;

public sealed class BuffDurationVariantTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly BuffDefinition[] Variants =
    [
        Variant(60, 60_000m), Variant(90, 135_000m), Variant(120, 240_000m),
        Variant(180, 540_000m), Variant(300, 1_200_000m),
    ];
    private static readonly BuffDefinition Family = new("automatic-tent", "Tent (duration unknown)", null, TimeSpan.FromMinutes(360))
    {
        RecognitionGroup = "tent-family",
        DurationVariantIds = ["tent-300", "tent-60", "tent-180", "tent-90", "tent-120"],
    };

    [Theory]
    [InlineData(280, 300)]
    [InlineData(160, 180)]
    [InlineData(121, 180)]
    [InlineData(91, 120)]
    [InlineData(61, 90)]
    [InlineData(1, 60)]
    [InlineData(60, 60)]
    [InlineData(90, 90)]
    [InlineData(180, 180)]
    [InlineData(300, 300)]
    public void BaselineChoosesTheSmallestDurationCoveringTheObservedRemainingTime(int minutes, int duration)
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, minutes * 60);

        var state = Apply(ledger, 10, minutes * 60 - 10);

        var expected = Variants.Single(item => item.Duration == TimeSpan.FromMinutes(duration));
        var active = Assert.Single(state.Active);
        Assert.Equal(expected.Id, active.BuffId);
        Assert.Equal(expected.Name, active.Name);
        Assert.Equal(expected.MarketItemId, active.MarketItemId);
        Assert.Equal(expected.FixedUnitPrice, active.Price!.UnitPrice);
        Assert.True(active.IsBaseline);
        AssertStartCharge(state, expected.Id);
        Assert.Equal(expected.FixedUnitPrice, state.ConsumedCost);
        var usage = Assert.Single(state.Usage);
        Assert.Equal(expected.Id, usage.BuffId);
        Assert.Equal(expected.FixedUnitPrice!.Value * TimeSpan.FromSeconds(10).Ticks / expected.Duration.Ticks,
            usage.KnownProratedCost);
    }

    [Fact]
    public void CountdownKeepsItsConfirmedVariantAcrossSmallerDurationBoundaries()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 180 * 60 + 20);
        Apply(ledger, 10, 180 * 60 + 10);
        Apply(ledger, 20, 180 * 60);

        var state = Apply(ledger, 30, 180 * 60 - 10, _ => throw new InvalidOperationException("A known cycle must retain its price."));

        Assert.Equal("tent-300", Assert.Single(state.Active).BuffId);
        Assert.Equal(1_200_000m, Assert.Single(state.Active).Price!.UnitPrice);
        Assert.Equal("tent-300", Assert.Single(state.Usage).BuffId);
        Assert.Equal(2_000m, state.ProratedCost);
        AssertStartCharge(state, "tent-300");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(110)]
    [InlineData(160)]
    [InlineData(280)]
    [InlineData(300)]
    public void MaximumDurationPolicyUsesOneIdentityAndPriceForEveryInitialCountdown(int remainingMinutes)
    {
        var ledger = new BuffLedger(Variants.Append(Family with { PreferMaximumDurationVariant = true }));
        Apply(ledger, 0, remainingMinutes * 60);
        var initial = Apply(ledger, 10, remainingMinutes * 60 - 10);

        AssertStartCharge(initial, "tent-300");
        Assert.Equal(1_200_000m, initial.ConsumedCost);

        // A lower countdown does not book again; a later increase counts one
        // more use even if the observed cycle would fit a much shorter variant.
        Apply(ledger, 20, 20);
        Apply(ledger, 30, 10);
        var renewed = Apply(ledger, 40, 60);
        Assert.Equal(2, renewed.Consumptions.Count);
        Assert.All(renewed.Consumptions, item => Assert.Equal("tent-300", item.BuffId));
        Assert.Equal(2_400_000m, renewed.ConsumedCost);
        Assert.Equal("tent-300", Assert.Single(renewed.Active).BuffId);
    }

    [Fact]
    public void MaximumDurationPolicyPreservesOldBookingsAndCountsOnlyANewTimerIncreaseAfterRestore()
    {
        var previous = CreateLedger();
        Apply(previous, 0, 20 * 60);
        var saved = Apply(previous, 10, 20 * 60 - 10);
        AssertStartCharge(saved, "tent-60");
        var ledger = new BuffLedger(Variants.Append(Family with { PreferMaximumDurationVariant = true }));
        ledger.Restore(saved);

        Apply(ledger, 20, 20 * 60 - 20);
        var continued = Apply(ledger, 30, 20 * 60 - 30);
        Assert.Equal(saved.Consumptions, continued.Consumptions);

        var renewed = Apply(ledger, 40, 60 * 60);
        Assert.Equal(2, renewed.Consumptions.Count);
        Assert.Equal(saved.Consumptions[0], renewed.Consumptions[0]);
        Assert.Equal("tent-300", renewed.Consumptions[1].BuffId);
        Assert.Equal(1_260_000m, renewed.ConsumedCost);
    }

    [Fact]
    public void PendingBaselineAlsoKeepsItsSelectionWhenConfirmationCrossesADurationBoundary()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 180 * 60 + 5);

        var state = Apply(ledger, 10, 180 * 60 - 5);

        Assert.Equal("tent-300", Assert.Single(state.Active).BuffId);
        AssertStartCharge(state, "tent-300");
    }

    [Fact]
    public void AnUnconfirmedBadBaselineDoesNotLockTheWrongVariant()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 280 * 60);
        Apply(ledger, 10, 50 * 60);

        var state = Apply(ledger, 20, 50 * 60 - 10);

        Assert.Equal("tent-60", Assert.Single(state.Active).BuffId);
        Assert.Equal(60_000m, Assert.Single(state.Active).Price!.UnitPrice);
        AssertStartCharge(state, "tent-60");
        Assert.Equal("tent-60", Assert.Single(state.Usage).BuffId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HigherTimerImmediatelyBooksTheDurationVariantThatCoversIt(bool previouslyConfirmed)
    {
        var ledger = CreateLedger();
        var coarse = new BuffObservation(Family.Id, TimeSpan.FromHours(2), TimeSpan.FromHours(1));
        ledger.Apply([coarse], Start, Price);
        if (previouslyConfirmed)
            Assert.Equal("tent-120", Assert.Single(ledger.Apply([coarse], Start.AddSeconds(10), Price).Active).BuffId);
        var elapsed = previouslyConfirmed ? 20 : 10;
        var precise = new BuffObservation(Family.Id, TimeSpan.FromMinutes(150), TimeSpan.FromMinutes(1));

        var renewed = ledger.Apply([precise], Start.AddSeconds(elapsed), Price);
        var state = ledger.Apply([precise with { Remaining = precise.Remaining - TimeSpan.FromSeconds(10) }],
            Start.AddSeconds(elapsed + 10), Price);

        Assert.Equal("tent-180", Assert.Single(renewed.Active).BuffId);
        Assert.Equal(previouslyConfirmed ? 2 : 1, renewed.Consumptions.Count);
        Assert.Equal("tent-180", Assert.Single(state.Consumptions, item => !item.IsSessionStart).BuffId);
        var active = Assert.Single(state.Active);
        Assert.Equal("tent-180", active.BuffId);
        Assert.Equal(540_000m, active.Price!.UnitPrice);
        Assert.False(active.IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(10), state.Usage.Single(item => item.BuffId == "tent-180").ObservedDuration);
        if (previouslyConfirmed)
            Assert.Equal(TimeSpan.FromSeconds(20), state.Usage.Single(item => item.BuffId == "tent-120").ObservedDuration);
        else Assert.Single(state.Usage);
    }

    [Fact]
    public void ConfirmedRefreshCanSwitchDurationAndBooksTheNewVariantExactlyOnce()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 160 * 60);
        Apply(ledger, 10, 160 * 60 - 10);
        Assert.Equal(2, Apply(ledger, 20, 280 * 60).Consumptions.Count);
        Apply(ledger, 30, 280 * 60 - 10);

        var state = Apply(ledger, 40, 280 * 60 - 20);

        Assert.Equal(2, state.Consumptions.Count);
        Assert.Equal("tent-180", Assert.Single(state.Consumptions, item => item.IsSessionStart).BuffId);
        var consumed = Assert.Single(state.Consumptions, item => !item.IsSessionStart);
        Assert.Equal("tent-300", consumed.BuffId);
        Assert.Equal(Start.AddSeconds(20), consumed.ConsumedAt);
        Assert.Equal(1_200_000m, consumed.Cost);
        Assert.Equal("tent-300", Assert.Single(state.Active).BuffId);
        Assert.False(Assert.Single(state.Active).IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(20), state.Usage.Single(item => item.BuffId == "tent-180").ObservedDuration);
        Assert.Equal(TimeSpan.FromSeconds(20), state.Usage.Single(item => item.BuffId == "tent-300").ObservedDuration);
    }

    [Fact]
    public void LaterRefreshCanSelectAShorterVariantAfterTheLongCycleHasCountedDown()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 280 * 60);
        Apply(ledger, 10, 280 * 60 - 10);
        // Two lower readings re-establish a noisy countdown without replacing its variant.
        Apply(ledger, 20, 20 * 60);
        Assert.Equal("tent-300", Assert.Single(Apply(ledger, 30, 20 * 60 - 10).Active).BuffId);
        Apply(ledger, 40, 160 * 60);
        Apply(ledger, 50, 160 * 60 - 10);

        var state = Apply(ledger, 60, 160 * 60 - 20);

        Assert.Equal("tent-180", Assert.Single(state.Active).BuffId);
        Assert.Equal(2, state.Consumptions.Count);
        Assert.Equal("tent-300", Assert.Single(state.Consumptions, item => item.IsSessionStart).BuffId);
        Assert.Equal("tent-180", Assert.Single(state.Consumptions, item => !item.IsSessionStart).BuffId);
        Assert.Equal(1_740_000m, state.ConsumedCost);
    }

    [Fact]
    public void LowerTimerAfterARenewalDoesNotReplaceItsBookedVariantOrPrice()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 20 * 60);
        Apply(ledger, 10, 20 * 60 - 10);
        Apply(ledger, 20, 280 * 60);
        Apply(ledger, 30, 160 * 60);

        var state = Apply(ledger, 40, 160 * 60 - 10);

        Assert.Equal(2, state.Consumptions.Count);
        Assert.Equal("tent-60", Assert.Single(state.Consumptions, item => item.IsSessionStart).BuffId);
        var consumed = Assert.Single(state.Consumptions, item => !item.IsSessionStart);
        Assert.Equal("tent-300", consumed.BuffId);
        Assert.Equal(1_200_000m, consumed.Cost);
        Assert.Equal(Start.AddSeconds(20), consumed.ConsumedAt);
        Assert.Equal("tent-300", Assert.Single(state.Active).BuffId);
        Assert.Equal(TimeSpan.FromSeconds(10), state.Usage.Single(item => item.BuffId == "tent-300").ObservedDuration);
    }

    [Fact]
    public void GroupContinuityBreakStartsANewBaselineWithoutRepricingPreviousUsage()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 280 * 60);
        Apply(ledger, 10, 280 * 60 - 10);
        ledger.Apply([Observe(160 * 60)], Start.AddSeconds(20), Price, [Family.Id]);

        var state = Apply(ledger, 30, 160 * 60 - 10);

        Assert.True(Assert.Single(state.Active).IsBaseline);
        Assert.Equal("tent-180", Assert.Single(state.Active).BuffId);
        AssertStartCharge(state, "tent-300");
        Assert.Equal(TimeSpan.FromSeconds(10), state.Usage.Single(item => item.BuffId == "tent-300").ObservedDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), state.Usage.Single(item => item.BuffId == "tent-180").ObservedDuration);
    }

    [Theory]
    [InlineData("unknown-id")]
    [InlineData("duplicate-id")]
    [InlineData("self-reference")]
    [InlineData("nested-group")]
    [InlineData("same-duration")]
    [InlineData("different-families")]
    [InlineData("missing-family")]
    [InlineData("null-list")]
    [InlineData("no-fitting-duration")]
    public void InvalidOrAmbiguousDurationCandidatesRemainAnUnpricedGroup(string scenario)
    {
        var variants = Variants.ToArray();
        var family = Family;
        switch (scenario)
        {
            case "unknown-id": family = family with { DurationVariantIds = ["tent-60", "missing"] }; break;
            case "duplicate-id": family = family with { DurationVariantIds = ["tent-60", "tent-60"] }; break;
            case "self-reference": family = family with { DurationVariantIds = [family.Id] }; break;
            case "nested-group": variants[0] = variants[0] with { DurationVariantIds = ["tent-90"] }; break;
            case "same-duration": variants[1] = variants[1] with { Duration = variants[0].Duration }; break;
            case "different-families": variants[0] = variants[0] with { RecognitionGroup = "other" }; break;
            case "missing-family": variants[0] = variants[0] with { RecognitionGroup = string.Empty }; break;
            case "null-list": family = family with { DurationVariantIds = null! }; break;
        }
        var ledger = new BuffLedger(variants.Append(family));
        var resolverCalls = 0;
        BuffPrice? IncorrectGroupPrice(BuffDefinition _) { resolverCalls++; return new(999_999m, "eu", Start, false); }
        var initial = (scenario == "no-fitting-duration" ? 310 : 50) * 60;
        var renewed = (scenario == "no-fitting-duration" ? 340 : 100) * 60;
        Apply(ledger, 0, initial, IncorrectGroupPrice);
        Apply(ledger, 10, initial - 10, IncorrectGroupPrice);
        Apply(ledger, 20, renewed, IncorrectGroupPrice);

        var state = Apply(ledger, 30, renewed - 10, IncorrectGroupPrice);

        Assert.Equal(family.Id, Assert.Single(state.Active).BuffId);
        Assert.Equal(2, state.Consumptions.Count);
        Assert.Equal(family.Id, Assert.Single(state.Consumptions, item => item.IsSessionStart).BuffId);
        Assert.Equal(family.Id, Assert.Single(state.Consumptions, item => !item.IsSessionStart).BuffId);
        Assert.Equal(family.Id, Assert.Single(state.Usage).BuffId);
        Assert.Null(state.ConsumedCost);
        Assert.Null(state.ProratedCost);
        Assert.Equal(0, resolverCalls);
    }

    [Fact]
    public void InitialDurationFamilyAccountingWaitsForItsOwnLateConfirmation()
    {
        var other = new BuffDefinition("other", "Other", 123, TimeSpan.FromHours(1));
        var ledger = new BuffLedger(Variants.Append(Family).Append(other));
        ledger.Apply([new(other.Id, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(1))], Start, Price);
        Assert.Empty(Apply(ledger, 10, 160 * 60).Consumptions);

        var initial = Apply(ledger, 20, 160 * 60 - 10);
        AssertStartCharge(initial, "tent-180");
        Assert.Equal(540_000m, initial.ConsumedCost);
        ledger.BreakContinuity();
        Apply(ledger, 30, 120 * 60);
        var changedSelection = Apply(ledger, 40, 120 * 60 - 10);
        Assert.Equal("tent-120", Assert.Single(changedSelection.Active).BuffId);
        Assert.Equal(initial.Consumptions, changedSelection.Consumptions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoredFamilyOrConcreteBookingPreventsAnotherInitialChargeForTheSameFamily(bool familyBooking)
    {
        var booked = familyBooking ? Family : Variants.Single(item => item.Id == "tent-300");
        var historical = new BuffConsumption(booked.Id, booked.Name, booked.MarketItemId, Start.AddHours(-1), Price(booked));
        var ledger = CreateLedger();
        ledger.Restore(new([historical], [], []));
        Apply(ledger, 0, 120 * 60);

        var current = Apply(ledger, 10, 120 * 60 - 10);

        Assert.Equal(historical, Assert.Single(current.Consumptions));
        Assert.Equal("tent-120", Assert.Single(current.Active).BuffId);
    }

    [Fact]
    public void RestoredConcreteDurationRetainsTheComparisonForItsAutomaticFamily()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 160 * 60);
        var before = Apply(ledger, 10, 160 * 60 - 10);
        var restored = CreateLedger();
        restored.Restore(before);
        Assert.Empty(restored.Snapshot.Active);

        var renewed = Apply(restored, 300, 280 * 60);

        Assert.Equal(2, renewed.Consumptions.Count);
        var consumption = Assert.Single(renewed.Consumptions, item => !item.IsSessionStart);
        Assert.Equal("tent-300", consumption.BuffId);
        Assert.Equal(Start.AddSeconds(300), consumption.ConsumedAt);
        Assert.Equal(1_200_000m, consumption.Cost);
        Assert.Equal(before.Usage, renewed.Usage);
        var continued = Apply(restored, 310, 280 * 60 - 10);
        Assert.Equal(TimeSpan.FromSeconds(10), continued.Usage.Single(item => item.BuffId == "tent-300").ObservedDuration);
    }

    [Fact]
    public void ExistingUnknownHistoryAndNewConcreteCostsSurviveRoundTripUnchanged()
    {
        var ledger = CreateLedger();
        var old = new BuffConsumption(Family.Id, Family.Name, null, Start.AddDays(-1), null);
        var oldUsage = new BuffUsage(Family.Id, Family.Name, null, TimeSpan.FromMinutes(1), 0, TimeSpan.FromMinutes(1));
        ledger.Restore(new([old], [oldUsage], []));
        Apply(ledger, 0, 20 * 60);
        Apply(ledger, 10, 20 * 60 - 10);
        Apply(ledger, 20, 280 * 60);
        var state = Apply(ledger, 30, 280 * 60 - 10);
        var restored = CreateLedger();

        restored.Restore(JsonSerializer.Deserialize<BuffLedgerSnapshot>(JsonSerializer.Serialize(state))!);

        Assert.Equal(old, restored.Snapshot.Consumptions[0]);
        Assert.Equal(oldUsage, restored.Snapshot.Usage.Single(item => item.BuffId == Family.Id));
        Assert.Equal("tent-300", restored.Snapshot.Consumptions[1].BuffId);
        Assert.Equal(1_200_000m, restored.Snapshot.Consumptions[1].Cost);
        Assert.Null(restored.Snapshot.ConsumedCost);
        Assert.Equal(state.KnownProratedCost, restored.Snapshot.KnownProratedCost);
        Assert.Empty(restored.Snapshot.Active);
    }

    private static void AssertStartCharge(BuffLedgerSnapshot snapshot, string id)
    {
        var charge = Assert.Single(snapshot.Consumptions);
        Assert.True(charge.IsSessionStart);
        Assert.Equal(id, charge.BuffId);
    }
    private static BuffDefinition Variant(int minutes, decimal price) => new(
        $"tent-{minutes}", $"Tent ({minutes} min)", null, TimeSpan.FromMinutes(minutes))
    { RecognitionGroup = "tent", FixedUnitPrice = price };

    private static BuffLedger CreateLedger() => new(Variants.Append(Family));
    private static BuffObservation Observe(int seconds) => new(Family.Id, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1));
    private static BuffPrice? Price(BuffDefinition definition) => definition.FixedUnitPrice is { } price
        ? new(price, "eu", null, false) { Source = BuffPriceSource.FixedNpc } : null;
    private static BuffLedgerSnapshot Apply(BuffLedger ledger, int elapsedSeconds, int remainingSeconds,
        Func<BuffDefinition, BuffPrice?>? prices = null) =>
        ledger.Apply([Observe(remainingSeconds)], Start.AddSeconds(elapsedSeconds), prices ?? Price);
}
