using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class GrindSessionClockTests
{
    [Fact]
    public void NewClockIsStoppedAtZero()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);

        time.Advance(TimeSpan.FromHours(1));

        Assert.False(clock.IsRunning);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public void RunningClockMeasuresMonotonicTimeWithoutLosingFractions()
    {
        var time = new ManualTimeProvider();
        time.Advance(TimeSpan.FromMinutes(10));
        var clock = new GrindSessionClock(time);
        clock.Start();

        time.Advance(TimeSpan.FromMilliseconds(1250));

        Assert.True(clock.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), clock.Elapsed);
    }

    [Fact]
    public void RepeatedStartDoesNotDiscardElapsedTime()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromSeconds(3));

        clock.Start();
        time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(5), clock.Elapsed);
    }

    [Fact]
    public void PauseFreezesTheClockAndStartResumesWithoutCountingDowntime()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromSeconds(10));

        clock.Pause();
        time.Advance(TimeSpan.FromMinutes(30));

        Assert.False(clock.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Elapsed);

        clock.Start();
        time.Advance(TimeSpan.FromSeconds(15));

        Assert.True(clock.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(25), clock.Elapsed);
    }

    [Fact]
    public void RepeatedPauseDoesNotAddDowntime()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Pause();
        clock.Start();
        time.Advance(TimeSpan.FromSeconds(1));
        clock.Pause();

        time.Advance(TimeSpan.FromMinutes(1));
        clock.Pause();

        Assert.Equal(TimeSpan.FromSeconds(1), clock.Elapsed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetStopsAndClearsRunningOrPausedSessions(bool pauseBeforeReset)
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromHours(2));
        if (pauseBeforeReset)
        {
            clock.Pause();
        }

        clock.Reset();
        time.Advance(TimeSpan.FromHours(1));

        Assert.False(clock.IsRunning);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);

        clock.Start();
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.FromSeconds(4), clock.Elapsed);
    }

    [Fact]
    public void AutomaticPauseExcludesTheEntireTrailingIdleInterval()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromMinutes(12));
        time.Advance(TimeSpan.FromSeconds(193.75));

        clock.Pause(TimeSpan.FromSeconds(193.75));
        time.Advance(TimeSpan.FromHours(1));

        Assert.False(clock.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(12), clock.Elapsed);
    }

    [Theory]
    [InlineData(180)]
    [InlineData(181)]
    [InlineData(3600)]
    public void ExcludingAnEntireResumedSegmentPreservesEarlierActiveTime(int idleSeconds)
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromMinutes(10));
        clock.Pause();
        time.Advance(TimeSpan.FromHours(2));
        clock.Start();
        time.Advance(TimeSpan.FromMinutes(3));

        clock.Pause(TimeSpan.FromSeconds(idleSeconds));

        Assert.Equal(TimeSpan.FromMinutes(10), clock.Elapsed);
        clock.Start();
        time.Advance(TimeSpan.FromMinutes(5));
        clock.Pause(TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(12), clock.Elapsed);
    }

    [Fact]
    public void ExcludingMoreThanFirstSegmentNeverCreatesNegativeDuration()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromMinutes(3));

        clock.Pause(TimeSpan.FromMinutes(4));

        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public void RepeatedAutomaticPauseCannotSubtractTwice()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromMinutes(5));
        clock.Pause(TimeSpan.FromMinutes(3));
        time.Advance(TimeSpan.FromMinutes(3));

        clock.Pause(TimeSpan.FromMinutes(3));

        Assert.Equal(TimeSpan.FromMinutes(2), clock.Elapsed);
    }

    [Fact]
    public void NegativeIdleExclusionIsRejectedWithoutChangingRunningState()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Pause(TimeSpan.FromTicks(-1)));

        Assert.True(clock.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(1), clock.Elapsed);
    }

    [Fact]
    public void UploadCutoffExcludesPendingIdleWithoutChangingDisplayedDuration()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromMinutes(59));
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(59), clock.GetElapsedExcludingTrailingIdle(TimeSpan.FromMinutes(2)));
        Assert.Equal(TimeSpan.FromMinutes(61), clock.Elapsed);
        Assert.True(clock.IsRunning);

        clock.Pause(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(59), clock.GetElapsedExcludingTrailingIdle(TimeSpan.FromMinutes(2)));
        clock.Start();
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromMinutes(59), clock.GetElapsedExcludingTrailingIdle(TimeSpan.FromMinutes(3)));
    }

    [Fact]
    public void WallClockAdjustmentsDoNotAffectSessionTime()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromSeconds(10));

        time.UtcNow = DateTimeOffset.UnixEpoch.AddYears(20);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Elapsed);
        time.UtcNow = DateTimeOffset.UnixEpoch.AddYears(-20);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Elapsed);
    }

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(59, "00:00:59")]
    [InlineData(60, "00:01:00")]
    [InlineData(3661, "01:01:01")]
    [InlineData(86399, "23:59:59")]
    [InlineData(86400, "24:00:00")]
    [InlineData(360061, "100:01:01")]
    public void DisplayUsesTotalHoursWithoutWrappingAtMidnight(int seconds, string expected)
    {
        Assert.Equal(expected, GrindSessionClock.FormatElapsed(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void DisplayTruncatesSubseconds()
    {
        Assert.Equal("00:00:59", GrindSessionClock.FormatElapsed(TimeSpan.FromMilliseconds(59999)));
    }

    [Fact]
    public void DisplayRejectsNegativeDurations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GrindSessionClock.FormatElapsed(TimeSpan.FromTicks(-1)));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
