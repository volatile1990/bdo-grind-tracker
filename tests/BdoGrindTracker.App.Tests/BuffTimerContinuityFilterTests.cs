using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffTimerContinuityFilterTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 22, 15, 0, 0, TimeSpan.Zero);
    private const string Boon = "adventurers-boon";

    [Fact]
    public void SingleMissingDigitDoesNotTurnNormalBoonCountdownIntoConsumption()
    {
        var filter = new BuffTimerContinuityFilter();
        var ledger = new BuffLedger([new(Boon, "Adventurer's Boon", null, TimeSpan.FromHours(2))]);
        Apply(ledger, filter, Minutes(99), 0);

        var suspect = Apply(ledger, filter, Minutes(9), 10);
        var recovered = Apply(ledger, filter, Minutes(98), 20);

        Assert.Empty(suspect.Observations);
        Assert.Contains(Boon, suspect.UnknownBuffIds);
        Assert.Equal(Minutes(98), Assert.Single(recovered.Observations));
        Assert.Empty(recovered.UnknownBuffIds);
        Assert.Empty(ledger.Snapshot.Consumptions);
    }

    [Fact]
    public void ConsistentSecondLowReadingAcceptsTheActualObservedTimer()
    {
        var filter = new BuffTimerContinuityFilter();
        Read(filter, Minutes(99), 0);
        Assert.Empty(Read(filter, Minutes(9), 10).Observations);

        var confirmed = Read(filter, Minutes(9), 20);

        Assert.Equal(Minutes(9), Assert.Single(confirmed.Observations));
        Assert.Empty(confirmed.UnknownBuffIds);
        Assert.Equal(Minutes(8), Assert.Single(Read(filter, Minutes(8), 30).Observations));
    }

    [Fact]
    public void DifferentImplausiblyLowValuesDoNotConfirmEachOther()
    {
        var filter = new BuffTimerContinuityFilter();
        Read(filter, Minutes(99), 0);

        Assert.Empty(Read(filter, Minutes(9), 10).Observations);
        Assert.Empty(Read(filter, Minutes(5), 20).Observations);
        Assert.Empty(Read(filter, Minutes(10), 30).Observations);
        Assert.Equal(Minutes(98), Assert.Single(Read(filter, Minutes(98), 40).Observations));
    }

    [Fact]
    public void SmallSecondTimerJitterDoesNotExcludeTheRestOfTheCountdown()
    {
        var filter = new BuffTimerContinuityFilter();
        var timers = new[] { 59, 47, 35, 27, 15 };

        for (var index = 0; index < timers.Length; index++)
        {
            var observation = new BuffObservation(Boon, TimeSpan.FromSeconds(timers[index]), TimeSpan.FromSeconds(1));
            var result = Read(filter, observation, index * 10);

            Assert.Equal(observation, Assert.Single(result.Observations));
            Assert.Empty(result.UnknownBuffIds);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    public void CorroboratingLowTimerAllowsSmallJitterInEitherDirection(int secondLowTimer)
    {
        var filter = new BuffTimerContinuityFilter();
        Read(filter, new(Boon, TimeSpan.FromSeconds(59), TimeSpan.FromSeconds(1)), 0);
        Assert.Empty(Read(filter, new(Boon, TimeSpan.FromSeconds(17), TimeSpan.FromSeconds(1)), 10).Observations);
        var observed = new BuffObservation(Boon, TimeSpan.FromSeconds(secondLowTimer), TimeSpan.FromSeconds(1));

        var result = Read(filter, observed, 20);

        Assert.Equal(observed, Assert.Single(result.Observations));
        Assert.Empty(result.UnknownBuffIds);
    }

    [Fact]
    public void FlooredHourToMinuteBoundaryRemainsReadableImmediately()
    {
        var filter = new BuffTimerContinuityFilter();
        Read(filter, new(Boon, TimeSpan.FromHours(2), TimeSpan.FromHours(1)), 0);

        var result = Read(filter, Minutes(119), 10);

        Assert.Equal(Minutes(119), Assert.Single(result.Observations));
        Assert.Empty(result.UnknownBuffIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualTimerIncreaseIsCountedImmediatelyEvenAfterOneBadRead(bool hasBadRead)
    {
        var filter = new BuffTimerContinuityFilter();
        var ledger = new BuffLedger([new(Boon, "Adventurer's Boon", null, TimeSpan.FromHours(2))]);
        Apply(ledger, filter, Minutes(99), 0);
        if (hasBadRead) Apply(ledger, filter, Minutes(9), 10);

        var result = Apply(ledger, filter, Minutes(120), 20);

        Assert.Equal(Minutes(120), Assert.Single(result.Observations));
        Assert.Equal(Start.AddSeconds(20), Assert.Single(ledger.Snapshot.Consumptions).ConsumedAt);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(120)]
    public void LongCaptureGapDoesNotInventCountdownContinuity(int gapSeconds)
    {
        var filter = new BuffTimerContinuityFilter();
        Read(filter, Minutes(99), 0);

        Assert.Equal(Minutes(9), Assert.Single(Read(filter, Minutes(9), gapSeconds).Observations));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(5)]
    public void DuplicateOrOlderCaptureCannotConfirmPendingDropOrMoveClockBackwards(int repeatedAt)
    {
        var filter = new BuffTimerContinuityFilter();
        Read(filter, Minutes(99), 0);
        Read(filter, Minutes(9), 10);

        var repeated = Read(filter, Minutes(9), repeatedAt);
        var recovered = Read(filter, Minutes(98), 20);

        Assert.Empty(repeated.Observations);
        Assert.Contains(Boon, repeated.UnknownBuffIds);
        Assert.Equal(Minutes(98), Assert.Single(recovered.Observations));
    }

    [Fact]
    public void GlobalUnavailableCaptureBreaksContinuity()
    {
        var filter = new BuffTimerContinuityFilter();
        Read(filter, Minutes(99), 0);

        Assert.Null(filter.Filter(null, Start.AddSeconds(10)));
        Assert.Equal(Minutes(9), Assert.Single(Read(filter, Minutes(9), 20).Observations));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrUnclearBuffBreaksOnlyItsOwnContinuity(bool unknownBoon)
    {
        var filter = new BuffTimerContinuityFilter();
        var tenacity = Minutes(99) with { BuffId = "tenacity" };
        filter.Filter(new([Minutes(99), tenacity]), Start);
        filter.Filter(new([tenacity]) { UnknownBuffIds = unknownBoon ? [Boon] : [] }, Start.AddSeconds(10));

        var result = filter.Filter(new([Minutes(9), tenacity with { Remaining = TimeSpan.FromMinutes(9) }]),
            Start.AddSeconds(20))!;

        Assert.Equal(Minutes(9), Assert.Single(result.Observations));
        Assert.Equal("tenacity", Assert.Single(result.UnknownBuffIds));
    }

    [Fact]
    public void UntimestampedScreenshotPassesThroughAndClearsLiveHistory()
    {
        var filter = new BuffTimerContinuityFilter();
        Read(filter, Minutes(99), 0);
        var screenshot = new BuffFrameReading([Minutes(9)]);

        Assert.Same(screenshot, filter.Filter(screenshot, null));
        Assert.Equal(Minutes(9), Assert.Single(Read(filter, Minutes(9), 20).Observations));
    }

    [Fact]
    public void ResetDiscardsAcceptedAndPendingTimerEvidence()
    {
        var filter = new BuffTimerContinuityFilter();
        Read(filter, Minutes(99), 0);
        Read(filter, Minutes(9), 10);

        filter.Reset();

        Assert.Equal(Minutes(5), Assert.Single(Read(filter, Minutes(5), 20).Observations));
    }

    private static BuffObservation Minutes(int remaining) =>
        new(Boon, TimeSpan.FromMinutes(remaining), TimeSpan.FromMinutes(1));

    private static BuffFrameReading Read(BuffTimerContinuityFilter filter, BuffObservation observation, int seconds) =>
        filter.Filter(new([observation]), Start.AddSeconds(seconds))!;

    private static BuffFrameReading Apply(BuffLedger ledger, BuffTimerContinuityFilter filter,
        BuffObservation observation, int seconds)
    {
        var reading = Read(filter, observation, seconds);
        ledger.Apply(reading.Observations, Start.AddSeconds(seconds), _ => null, reading.UnknownBuffIds);
        return reading;
    }
}
