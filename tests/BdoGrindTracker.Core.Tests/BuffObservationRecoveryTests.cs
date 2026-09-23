using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.Core.Tests;

public sealed class BuffObservationRecoveryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly BuffDefinition Tenacity = new("perfume-of-tenacity", "Perfume of Tenacity", 1413, TimeSpan.FromMinutes(20));
    private static readonly BuffDefinition Other = new("other", "Other", 1, TimeSpan.FromHours(5));
    private static readonly BuffPrice Price = new(600_000m, "eu", Start, false);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NewApplicationSurvivesOneLocalGapBeforeConfirmation(bool explicitUnknown, bool otherVisible)
    {
        var ledger = Create();
        Apply(ledger, 0, otherVisible: otherVisible);
        Apply(ledger, 10, 1190, otherVisible: otherVisible);
        Apply(ledger, 20, unknown: explicitUnknown, otherVisible: otherVisible);
        Assert.Empty(Apply(ledger, 30, 1170, otherVisible: otherVisible).Consumptions);
        var result = Apply(ledger, 40, 1160, otherVisible: otherVisible);

        var consumption = Assert.Single(result.Consumptions);
        Assert.Equal(Tenacity.Id, consumption.BuffId);
        Assert.Equal(Start.AddSeconds(10), consumption.ConsumedAt);
        Assert.Equal(Price, consumption.Price);
        Assert.False(result.Active.Single(item => item.BuffId == Tenacity.Id).IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Usage.Single(item => item.BuffId == Tenacity.Id).ObservedDuration);
        Assert.Single(Apply(ledger, 50, 1150, otherVisible: otherVisible).Consumptions);
    }

    [Fact]
    public void NewlyAppearingUnknownIconCanConfirmItsFirstReadableCountdown()
    {
        var ledger = Create();
        Apply(ledger, 0);
        Apply(ledger, 10, unknown: true);
        Assert.Empty(Apply(ledger, 20, 1180).Consumptions);
        var result = Apply(ledger, 30, 1170);

        Assert.Equal(Start.AddSeconds(20), Assert.Single(result.Consumptions).ConsumedAt);
        Assert.False(Assert.Single(result.Active).IsBaseline);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(result.Usage).ObservedDuration);
    }

    [Fact]
    public void RecoveryKeepsTheFirstReadableApplicationPrice()
    {
        var ledger = Create();
        Apply(ledger, 0);
        Apply(ledger, 10, 1190);
        Apply(ledger, 20, unknown: true);
        var newerPrice = Price with { UnitPrice = 900_000m };
        Apply(ledger, 30, 1170, price: newerPrice);
        var result = Apply(ledger, 40, 1160, price: newerPrice);

        Assert.Equal(Price, Assert.Single(result.Consumptions).Price);
        Assert.Equal(Start.AddSeconds(10), Assert.Single(result.Consumptions).ConsumedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialOrOfflineUnknownIconRemainsAnUnchargedBaseline(bool interrupted)
    {
        var ledger = Create();
        if (interrupted)
        {
            Apply(ledger, 0);
            ledger.BreakContinuity();
        }
        Apply(ledger, 10, unknown: true);
        Apply(ledger, 20, 1180);
        var result = Apply(ledger, 30, 1170);

        Assert.Empty(result.Consumptions);
        Assert.True(Assert.Single(result.Active).IsBaseline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconfirmedApplicationExpiresEvenWhileOtherFramesRemainReadable(bool firstTimerReadable)
    {
        var ledger = Create();
        Apply(ledger, 0);
        Apply(ledger, 10, firstTimerReadable ? 1190 : null, unknown: !firstTimerReadable);
        Apply(ledger, 20, unknown: true);
        Apply(ledger, 40, unknown: true);
        Apply(ledger, 60, 1140);
        var result = Apply(ledger, 70, 1130);

        Assert.Empty(result.Consumptions);
        Assert.True(Assert.Single(result.Active).IsBaseline);
    }

    [Fact]
    public void LatePartialTimerAfterNewUnknownIconCannotProveConsumption()
    {
        var ledger = Create();
        Apply(ledger, 0);
        Apply(ledger, 10, unknown: true);
        Apply(ledger, 20, 480);
        var result = Apply(ledger, 30, 470);

        Assert.Empty(result.Consumptions);
        Assert.True(Assert.Single(result.Active).IsBaseline);
    }

    [Fact]
    public void GlobalBreakDiscardsAnUnconfirmedApplication()
    {
        var ledger = Create();
        Apply(ledger, 0);
        Apply(ledger, 10, 1190);
        ledger.BreakContinuity();
        Apply(ledger, 20, 1180);
        var result = Apply(ledger, 30, 1170);

        Assert.Empty(result.Consumptions);
        Assert.True(Assert.Single(result.Active).IsBaseline);
    }

    [Theory]
    [InlineData(1, 119)]
    [InlineData(2, 179)]
    public void FlooredHourTimerRefinedToMinutesDoesNotCountAsConsumption(int hours, int minutes)
    {
        var ledger = Create();
        ledger.Apply([ObserveOther(hours * 3600, 3600)], Start, _ => Price);
        var result = ledger.Apply([ObserveOther(minutes * 60, 60)], Start.AddSeconds(10), _ => Price);

        Assert.Empty(result.Consumptions);
        Assert.True(Assert.Single(result.Active).IsBaseline);
        Assert.Equal(TimeSpan.FromMinutes(minutes), Assert.Single(result.Active).Remaining);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HourPrecisionSurvivesLocalAndGlobalGaps(bool globalBreak)
    {
        var ledger = Create();
        ledger.Apply([ObserveOther(3600, 3600)], Start, _ => Price);
        if (globalBreak) ledger.BreakContinuity();
        else ledger.Apply([], Start.AddSeconds(10), _ => Price, [Other.Id]);
        ledger.Apply([ObserveOther(119 * 60, 60)], Start.AddSeconds(20), _ => Price);
        var result = ledger.Apply([ObserveOther(119 * 60, 60)], Start.AddSeconds(30), _ => Price);

        Assert.Empty(result.Consumptions);
        Assert.True(Assert.Single(result.Active).IsBaseline);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(121)]
    public void TimerAboveThePreviousFlooredIntervalStillCountsImmediately(int minutes)
    {
        var ledger = Create();
        ledger.Apply([ObserveOther(3600, 3600)], Start, _ => Price);
        var result = ledger.Apply([ObserveOther(minutes * 60, 60)], Start.AddSeconds(10), _ => Price);

        Assert.Single(result.Consumptions);
        Assert.False(Assert.Single(result.Active).IsBaseline);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(3600)]
    [InlineData(7200)]
    public void TimeElapsedSinceTheHourReadingCanProveThatAHigherMinuteTimerIsANewUse(int elapsed)
    {
        var ledger = Create();
        ledger.Apply([ObserveOther(3600, 3600)], Start, _ => Price);
        ledger.BreakContinuity();
        var result = ledger.Apply([ObserveOther(119 * 60, 60)], Start.AddSeconds(elapsed), _ => Price);

        Assert.Single(result.Consumptions);
        Assert.False(Assert.Single(result.Active).IsBaseline);
    }

    [Fact]
    public void RefinementDoesNotSuppressTheNextIncreaseAtTheSamePrecision()
    {
        var ledger = Create();
        ledger.Apply([ObserveOther(3600, 3600)], Start, _ => Price);
        ledger.Apply([ObserveOther(110 * 60, 60)], Start.AddSeconds(10), _ => Price);
        var result = ledger.Apply([ObserveOther(111 * 60, 60)], Start.AddSeconds(20), _ => Price);

        Assert.Equal(Start.AddSeconds(20), Assert.Single(result.Consumptions).ConsumedAt);
    }

    private static BuffLedger Create() => new([Tenacity, Other]);
    private static BuffObservation ObserveOther(int seconds, int precision) =>
        new(Other.Id, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(precision));

    private static BuffLedgerSnapshot Apply(BuffLedger ledger, int seconds, int? tenacitySeconds = null,
        bool unknown = false, bool otherVisible = false, BuffPrice? price = null)
    {
        var observations = new List<BuffObservation>();
        if (tenacitySeconds is { } remaining)
            observations.Add(new(Tenacity.Id, TimeSpan.FromSeconds(remaining), TimeSpan.FromMinutes(1)));
        if (otherVisible) observations.Add(ObserveOther(10_000 - seconds, 1));
        return ledger.Apply(observations, Start.AddSeconds(seconds), _ => price ?? Price, unknown ? [Tenacity.Id] : []);
    }
}
