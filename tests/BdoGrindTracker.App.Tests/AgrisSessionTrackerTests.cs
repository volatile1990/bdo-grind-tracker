using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class AgrisSessionTrackerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-10T12:00:00Z");

    [Fact]
    public void CountsOnlyIntervalsConfirmedByTwoSamplesAndDoesNotExtrapolate()
    {
        var tracker = new AgrisSessionTracker();
        AssertDuration(Update(tracker, 3, AgrisStatus.Active), 0, 0);
        AssertDuration(Update(tracker, 8, AgrisStatus.Active), 5, 5);
        AssertDuration(tracker.Snapshot(TimeSpan.FromSeconds(100)), 5, 5);
    }

    [Fact]
    public void RepeatedUiSnapshotsDoNotCreateAdditionalSamplesOrBreakTheBaseline()
    {
        var tracker = new AgrisSessionTracker();
        var sample = new AgrisState(AgrisStatus.Active, Start);
        tracker.Update(TimeSpan.Zero, sample, true, Start);
        for (var second = 1; second < 5; second++)
            AssertDuration(tracker.Update(TimeSpan.FromSeconds(second), sample, true, Start.AddSeconds(second)), 0, 0);

        AssertDuration(Update(tracker, 5, AgrisStatus.Active), 5, 5);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(7), new(AgrisStatus.Active, Start.AddSeconds(5)),
            true, Start.AddSeconds(7)), 5, 5);
    }

    [Fact]
    public void InactiveAndTransitionIntervalsRemainObservedWithoutClaimingActiveTime()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Inactive);
        AssertDuration(Update(tracker, 5, AgrisStatus.Inactive), 0, 5);
        AssertDuration(Update(tracker, 10, AgrisStatus.Active), 0, 10);
        AssertDuration(Update(tracker, 15, AgrisStatus.Active), 5, 15);
        AssertDuration(Update(tracker, 20, AgrisStatus.Inactive), 5, 20);
    }

    [Fact]
    public void HiddenHudAndUnknownSamplesDoNotBridgePreviouslyActiveTime()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        Update(tracker, 5, AgrisStatus.Active);
        AssertDuration(Update(tracker, 8, AgrisStatus.Unknown), 5, 5);
        AssertDuration(Update(tracker, 10, AgrisStatus.Active), 5, 5);
        AssertDuration(Update(tracker, 15, AgrisStatus.Active), 10, 10);
    }

    [Fact]
    public void AnOlderQueuedFrameCannotBackfillAcrossHudLoss()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        Update(tracker, 5, AgrisStatus.Unknown);

        AssertDuration(tracker.Update(TimeSpan.FromSeconds(6), new(AgrisStatus.Active, Start.AddSeconds(4)),
            true, Start.AddSeconds(6)), 0, 0);
        AssertDuration(Update(tracker, 10, AgrisStatus.Active), 0, 0);
        AssertDuration(Update(tracker, 15, AgrisStatus.Active), 5, 5);
    }

    [Fact]
    public void PausedTrackingNeverCountsAndResumeNeedsTwoNewSamples()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        Update(tracker, 5, AgrisStatus.Active);
        tracker.Pause(TimeSpan.FromSeconds(7));
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(7), new(AgrisStatus.Active, Start.AddSeconds(10)),
            false, Start.AddSeconds(10)), 5, 5);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(8), new(AgrisStatus.Active, Start.AddSeconds(11)),
            true, Start.AddSeconds(11)), 5, 5);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(13), new(AgrisStatus.Active, Start.AddSeconds(16)),
            true, Start.AddSeconds(16)), 10, 10);
    }

    [Fact]
    public void PausingWithoutAnotherUiTickStillBreaksTheSampleInterval()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        Update(tracker, 5, AgrisStatus.Active);
        tracker.Pause(TimeSpan.FromSeconds(6));

        AssertDuration(tracker.Update(TimeSpan.FromSeconds(8), new(AgrisStatus.Active, Start.AddSeconds(10)),
            true, Start.AddSeconds(10)), 5, 5);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(13), new(AgrisStatus.Active, Start.AddSeconds(15)),
            true, Start.AddSeconds(15)), 10, 10);
    }

    [Fact]
    public void StaleAndUnstampedActiveStatesCannotContributeTime()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(16), new(AgrisStatus.Active, Start),
            true, Start.AddSeconds(16)), 0, 0);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(20), new(AgrisStatus.Active),
            true, Start.AddSeconds(20)), 0, 0);
        AssertDuration(Update(tracker, 25, AgrisStatus.Active), 0, 0);
        AssertDuration(Update(tracker, 30, AgrisStatus.Active), 5, 5);
    }

    [Fact]
    public void FutureTimestampsAreNotAcceptedAsHudEvidence()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(5), new(AgrisStatus.Active, Start.AddSeconds(10)),
            true, Start.AddSeconds(5)), 0, 0);
        AssertDuration(Update(tracker, 10, AgrisStatus.Active), 0, 0);
        AssertDuration(Update(tracker, 15, AgrisStatus.Active), 5, 5);
    }

    [Theory]
    [InlineData(15, 15)]
    [InlineData(16, 0)]
    public void ObservationGapCannotExceedTheMaximumFreshnessWindow(int gapSeconds, int expectedSeconds)
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        AssertDuration(Update(tracker, gapSeconds, AgrisStatus.Active), expectedSeconds, expectedSeconds);
    }

    [Fact]
    public void DelayedUiProcessingUsesCaptureTimeAndDoesNotTrimTheSessionToAnOlderFrame()
    {
        var tracker = new AgrisSessionTracker();
        tracker.Update(TimeSpan.FromSeconds(2), new(AgrisStatus.Active, Start), true, Start.AddSeconds(2));
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(9), new(AgrisStatus.Active, Start.AddSeconds(5)),
            true, Start.AddSeconds(9)), 5, 5);
        AssertDuration(tracker.Snapshot(TimeSpan.FromSeconds(12)), 5, 5);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(13), new(AgrisStatus.Active, Start.AddSeconds(10)),
            true, Start.AddSeconds(13)), 10, 10);
    }

    [Fact]
    public void DelayedOlderSamplesDoNotReplaceTheCurrentBaseline()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        Update(tracker, 5, AgrisStatus.Active);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(7), new(AgrisStatus.Inactive, Start.AddSeconds(3)),
            true, Start.AddSeconds(7)), 5, 5);
        AssertDuration(Update(tracker, 10, AgrisStatus.Active), 10, 10);
    }

    [Fact]
    public void MismatchedSessionAndSampleClocksDoNotOvercountAgrisTime()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(100), new(AgrisStatus.Active, Start.AddSeconds(5)),
            true, Start.AddSeconds(5)), 0, 0);
        AssertDuration(tracker.Update(TimeSpan.FromSeconds(105), new(AgrisStatus.Active, Start.AddSeconds(10)),
            true, Start.AddSeconds(10)), 5, 5);
    }

    [Fact]
    public void SmallClockToleranceNeverAddsMoreThanTheCapturedInterval()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);

        AssertDuration(tracker.Update(TimeSpan.FromSeconds(5.1), new(AgrisStatus.Active, Start.AddSeconds(5)),
            true, Start.AddSeconds(5)), 5, 5);
    }

    [Fact]
    public void AutomaticPauseRemovesTheActualAgrisTailInsteadOfOnlyClampingTheSum()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Inactive);
        Update(tracker, 5, AgrisStatus.Inactive);
        Update(tracker, 10, AgrisStatus.Active);
        Update(tracker, 15, AgrisStatus.Active);
        AssertDuration(Update(tracker, 20, AgrisStatus.Active), 10, 20);

        tracker.Pause(TimeSpan.FromSeconds(8));

        AssertDuration(tracker.Snapshot(TimeSpan.FromSeconds(8)), 0, 8);
    }

    [Fact]
    public void SnapshotCanTrimInsideAnActiveIntervalAndDoesNotRestoreRemovedTime()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        Update(tracker, 5, AgrisStatus.Active);
        Update(tracker, 10, AgrisStatus.Active);
        AssertDuration(tracker.Snapshot(TimeSpan.FromSeconds(7)), 7, 7);
        AssertDuration(tracker.Snapshot(TimeSpan.FromSeconds(12)), 7, 7);
        AssertDuration(Update(tracker, 15, AgrisStatus.Active), 7, 7);
        AssertDuration(Update(tracker, 20, AgrisStatus.Active), 12, 12);
    }

    [Fact]
    public void ResetStartsANewSessionWithoutOldIntervalsOrSampleTimestamps()
    {
        var tracker = new AgrisSessionTracker();
        Update(tracker, 0, AgrisStatus.Active);
        Update(tracker, 5, AgrisStatus.Active);

        tracker.Reset();

        AssertDuration(tracker.Snapshot(TimeSpan.Zero), 0, 0);
        AssertDuration(Update(tracker, 0, AgrisStatus.Inactive), 0, 0);
        AssertDuration(Update(tracker, 5, AgrisStatus.Inactive), 0, 5);
    }

    private static AgrisSessionDuration Update(AgrisSessionTracker tracker, double seconds, AgrisStatus status) =>
        tracker.Update(TimeSpan.FromSeconds(seconds), new(status, Start.AddSeconds(seconds)), true, Start.AddSeconds(seconds));

    private static void AssertDuration(AgrisSessionDuration duration, double activeSeconds, double observedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(activeSeconds), duration.ActiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(observedSeconds), duration.ObservedDuration);
    }
}
