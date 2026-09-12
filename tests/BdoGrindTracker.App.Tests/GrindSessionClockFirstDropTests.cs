using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class GrindSessionClockFirstDropTests
{
    [Fact]
    public void ArmedClockExcludesTheEntireWaitBeforeItsFirstDrop()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        time.Advance(TimeSpan.FromHours(1));
        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.True(clock.IsRunning);
        Assert.True(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
        Assert.Equal(TimeSpan.Zero, clock.GetElapsedExcludingTrailingIdle(TimeSpan.FromSeconds(30)));

        clock.RecordDrop();

        Assert.True(clock.IsRunning);
        Assert.False(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
        time.Advance(TimeSpan.FromMilliseconds(1250));
        Assert.Equal(TimeSpan.FromMilliseconds(1250), clock.Elapsed);
        Assert.Equal(TimeSpan.FromMilliseconds(1000),
            clock.GetElapsedExcludingTrailingIdle(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void RepeatedStartsAndDropsNeitherBypassTheWaitNorMoveTheFirstDropTime()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromSeconds(20));
        clock.Start();
        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromSeconds(40));

        Assert.True(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);

        clock.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(3));
        clock.Start(waitForFirstDrop: true);
        clock.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(2));
        clock.Start();
        clock.RecordDrop();

        Assert.False(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.FromSeconds(5), clock.Elapsed);
        clock.Pause();
        Assert.Equal(TimeSpan.FromSeconds(5), clock.Elapsed);
    }

    [Fact]
    public void WaitingAfterResumePreservesEarlierTimeAndExcludesOnlyTheNewWait()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromMinutes(1));
        clock.RecordDrop();
        time.Advance(TimeSpan.FromMinutes(10));
        clock.Pause();
        time.Advance(TimeSpan.FromHours(2));

        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.True(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.FromMinutes(10), clock.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(10),
            clock.GetElapsedExcludingTrailingIdle(TimeSpan.FromHours(3)));

        clock.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(15));

        Assert.Equal(TimeSpan.FromSeconds(615), clock.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(610),
            clock.GetElapsedExcludingTrailingIdle(TimeSpan.FromSeconds(5)));
        clock.Pause(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(610), clock.Elapsed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(180)]
    [InlineData(3600)]
    public void PausingWithoutAnyDropDisarmsTheClockAtZero(int excludedSeconds)
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromMinutes(3));

        clock.Pause(TimeSpan.FromSeconds(excludedSeconds));
        time.Advance(TimeSpan.FromHours(1));
        clock.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.False(clock.IsRunning);
        Assert.False(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetDisarmsBothWaitingAndStartedSegmentsAgainstLateDrops(bool hasFirstDrop)
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.RestorePaused(TimeSpan.FromMinutes(5));
        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromMinutes(1));
        if (hasFirstDrop) clock.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(20));

        clock.Reset();
        clock.RecordDrop();
        time.Advance(TimeSpan.FromHours(1));

        Assert.False(clock.IsRunning);
        Assert.False(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);

        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
        clock.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.FromSeconds(4), clock.Elapsed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreDisarmsThePreviousSegmentAndKeepsSavedTimeUntilAFreshStart(bool hasFirstDrop)
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromMinutes(1));
        if (hasFirstDrop) clock.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(20));

        clock.RestorePaused(TimeSpan.FromMinutes(5));
        clock.RecordDrop();
        time.Advance(TimeSpan.FromHours(1));

        Assert.False(clock.IsRunning);
        Assert.False(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.FromMinutes(5), clock.Elapsed);

        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(5), clock.Elapsed);
        clock.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(310), clock.Elapsed);
    }

    [Fact]
    public void DropBeforeTrackingOrAfterAnActivePauseCannotStartTheClock()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.RecordDrop();
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(clock.IsRunning);
        Assert.False(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);

        clock.Start(waitForFirstDrop: true);
        clock.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(10));
        clock.Pause();
        time.Advance(TimeSpan.FromMinutes(1));
        clock.RecordDrop();
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.False(clock.IsRunning);
        Assert.False(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Elapsed);
    }

    [Fact]
    public void WallClockChangesCannotTurnWaitingIntoGrindTimeOrMoveTheFirstDrop()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        clock.Start(waitForFirstDrop: true);
        time.Advance(TimeSpan.FromMinutes(1));
        time.UtcNow = DateTimeOffset.UnixEpoch.AddYears(30);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);

        clock.RecordDrop();
        time.Advance(TimeSpan.FromMilliseconds(1250));
        time.UtcNow = DateTimeOffset.UnixEpoch.AddYears(-30);

        Assert.Equal(TimeSpan.FromMilliseconds(1250), clock.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(1),
            clock.GetElapsedExcludingTrailingIdle(TimeSpan.FromMilliseconds(250)));
        clock.Pause(TimeSpan.FromMilliseconds(250));
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Elapsed);
    }

    [Fact]
    public void AutomaticPauseRemovesOnlyIdleAfterTheFirstDropAndPreservesEarlierSegments()
    {
        var time = new ManualTimeProvider();
        var clock = new GrindSessionClock(time);
        var inactivity = new GrindInactivityTimer(time);
        clock.RestorePaused(TimeSpan.FromMinutes(5));
        clock.Start(waitForFirstDrop: true);
        inactivity.Start();
        time.Advance(TimeSpan.FromMinutes(2));
        clock.RecordDrop();
        inactivity.RecordDrop();
        time.Advance(TimeSpan.FromMinutes(12));
        clock.RecordDrop();
        inactivity.RecordDrop();
        time.Advance(TimeSpan.FromSeconds(193.75));

        Assert.True(inactivity.ShouldPause(TimeSpan.FromMinutes(3)));
        Assert.Equal(TimeSpan.FromMinutes(17),
            clock.GetElapsedExcludingTrailingIdle(inactivity.IdleDuration));
        clock.Pause(inactivity.PauseAndGetIdleDuration());

        Assert.False(clock.IsRunning);
        Assert.False(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.FromMinutes(17), clock.Elapsed);

        time.Advance(TimeSpan.FromHours(1));
        clock.Start(waitForFirstDrop: true);
        inactivity.Start();
        time.Advance(TimeSpan.FromMinutes(3));
        Assert.True(inactivity.ShouldPause(TimeSpan.FromMinutes(3)));
        Assert.Equal(TimeSpan.FromMinutes(17), clock.Elapsed);
        clock.Pause(inactivity.PauseAndGetIdleDuration());
        clock.RecordDrop();
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.False(clock.IsRunning);
        Assert.False(clock.IsWaitingForFirstDrop);
        Assert.Equal(TimeSpan.FromMinutes(17), clock.Elapsed);
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
