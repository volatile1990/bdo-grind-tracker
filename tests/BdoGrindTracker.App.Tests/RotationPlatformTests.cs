using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationPlatformTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;
    private static RotationDefinition Simple(string spot = "test") => new(spot,
        [new("a", ["a"]), new("b", ["b"]), new("afk", ["afk"], Afk: true)], ["start"], ["end"], ["failure"]);
    private static void Message(RotationPlatform tracker, string kind, double seconds) => tracker.Observe(kind, kind, Epoch.AddSeconds(seconds));
    private static void Full(RotationPlatform tracker, double offset = 0, double pace = 1)
    {
        Message(tracker, "start", offset);
        Message(tracker, "a", offset + 10 * pace);
        Message(tracker, "b", offset + 20 * pace);
        Message(tracker, "afk", offset + 30 * pace);
        Message(tracker, "end", offset + 40 * pace);
    }

    [Fact]
    public void FullHourMessageFixtureKeepsAllEightCyclesAndRecoversWithoutLoot()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "fixtures", "rotation", "event-horizon-hour.messages.json")));
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        foreach (var e in document.RootElement.GetProperty("events").EnumerateArray())
            Message(tracker, e.GetProperty("kind").GetString()!, e.GetProperty("seconds").GetDouble());
        var runs = tracker.DrainCompleted();
        Assert.Equal(8, runs.Length);
        Assert.All(runs, r => Assert.Equal("incomplete", r.Run.Outcome));
        Assert.Equal(8, tracker.DrainTimeline().Count(e => e.Kind == "afk-end"));
        Assert.Null(tracker.Snapshot(Epoch.AddHours(2)).Best);
        Assert.True(tracker.ObserveLoot(Epoch.AddHours(2)));
    }

    [Fact]
    public void RecordedHourAcceptsSubsequentCleanLootStartsWithoutInventingTheFirstStart()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "fixtures", "rotation", "event-horizon-hour.messages.json")));
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        foreach (var e in document.RootElement.GetProperty("events").EnumerateArray())
        {
            var kind = e.GetProperty("kind").GetString()!;
            var seconds = e.GetProperty("seconds").GetDouble();
            Message(tracker, kind, seconds);
            // Synthetic loot is explicitly added for this integration scenario, not claimed as video evidence.
            if (kind == "end") tracker.ObserveLoot(Epoch.AddSeconds(seconds + 6));
        }
        var runs = tracker.DrainCompleted();
        Assert.Equal("incomplete", runs[0].Run.Outcome);
        Assert.Contains(runs.Skip(1), r => r.Run.EligibleForStatistics);
        Assert.DoesNotContain(runs, r => r.Run.Outcome == "aborted");
    }

    [Fact]
    public void RepeatedFrameInterruptionsDoNotFloodTheTimeline()
    {
        var tracker = new RotationPlatform(Simple());
        Message(tracker, "start", 0);
        for (var i = 1; i <= 1000; i++) tracker.InterruptAt("Bildsignal fehlt", Epoch.AddSeconds(i));
        Assert.Single(tracker.DrainCompleted());
        Assert.Single(tracker.DrainTimeline(), e => e.Type == "interrupt");
    }

    [Fact]
    public void TeleportSplitBannerIsSuppressedButTheNextGenuineSightingIsAccepted()
    {
        var search = new BufferedRotationSearch(EventHorizonMessages.Parse, 7, RotationMessageProfile.EventHorizon.DuplicateSeconds);
        var times = Enumerable.Range(0, 55).Select(i => Epoch.AddSeconds(i * .5)).ToArray();
        string Text(int i) => i is <= 4 or >= 20 and <= 24 or >= 50 ? "Anomaly removal halted." : "";
        Assert.Single(search.Read(times.Take(4).ToArray(), Text));
        Assert.Empty(search.Read(times.Take(25).ToArray(), Text));
        Assert.Single(search.Read(times, Text));
    }

    [Fact]
    public void TimeoutsRunOnCaptureTimeEvenWithoutAnOverlaySnapshot()
    {
        var tracker = new RotationPlatform(Simple());
        Full(tracker); Full(tracker, 100); Full(tracker, 200); tracker.DrainCompleted();
        Message(tracker, "start", 300);
        tracker.Advance(Epoch.AddSeconds(385));
        var aborted = Assert.Single(tracker.DrainCompleted()).Run;
        Assert.Equal("aborted", aborted.Outcome);
        // Twice ten seconds is no slack: a section may always take its average plus a minute.
        Assert.Equal(10 + RotationPlatform.MinimumTimeoutSlackSeconds, aborted.Duration);
    }

    [Fact]
    public void ReportedSpacetimeEntryNeverBlocksFollowingWormholeMessages()
    {
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        tracker.ObserveLoot(Epoch);
        Message(tracker, "spacetime", 119.5);
        Message(tracker, "halted", 246.8);
        Message(tracker, "halted", 482.5);
        Message(tracker, "spacetime", 522.1);
        var state = tracker.Snapshot(Epoch.AddSeconds(530));
        Assert.True(state.Synchronized);
        Assert.Equal("partial", state.TrackingState);
        Assert.Equal("spacetime", state.Events[^1].Kind);
        // Once the wormhole is known, earlier phase decisions are corrected in place (Corrects), never duplicated.
        var timeline = tracker.DrainTimeline();
        Assert.Equal(4, timeline.Count(e => e.Type == "decision" && e.Kind == "phase" && !timeline.Any(later => later.Corrects == e.Id)));
        Assert.Null(state.Best);
    }

    [Fact]
    public void AnOldAfkBoundaryCannotQualifyARestartAfterFailure()
    {
        var tracker = new RotationPlatform(Simple());
        Message(tracker, "end", -10); tracker.ObserveLoot(Epoch);
        Message(tracker, "failure", 5); tracker.DrainCompleted();
        tracker.ObserveLoot(Epoch.AddSeconds(10));
        Message(tracker, "a", 20); Message(tracker, "b", 30); Message(tracker, "afk", 40); Message(tracker, "end", 50);
        Assert.Equal("incomplete", Assert.Single(tracker.DrainCompleted()).Run.Outcome);
    }

    [Fact]
    public void DamagedReferenceFileDoesNotStopRecognitionOrGetOverwritten()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{broken");
            var tracker = new RotationPlatform(Simple(), path);
            Full(tracker);
            Assert.True(Assert.Single(tracker.DrainCompleted()).Run.EligibleForStatistics);
            Assert.NotNull(tracker.Snapshot(Epoch.AddSeconds(40)).Error);
            Assert.Equal("{broken", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingConditionalAfkEndCannotTrainSectionAverages()
    {
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        Message(tracker, "end", -10); tracker.ObserveLoot(Epoch);
        // Without the mini AFK's middle nothing can fill in its end.
        foreach (var e in EventHorizonRotationDemo.Reference.Events.Where(e => e.Kind is not "start" and not "spacetime" and not "distortion"))
            Message(tracker, e.Kind, e.Seconds);
        Assert.Equal("incomplete", Assert.Single(tracker.DrainCompleted()).Run.Outcome);
        Assert.Null(tracker.Snapshot(Epoch.AddSeconds(600)).Best);
    }

    [Fact]
    public void ActiveRunsSurviveCheckpointAsAbortedAfterRestart()
    {
        var tracker = new RotationPlatform(Simple());
        using var monitor = new RotationMonitor(_ => new BufferedRotationProfileMonitor(tracker, RotationMessageProfile.EventHorizon));
        monitor.Snapshot(Epoch, "event-horizon");
        Message(tracker, "start", 0); Message(tracker, "a", 10);
        var saved = monitor.ExportSession();
        Assert.Equal("active", Assert.Single(saved).Run.Outcome);
        var timeline = monitor.ExportTimeline();
        monitor.RestoreSession(saved, timeline);
        Assert.Equal("aborted", Assert.Single(monitor.ExportSession()).Run.Outcome);
        Assert.Equal(timeline, monitor.ExportTimeline().Take(timeline.Length));
        Assert.Contains(monitor.ExportTimeline(), e => e.Kind == "finish" && e.Detail.Contains("Neustart"));
    }

    [Fact]
    public void PersistedAfkBoundaryKeepsItsLootLockoutAfterRestart()
    {
        var tracker = new RotationPlatform(RotationDefinition.EventHorizon);
        Message(tracker, "end", 100);
        using var monitor = new RotationMonitor(_ => new BufferedRotationProfileMonitor(
            new RotationPlatform(RotationDefinition.EventHorizon), RotationMessageProfile.EventHorizon));
        monitor.RestoreSession([], tracker.DrainTimeline());
        monitor.ObserveLoot(Epoch.AddSeconds(103), "event-horizon");
        Assert.False(monitor.Snapshot(Epoch.AddSeconds(103), "event-horizon").Synchronized);
        monitor.ObserveLoot(Epoch.AddSeconds(105), "event-horizon");
        Assert.True(monitor.Snapshot(Epoch.AddSeconds(105), "event-horizon").Synchronized);
    }

    [Fact]
    public void AfkEndLocksAllLootForFiveSecondsWithoutExtendingTheDeadline()
    {
        var tracker = new RotationPlatform(Simple());
        Message(tracker, "end", 100);
        foreach (var second in new[] { 100d, 101, 103, 104.99 }) Assert.False(tracker.ObserveLoot(Epoch.AddSeconds(second)));
        Assert.True(tracker.ObserveLoot(Epoch.AddSeconds(105)));
        Assert.Equal(1, tracker.Snapshot(Epoch.AddSeconds(106)).Elapsed);
    }

    [Fact]
    public void ExplicitStartOverridesLootLockout()
    {
        var tracker = new RotationPlatform(Simple());
        Message(tracker, "end", 100); Message(tracker, "start", 101);
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(102)).Synchronized);
        Assert.Equal(1, tracker.Snapshot(Epoch.AddSeconds(102)).Elapsed);
    }

    [Fact]
    public void DelayedAfkEndReplaysAlreadyReceivedLootAndKeepsAuditCorrections()
    {
        var tracker = new RotationPlatform(Simple());
        tracker.ObserveLoot(Epoch.AddSeconds(102));
        tracker.ObserveLoot(Epoch.AddSeconds(106));
        Message(tracker, "end", 100);
        Assert.Equal(1, tracker.Snapshot(Epoch.AddSeconds(107)).Elapsed);
        Assert.Contains(tracker.DrainTimeline(), e => e.Type == "correction");
    }

    [Fact]
    public void MidRotationEntryContinuesRecognizingAndNeverBecomesARecord()
    {
        var tracker = new RotationPlatform(Simple());
        tracker.ObserveLoot(Epoch); Message(tracker, "b", 20); Message(tracker, "afk", 30); Message(tracker, "end", 40);
        Assert.Equal("incomplete", Assert.Single(tracker.DrainCompleted()).Run.Outcome);
        Assert.Null(tracker.Snapshot(Epoch.AddSeconds(41)).Best);
        Full(tracker, 100);
        Assert.True(Assert.Single(tracker.DrainCompleted()).Run.EligibleForStatistics);
    }

    [Fact]
    public void WrongOrderAbortsAndImmediatelySynchronizesToTheNewMessage()
    {
        var tracker = new RotationPlatform(Simple());
        Message(tracker, "start", 0); Message(tracker, "a", 10); Message(tracker, "b", 20); Message(tracker, "a", 40);
        var failed = Assert.Single(tracker.DrainCompleted()).Run;
        Assert.Equal("aborted", failed.Outcome);
        Assert.Contains("Reihenfolge", failed.Reason);
        Assert.Equal("a", tracker.Snapshot(Epoch.AddSeconds(41)).Events[^1].Kind);
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(41)).Synchronized);
    }

    [Fact]
    public void FailureAndCaptureInterruptionsKeepTheAbortedRun()
    {
        var tracker = new RotationPlatform(Simple());
        Message(tracker, "start", 0); Message(tracker, "failure", 5);
        Assert.Equal("aborted", Assert.Single(tracker.DrainCompleted()).Run.Outcome);
        Message(tracker, "start", 10); tracker.InterruptAt("Bildsignal fehlt", Epoch.AddSeconds(15));
        Assert.Equal("aborted", Assert.Single(tracker.DrainCompleted()).Run.Outcome);
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(16)).Synchronized);
        Assert.True(tracker.ObserveLoot(Epoch.AddSeconds(17)));
    }

    [Fact]
    public void DelayedEarlierMessageRepairsOrderingAndTheCompletedRun()
    {
        var tracker = new RotationPlatform(Simple());
        Message(tracker, "start", 0); Message(tracker, "b", 20); Message(tracker, "afk", 30); Message(tracker, "end", 40);
        var old = Assert.Single(tracker.DrainCompleted());
        Message(tracker, "a", 10);
        var corrected = Assert.Single(tracker.DrainCompleted());
        Assert.Equal(old.Run.Id, corrected.Run.Id);
        Assert.True(corrected.Run.EligibleForStatistics);
    }

    [Fact]
    public void TimeoutNeedsThreeSamplesAndPersistsAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var tracker = new RotationPlatform(Simple(), path);
            Full(tracker, 0); Full(tracker, 100);
            Message(tracker, "start", 200);
            Assert.True(tracker.Snapshot(Epoch.AddSeconds(235)).Synchronized);
            Message(tracker, "a", 210); Message(tracker, "b", 220); Message(tracker, "afk", 230); Message(tracker, "end", 240);
            var restored = new RotationPlatform(Simple(), path);
            Message(restored, "start", 300);
            Assert.False(restored.Snapshot(Epoch.AddSeconds(385)).Synchronized);
            var failed = Assert.Single(restored.DrainCompleted());
            Assert.Equal(70, failed.Run.Duration);
            Assert.Contains("Durchschnitt", failed.Run.Reason);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LastTwentySamplesAreRecentRatherThanFastest()
    {
        var tracker = new RotationPlatform(Simple());
        Full(tracker, 0); Full(tracker, 100); Full(tracker, 200);
        for (var i = 0; i < 20; i++) Full(tracker, 300 + i * 100, 1.5);
        tracker.DrainCompleted();
        Message(tracker, "start", 2400);
        // The fastest samples (10 s) would expire after 70 s plus the confirmation allowance, the recent ones (15 s) after 75.
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(2483)).Synchronized);
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(2486)).Synchronized);
        Assert.Equal(75, Assert.Single(tracker.DrainCompleted()).Run.Duration);
    }

    [Theory]
    [InlineData("event-horizon")]
    [InlineData("aphrodon")]
    [InlineData("hermesia")]
    public void EveryExistingSpotUsesTheSharedEngineWithItsRecordedReference(string spot)
    {
        var definition = RotationDefinition.For(spot);
        var tracker = new RotationPlatform(definition);
        var reference = spot switch { "event-horizon" => EventHorizonRotationDemo.Reference,
            "aphrodon" => AphrodonRotationDemo.Reference, _ => HermesiaRotationDemo.Reference };
        if (spot == "event-horizon") { Message(tracker, "end", -10); tracker.ObserveLoot(Epoch); }
        if (spot == "aphrodon") Message(tracker, "restart", 0);
        foreach (var e in reference.Events.Where(e => e.Kind != "start"))
            Message(tracker, spot == "hermesia" && e.Kind == "end" ? "mine-cleared" : e.Kind, e.Seconds);
        var run = Assert.Single(tracker.DrainCompleted()).Run;
        Assert.True(run.EligibleForStatistics, run.Reason);
        Assert.Equal(reference.Duration, run.Duration, 3);
    }

    [Fact]
    public void TimelineLootCorrectionsAndIncompleteRunsSurviveHistoryRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            using var monitor = new RotationMonitor(_ => null);
            var id = Guid.NewGuid();
            monitor.ObserveLootEvents([new(id, Epoch, "Trash", 5)], "event-horizon");
            monitor.ObserveLootEvents([new(id, Epoch, "Trash", 8) { Revision = 1, TotalDropQuantity = 8 }], "event-horizon");
            monitor.ObserveLootEvents([new(id, Epoch, "Trash", 8) { Revision = 1, TotalDropQuantity = 8 }], "event-horizon");
            var timeline = monitor.ExportTimeline();
            Assert.Equal(2, timeline.Length); Assert.Equal(timeline[0].Id, timeline[1].Corrects);
            var store = new LootHistoryStore(path);
            store.Save([new() { SessionId = Guid.NewGuid(), StartedAt = Epoch, UpdatedAt = Epoch, Duration = TimeSpan.Zero,
                SpotId = "event-horizon", Totals = [], SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = true,
                RotationTimeline = timeline }]);
            Assert.Equal(timeline, Assert.Single(store.Load()).RotationTimeline);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ARepeatBeyondTheModelledOnesResynchronisesInsteadOfCountingAsADuplicate()
    {
        var tracker = new RotationPlatform(RotationDefinition.Hermesia);
        for (var i = 0; i < 5; i++) Message(tracker, "offer", i * 20);
        // A sixth offering before the Drakania means the rotation restarted. The five modelled steps are exhausted,
        // so it must abort and resynchronise rather than silently extend the running one.
        Message(tracker, "offer", 100);

        var aborted = Assert.Single(tracker.DrainCompleted(), run => run.Run.Outcome == "aborted");
        Assert.StartsWith("Mitteilung außerhalb der erlaubten Reihenfolge", aborted.Run.Reason);
        Assert.Equal(Epoch.AddSeconds(100), tracker.ActiveRun()!.Value.StartedAt);
    }

    [Fact]
    public void AMessageOfItsOwnSingleStepMayRepeatInsideThatPhase()
    {
        var tracker = new RotationPlatform(Simple());
        Message(tracker, "start", 0);
        Message(tracker, "a", 10);
        Message(tracker, "b", 20);
        // Several orbs of one mechanic: the message belongs to this step alone and keeps the phase.
        Message(tracker, "b", 26);
        Message(tracker, "afk", 30);
        Message(tracker, "end", 40);

        var run = Assert.Single(tracker.DrainCompleted());
        Assert.Equal("complete", run.Run.Outcome);
        Assert.Equal(40, run.Run.Duration, 1);
    }
}
