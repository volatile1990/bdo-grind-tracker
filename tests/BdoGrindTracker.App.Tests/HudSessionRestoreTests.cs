using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class HudSessionRestoreTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-11T12:00:00Z");

    [Fact]
    public void AgrisRestoreKeepsEarnedTimeAndNeedsTwoFreshSamplesAfterOfflineGap()
    {
        var tracker = new AgrisSessionTracker();
        tracker.Update(TimeSpan.Zero, new(AgrisStatus.Active, Start), true, Start);
        tracker.Update(Seconds(5), new(AgrisStatus.Active, Start.AddSeconds(5)), true, Start.AddSeconds(5));
        var restored = new AgrisSessionDuration(Seconds(120), Seconds(300));
        tracker.Restore(Seconds(600), restored);
        Assert.Equal(restored, tracker.Snapshot(Seconds(600)));
        var resumedAt = Start.AddDays(1);

        Assert.Equal(restored, tracker.Update(Seconds(600), new(AgrisStatus.Active, resumedAt), true, resumedAt));
        Assert.Equal(new(Seconds(125), Seconds(305)),
            tracker.Update(Seconds(605), new(AgrisStatus.Active, resumedAt.AddSeconds(5)), true, resumedAt.AddSeconds(5)));
        tracker.Pause(Seconds(601));
        Assert.Equal(new(Seconds(121), Seconds(301)), tracker.Snapshot(Seconds(601)));
        tracker.Pause(TimeSpan.Zero);
        Assert.Equal(restored, tracker.Snapshot(TimeSpan.Zero));
    }

    [Fact]
    public void AgrisRestoreRejectsDelayedSamplesFromBeforeTheRestoredClockBoundary()
    {
        var tracker = new AgrisSessionTracker();
        var restored = new AgrisSessionDuration(Seconds(10), Seconds(20));
        tracker.Restore(Seconds(100), restored);
        Assert.Equal(restored, tracker.Update(Seconds(101), new(AgrisStatus.Active, Start.AddSeconds(-5)), true, Start));
        Assert.Equal(restored, tracker.Update(Seconds(104), new(AgrisStatus.Active, Start.AddSeconds(3)), true, Start.AddSeconds(3)));
        Assert.Equal(new(Seconds(15), Seconds(25)),
            tracker.Update(Seconds(109), new(AgrisStatus.Active, Start.AddSeconds(8)), true, Start.AddSeconds(8)));
    }

    [Theory]
    [InlineData(-1, 0, 10)]
    [InlineData(0, -1, 10)]
    [InlineData(5, 4, 10)]
    [InlineData(0, 11, 10)]
    [InlineData(0, 0, -1)]
    public void InvalidAgrisRestoreLeavesExistingAggregateIntact(int active, int observed, int elapsed)
    {
        var tracker = new AgrisSessionTracker();
        var restored = new AgrisSessionDuration(Seconds(3), Seconds(5));
        tracker.Restore(Seconds(10), restored);
        Assert.ThrowsAny<ArgumentException>(() => tracker.Restore(Seconds(elapsed), new(Seconds(active), Seconds(observed))));
        Assert.Equal(restored, tracker.Snapshot(Seconds(10)));
    }

    [Fact]
    public void ExperienceRestoreClearsPendingLevelUpAndRetainsSavedGainWhenNewIdleTailIsTrimmed()
    {
        var tracker = new ExperienceSessionTracker();
        tracker.Update(TimeSpan.Zero, new(62, 99m, Start), true, Start);
        tracker.Update(Seconds(60), new(63, .5m, Start.AddSeconds(60)), true, Start.AddSeconds(60));
        var restored = new ExperienceSessionProgress(1.5m, Seconds(300), 61, 62);
        tracker.Restore(Seconds(600), restored);
        var resumedAt = Start.AddDays(1);

        Assert.Equal(restored, tracker.Update(Seconds(600), new(63, 1m, resumedAt), true, resumedAt));
        Assert.Equal(new(1.6m, Seconds(360), 61, 63),
            tracker.Update(Seconds(660), new(63, 1.1m, resumedAt.AddSeconds(60)), true, resumedAt.AddSeconds(60)));
        tracker.Pause(Seconds(650));
        Assert.Equal(restored, tracker.Snapshot(Seconds(650)));
        tracker.Pause(TimeSpan.Zero);
        Assert.Equal(restored, tracker.Snapshot(TimeSpan.Zero));
    }

    [Fact]
    public void ExperienceRestorePreservesMeasuredZeroAndSignedLoss()
    {
        var tracker = new ExperienceSessionTracker();
        foreach (var gain in new[] { 0m, -.25m })
        {
            var restored = new ExperienceSessionProgress(gain, Seconds(60), 65, 65);
            tracker.Restore(Seconds(100), restored);
            Assert.Equal(restored, tracker.Snapshot(Seconds(100)));
            Assert.Equal(restored, tracker.Update(Seconds(100), new(65, 20m, Start), true, Start));
            Assert.Equal(new(gain + .1m, Seconds(120), 65, 65),
                tracker.Update(Seconds(160), new(65, 20.1m, Start.AddSeconds(60)), true, Start.AddSeconds(60)));
        }
    }

    [Fact]
    public void UnknownExperienceRestoreStillRequiresNewObservedEndpoints()
    {
        var tracker = new ExperienceSessionTracker();
        tracker.Restore(Seconds(600), default);
        Assert.Equal(default, tracker.Snapshot(Seconds(600)));
        Assert.Null(tracker.Update(Seconds(600), new(65, 20m, Start), true, Start).GainedPercentagePoints);
        Assert.Equal(new(.2m, Seconds(60), 65, 65),
            tracker.Update(Seconds(660), new(65, 20.2m, Start.AddSeconds(60)), true, Start.AddSeconds(60)));
    }

    [Fact]
    public void ExperienceRestoreRejectsQueuedSamplesFromBeforeTheRestoredClockBoundary()
    {
        var tracker = new ExperienceSessionTracker();
        var restored = new ExperienceSessionProgress(.5m, Seconds(60), 65, 65);
        tracker.Restore(Seconds(100), restored);
        Assert.Equal(restored, tracker.Update(Seconds(101), new(65, 10m, Start.AddSeconds(-5)), true, Start));
        Assert.Equal(restored, tracker.Update(Seconds(160), new(65, 20m, Start.AddSeconds(59)), true, Start.AddSeconds(59)));
        Assert.Equal(new(.6m, Seconds(120), 65, 65),
            tracker.Update(Seconds(220), new(65, 20.1m, Start.AddSeconds(119)), true, Start.AddSeconds(119)));
    }

    [Fact]
    public void InvalidExperienceRestoreDoesNotMutateSavedProgressOrLiveBaseline()
    {
        var tracker = new ExperienceSessionTracker();
        var restored = new ExperienceSessionProgress(.5m, Seconds(60), 65, 65);
        tracker.Restore(Seconds(100), restored);
        tracker.Update(Seconds(100), new(65, 20m, Start), true, Start);
        ExperienceSessionProgress[] malformed =
        [
            new(null, Seconds(1), null, null),
            new(null, TimeSpan.Zero, 65, 65),
            new(.1m, TimeSpan.Zero, 65, 65),
            new(.1m, Seconds(-1), 65, 65),
            new(.1m, Seconds(101), 65, 65),
            new(.1m, Seconds(60), null, 65),
            new(.1m, Seconds(60), 65, null),
            new(.1m, Seconds(60), 0, 65),
            new(.1m, Seconds(60), 65, 101),
        ];
        foreach (var progress in malformed)
            Assert.Throws<ArgumentException>(() => tracker.Restore(Seconds(100), progress));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Restore(Seconds(-1), default));
        Assert.Equal(restored, tracker.Snapshot(Seconds(100)));
        Assert.Equal(new(.6m, Seconds(120), 65, 65),
            tracker.Update(Seconds(160), new(65, 20.1m, Start.AddSeconds(60)), true, Start.AddSeconds(60)));
    }

    [Fact]
    public void ResetAndRepeatedRestoreNeverCarryDuplicateHudTotals()
    {
        var agris = new AgrisSessionTracker();
        var experience = new ExperienceSessionTracker();
        var duration = new AgrisSessionDuration(Seconds(5), Seconds(10));
        var progress = new ExperienceSessionProgress(.1m, Seconds(10), 65, 65);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            agris.Restore(Seconds(20), duration);
            experience.Restore(Seconds(20), progress);
            Assert.Equal(duration, agris.Snapshot(Seconds(20)));
            Assert.Equal(progress, experience.Snapshot(Seconds(20)));
        }
        agris.Reset();
        experience.Reset();
        Assert.Equal(default, agris.Snapshot(TimeSpan.Zero));
        Assert.Equal(default, experience.Snapshot(TimeSpan.Zero));
    }

    private static TimeSpan Seconds(int seconds) => TimeSpan.FromSeconds(seconds);
}
