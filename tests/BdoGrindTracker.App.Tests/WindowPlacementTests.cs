using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void PlacementOnSecondaryMonitorIsPreserved()
    {
        var saved = new WindowPlacement(-1700, 120, 1200, 800, true);
        Assert.Equal(saved.Bounds, saved.Fit([new(0, 0, 1920, 1040), new(-1920, 0, 1920, 1040)]));
    }
    [Fact]
    public void MissingMonitorReturnsWindowToVisibleWorkArea()
    {
        var bounds = new WindowPlacement(-4000, -2000, 3000, 2000, false).Fit([new(0, 0, 1366, 728)]);
        Assert.Equal(new Rectangle(0, 0, 1366, 728), bounds);
    }
    [Fact]
    public void PlacementRoundTripsIncludingMaximization()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GrindcrestWindowTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "window.json");
        try
        {
            var store = new WindowPlacementStore(path);
            Assert.Null(store.Load());
            var saved = new WindowPlacement(42, 70, 1100, 750, true);
            store.Save(saved);
            Assert.Equal(saved, store.Load());
            File.WriteAllText(path, "broken");
            Assert.Null(store.Load());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
