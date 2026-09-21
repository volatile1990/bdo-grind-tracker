using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationLootIndexTests
{
    [Fact]
    public void LatestCorrectionSurvivesRestoreAndInterleavedDrops()
    {
        using var monitor = new RotationMonitor(_ => null);
        var at = DateTimeOffset.UnixEpoch;
        var id = Guid.NewGuid();
        monitor.ObserveLootEvents([new(id, at, "Trash", 5)], "event-horizon");
        monitor.ObserveLootEvents([new(id, at, "Trash", 8) { Revision = 1 }], "event-horizon");
        var saved = monitor.ExportTimeline();
        monitor.RestoreSession([], saved);
        monitor.ObserveLootEvents(Enumerable.Range(0, 1000)
            .Select(i => new LootEventView(Guid.NewGuid(), at.AddSeconds(i), "Trash", 1)), "event-horizon");
        monitor.ObserveLootEvents([new(id, at, "Trash", 9) { Revision = 2 }], "event-horizon");
        monitor.ObserveLootEvents([new(id, at, "Trash", 9) { Revision = 2 },
            new(id, at, "Trash", 8) { Revision = 1 }], "event-horizon");
        var timeline = monitor.ExportTimeline();
        Assert.Equal(1003, timeline.Length);
        Assert.Equal(saved[^1].Id, timeline[^1].Corrects);
        Assert.Equal("correction", timeline[^1].Kind);
        Assert.Equal(9, timeline[^1].Quantity);
    }

    [Fact]
    public void NewSessionDoesNotKeepThePreviousCorrectionIndex()
    {
        using var monitor = new RotationMonitor(_ => null);
        var id = Guid.NewGuid();
        monitor.ObserveLootEvents([new(id, DateTimeOffset.UnixEpoch, "Trash", 5)], "event-horizon");
        monitor.RestoreSession([]);
        monitor.ObserveLootEvents([new(id, DateTimeOffset.UnixEpoch, "Trash", 7)], "event-horizon");
        var entry = Assert.Single(monitor.ExportTimeline());
        Assert.Equal("drop", entry.Kind);
        Assert.Null(entry.Corrects);
    }
}
