using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationMonitorSpotTests
{
    [Fact]
    public void UnknownAndUnsupportedSpotsNeverShowHermesiaRecords()
    {
        var demo = HermesiaRotationDemo.At(350);
        foreach (var spot in new string?[] { null, "unsupported-spot" })
        {
            var snapshot = new OverlayMetrics().Update(new TrackerState { SpotId = spot, Rotation = demo }, new());
            Assert.Equal(spot, snapshot.Rotation.SpotId);
            Assert.False(snapshot.Rotation.HasProfile);
            Assert.Null(snapshot.Rotation.Best);
            Assert.Empty(snapshot.Rotation.Events);
            Assert.False(snapshot.Rotation.Synchronized);
            Assert.DoesNotContain("Hermesia", snapshot.Rotation.SpotName);
        }
    }

    [Fact]
    public void SupportedSpotKeepsItsOwnRunAndUsesTrackerSpotName()
    {
        var snapshot = new OverlayMetrics().Update(new TrackerState {
            SpotId = LootSpotCatalog.HermesiaId, Rotation = HermesiaRotationDemo.At(350)
        }, new());
        Assert.True(snapshot.Rotation.HasProfile);
        Assert.Contains("Hermesia", snapshot.Rotation.SpotName);
        Assert.Equal(350, snapshot.Rotation.Elapsed);
        Assert.NotNull(snapshot.Rotation.Best);
    }

    [Fact]
    public void SpotSwitchDisposesPreviousRecognitionAndReturningStartsFresh()
    {
        var created = new List<Profile>();
        using var monitor = new RotationMonitor(spot => {
            if (spot != LootSpotCatalog.HermesiaId) return null;
            var profile = new Profile(); created.Add(profile); return profile;
        });
        var now = DateTimeOffset.UtcNow;
        Assert.True(monitor.Snapshot(now, LootSpotCatalog.HermesiaId).HasProfile);
        Assert.False(monitor.Snapshot(now, "unsupported-spot").HasProfile);
        Assert.True(created[0].Disposed);
        Assert.True(monitor.Snapshot(now, LootSpotCatalog.HermesiaId).HasProfile);
        Assert.Equal(2, created.Count);
        Assert.False(monitor.Snapshot(now, LootSpotCatalog.HermesiaId).Synchronized);
        Assert.Equal(2, created.Count);
    }

    [Fact]
    public void BeforeTheSpotIsKnownEveryProfileWatchesAndTheDetectedSpotKeepsItsOwn()
    {
        var created = new Dictionary<string, Profile>();
        using var monitor = new RotationMonitor(spot => spot is null ? null : created[spot] = new Profile());
        using var frame = new Bitmap(4, 4);
        var now = DateTimeOffset.UtcNow;

        monitor.Observe(frame, now, null);
        monitor.Observe(frame, now.AddSeconds(1), null);
        Assert.Equal(RotationProfiles.SupportedSpotIds.Order(), created.Keys.Order());
        Assert.All(created.Values, profile => Assert.Equal(2, profile.Observed));
        Assert.False(monitor.Snapshot(now.AddSeconds(1), null).HasProfile);

        var hermesia = created[LootSpotCatalog.HermesiaId];
        monitor.Observe(frame, now.AddSeconds(2), LootSpotCatalog.HermesiaId);
        Assert.Equal(3, hermesia.Observed);
        Assert.False(hermesia.Disposed);
        Assert.All(created.Where(pair => pair.Key != LootSpotCatalog.HermesiaId), pair => Assert.True(pair.Value.Disposed));
        // The detected spot keeps its provisional profile instead of creating another.
        Assert.Equal(RotationProfiles.SupportedSpotIds.Count, created.Count);
        Assert.Equal("Vorläufig", monitor.Snapshot(now.AddSeconds(2), LootSpotCatalog.HermesiaId).Events.Single().Label);
    }

    [Fact]
    public async Task TheFirstOfferingOrderBeforeTheFirstDropStartsTheRotation()
    {
        var text = "The overseer orders the Black Crystals to be offered up.";
        HermesiaRotationMonitor? hermesia = null;
        using var monitor = new RotationMonitor(spot => spot switch
        {
            LootSpotCatalog.HermesiaId => hermesia = new HermesiaRotationMonitor(recognize: _ => text),
            LootSpotCatalog.AphrodonId => new BufferedRotationProfileMonitor(new AphrodonRotationTracker(),
                RotationMessageProfile.Aphrodon, _ => text),
            _ => null,
        });
        using var frame = new Bitmap(320, 200);
        var start = DateTimeOffset.UnixEpoch;
        async Task Observe(double seconds, string? spot)
        {
            monitor.Observe(frame, start.AddSeconds(seconds), spot);
            await hermesia!.PendingAnalysis.WaitAsync(TimeSpan.FromSeconds(30));
        }
        // The banner is visible before any loot identifies the spot.
        for (var seconds = 0d; seconds <= 6; seconds += .5) await Observe(seconds, null);
        text = "";
        for (var seconds = 6.5d; seconds <= 9.5; seconds += .5) await Observe(seconds, null);

        // The first trash drop reveals Hermesia ten seconds after the offering order.
        await Observe(10, LootSpotCatalog.HermesiaId);
        var state = monitor.Snapshot(start.AddSeconds(10), LootSpotCatalog.HermesiaId);
        Assert.True(state.Synchronized);
        Assert.Equal(10, state.Elapsed);
        Assert.Equal(new[] { "start", "offer" }, state.Events.Select(e => e.Kind));
        Assert.Contains("1 / 5", state.Status);
    }

    [Fact]
    public void ARotationStartSeenBeforeTheSessionIsMeasuredFromItsBanner()
    {
        // The automatic grind detection recognizes the banner while no session exists; the trash drop that starts
        // the session arrives forty seconds later, and only then is the spot known.
        using var monitor = new RotationMonitor(spot => spot == LootSpotCatalog.MagaiaId
            ? new BufferedRotationProfileMonitor(new RotationPlatform(RotationDefinition.Magaia), RotationMessageProfile.Magaia)
            : null);
        var start = DateTimeOffset.UnixEpoch;

        monitor.ObserveRotationStart(new RotationStartSighting(LootSpotCatalog.MagaiaId, "start", "Sünder beschworen", start));
        // The first captured frames may carry no HUD. That says nothing about a banner seen before any capture.
        monitor.Interrupt("Bildsignal fehlt · warte auf erstes Ereignis");
        var state = monitor.Snapshot(start.AddSeconds(40), LootSpotCatalog.MagaiaId);

        Assert.True(state.Synchronized);
        Assert.Equal(40, state.Elapsed, 1);
        Assert.Equal("start", Assert.Single(state.Events).Kind);

        // A sighting for another spot never reaches the running one.
        monitor.ObserveRotationStart(new RotationStartSighting(LootSpotCatalog.AphrodonId, "restart", "Rotation aktiviert", start.AddSeconds(50)));
        Assert.Equal(60, monitor.Snapshot(start.AddSeconds(60), LootSpotCatalog.MagaiaId).Elapsed, 1);
    }

    [Fact]
    public void ExistingModuleMigratesWithoutLosingGeometryOrComparison()
    {
        var widget = new OverlayWidget { Kind = "hermesia-rotation", X = 20, Y = 40, Width = 500,
            Height = 260, RotationComparison = "ideal" };
        var migrated = Assert.Single(OverlayLayout.Normalize(new() { Widgets = [widget] }).Widgets);
        Assert.Equal("rotation-monitor", migrated.Kind);
        Assert.Equal(widget.Id, migrated.Id);
        Assert.Equal(500, migrated.Width);
        Assert.Equal(20, migrated.X);
        Assert.Equal("ideal", migrated.RotationComparison);
        Assert.Equal("Rotation Monitor", OverlayCatalog.Find(migrated.Kind)!.Label);
    }

    private sealed class Profile : IRotationProfileMonitor
    {
        public bool Disposed { get; private set; }
        public int Observed { get; private set; }
        public void Observe(Bitmap frame, DateTimeOffset at) => Observed++;
        public void Interrupt(string status) { }
        public RotationMonitorSnapshot Snapshot(DateTimeOffset now) => new() { Events = [new("offer", "Vorläufig", 0)] };
        public void Dispose() => Disposed = true;
    }
}
