using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationSharedBannersTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;
    private static readonly string[] SharedSpots = ["hermesia", "zephyros", "event-horizon", "magaia"];

    [WindowsOcrFact("en-US")]
    public void NativeColdAndSharedWarmReadsMatchIndependentEnginesAndProfileParsers()
    {
        using var frame = new Bitmap(1600, 900);
        var region = RotationMessageProfile.Hermesia.Crop(frame.Width, frame.Height);
        using (var graphics = Graphics.FromImage(frame))
        using (var font = new Font(FontFamily.GenericSansSerif, 14))
        {
            graphics.Clear(Color.Black);
            graphics.DrawString("The sinners are summoned.", font, Brushes.White, region.X + 4, region.Y + 4);
            graphics.DrawString("Aetos drops a Fragment of Divinity.", font, Brushes.White, region.X + 4, region.Y + 40);
        }
        using var samples = new RotationFrameSamples(frame);
        var shared = samples.Acquire(region, "en-US");
        try
        {
            var engine = CompanionWindowsOcrRecognizer.TryCreate("en-US", throwIfUnavailable: true, requirePreferredLanguage: true);
            Assert.NotNull(engine);
            var cold = shared.Read((pixels, token) => RotationMessageProfile.Hermesia.Recognize(pixels, engine, token));
            Assert.NotEmpty(cold.Replace("\f", "").Trim());
            Assert.Equal(cold, shared.Read((_, _) => throw new InvalidOperationException("Warm shared read repeated native OCR")));
            foreach (var spot in SharedSpots)
            {
                var profile = RotationProfiles.Messages(spot)!;
                var independent = CompanionWindowsOcrRecognizer.TryCreate("en-US", throwIfUnavailable: true, requirePreferredLanguage: true);
                Assert.NotNull(independent);
                using var crop = frame.Clone(region, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using var pixels = CompanionFrameDecoder.Decode(crop);
                var separate = profile.Recognize(pixels, independent);
                Assert.Equal(separate, cold);
                Assert.Equal(profile.Parse(separate), profile.Parse(cold));
            }
        }
        finally { shared.Release(); }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("sp")]
    public async Task SharedReadsPreserveEachProfilesLazySearchEventsAndCaptureTimes(string language)
    {
        var phrases = SharedSpots.Select(spot => LocalizedRotationMessages.Patterns.First(p =>
            p.Spot == spot && p.Language == language).Text).ToArray();
        var fragment = LocalizedRotationMessages.Patterns.First(p => p.Spot == "magaia" &&
            p.Kind == "fragment" && p.Language == language).Text;
        var texts = Enumerable.Range(0, 49).Select(i => i < 2 || i is >= 20 and < 31 ? "" :
            string.Join('\n', phrases) + "\n" + string.Join('\n', Enumerable.Repeat(fragment, i % 6 < 3 ? 1 : 2)))
            .Select(text => text + "\f" + text).ToArray();
        var sharedTrackers = SharedSpots.ToDictionary(spot => spot, _ => new RecordingTracker());
        var separateTrackers = SharedSpots.ToDictionary(spot => spot, _ => new RecordingTracker());
        var sharedMonitors = new Dictionary<string, BufferedRotationProfileMonitor>();
        var separateMonitors = new Dictionary<string, BufferedRotationProfileMonitor>();
        var sharedReads = new Dictionary<int, int>();
        var separateReads = 0;
        var nameReads = 0;
        using var tracker = new RotationMonitor(spot =>
        {
            if (spot is null || !sharedTrackers.TryGetValue(spot, out var state)) return null;
            return sharedMonitors[spot] = new BufferedRotationProfileMonitor(state,
                RotationProfiles.Messages(spot, language)!, recognizeName: _ => { Interlocked.Increment(ref nameReads); return ""; });
        }, (pixels, _) =>
        {
            var index = pixels.At<Vec3b>(0, 0)[2];
            lock (sharedReads) sharedReads[index] = sharedReads.GetValueOrDefault(index) + 1;
            return texts[index];
        });
        foreach (var spot in SharedSpots)
            separateMonitors[spot] = new BufferedRotationProfileMonitor(separateTrackers[spot],
                RotationProfiles.Messages(spot, language)!, pixels =>
                {
                    Interlocked.Increment(ref separateReads);
                    return texts[pixels.GetPixel(0, 0).R];
                }, _ => "");
        using var frame = new Bitmap(320, 200);
        try
        {
            for (var i = 0; i < texts.Length; i++)
            {
                using (var graphics = Graphics.FromImage(frame)) graphics.Clear(Color.FromArgb(i, 30, 40));
                var at = Epoch.AddSeconds(i * .5);
                tracker.Observe(frame, at, null);
                foreach (var profile in separateMonitors.Values) profile.Observe(frame, at);
                await Task.WhenAll(sharedMonitors.Values.Concat(separateMonitors.Values)
                    .Select(profile => profile.PendingAnalysis)).WaitAsync(TimeSpan.FromSeconds(10));
                if (i == 5) Assert.Equal(0, Assert.Single(sharedReads).Key);
            }
            foreach (var spot in SharedSpots)
            {
                Assert.NotEmpty(sharedTrackers[spot].Events);
                Assert.Equal(separateTrackers[spot].Events, sharedTrackers[spot].Events);
                var sharedTimeline = sharedMonitors[spot].DrainTimeline().Select(e => (e.At, e.Kind, e.Detail));
                var separateTimeline = separateMonitors[spot].DrainTimeline().Select(e => (e.At, e.Kind, e.Detail));
                Assert.Equal(separateTimeline, sharedTimeline);
            }
            Assert.All(sharedReads.Values, reads => Assert.InRange(reads, 1, SharedSpots.Length));
            Assert.True(sharedReads.Values.Sum() <= Volatile.Read(ref separateReads));
            Assert.True(Volatile.Read(ref nameReads) > 0);
        }
        finally { foreach (var profile in separateMonitors.Values) profile.Dispose(); }
    }

    [Fact]
    public async Task CustomReadersKeepTheirIndependentResults()
    {
        var states = new Dictionary<string, RecordingTracker>();
        var profiles = new Dictionary<string, BufferedRotationProfileMonitor>();
        var sharedReads = 0;
        using var monitor = new RotationMonitor(spot =>
        {
            if (spot is not ("hermesia" or "aphrodon")) return null;
            states[spot] = new RecordingTracker();
            var profile = RotationProfiles.Messages(spot)!;
            return profiles[spot] = new BufferedRotationProfileMonitor(states[spot], profile,
                _ => spot == "hermesia" ? "Markthanan's patrol descends." : "A golden fragrance rides the wind.");
        }, (_, _) => { Interlocked.Increment(ref sharedReads); throw new InvalidOperationException("Custom reads cannot share"); });
        using var frame = new Bitmap(1280, 720);
        monitor.Observe(frame, Epoch, null);
        await Task.WhenAll(profiles.Values.Select(profile => profile.PendingAnalysis)).WaitAsync(TimeSpan.FromSeconds(5));
        monitor.Observe(frame, Epoch.AddSeconds(.5), null);
        await Task.WhenAll(profiles.Values.Select(profile => profile.FlushAsync(CancellationToken.None))).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, Volatile.Read(ref sharedReads));
        Assert.Equal("dragon", Assert.Single(states["hermesia"].Events).Kind);
        Assert.Equal("restart", Assert.Single(states["aphrodon"].Events).Kind);
    }

    [Fact]
    public async Task SpotSelectionAndInterruptKeepActiveReadersPixelsUntilWorkersComplete()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var states = SharedSpots.ToDictionary(spot => spot, _ => new RecordingTracker());
        var profiles = new Dictionary<string, BufferedRotationProfileMonitor>();
        var retained = new List<Mat>();
        var reads = 0;
        using var monitor = new RotationMonitor(spot =>
        {
            if (spot is null || !states.TryGetValue(spot, out var state)) return null;
            return profiles[spot] = new BufferedRotationProfileMonitor(state,
                RotationProfiles.Messages(spot)!, recognizeName: _ => "");
        }, (pixels, _) =>
        {
            Interlocked.Increment(ref reads);
            lock (retained) retained.Add(pixels);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            pixels.At<Vec3b>(0, 0);
            return "Markthanan's patrol descends.";
        });
        using var frame = new Bitmap(320, 200);
        monitor.Observe(frame, Epoch, null);
        frame.Dispose();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            monitor.Snapshot(Epoch, "hermesia"); // Adopt one profile; dispose the other shared owners.
            using var cancellation = new CancellationTokenSource();
            var flush = profiles["hermesia"].FlushAsync(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush.WaitAsync(TimeSpan.FromSeconds(5)));
            monitor.Interrupt();
            lock (retained) Assert.All(retained, pixels => pixels.At<Vec3b>(0, 0));
            monitor.Dispose();
            lock (retained) Assert.All(retained, pixels => pixels.At<Vec3b>(0, 0));
        }
        finally { release.Set(); }
        await Task.WhenAll(profiles.Values.Select(profile => profile.PendingAnalysis)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.InRange(Volatile.Read(ref reads), 1, SharedSpots.Length);
        Assert.All(states.Values, state => Assert.Empty(state.Events));
        Assert.All(retained, pixels => Assert.True(pixels.IsDisposed));
    }

    [Fact]
    public async Task TheSelectedProfileKeepsItsOwnConfirmationAndFlushesItsLastSample()
    {
        var states = SharedSpots.ToDictionary(spot => spot, _ => new RecordingTracker());
        var profiles = new Dictionary<string, BufferedRotationProfileMonitor>();
        using var monitor = new RotationMonitor(spot =>
        {
            if (spot is null || !states.TryGetValue(spot, out var state)) return null;
            return profiles[spot] = new BufferedRotationProfileMonitor(state,
                RotationProfiles.Messages(spot)!, recognizeName: _ => "");
        }, (_, _) => "Markthanan's patrol descends.");
        using var frame = new Bitmap(320, 200);
        monitor.Observe(frame, Epoch, null);
        await Task.WhenAll(profiles.Values.Select(profile => profile.PendingAnalysis)).WaitAsync(TimeSpan.FromSeconds(5));
        monitor.Observe(frame, Epoch.AddSeconds(.5), null);
        monitor.Snapshot(Epoch.AddSeconds(.5), "hermesia");
        await monitor.FlushAsync();
        var confirmed = Assert.Single(states["hermesia"].Events);
        Assert.Equal(("dragon", "Drachen-Spawn", Epoch), confirmed);
        Assert.All(states.Where(pair => pair.Key != "hermesia"), pair => Assert.Empty(pair.Value.Events));
        Assert.Equal(SharedSpots.Length, profiles.Count);
    }

    private sealed class RecordingTracker : IRotationEventTracker
    {
        internal List<(string Kind, string Label, DateTimeOffset At)> Events { get; } = [];
        public void Observe(string kind, string label, DateTimeOffset at) => Events.Add((kind, label, at));
        public void Interrupt(string status) { }
        public RotationMonitorSnapshot Snapshot(DateTimeOffset now) => new();
        public (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted() => [];
    }
}
