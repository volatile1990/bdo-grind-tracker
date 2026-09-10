using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class ExperienceSessionTrackerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-10T12:00:00Z");

    [Fact]
    public void StartupUnknownDoesNotDiscardTheFirstFrameWhileItsOcrIsStillRunning()
    {
        var tracker = new ExperienceSessionTracker();
        tracker.Update(TimeSpan.FromSeconds(1), ExperienceState.Unknown, true, Start.AddSeconds(1));

        AssertProgress(tracker.Update(TimeSpan.FromSeconds(2), new(62, 10m, Start),
            true, Start.AddSeconds(2)), null, 0, null, null);
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(62), new(62, 10.125m, Start.AddSeconds(60)),
            true, Start.AddSeconds(62)), .125m, 60, 62, 62);
    }

    [Fact]
    public void FirstSampleIsUnknownProgressAndAnUnchangedSecondSampleIsMeasuredZero()
    {
        var tracker = new ExperienceSessionTracker();
        AssertProgress(Update(tracker, 0, 62, 12.345m), null, 0, null, null);
        AssertProgress(Update(tracker, 60, 62, 12.345m), 0, 60, 62, 62);
        AssertProgress(tracker.Snapshot(TimeSpan.FromSeconds(120)), 0, 60, 62, 62);
    }

    [Fact]
    public void PercentagePointChangesRetainDecimalPrecisionAndDoNotImposeAGainRateLimit()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 2, 1.123m);
        AssertProgress(Update(tracker, 60, 2, 95.456m), 94.333m, 60, 2, 2);
    }

    [Fact]
    public void DeathLossAtTheSameLevelIsNegativeAndNeverBecomesAnInferredLevelUp()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 99.9m);
        AssertProgress(Update(tracker, 60, 62, 1.2m), -98.7m, 60, 62, 62);
        AssertProgress(Update(tracker, 120, 62, 1.3m), -98.6m, 120, 62, 62);
    }

    [Fact]
    public void ATransientPercentageOutlierCorrectsItselfInTheNetTotal()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        Update(tracker, 60, 62, 80m);
        AssertProgress(Update(tracker, 120, 62, 10.125m), .125m, 120, 62, 62);
    }

    [Fact]
    public void RepeatedUiTicksNeitherAddProgressNorBreakTheCurrentBaseline()
    {
        var tracker = new ExperienceSessionTracker();
        var sample = new ExperienceState(62, 15m, Start);
        tracker.Update(TimeSpan.Zero, sample, true, Start);
        for (var seconds = 1; seconds < 60; seconds++)
            AssertProgress(tracker.Update(TimeSpan.FromSeconds(seconds), sample, true, Start.AddSeconds(seconds)), null, 0, null, null);
        AssertProgress(Update(tracker, 60, 62, 15.1m), .1m, 60, 62, 62);
    }

    [Fact]
    public void LevelUpNeedsASecondFreshSampleAtTheNewLevelBeforeAddingOneHundredPoints()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 99m);
        AssertProgress(Update(tracker, 60, 63, .25m), null, 0, null, null);
        AssertProgress(Update(tracker, 120, 63, .5m), 1.5m, 120, 62, 63);
    }

    [Fact]
    public void LossAfterAConfirmedLevelUpRemainsPartOfTheNetProgress()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 99m);
        Update(tracker, 60, 63, .5m);
        AssertProgress(Update(tracker, 120, 63, .2m), 1.2m, 120, 62, 63);
    }

    [Fact]
    public void AOneSampleWrongLevelCannotManufactureAFullExperienceBar()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        Update(tracker, 60, 63, 80m);
        AssertProgress(Update(tracker, 120, 62, 10.1m), null, 0, null, null);
        AssertProgress(Update(tracker, 180, 62, 10.2m), .1m, 60, 62, 62);
    }

    [Theory]
    [InlineData(61)]
    [InlineData(64)]
    public void ALowerLevelOrMultiLevelJumpStartsANewBaselineWithoutCrossCharacterGain(int nextLevel)
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        AssertProgress(Update(tracker, 60, nextLevel, 80m), null, 0, null, null);
        AssertProgress(Update(tracker, 120, nextLevel, 81m), 1m, 60, nextLevel, nextLevel);
    }

    [Fact]
    public void AnExpiredLevelUpConfirmationDoesNotRecoverTheOldInterval()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 99m);
        Update(tracker, 60, 63, .5m);
        AssertProgress(Update(tracker, 211, 63, 1m), null, 0, null, null);
        AssertProgress(Update(tracker, 271, 63, 1.1m), .1m, 60, 63, 63);
    }

    [Fact]
    public void UnknownBreaksEvenAPendingLevelUpAndRejectsQueuedFramesFromBeforeTheGap()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 99m);
        Update(tracker, 60, 63, .5m);
        tracker.Update(TimeSpan.FromSeconds(70), ExperienceState.Unknown, true, Start.AddSeconds(70));
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(80), new(63, .6m, Start.AddSeconds(65)),
            true, Start.AddSeconds(80)), null, 0, null, null);
        AssertProgress(Update(tracker, 120, 63, .7m), null, 0, null, null);
        AssertProgress(Update(tracker, 180, 63, .8m), .1m, 60, 63, 63);
    }

    [Fact]
    public void PauseDiscardsAnUnconfirmedLevelUpBeforeTheNextCaptureSegment()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 61, 99m);
        Update(tracker, 60, 62, .1m);
        tracker.Pause(TimeSpan.FromSeconds(60));
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(70), new(62, .2m, Start.AddSeconds(80)),
            true, Start.AddSeconds(80)), null, 0, null, null);
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(130), new(62, .3m, Start.AddSeconds(140)),
            true, Start.AddSeconds(140)), .1m, 60, 62, 62);
    }

    [Fact]
    public void PausesAndResumeExcludeXpEarnedOutsideTheActiveGrind()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        Update(tracker, 60, 62, 11m);
        tracker.Pause(TimeSpan.FromSeconds(60));
        tracker.Update(TimeSpan.FromSeconds(60), new(62, 20m, Start.AddSeconds(90)), false, Start.AddSeconds(90));
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(70), new(62, 30m, Start.AddSeconds(100)),
            true, Start.AddSeconds(100)), 1m, 60, 62, 62);
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(130), new(62, 30.5m, Start.AddSeconds(160)),
            true, Start.AddSeconds(160)), 1.5m, 120, 62, 62);
    }

    [Theory]
    [InlineData(150, true)]
    [InlineData(151, false)]
    public void GapsMayNotExceedTheFreshObservationWindow(int seconds, bool accepted)
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        var result = Update(tracker, seconds, 62, 11m);
        AssertProgress(result, accepted ? 1m : null, accepted ? seconds : 0, accepted ? 62 : null, accepted ? 62 : null);
    }

    [Fact]
    public void DelayedUiProcessingUsesTheFrameTimestampWithoutRollingBackTheSessionClock()
    {
        var tracker = new ExperienceSessionTracker();
        tracker.Update(TimeSpan.FromSeconds(10), new(62, 10m, Start), true, Start.AddSeconds(10));
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(80), new(62, 10.1m, Start.AddSeconds(60)),
            true, Start.AddSeconds(80)), .1m, 60, 62, 62);
        AssertProgress(tracker.Snapshot(TimeSpan.FromSeconds(100)), .1m, 60, 62, 62);
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(130), new(62, 10.2m, Start.AddSeconds(120)),
            true, Start.AddSeconds(130)), .2m, 120, 62, 62);
    }

    [Fact]
    public void OlderSamplesCannotReplaceTheLatestBaseline()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        Update(tracker, 60, 62, 10.1m);
        tracker.Update(TimeSpan.FromSeconds(70), new(62, 30m, Start.AddSeconds(30)), true, Start.AddSeconds(70));
        AssertProgress(Update(tracker, 120, 62, 10.2m), .2m, 120, 62, 62);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(101, 1)]
    [InlineData(62, -1)]
    [InlineData(62, 100)]
    public void InvalidLevelOrPercentageBreaksTheObservationInterval(int level, int percent)
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        AssertProgress(Update(tracker, 60, level, percent), null, 0, null, null);
        AssertProgress(Update(tracker, 120, 62, 12m), null, 0, null, null);
        AssertProgress(Update(tracker, 180, 62, 12.1m), .1m, 60, 62, 62);
    }

    [Fact]
    public void StaleFutureAndUnstampedReadingsNeverContributeGain()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(151), new(62, 90m, Start), true, Start.AddSeconds(151)), null, 0, null, null);
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(160), new(62, 90m, Start.AddSeconds(170)), true, Start.AddSeconds(160)), null, 0, null, null);
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(180), new(62, 90m), true, Start.AddSeconds(180)), null, 0, null, null);
    }

    [Fact]
    public void ImplausibleClockDifferencesDoNotTurnUiDelayIntoXpProgress()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        AssertProgress(tracker.Update(TimeSpan.FromSeconds(180), new(62, 20m, Start.AddSeconds(60)),
            true, Start.AddSeconds(60)), null, 0, null, null);
    }

    [Fact]
    public void AutomaticPauseDiscardsAnIntervalWhenItsFinalXpSampleWasRemoved()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        Update(tracker, 60, 62, 11m);
        AssertProgress(Update(tracker, 120, 62, 20m), 10m, 120, 62, 62);

        tracker.Pause(TimeSpan.FromSeconds(90));

        AssertProgress(tracker.Snapshot(TimeSpan.FromSeconds(90)), 1m, 60, 62, 62);
        AssertProgress(tracker.Snapshot(TimeSpan.FromSeconds(120)), 1m, 60, 62, 62);
    }

    [Fact]
    public void RemovingEveryIntervalRestoresUnknownProgressRatherThanInventingZeroGain()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 10m);
        Update(tracker, 60, 62, 9m);
        AssertProgress(tracker.Snapshot(TimeSpan.FromSeconds(59)), null, 0, null, null);
    }

    [Fact]
    public void ResetRemovesGainLevelsAndOldSampleWatermarksForTheNextSession()
    {
        var tracker = new ExperienceSessionTracker();
        Update(tracker, 0, 62, 99m);
        Update(tracker, 60, 63, 1m);
        Update(tracker, 120, 63, 2m);
        tracker.Reset();
        AssertProgress(tracker.Snapshot(TimeSpan.Zero), null, 0, null, null);
        Update(tracker, 0, 20, 1m);
        AssertProgress(Update(tracker, 60, 20, 3m), 2m, 60, 20, 20);
    }

    private static ExperienceSessionProgress Update(ExperienceSessionTracker tracker, int seconds, int level, decimal percent) =>
        tracker.Update(TimeSpan.FromSeconds(seconds), new(level, percent, Start.AddSeconds(seconds)), true, Start.AddSeconds(seconds));

    private static void AssertProgress(ExperienceSessionProgress progress, decimal? gained, int observedSeconds, int? startLevel, int? endLevel)
    {
        Assert.Equal(gained, progress.GainedPercentagePoints);
        Assert.Equal(TimeSpan.FromSeconds(observedSeconds), progress.ObservedDuration);
        Assert.Equal(startLevel, progress.StartLevel);
        Assert.Equal(endLevel, progress.EndLevel);
    }
}
