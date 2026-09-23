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
        Assert.Empty(state.Consumptions);
        Assert.Equal(0m, state.ConsumedCost);
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
        Assert.Empty(state.Consumptions);
    }

    [Fact]
    public void PendingBaselineAlsoKeepsItsSelectionWhenConfirmationCrossesADurationBoundary()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 180 * 60 + 5);

        var state = Apply(ledger, 10, 180 * 60 - 5);

        Assert.Equal("tent-300", Assert.Single(state.Active).BuffId);
        Assert.Empty(state.Consumptions);
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
        Assert.Empty(state.Consumptions);
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
            Assert.Equal("tent-180", Assert.Single(ledger.Apply([coarse], Start.AddSeconds(10), Price).Active).BuffId);
        var elapsed = previouslyConfirmed ? 20 : 10;
        var precise = new BuffObservation(Family.Id, TimeSpan.FromMinutes(250), TimeSpan.FromMinutes(1));

        var renewed = ledger.Apply([precise], Start.AddSeconds(elapsed), Price);
        var state = ledger.Apply([precise with { Remaining = precise.Remaining - TimeSpan.FromSeconds(10) }],
            Start.AddSeconds(elapsed + 10), Price);

        Assert.Equal("tent-300", Assert.Single(renewed.Active).BuffId);
        Assert.Single(renewed.Consumptions);
        Assert.Equal("tent-300", Assert.Single(state.Consumptions, item => !item.IsSessionStart).BuffId);
        var active = Assert.Single(state.Active);
        Assert.Equal("tent-300", active.BuffId);
        Assert.Equal(1_200_000m, active.Price!.UnitPrice);
        Assert.False(active.IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(10), state.Usage.Single(item => item.BuffId == "tent-300").ObservedDuration);
        if (previouslyConfirmed)
            Assert.Equal(TimeSpan.FromSeconds(20), state.Usage.Single(item => item.BuffId == "tent-180").ObservedDuration);
        else Assert.Single(state.Usage);
    }

    [Theory]
    [InlineData(2, 180)]
    [InlineData(3, 300)]
    [InlineData(4, 300)]
    public void HourBaselineUsesTheFlooredIntervalWithoutInventingConsumption(int hours, int durationMinutes)
    {
        var ledger = CreateLedger();
        var observation = new BuffObservation(Family.Id, TimeSpan.FromHours(hours), TimeSpan.FromHours(1));
        ledger.Apply([observation], Start, Price);

        var state = ledger.Apply([observation], Start.AddSeconds(10), Price);

        var expected = Variants.Single(item => item.Duration == TimeSpan.FromMinutes(durationMinutes));
        Assert.Equal(expected.Id, Assert.Single(state.Active).BuffId);
        Assert.Equal(expected.FixedUnitPrice, Assert.Single(state.Active).Price!.UnitPrice);
        Assert.Empty(state.Consumptions);
    }

    [Theory]
    [InlineData(2, 180)]
    [InlineData(4, 300)]
    public void NewHourDurationApplicationCountsItsUniqueVariant(int hours, int durationMinutes)
    {
        var ledger = CreateLedger();
        ledger.Apply([], Start, Price);
        var observation = new BuffObservation(Family.Id, TimeSpan.FromHours(hours), TimeSpan.FromHours(1));
        ledger.Apply([observation], Start.AddSeconds(10), Price);

        var state = ledger.Apply([observation], Start.AddSeconds(20), Price);

        var consumption = Assert.Single(state.Consumptions);
        Assert.Equal($"tent-{durationMinutes}", consumption.BuffId);
        Assert.False(consumption.IsSessionStart);
        Assert.Equal(Start.AddSeconds(10), consumption.ConsumedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousOneHourApplicationCountsOnceWithoutChoosingBetweenNinetyAndOneHundredTwentyMinutes(bool renewal)
    {
        var ledger = CreateLedger();
        if (renewal) Apply(ledger, 0, 20 * 60);
        else ledger.Apply([], Start, Price);
        var observation = new BuffObservation(Family.Id, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        ledger.Apply([observation], Start.AddSeconds(10), Price);
        var confirmed = ledger.Apply([observation], Start.AddSeconds(20), Price);
        var state = ledger.Apply([observation], Start.AddSeconds(30), Price);

        var consumption = Assert.Single(state.Consumptions);
        Assert.Equal(Family.Id, consumption.BuffId);
        Assert.False(consumption.IsSessionStart);
        Assert.Null(consumption.Cost);
        Assert.Null(state.ConsumedCost);
        Assert.Equal(confirmed.Consumptions, state.Consumptions);
        Assert.Null(Assert.Single(state.Active).Price);
        Assert.False(Assert.Single(state.Active).IsBaseline);
    }

    [Fact]
    public void ThreeHourFirstAppearanceCannotProveAFreshFiveHourPurchase()
    {
        var ledger = CreateLedger();
        ledger.Apply([], Start, Price);
        var observation = new BuffObservation(Family.Id, TimeSpan.FromHours(3), TimeSpan.FromHours(1));
        ledger.Apply([observation], Start.AddSeconds(10), Price);

        var state = ledger.Apply([observation], Start.AddSeconds(20), Price);

        Assert.Equal("tent-300", Assert.Single(state.Active).BuffId);
        Assert.True(Assert.Single(state.Active).IsBaseline);
        Assert.Empty(state.Consumptions);
    }

    [Fact]
    public void UnconfirmedNewApplicationThatBecomesAPartialReadingReturnsToBaseline()
    {
        var ledger = CreateLedger();
        ledger.Apply([], Start, Price);
        Apply(ledger, 10, 300 * 60 - 10);
        Apply(ledger, 20, 20 * 60);

        var state = Apply(ledger, 30, 20 * 60 - 10);

        Assert.Equal("tent-60", Assert.Single(state.Active).BuffId);
        Assert.True(Assert.Single(state.Active).IsBaseline);
        Assert.Empty(state.Consumptions);
    }

    [Fact]
    public void ConfirmedRefreshCanSwitchDurationAndBooksTheNewVariantExactlyOnce()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 160 * 60);
        Apply(ledger, 10, 160 * 60 - 10);
        Assert.Single(Apply(ledger, 20, 280 * 60).Consumptions);
        Apply(ledger, 30, 280 * 60 - 10);

        var state = Apply(ledger, 40, 280 * 60 - 20);

        var consumed = Assert.Single(state.Consumptions);
        Assert.False(consumed.IsSessionStart);
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
        var consumed = Assert.Single(state.Consumptions);
        Assert.Equal("tent-180", consumed.BuffId);
        Assert.False(consumed.IsSessionStart);
        Assert.Equal(540_000m, state.ConsumedCost);
    }

    [Fact]
    public void RenewalsOfLongAndShortVariantsAreCountedSeparatelyWithoutTheInitialBuff()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 20 * 60);
        Assert.Empty(Apply(ledger, 10, 20 * 60 - 10).Consumptions);
        Apply(ledger, 20, 300 * 60);
        Apply(ledger, 30, 300 * 60 - 10);
        // Reconfirm a much lower reading before a shorter variant is reapplied.
        Apply(ledger, 40, 20 * 60);
        Apply(ledger, 50, 20 * 60 - 10);
        Apply(ledger, 60, 60 * 60);

        var state = Apply(ledger, 70, 60 * 60 - 10);

        Assert.Collection(state.Consumptions,
            longer =>
            {
                Assert.Equal("tent-300", longer.BuffId);
                Assert.Equal(1_200_000m, longer.Cost);
                Assert.Equal(Start.AddSeconds(20), longer.ConsumedAt);
                Assert.False(longer.IsSessionStart);
            },
            shorter =>
            {
                Assert.Equal("tent-60", shorter.BuffId);
                Assert.Equal(60_000m, shorter.Cost);
                Assert.Equal(Start.AddSeconds(60), shorter.ConsumedAt);
                Assert.False(shorter.IsSessionStart);
            });
        Assert.Equal(1_260_000m, state.ConsumedCost);
        Assert.Equal("tent-60", Assert.Single(state.Active).BuffId);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(180)]
    [InlineData(300)]
    public void NewDurationVariantAfterReadableAbsenceCountsItsOwnDuration(int durationMinutes)
    {
        var ledger = CreateLedger();
        ledger.Apply([], Start, Price);
        Assert.Empty(Apply(ledger, 10, durationMinutes * 60 - 10).Consumptions);

        var state = Apply(ledger, 20, durationMinutes * 60 - 20);

        var expected = Variants.Single(item => item.Duration == TimeSpan.FromMinutes(durationMinutes));
        var consumption = Assert.Single(state.Consumptions);
        Assert.Equal(expected.Id, consumption.BuffId);
        Assert.Equal(expected.FixedUnitPrice, consumption.Cost);
        Assert.Equal(Start.AddSeconds(10), consumption.ConsumedAt);
        Assert.False(consumption.IsSessionStart);
        Assert.False(Assert.Single(state.Active).IsBaseline);
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

        var consumed = Assert.Single(state.Consumptions);
        Assert.False(consumed.IsSessionStart);
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
        Assert.Empty(state.Consumptions);
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
        var consumption = Assert.Single(state.Consumptions);
        Assert.Equal(family.Id, consumption.BuffId);
        Assert.False(consumption.IsSessionStart);
        Assert.Equal(family.Id, Assert.Single(state.Usage).BuffId);
        Assert.Null(state.ConsumedCost);
        Assert.Null(state.ProratedCost);
        Assert.Equal(0, resolverCalls);
    }

    [Fact]
    public void LatePartialDurationFamilyRemainsABaselineWithoutConsumption()
    {
        var other = new BuffDefinition("other", "Other", 123, TimeSpan.FromHours(1));
        var ledger = new BuffLedger(Variants.Append(Family).Append(other));
        ledger.Apply([new(other.Id, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(1))], Start, Price);
        Assert.Empty(Apply(ledger, 10, 160 * 60).Consumptions);

        var initial = Apply(ledger, 20, 160 * 60 - 10);
        Assert.Empty(initial.Consumptions);
        Assert.Equal(0m, initial.ConsumedCost);
        Assert.True(Assert.Single(initial.Active).IsBaseline);
        ledger.BreakContinuity();
        Apply(ledger, 30, 120 * 60);
        var changedSelection = Apply(ledger, 40, 120 * 60 - 10);
        Assert.Equal("tent-120", Assert.Single(changedSelection.Active).BuffId);
        Assert.Equal(initial.Consumptions, changedSelection.Consumptions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoredLegacyStartupBookingIsPreservedWithoutAddingAnotherConsumption(bool familyBooking)
    {
        var booked = familyBooking ? Family : Variants.Single(item => item.Id == "tent-300");
        var historical = new BuffConsumption(booked.Id, booked.Name, booked.MarketItemId, Start.AddHours(-1), Price(booked))
        { IsSessionStart = true };
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

        var consumption = Assert.Single(renewed.Consumptions);
        Assert.False(consumption.IsSessionStart);
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
