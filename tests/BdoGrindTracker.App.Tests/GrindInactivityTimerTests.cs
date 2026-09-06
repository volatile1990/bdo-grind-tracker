using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class GrindInactivityTimerTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public void IdleClockDoesNotRunBeforeTrackingStarts()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        time.Advance(TimeSpan.FromHours(1));
        timer.RecordDrop();

        Assert.False(timer.IsRunning);
        Assert.Equal(TimeSpan.Zero, timer.IdleDuration);
        Assert.False(timer.ShouldPause(DefaultTimeout));
    }

    [Fact]
    public void TrackingWithoutAnyFirstDropPausesAtExactlyThreeMinutes()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        time.Advance(TimeSpan.FromHours(1));
        timer.Start();
        time.Advance(DefaultTimeout - TimeSpan.FromTicks(1));

        Assert.True(timer.IsRunning);
        Assert.False(timer.ShouldPause(DefaultTimeout));

        time.Advance(TimeSpan.FromTicks(1));

        Assert.Equal(DefaultTimeout, timer.IdleDuration);
        Assert.True(timer.ShouldPause(DefaultTimeout));
    }

    [Fact]
    public void RepeatedStartDoesNotKeepAnIdleSessionAlive()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        timer.Start();
        time.Advance(TimeSpan.FromMinutes(2));
        timer.Start();
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.True(timer.ShouldPause(DefaultTimeout));
    }

    [Fact]
    public void NewConfirmedDropStartsAFreshIdleWindowAtProducerArrival()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        timer.Start();
        time.Advance(TimeSpan.FromMinutes(2));

        timer.RecordDrop();
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromMinutes(1), timer.IdleDuration);
        Assert.False(timer.ShouldPause(DefaultTimeout));

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(timer.ShouldPause(DefaultTimeout));
    }

    [Fact]
    public void PausingDisarmsTimeoutAndResumingStartsAFreshWindow()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        timer.Start();
        time.Advance(TimeSpan.FromMinutes(2));
        timer.Pause();
        time.Advance(TimeSpan.FromHours(1));
        timer.RecordDrop();

        Assert.False(timer.IsRunning);
        Assert.False(timer.ShouldPause(DefaultTimeout));
        Assert.Equal(TimeSpan.Zero, timer.IdleDuration);

        timer.Start();
        Assert.Equal(TimeSpan.Zero, timer.IdleDuration);
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(timer.ShouldPause(DefaultTimeout));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(timer.ShouldPause(DefaultTimeout));
    }

    [Fact]
    public void ResetClearsAndDisarmsExpiredTimer()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        timer.Start();
        time.Advance(DefaultTimeout);
        Assert.True(timer.ShouldPause(DefaultTimeout));

        timer.Reset();

        Assert.False(timer.IsRunning);
        Assert.Equal(TimeSpan.Zero, timer.IdleDuration);
        Assert.False(timer.ShouldPause(DefaultTimeout));
        timer.Start();
        Assert.False(timer.ShouldPause(DefaultTimeout));
    }

    [Fact]
    public void PausingReturnsActualIdleTimeIncludingDelayedTimerChecks()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        timer.Start();
        time.Advance(TimeSpan.FromMinutes(2));
        timer.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(191.25));

        Assert.Equal(TimeSpan.FromSeconds(191.25), timer.PauseAndGetIdleDuration());
        Assert.False(timer.IsRunning);
        Assert.Equal(TimeSpan.Zero, timer.IdleDuration);
        Assert.False(timer.ShouldPause(DefaultTimeout));
        time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.Zero, timer.PauseAndGetIdleDuration());
    }

    [Fact]
    public void DropArrivingAfterTimeoutCheckStillUpdatesFinalIdleCutoff()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        timer.Start();
        time.Advance(DefaultTimeout);
        Assert.True(timer.ShouldPause(DefaultTimeout));

        // An already-running frame may still produce a counted drop while stopping.
        time.Advance(TimeSpan.FromMilliseconds(50));
        timer.RecordDrop();
        time.Advance(TimeSpan.FromMilliseconds(20));

        Assert.Equal(TimeSpan.FromMilliseconds(20), timer.PauseAndGetIdleDuration());
    }

    [Fact]
    public void NoDropAfterResumeReturnsOnlyTheNewWaitWindow()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        timer.Start();
        time.Advance(TimeSpan.FromMinutes(1));
        timer.RecordDrop();
        timer.Pause();
        time.Advance(TimeSpan.FromHours(2));
        timer.Start();
        time.Advance(DefaultTimeout);

        Assert.Equal(DefaultTimeout, timer.PauseAndGetIdleDuration());
    }

    [Fact]
    public void PausingInactiveTimerReturnsZero()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.Zero, timer.PauseAndGetIdleDuration());
    }

    [Fact]
    public void TimeoutCanChangeWithoutLosingCurrentInactivity()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        timer.Start();
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.False(timer.ShouldPause(TimeSpan.FromMinutes(3)));
        Assert.True(timer.ShouldPause(TimeSpan.FromMinutes(1)));
        Assert.False(timer.ShouldPause(TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromMinutes(2), timer.IdleDuration);
    }

    [Fact]
    public void WallClockAdjustmentsDoNotAffectTheTimeout()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        timer.Start();
        time.Advance(TimeSpan.FromMinutes(2));

        time.UtcNow = DateTimeOffset.UnixEpoch.AddYears(30);
        Assert.False(timer.ShouldPause(DefaultTimeout));
        time.UtcNow = DateTimeOffset.UnixEpoch.AddYears(-30);
        Assert.False(timer.ShouldPause(DefaultTimeout));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(timer.ShouldPause(DefaultTimeout));
    }

    [Fact]
    public void ProducerActivityAndUiChecksCanRunConcurrently()
    {
        var time = new ManualTimeProvider();
        var timer = new GrindInactivityTimer(time);
        timer.Start();
        time.Advance(TimeSpan.FromSeconds(1));

        Parallel.For(0, 1000, index =>
        {
            if (index % 2 == 0)
            {
                timer.RecordDrop();
            }
            else
            {
                Assert.False(timer.ShouldPause(DefaultTimeout));
                Assert.InRange(timer.IdleDuration, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            }
        });

        Assert.Equal(TimeSpan.Zero, timer.IdleDuration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TimeoutMustBePositive(int seconds)
    {
        var timer = new GrindInactivityTimer(new ManualTimeProvider());
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.ShouldPause(TimeSpan.FromSeconds(seconds)));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
    }
}
