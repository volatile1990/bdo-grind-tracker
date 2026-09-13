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
    public void LargestOverlapKeepsTheWindowOnItsMainMonitorInsteadOfTheFirstIntersectingOne()
    {
        // Only 20 pixels overlap the primary screen; 1180 belong to the secondary.
        var saved = new WindowPlacement(-1180, 120, 1200, 800, false);

        var restored = saved.Fit([new(0, 0, 1920, 1040), new(-1920, 0, 1920, 1040)]);

        Assert.Equal(new Rectangle(-1200, 120, 1200, 800), restored);
    }

    [Fact]
    public void NormalWindowUsesItsCurrentBoundsInsteadOfOldRestoreBounds()
    {
        var current = new Rectangle(-1700, 120, 1200, 800);

        var placement = WindowPlacement.Capture(current, new(20, 30, 860, 640),
            FormWindowState.Normal, FormWindowState.Maximized);

        Assert.Equal(new WindowPlacement(current.X, current.Y, current.Width, current.Height, false), placement);
    }

    [Theory]
    [InlineData(FormWindowState.Maximized, true)]
    [InlineData(FormWindowState.Normal, false)]
    public void MinimizedWindowPreservesItsPreviousStateAndRealRestoreBounds(
        FormWindowState lastNonMinimizedState, bool maximized)
    {
        var restore = new Rectangle(-1700, 120, 1200, 800);

        var placement = WindowPlacement.Capture(new(-32000, -32000, 160, 28), restore,
            FormWindowState.Minimized, lastNonMinimizedState);

        Assert.Equal(new WindowPlacement(restore.X, restore.Y, restore.Width, restore.Height, maximized), placement);
    }

    [Fact]
    public void MaximizedWindowSavesItsNormalRestoreRectangleAndMaximization()
    {
        var restore = new Rectangle(110, 90, 1200, 800);

        var placement = WindowPlacement.Capture(new(0, 0, 1920, 1040), restore,
            FormWindowState.Maximized, FormWindowState.Normal);

        Assert.Equal(new WindowPlacement(restore.X, restore.Y, restore.Width, restore.Height, true), placement);
    }

    [Theory]
    [InlineData(FormWindowState.Normal, 0, 800)]
    [InlineData(FormWindowState.Normal, 1200, 0)]
    [InlineData(FormWindowState.Maximized, -1, 800)]
    [InlineData(FormWindowState.Maximized, 1200, -1)]
    [InlineData(FormWindowState.Minimized, 0, 800)]
    [InlineData(FormWindowState.Minimized, 1200, 0)]
    public void InvalidSelectedBoundsCannotOverwriteAUsableSavedPlacement(FormWindowState state, int width, int height)
    {
        var invalid = new Rectangle(100, 100, width, height);
        var valid = new Rectangle(100, 100, 1200, 800);

        var placement = WindowPlacement.Capture(state == FormWindowState.Normal ? invalid : valid,
            state == FormWindowState.Normal ? valid : invalid, state, FormWindowState.Normal);

        Assert.Null(placement);
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
