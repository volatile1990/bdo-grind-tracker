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
        foreach (var spot in new string?[] { null, LootSpotCatalog.AphrodonId })
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
        Assert.False(monitor.Snapshot(now, LootSpotCatalog.AphrodonId).HasProfile);
        Assert.True(created[0].Disposed);
        Assert.True(monitor.Snapshot(now, LootSpotCatalog.HermesiaId).HasProfile);
        Assert.Equal(2, created.Count);
        Assert.False(monitor.Snapshot(now, LootSpotCatalog.HermesiaId).Synchronized);
        Assert.Equal(2, created.Count);
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
        public void Observe(Bitmap frame, DateTimeOffset at) { }
        public void Interrupt(string status) { }
        public RotationMonitorSnapshot Snapshot(DateTimeOffset now) => new();
        public void Dispose() => Disposed = true;
    }
}
