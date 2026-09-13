using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationSessionPersistenceTests
{
    [Fact]
    public void SessionCollectionKeepsSpotAndDoesNotDuplicateOrLeakIntoNextSession()
    {
        var profile = new Profile();
        using var monitor = new RotationMonitor(_ => profile);
        monitor.Snapshot(DateTimeOffset.UtcNow, LootSpotCatalog.HermesiaId);
        Assert.Single(monitor.ExportSession());
        Assert.Single(monitor.ExportSession());
        var saved = monitor.ExportSession();
        monitor.RestoreSession([]);
        Assert.Empty(monitor.ExportSession());
        monitor.RestoreSession(saved);
        Assert.Equal(LootSpotCatalog.HermesiaId, Assert.Single(monitor.ExportSession()).SpotId);
    }

    [Fact]
    public void HistoryRoundTripPreservesRotationWithoutLoot()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var store = new LootHistoryStore(Path.Combine(directory,"history.json"));
        var now = DateTimeOffset.UtcNow;
        try
        {
            store.Save([new LootHistoryEntry { SessionId = Guid.NewGuid(), StartedAt = now, UpdatedAt = now,
                Duration = TimeSpan.FromMinutes(11), SpotId = LootSpotCatalog.HermesiaId, Totals = [],
                SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = true,
                Rotations = [new(LootSpotCatalog.HermesiaId, now, HermesiaRotationDemo.Reference)] }]);
            var saved = Assert.Single(Assert.Single(store.Load()).Rotations);
            Assert.Equal(now, saved.StartedAt);
            Assert.Equal(HermesiaRotationDemo.Reference.Events, saved.Run.Events);
            Assert.Equal(HermesiaRotationDemo.Reference.Duration, saved.Run.Duration);
        }
        finally { File.Delete(Path.Combine(directory,"history.json")); Directory.Delete(directory); }
    }

    private sealed class Profile : IRotationProfileMonitor
    {
        private bool _drained;
        public (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted()
        {
            if (_drained) return [];
            _drained = true;
            return [(DateTimeOffset.UtcNow, HermesiaRotationDemo.Reference)];
        }
        public void Observe(System.Drawing.Bitmap frame, DateTimeOffset at) { }
        public void Interrupt(string status) { }
        public RotationMonitorSnapshot Snapshot(DateTimeOffset now) => new();
        public void Dispose() { }
    }
}
