using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class AphrodonRotationTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;
    private static void Send(AphrodonRotationTracker tracker, string kind, double seconds) => tracker.Observe(kind, kind, Epoch.AddSeconds(seconds));

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(9)]
    public void CompletedRotationDoesNotRequireEveryOptionalScarecrowBanner(int scarecrows)
    {
        var tracker = new AphrodonRotationTracker(); Send(tracker, "restart", 0);
        for (var i = 0; i < 9; i++)
        {
            Send(tracker, i == 5 ? "agris" : "hog", 10 + i * 30);
            if (i < scarecrows) Send(tracker, "big-scarecrow", 20 + i * 30);
        }
        Send(tracker, "afk", 290); Send(tracker, "end", 400);
        Assert.Single(tracker.DrainCompleted());
        var state = tracker.Snapshot(Epoch.AddSeconds(410));
        Assert.NotNull(state.Best); Assert.NotNull(state.Ideal); Assert.Null(state.Error);
        Assert.Equal(400, state.Best.Duration);
        Assert.DoesNotContain(state.SectorBests.Keys, key => key.StartsWith("big-scarecrow"));
    }

    [Fact]
    public void SetupCounterTracksPlacementsFailuresAndFallbackActivation()
    {
        var tracker = new AphrodonRotationTracker();
        Send(tracker, "setup", 0); Check(0, false);
        Send(tracker, "small-scarecrow", 10); Check(1, false);
        Send(tracker, "small-scarecrow", 20); Check(2, false);
        Send(tracker, "restart", 30); Check(3, true);
        Send(tracker, "failure", 40); Check(2, false);
        Send(tracker, "failure", 50); Check(1, false);
        Send(tracker, "small-scarecrow", 60); Check(2, false);
        Send(tracker, "failure", 70); Check(1, false);
        Send(tracker, "failure", 80); Check(0, false);
        Send(tracker, "restart", 90); Check(3, true);
        void Check(int count, bool active)
        {
            var state = tracker.Snapshot(Epoch.AddSeconds(100)) with { SpotId = LootSpotCatalog.AphrodonId };
            Assert.Equal(count, state.SmallScarecrows);
            Assert.Equal(active, state.Synchronized);
            Assert.Equal(!active, RotationTimelinePresentation.ShowSetup(state));
            Assert.Equal($"{count}/3 Small Scarecrows spawned", RotationTimelinePresentation.SetupCount(state));
        }
    }

    [Fact]
    public async Task BufferedMonitorUsesOnlyAphrodonCropAndBackdatesTheStart()
    {
        using var frame = new Bitmap(2560, 1440);
        using (var graphics = Graphics.FromImage(frame))
        { graphics.Clear(Color.Red); graphics.FillRectangle(Brushes.Green, new Rectangle(1040, 888, 478, 32)); }
        var tracker = new AphrodonRotationTracker();
        using var monitor = new BufferedRotationProfileMonitor(tracker, RotationMessageProfile.Aphrodon, image =>
        {
            Assert.Equal(new Size(478, 32), image.Size);
            Assert.Equal(Color.Green.ToArgb(), image.GetPixel(0, 0).ToArgb());
            return "A golden fragrance rides the wind.";
        });
        monitor.Observe(frame, Epoch); await monitor.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30));
        for (var i = 1; i <= 6; i++) monitor.Observe(frame, Epoch.AddSeconds(i * .5));
        await monitor.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30));
        var state = monitor.Snapshot(Epoch.AddSeconds(3));
        Assert.Null(state.Error); Assert.True(state.Synchronized); Assert.Equal(3, state.Elapsed);
    }

    private static void Replay(AphrodonRotationTracker tracker)
    {
        Send(tracker, "setup", 2.9166667);
        foreach (var at in new[] { 56.7, 103.5166667 }) Send(tracker, "small-scarecrow", at);
        Send(tracker, "restart", 149.1);
        double[] waves = [200.95, 263.95, 332.4333333, 397.6166667, 456.1166667, 514.4166667, 575.7166667, 636.1666667, 700.7];
        double[] scarecrows = [216.0333333, 279.0333333, 347.5166667, 412.7, 471.2, 524.95, 590.8, 651.25, 715.7833333];
        for (var i = 0; i < 9; i++) { Send(tracker, i == 5 ? "agris" : "hog", waves[i]); Send(tracker, "big-scarecrow", scarecrows[i]); }
        Send(tracker, "afk", 760.1166667); Send(tracker, "end", 913.5);
    }

    [Fact]
    public void SuppliedTimelineCompletesNineWavesAndStartsNextRunAtAfkEnd()
    {
        var tracker = new AphrodonRotationTracker(); Replay(tracker);
        var completed = Assert.Single(tracker.DrainCompleted());
        Assert.Equal(Epoch.AddSeconds(149.1), completed.StartedAt);
        Assert.Equal(764.4, completed.Run.Duration, 5);
        Assert.Equal(9, completed.Run.Events.Count(e => e.Kind is "hog" or "agris"));
        Assert.Single(completed.Run.Events, e => e.Kind == "agris");
        var state = tracker.Snapshot(Epoch.AddSeconds(923.5));
        Assert.True(state.Synchronized); Assert.Equal(10, state.Elapsed, 5); Assert.Equal(1, state.Completed);
        Assert.NotNull(state.Ideal); Assert.NotEmpty(state.SectorBests);
        Assert.Empty(tracker.DrainCompleted());
    }

    [Fact]
    public void StartupCountsWindBannersButWaitsForGoldenFragrance()
    {
        var tracker = new AphrodonRotationTracker(); Send(tracker, "hog", 0); Send(tracker, "setup", 1);
        Send(tracker, "small-scarecrow", 10); Send(tracker, "small-scarecrow", 20);
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(21)).Synchronized);
        Assert.Contains("2 / 3", tracker.Snapshot(Epoch.AddSeconds(21)).Status);
        Send(tracker, "restart", 30);
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(30)).Synchronized);
        Assert.Equal(0, tracker.Snapshot(Epoch.AddSeconds(30)).Elapsed);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void FailureDiscardsRunAndWaitsForConfirmedReactivation(int failures)
    {
        var tracker = new AphrodonRotationTracker(); Send(tracker, "restart", 0); Send(tracker, "hog", 10);
        for (var i = 0; i < failures; i++) Send(tracker, "failure", 20 + i * 10);
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(50)).Synchronized);
        Assert.Empty(tracker.DrainCompleted()); Assert.Null(tracker.Snapshot(Epoch.AddSeconds(50)).Best);
        for (var i = 0; i < failures; i++)
        {
            Send(tracker, "small-scarecrow", 60 + i * 10);
            Assert.False(tracker.Snapshot(Epoch.AddSeconds(60 + i * 10)).Synchronized);
        }
        Send(tracker, "restart", 100);
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(100)).Synchronized);
    }

    [Fact]
    public void GoldenFragranceResynchronizesAfterFailureButDoesNotResetActiveRun()
    {
        var tracker = new AphrodonRotationTracker(); Send(tracker, "restart", 0); Send(tracker, "failure", 20);
        Send(tracker, "restart", 60); Send(tracker, "restart", 63);
        Assert.Equal(5, tracker.Snapshot(Epoch.AddSeconds(65)).Elapsed);
        // Recover even when the failure banner itself was missed.
        Send(tracker, "restart", 120);
        Assert.Equal(0, tracker.Snapshot(Epoch.AddSeconds(120)).Elapsed);
    }

    [Fact]
    public void NativeOverlayInvalidatesWhenOnlyScarecrowCountChanges()
    {
        var cache = new BdoGrindTracker.App.Overlay.Native.NativeOverlayRenderState();
        var settings = new OverlaySettings { Widgets = [OverlayCatalog.CreateWidget("rotation-monitor")] };
        var snapshot = new OverlaySnapshot { Rotation = new() { SpotId = LootSpotCatalog.AphrodonId, SmallScarecrows = 0 } };
        var size = new Size(600, 120);
        cache.Remember(settings, snapshot, size);
        Assert.True(cache.Matches(settings, snapshot, size));
        Assert.False(cache.Matches(settings, snapshot with { Rotation = snapshot.Rotation with { SmallScarecrows = 1 } }, size));
    }

    [Fact]
    public void DelayedAfkEndPreservesAlreadyRecognizedNextRotationEvent()
    {
        var tracker = new AphrodonRotationTracker();
        Send(tracker, "restart", 0);
        foreach (var e in AphrodonRotationDemo.Reference.Events.Where(e => e.Kind is not "start" and not "end"))
            Send(tracker, e.Kind, e.Seconds);
        Send(tracker, "hog", 770);
        Send(tracker, "end", AphrodonRotationDemo.Reference.Duration);
        Assert.Single(tracker.DrainCompleted());
        var next = tracker.Snapshot(Epoch.AddSeconds(780));
        Assert.Equal(5.6, Assert.Single(next.Events, e => e.Kind == "hog").Seconds, 5);
    }

    [Fact]
    public void ThreeSecondProbesRecoverAWholeRotationWithAgrisReplacingOneHog()
    {
        var search = new BufferedRotationSearch(AphrodonMessages.Parse);
        var tracker = new AphrodonRotationTracker();
        var messages = AphrodonRotationDemo.Reference.Events.Select(e =>
            (At: 1 + e.Seconds, Text: AphrodonMessages.Definitions.Single(d => d.Kind == (e.Kind == "start" ? "restart" : e.Kind)).Phrase)).ToArray();
        for (var probe = 0d; probe < 771; probe += 3)
        {
            var first = Math.Max(0, probe - 10);
            var times = Enumerable.Range(0, (int)((probe - first) * 2) + 1).Select(i => Epoch.AddSeconds(first + i * .5)).ToArray();
            foreach (var e in search.Read(times, i => string.Join("\n", messages.Where(m =>
                (times[i] - Epoch).TotalSeconds >= m.At && (times[i] - Epoch).TotalSeconds < m.At + 6.5).Select(m => m.Text))))
                tracker.Observe(e.Kind, e.Label, e.At);
        }
        var run = Assert.Single(tracker.DrainCompleted()).Run;
        Assert.Equal(9, run.Events.Count(e => e.Kind is "hog" or "agris"));
        Assert.InRange(run.Duration, 764.4, 764.9);
    }

    [Fact]
    public void MissingWaveCannotBecomeARecord()
    {
        var tracker = new AphrodonRotationTracker(); Send(tracker, "restart", 0);
        Send(tracker, "hog", 10); Send(tracker, "big-scarecrow", 20); Send(tracker, "afk", 30); Send(tracker, "end", 40);
        Assert.Empty(tracker.DrainCompleted()); Assert.Null(tracker.Snapshot(Epoch.AddSeconds(40)).Best);
    }

    [Fact]
    public void RecordsPersistSeparatelyAndDoNotResumeAnOldRun()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try {
            Replay(new AphrodonRotationTracker(path)); var reloaded = new AphrodonRotationTracker(path).Snapshot(Epoch);
            Assert.NotNull(reloaded.Best); Assert.NotNull(reloaded.Ideal); Assert.False(reloaded.Synchronized);
        } finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    [Fact]
    public void ProfileUsesNarrowRelativeCropAndDistinctAgrisMessage()
    {
        var crop = RotationMessageProfile.Aphrodon.Crop(2560, 1440);
        Assert.Equal(new Rectangle(1040, 888, 478, 32), crop);
        Assert.Equal(new Rectangle(780, 666, 358, 24), RotationMessageProfile.Aphrodon.Crop(1920, 1080));
        Assert.Equal("agris", Assert.Single(AphrodonMessages.Parse("You sense an intoxicating energy of abundance.")).Kind);
        Assert.Equal("end", Assert.Single(AphrodonMessages.Parse("Agris's blessing fades from the fields.")).Kind);
        Assert.True(RotationProfiles.Supports(LootSpotCatalog.AphrodonId));
        var state = RotationProfiles.Present(LootSpotCatalog.AphrodonId, HermesiaRotationDemo.At(350));
        Assert.Empty(state.Events); Assert.Null(state.Best);
    }

    [Theory]
    [InlineData("colored")] [InlineData("gold")] [InlineData("slate")] [InlineData("minimal")]
    public void TimelineShowsWaveScarecrowAndAfkDurations(string colors)
    {
        var tracker = new AphrodonRotationTracker(); Replay(tracker); var run = Assert.Single(tracker.DrainCompleted()).Run;
        var phases = RotationPhases.Create(LootSpotCatalog.AphrodonId, run.Events, run.Duration, colors);
        Assert.Equal(11, phases.Count); Assert.Equal("AFK", phases[^1].Name);
        Assert.Equal(run.Duration, phases.Sum(p => p.End - p.Start), 5);
        Assert.Equal(9, phases.Where(p => p.Group.StartsWith("wave-")).Select(p => p.Group).Distinct().Count());
    }
}

