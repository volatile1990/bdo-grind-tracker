using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationDiagnosticRecordingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grindcrest-rotation-diagnostics-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task TheRecordingFollowsTheFirstOfferingOrderFromItsProbeImageToTheTrackerState()
    {
        var text = "The overseer orders the Black Crystals to be offered up.";
        HermesiaRotationMonitor? hermesia = null;
        using var recording = RotationDiagnosticRecording.Start(_directory);
        using (var monitor = new RotationMonitor(spot => spot == LootSpotCatalog.HermesiaId
                   ? hermesia = new HermesiaRotationMonitor(recognize: _ => text) : null))
        {
            monitor.AttachDiagnostics(recording);
            using var frame = new Bitmap(320, 200);
            var start = DateTimeOffset.UnixEpoch;
            async Task Observe(double seconds, string? spot)
            {
                monitor.Observe(frame, start.AddSeconds(seconds), spot);
                await hermesia!.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30));
            }
            for (var seconds = 0d; seconds <= 6; seconds += .5) await Observe(seconds, null);
            text = "";
            for (var seconds = 6.5d; seconds <= 9.5; seconds += .5) await Observe(seconds, null);
            await Observe(10, LootSpotCatalog.HermesiaId);
        }
        recording.Dispose();

        Assert.Null(recording.LastError);
        var entries = Entries(recording.RecordingPath!);
        Assert.Equal(("header", 1), (Type(entries[0]), entries[0].GetProperty("version").GetInt32()));
        Assert.Contains(entries, entry => Type(entry) == "note" && entry.GetProperty("kind").GetString() == "candidates");

        var probes = entries.Where(entry => Type(entry) == "probe").ToArray();
        Assert.Equal(new[] { 0d, 3, 6, 9 }, probes.Select(probe => (probe.GetProperty("sampleAt").GetDateTimeOffset() - DateTimeOffset.UnixEpoch).TotalSeconds));
        var probe = probes[1];
        var region = RotationMessageProfile.Hermesia.Crop(320, 200);
        Assert.Equal((LootSpotCatalog.HermesiaId, 320, 200, region.X, region.Width), (probe.GetProperty("spot").GetString(),
            probe.GetProperty("frameWidth").GetInt32(), probe.GetProperty("frameHeight").GetInt32(),
            probe.GetProperty("crop").GetProperty("x").GetInt32(), probe.GetProperty("crop").GetProperty("width").GetInt32()));
        using (var image = new Bitmap(Path.Combine(Path.GetDirectoryName(recording.RecordingPath)!, probe.GetProperty("image").GetString()!)))
            Assert.Equal(region.Size, image.Size);
        Assert.Equal(4, recording.ImageCount);

        var reads = entries.Where(entry => Type(entry) == "read").ToArray();
        // Every buffered sample is read at most once; the offer banner is visible through six seconds.
        Assert.Equal(reads.Length, reads.Select(read => read.GetProperty("sampleAt").GetDateTimeOffset()).Distinct().Count());
        Assert.Contains(reads, read => read.GetProperty("kinds").EnumerateArray().Select(kind => kind.GetString()).SequenceEqual(["offer"]));

        var confirmed = Assert.Single(entries, entry => Type(entry) == "event");
        Assert.Equal(("offer", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(3)), (confirmed.GetProperty("kind").GetString(),
            confirmed.GetProperty("at").GetDateTimeOffset(), confirmed.GetProperty("probedAt").GetDateTimeOffset()));
        var state = Assert.Single(entries, entry => Type(entry) == "state");
        Assert.Equal("Startup · 1 / 5 Opfergaben", state.GetProperty("status").GetString());
        Assert.True(state.GetProperty("synchronized").GetBoolean());
        Assert.Contains(entries, entry => Type(entry) == "note" && entry.GetProperty("kind").GetString() == "spot" &&
            entry.GetProperty("message").GetString()!.Contains("übernommen"));
    }

    [Fact]
    public void CompletedRotationsAndInterruptionsAreRecorded()
    {
        using var recording = RotationDiagnosticRecording.Start(_directory);
        var profile = new CompletingProfile();
        using (var monitor = new RotationMonitor(spot => spot == LootSpotCatalog.HermesiaId ? profile : null))
        {
            monitor.AttachDiagnostics(recording);
            var start = DateTimeOffset.UnixEpoch;
            monitor.Snapshot(start, LootSpotCatalog.HermesiaId);
            profile.Completed.Add((start, new RotationRun(610, [new("start", "Rotationsstart", 0), new("end", "AFK-Ende", 610)])));
            monitor.Snapshot(start.AddSeconds(620), LootSpotCatalog.HermesiaId);
        }
        recording.Dispose();

        var completed = Assert.Single(Entries(recording.RecordingPath!), entry => Type(entry) == "completed");
        Assert.Equal((LootSpotCatalog.HermesiaId, 610d), (completed.GetProperty("spot").GetString(), completed.GetProperty("duration").GetDouble()));
        Assert.Equal(new[] { "start", "end" }, completed.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("kind").GetString()));
        Assert.Same(recording, profile.Recording);
    }

    [Fact]
    public void TheImageLimitKeepsRecordingProbesAsText()
    {
        using var recording = RotationDiagnosticRecording.Start(_directory, maximumImages: 1);
        using var crop = new Bitmap(40, 20);
        for (var index = 0; index < 3; index++)
            recording.Probe(LootSpotCatalog.HermesiaId, DateTimeOffset.UnixEpoch.AddSeconds(index * 3), crop, new Size(400, 200), new Rectangle(0, 0, 40, 20));
        recording.Dispose();

        var entries = Entries(recording.RecordingPath!);
        Assert.Equal(new[] { true, false, false }, entries.Where(entry => Type(entry) == "probe")
            .Select(probe => probe.GetProperty("image").ValueKind == JsonValueKind.String));
        Assert.Single(entries, entry => Type(entry) == "note" && entry.GetProperty("kind").GetString() == "image-limit");
        Assert.Single(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(recording.RecordingPath)!, RotationDiagnosticRecording.ImageDirectory)));
    }

    [Fact]
    public void AnUnwritableLocationStopsOnlyTheRecording()
    {
        Directory.CreateDirectory(_directory);
        var blocked = Path.Combine(_directory, "file");
        File.WriteAllText(blocked, "");

        using var recording = RotationDiagnosticRecording.Start(blocked);
        using var crop = new Bitmap(40, 20);
        recording.Probe(LootSpotCatalog.HermesiaId, DateTimeOffset.UnixEpoch, crop, new Size(400, 200), new Rectangle(0, 0, 40, 20));
        recording.Read(LootSpotCatalog.HermesiaId, DateTimeOffset.UnixEpoch, "text", []);

        Assert.False(recording.IsRecording);
        Assert.NotNull(recording.LastError);
    }

    private static JsonElement[] Entries(string path) =>
        File.ReadAllLines(path).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();

    private static string? Type(JsonElement entry) => entry.GetProperty("type").GetString();

    private sealed class CompletingProfile : IRotationProfileMonitor
    {
        public List<(DateTimeOffset StartedAt, RotationRun Run)> Completed { get; } = [];
        public RotationDiagnosticRecording? Recording { get; private set; }
        public void Observe(Bitmap frame, DateTimeOffset at) { }
        public void Interrupt(string status) { }
        public RotationMonitorSnapshot Snapshot(DateTimeOffset now) => new() { Status = "Test" };
        public (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted()
        {
            var result = Completed.ToArray();
            Completed.Clear();
            return result;
        }
        public void AttachDiagnostics(RotationDiagnosticRecording? recording, string spotId) => Recording = recording;
        public void Dispose() { }
    }
}

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task RotationDiagnosticsRecordOnlySessionsStartedWithTheOption()
    {
        await using var fixture = new Fixture(autoUpload: false);
        Assert.True((await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { RecordRotation = true })).Succeeded);

        await fixture.Service.ToggleTrackingAsync();

        Assert.True(fixture.Service.State.IsRecordingRotation);
        Assert.False(fixture.Service.State.IsRecording);
        var path = fixture.Service.State.RotationRecordingPath!;
        Assert.StartsWith(Path.Combine(fixture.DirectoryPath, "diagnostics", "rotation-"), path);
        Assert.True(File.Exists(path));
        Assert.False((await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { RecordRotation = false })).Succeeded);

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(path, fixture.Service.State.RotationRecordingPath);
        await fixture.Service.NewSessionAsync();
        Assert.False(fixture.Service.State.IsRecordingRotation);
        Assert.Null(fixture.Service.State.RotationRecordingPath);
        Assert.False(fixture.Service.Preferences.RecordRotation);
    }
}
