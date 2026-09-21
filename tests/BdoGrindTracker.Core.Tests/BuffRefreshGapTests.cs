using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.Core.Tests;

public sealed class BuffRefreshGapTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly BuffDefinition Buff = new("consumable", "Consumable", 123, TimeSpan.FromMinutes(20));
    private static readonly BuffDefinition Other = new("other", "Other", 456, TimeSpan.FromHours(1));
    private static readonly BuffPrice Price = new(120_000, "eu", Start, false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpiredBuffReturningAfterOneLocalGapBooksOneRenewalImmediately(bool explicitUnknown)
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 15);
        Apply(ledger, 10, 5);
        Assert.DoesNotContain(Apply(ledger, 20, null, explicitUnknown).Active, item => item.BuffId == Buff.Id);

        var renewed = Apply(ledger, 30, 1200);
        Assert.Equal(2, renewed.Consumptions.Count(item => item.BuffId == Buff.Id));
        var confirmed = Apply(ledger, 40, 1190);
        var renewal = Assert.Single(confirmed.Consumptions, item => item.BuffId == Buff.Id && !item.IsSessionStart);
        Assert.Equal(Start.AddSeconds(30), renewal.ConsumedAt);
        Assert.Equal(Price, renewal.Price);
        Assert.Equal(TimeSpan.FromSeconds(20), Usage(confirmed).ObservedDuration);
        Assert.Equal(2_000, Usage(confirmed).KnownProratedCost);

        var continued = Apply(ledger, 50, 1180);
        Assert.Equal(2, continued.Consumptions.Count(item => item.BuffId == Buff.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturningOriginalCountdownIsNotAConsumptionAndDoesNotChargeTheGap(bool explicitUnknown)
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 300);
        Apply(ledger, 10, 290);
        Apply(ledger, 20, null, explicitUnknown);
        Apply(ledger, 30, 270);
        var confirmed = Apply(ledger, 40, 260);

        Assert.True(Assert.Single(confirmed.Consumptions, item => item.BuffId == Buff.Id).IsSessionStart);
        Assert.Equal(TimeSpan.FromSeconds(20), Usage(confirmed).ObservedDuration);
    }

    [Fact]
    public void ReadTimerIncreaseAfterGapIsNotUndoneByALaterDecrease()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 60);
        Apply(ledger, 10, 50);
        Apply(ledger, 20, null);
        Apply(ledger, 30, 1200);
        Apply(ledger, 40, 20);
        var recovered = Apply(ledger, 50, 10);

        Assert.Equal(2, recovered.Consumptions.Count(item => item.BuffId == Buff.Id));
        Assert.Equal(Start.AddSeconds(30), Assert.Single(recovered.Consumptions, item => item.BuffId == Buff.Id && !item.IsSessionStart).ConsumedAt);
        Assert.Equal(TimeSpan.FromSeconds(20), Usage(recovered).ObservedDuration);
    }

    [Fact]
    public void HigherTimerBeforeStartupConfirmationCountsOnlyTheNewUse()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 10);
        Apply(ledger, 10, null);
        Apply(ledger, 20, 1200);
        var confirmed = Apply(ledger, 30, 1190);

        Assert.False(Assert.Single(confirmed.Consumptions, item => item.BuffId == Buff.Id).IsSessionStart);
        Assert.Equal(TimeSpan.FromSeconds(10), Usage(confirmed).ObservedDuration);
    }

    [Fact]
    public void RepeatedUnknownDoesNotRebookOrRepriceTheAlreadyReadRenewal()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 30);
        Apply(ledger, 10, 20);
        var renewalPrice = Price with { UnitPrice = 180_000 };
        Apply(ledger, 20, 1200, explicitUnknown: true, price: renewalPrice);
        Apply(ledger, 30, null, explicitUnknown: true);
        var confirmedPrice = Price with { UnitPrice = 240_000, IsStale = true };
        Apply(ledger, 40, 1180, price: confirmedPrice);
        var confirmed = Apply(ledger, 50, 1170);

        var renewal = Assert.Single(confirmed.Consumptions, item => item.BuffId == Buff.Id && !item.IsSessionStart);
        Assert.Equal(Start.AddSeconds(20), renewal.ConsumedAt);
        Assert.Equal(renewalPrice, renewal.Price);
        Assert.Equal(TimeSpan.FromSeconds(20), Usage(confirmed).ObservedDuration);
        Assert.Equal(3_000, Usage(confirmed).KnownProratedCost);
    }

    [Fact]
    public void RepeatedUnknownKeepsTheLastTimerWithoutCountingEqualOrLowerValuesAgain()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        Apply(ledger, 10, 110);
        Apply(ledger, 20, null, explicitUnknown: true);
        Apply(ledger, 30, null, explicitUnknown: true);
        Apply(ledger, 40, 1200);
        Apply(ledger, 50, null, explicitUnknown: true);
        Apply(ledger, 60, 1180);
        var confirmed = Apply(ledger, 70, 1170);

        Assert.Equal(2, confirmed.Consumptions.Count(item => item.BuffId == Buff.Id));
        Assert.Equal(TimeSpan.FromSeconds(20), Usage(confirmed).ObservedDuration);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(600)]
    public void IndividualGapPreservesTheComparisonRegardlessOfItsDuration(int returnAt)
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        Apply(ledger, 10, 110);
        for (var at = 20; at < returnAt; at += 10) Apply(ledger, at, null);
        Apply(ledger, returnAt, 1200);
        var confirmed = Apply(ledger, returnAt + 10, 1190);

        Assert.Equal(2, confirmed.Consumptions.Count(item => item.BuffId == Buff.Id));
        Assert.Equal(TimeSpan.FromSeconds(20), Usage(confirmed).ObservedDuration);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("restore")]
    [InlineData("empty")]
    [InlineData("long-gap")]
    public void GlobalBreaksRetainTheTimerComparisonWithoutChargingTheGap(string interruption)
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 120);
        var before = Apply(ledger, 10, 110);
        switch (interruption)
        {
            case "pause": ledger.BreakContinuity(); break;
            case "restore": ledger.Restore(before); break;
            case "empty": ledger.Apply([], Start.AddSeconds(20), _ => Price); break;
        }
        var returnAt = interruption == "long-gap" ? 70 : 30;
        var renewed = Apply(ledger, returnAt, 1200);
        Assert.Equal(2, renewed.Consumptions.Count(item => item.BuffId == Buff.Id));
        var confirmed = Apply(ledger, returnAt + 10, 1190);

        Assert.Equal(2, confirmed.Consumptions.Count(item => item.BuffId == Buff.Id));
        Assert.Equal(TimeSpan.FromSeconds(20), Usage(confirmed).ObservedDuration);
    }

    [Theory]
    [InlineData(290)]
    [InlineData(100)]
    public void SameOrLowerTimerAfterALongPauseDoesNotCount(int remaining)
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 300);
        Apply(ledger, 10, 290);
        ledger.BreakContinuity();
        Apply(ledger, 600, remaining);
        var recovered = Apply(ledger, 610, remaining - 10);

        Assert.True(Assert.Single(recovered.Consumptions, item => item.BuffId == Buff.Id).IsSessionStart);
        Assert.Equal(TimeSpan.FromSeconds(20), Usage(recovered).ObservedDuration);
    }

    [Fact]
    public void EveryIncreaseUsesTheImmediatelyPreviousReadRatherThanAnOlderHighWaterMark()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 480);
        Apply(ledger, 10, 470);
        ledger.BreakContinuity(Buff.Id);
        Apply(ledger, 20, 240);
        var renewed = Apply(ledger, 30, 480);
        var unchanged = Apply(ledger, 40, 480);

        Assert.Equal(2, renewed.Consumptions.Count(item => item.BuffId == Buff.Id));
        Assert.Equal(2, unchanged.Consumptions.Count(item => item.BuffId == Buff.Id));
    }

    [Fact]
    public void EvenOneSecondIncreaseCountsDespiteTimerPrecision()
    {
        var ledger = CreateLedger();
        var first = new BuffObservation(Buff.Id, TimeSpan.FromSeconds(300), TimeSpan.FromMinutes(1));
        ledger.Apply([first], Start, _ => Price);
        var renewed = ledger.Apply([first with { Remaining = TimeSpan.FromSeconds(301) }], Start.AddSeconds(10), _ => Price);
        var unchanged = ledger.Apply([first with { Remaining = TimeSpan.FromSeconds(301) }], Start.AddSeconds(20), _ => Price);

        Assert.False(Assert.Single(renewed.Consumptions).IsSessionStart);
        Assert.Single(unchanged.Consumptions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BreakOrRestoreCannotMakeOldFramesCountAsNewTimerIncreases(bool restore)
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 300);
        var before = Apply(ledger, 10, 290);
        if (restore) ledger.Restore(before);
        else ledger.BreakContinuity();

        Apply(ledger, 5, 1200);
        Apply(ledger, 10, 1200);
        var unchanged = Apply(ledger, 20, 280);

        Assert.Equal(before.Consumptions, unchanged.Consumptions);
        Assert.Equal(before.Usage, unchanged.Usage);
    }

    [Fact]
    public void ResetClearsAllLastReadComparisonsForTheNewSession()
    {
        var ledger = CreateLedger();
        Apply(ledger, 0, 300);
        Apply(ledger, 10, 290);
        ledger.Reset();

        Assert.Empty(Apply(ledger, 20, 1200).Consumptions);
        var initial = Apply(ledger, 30, 1190);
        Assert.True(Assert.Single(initial.Consumptions, item => item.BuffId == Buff.Id).IsSessionStart);
    }

    private static BuffLedger CreateLedger() => new([Buff, Other]);
    private static BuffUsage Usage(BuffLedgerSnapshot snapshot) => snapshot.Usage.Single(item => item.BuffId == Buff.Id);

    private static BuffLedgerSnapshot Apply(BuffLedger ledger, int at, int? remaining,
        bool explicitUnknown = false, BuffPrice? price = null)
    {
        var observations = new List<BuffObservation>
        {
            new(Other.Id, TimeSpan.FromSeconds(3000 - at), TimeSpan.FromSeconds(1)),
        };
        if (remaining is { } seconds)
            observations.Add(new(Buff.Id, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1)));
        return ledger.Apply(observations, Start.AddSeconds(at), _ => price ?? Price,
            explicitUnknown ? [Buff.Id] : []);
    }
}
