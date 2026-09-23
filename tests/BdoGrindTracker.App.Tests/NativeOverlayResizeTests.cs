using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeOverlayResizeTests
{
    private static readonly Rectangle LargeMonitor = new(-1920, -200, 5120, 2880);

    [Theory]
    // The pointer's distance along the diagonal drives one zoom; the canvas and every module stay as saved.
    [InlineData(140, 0, 1.2, 480, 360)]
    [InlineData(0, 140, 1.2, 480, 360)]
    [InlineData(140, 140, 1.4, 560, 420)]
    [InlineData(-70, -70, .8, 320, 240)]
    [InlineData(-140, 0, .8, 320, 240)]
    public void CornerDragZoomsTheWholeOverlayInsteadOfTheCanvas(int dx, int dy, double scale, int width, int height)
    {
        var original = InitialLayout();
        var bounds = new Rectangle(-1700, 0, 400, 300);
        var resize = new NativeOverlayResize(original, bounds, LargeMonitor, 96);

        var frame = resize.Update(new Size(dx, dy));

        Assert.Equal(scale, frame.Settings.Scale, 8);
        Assert.Equal(new Rectangle(bounds.Location, new Size(width, height)), frame.Bounds);
        // Canvas, modules and their content references are untouched: only the zoom changed.
        AssertLayout(original with { Scale = frame.Settings.Scale }, frame.Settings);
        Assert.Same(frame, resize.Current);
        AssertPlacedBounds(frame, LargeMonitor, 96);
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("dashboard")]
    [InlineData("loot")]
    [InlineData("loot-strip")]
    public void EveryPresetKeepsItsModulesAndPreferencesWhileZooming(string preset)
    {
        var original = OverlayCatalog.Preset(preset) with { SnapToGrid = false };
        var bounds = new Rectangle(-1700, 0, (int)original.Width, (int)original.Height);

        var frame = new NativeOverlayResize(original, bounds, LargeMonitor, 96).Update(new Size(168, 48));

        Assert.True(frame.Settings.Scale > original.Scale);
        Assert.Equal(original.Width, frame.Settings.Width);
        Assert.Equal(original.Height, frame.Settings.Height);
        for (var index = 0; index < original.Widgets.Count; index++)
        {
            var before = original.Widgets[index];
            var after = frame.Settings.Widgets[index];
            Assert.Equal(before, after);
            Assert.Equal(before.FontScale, after.FontScale);
            Assert.Equal(before.ItemSize, after.ItemSize);
            Assert.Equal(before.ItemLimit, after.ItemLimit);
            Assert.Equal(before.ItemFilter, after.ItemFilter);
            Assert.Equal(before.ItemSort, after.ItemSort);
        }
        AssertPlacedBounds(frame, LargeMonitor, 96);
    }

    [Fact]
    public void EveryPointerSampleUsesTheOriginalGestureLayoutAndCanReturnToItsStart()
    {
        var original = InitialLayout();
        var bounds = new Rectangle(-1700, 0, 400, 300);
        var resize = new NativeOverlayResize(original, bounds, LargeMonitor, 96);

        resize.Update(new Size(240, 80));
        var second = resize.Update(new Size(-80, 40));

        Assert.Equal(.94, second.Settings.Scale, 8);
        Assert.Equal(new Size(376, 282), second.Bounds.Size);

        var restored = resize.Update(Size.Empty);
        Assert.Equal(bounds, restored.Bounds);
        AssertLayout(original, restored.Settings);
        Assert.All(restored.Settings.Widgets, widget =>
        {
            Assert.Null(widget.ContentWidth);
            Assert.Null(widget.ContentHeight);
        });
    }

    [Fact]
    public void RepeatedGesturesRoundTripWithoutChangingModulesOrTheCanvas()
    {
        var original = InitialLayout();
        var layout = original;
        var bounds = new Rectangle(-1700, 0, 400, 300);
        foreach (var target in new[] { new Size(200, 150), new Size(800, 600), new Size(400, 300) })
        {
            var frame = new NativeOverlayResize(layout, bounds, LargeMonitor, 96)
                .Update(Size.Subtract(target, bounds.Size));
            layout = frame.Settings;
            bounds = frame.Bounds;
            AssertPlacedBounds(frame, LargeMonitor, 96);
        }

        Assert.Equal(new Size(400, 300), bounds.Size);
        Assert.Equal(original.Scale, layout.Scale, 8);
        AssertLayout(original, layout);
        for (var index = 0; index < original.Widgets.Count; index++)
        {
            var before = original.Widgets[index];
            var after = layout.Widgets[index];
            Assert.Equal(before.ContentWidth, after.ContentWidth);
            Assert.Equal(before.ContentHeight, after.ContentHeight);
            Assert.Equal(before.ItemNames, after.ItemNames);
            var initialContent = OverlayContentLayout.Create(before, OverlaySnapshot.Demo);
            var restoredContent = OverlayContentLayout.Create(after, OverlaySnapshot.Demo);
            Assert.Equal(initialContent.Scale, restoredContent.Scale, 8);
            Assert.Equal(initialContent.LayoutWidget.Width, restoredContent.LayoutWidget.Width, 8);
            Assert.Equal(initialContent.LayoutWidget.Height, restoredContent.LayoutWidget.Height, 8);
        }
    }

    [Theory]
    // The same pointer distance is a smaller share of a window that a higher zoom or DPI already enlarged.
    [InlineData(1, 96, 1.28)]
    [InlineData(1.25, 144, 1.44)]
    [InlineData(.75, 192, .89)]
    public void PhysicalPointerDistanceRespectsConfiguredScaleAndDpi(double scale, double dpi, double expected)
    {
        var original = InitialLayout() with { Width = 384, Height = 256, Scale = scale, PositionX = .1, PositionY = .15 };
        var bounds = Place(original, LargeMonitor, dpi);

        var frame = new NativeOverlayResize(original, bounds, LargeMonitor, dpi).Update(new Size(120, 60));

        Assert.Equal(expected, frame.Settings.Scale, 8);
        Assert.Equal(original.Width, frame.Settings.Width);
        Assert.Equal(original.Height, frame.Settings.Height);
        AssertPlacedBounds(frame, LargeMonitor, dpi);
    }

    [Theory]
    [InlineData(-10000, -10000, .5)]
    [InlineData(10000, 10000, 2)]
    public void TheZoomStaysWithinTheEditorsLimits(int dx, int dy, double scale)
    {
        var bounds = new Rectangle(-1700, 0, 400, 300);
        var frame = new NativeOverlayResize(InitialLayout(), bounds, LargeMonitor, 96).Update(new Size(dx, dy));

        Assert.Equal(scale, frame.Settings.Scale, 8);
        Assert.Equal(new Size((int)(400 * scale), (int)(300 * scale)), frame.Bounds.Size);
        Assert.Equal(400, frame.Settings.Width);
        Assert.Equal(300, frame.Settings.Height);
        AssertPlacedBounds(frame, LargeMonitor, 96);
    }

    [Fact]
    public void AZoomBeyondTheMonitorIsSavedAsTheZoomThatStillFits()
    {
        var monitor = new Rectangle(-1920, -200, 1920, 1080);
        var original = InitialLayout() with { Width = 1200, Height = 800, PositionX = 0, PositionY = 0 };
        var bounds = Place(original, monitor, 96);
        Assert.Equal(new Size(1200, 800), bounds.Size);

        var frame = new NativeOverlayResize(original, bounds, monitor, 96).Update(new Size(10000, 10000));

        Assert.Equal(1.35, frame.Settings.Scale, 8);
        Assert.Equal(new Size(1620, 1080), frame.Bounds.Size);
        Assert.True(monitor.Contains(frame.Bounds));
        // Reopening the overlay shows exactly the size the drag ended with.
        AssertPlacedBounds(frame, monitor, 96);
    }

    [Fact]
    public void ReachingTheMonitorEdgeKeepsTheOppositeCornerFixed()
    {
        var monitor = new Rectangle(-1920, -200, 1920, 1080);
        var bounds = new Rectangle(-600, 500, 400, 300);

        var frame = new NativeOverlayResize(InitialLayout(), bounds, monitor, 96).Update(new Size(10000, 10000));

        // The zoom stops where the opposite edge reaches the monitor, so the dragged window keeps its corner.
        Assert.Equal(bounds.Location, frame.Bounds.Location);
        Assert.Equal(1.26, frame.Settings.Scale, 8);
        Assert.True(monitor.Contains(frame.Bounds));
        AssertPlacedBounds(frame, monitor, 96);
    }

    [Fact]
    public void ExtremeDpiAndSmallMonitorStillGiveAValidZoomAndIdenticalSavedBounds()
    {
        var monitor = new Rectangle(-1280, 200, 1280, 720);
        var original = InitialLayout() with { Scale = 2, PositionX = 0, PositionY = 0 };
        var bounds = Place(original, monitor, 768);

        var frame = new NativeOverlayResize(original, bounds, monitor, 768).Update(new Size(-10000, -10000));

        Assert.Equal(original.Width, frame.Settings.Width);
        Assert.Equal(original.Height, frame.Settings.Height);
        Assert.Equal(.5, frame.Settings.Scale, 8);
        Assert.True(monitor.Contains(frame.Bounds));
        Assert.True(frame.Bounds.Width > 0 && frame.Bounds.Height > 0);
        AssertPlacedBounds(frame, monitor, 768);
    }

    [Theory]
    // Without the grid every hundredth of the zoom is available; with it, five-percent steps.
    [InlineData(false, 1.04, 416, 312)]
    [InlineData(true, 1.05, 420, 315)]
    public void OptionalGridSnapsTheZoomToFivePercentSteps(bool snap, double scale, int width, int height)
    {
        var original = InitialLayout() with { SnapToGrid = snap };
        var frame = new NativeOverlayResize(original, new Rectangle(-1700, 0, 400, 300), LargeMonitor, 96)
            .Update(new Size(13, 13));

        Assert.Equal(scale, frame.Settings.Scale, 8);
        Assert.Equal(new Size(width, height), frame.Bounds.Size);
        Assert.Equal(400, frame.Settings.Width);
        Assert.Equal(300, frame.Settings.Height);
        AssertPlacedBounds(frame, LargeMonitor, 96);
    }

    [Theory]
    [InlineData(240, -100)]
    [InlineData(-160, 140)]
    public void ThePreviewRendersTheSamePixelsAndControlsAsTheZoomedLayout(int dx, int dy)
    {
        var original = InitialLayout();
        var frame = new NativeOverlayResize(original, new Rectangle(-1700, 0, 400, 300), LargeMonitor, 96)
            .Update(new Size(dx, dy));
        var zoomed = original with { Scale = frame.Settings.Scale };
        var snapshot = OverlaySnapshot.Demo with { CanToggleTracking = true };
        using var renderer = new NativeOverlayRenderer();
        using var nativeImage = renderer.Render(frame.Bounds.Size, frame.Settings, snapshot, out var nativeActions);
        using var zoomedImage = renderer.Render(frame.Bounds.Size, zoomed, snapshot, out var zoomedActions);

        Assert.Equal(Pixels(zoomedImage), Pixels(nativeImage));
        Assert.Equal(zoomedActions.Count, nativeActions.Count);
        foreach (var (key, rectangle) in zoomedActions)
            Assert.Equal(rectangle, nativeActions[key]);
        Assert.Contains(nativeActions.Keys, key => key.StartsWith("toggle-tracking:", StringComparison.Ordinal));
    }

    private static OverlaySettings InitialLayout() => new()
    {
        Width = 400, Height = 300, SnapToGrid = false,
        Widgets =
        [
            OverlayCatalog.CreateWidget("duration", 12.5, 10.25) with { Width = 160, Height = 72, FontScale = 1.2 },
            OverlayCatalog.CreateWidget("drop-grid", 190, 110) with
            {
                Width = 200, Height = 176, FontScale = 1.4, ItemSize = 48, ItemLimit = 17,
                ItemSort = "quantity", ItemNames = Array.AsReadOnly(new[] { "Black Stone" }),
            },
            OverlayCatalog.CreateWidget("controls", 12, 220) with { Width = 140, Height = 56 },
        ],
    };

    private static void AssertLayout(OverlaySettings expected, OverlaySettings actual)
    {
        Assert.Equal(expected.Width, actual.Width, 8);
        Assert.Equal(expected.Height, actual.Height, 8);
        Assert.Equal(expected.Scale, actual.Scale, 8);
        Assert.Equal(expected.Widgets.Count, actual.Widgets.Count);
        for (var index = 0; index < expected.Widgets.Count; index++)
            Assert.Equal(expected.Widgets[index], actual.Widgets[index]);
    }

    private static Rectangle Place(OverlaySettings settings, Rectangle monitor, double dpi) =>
        NativeOverlayGeometry.Place(monitor, settings.Width, settings.Height,
            settings.PositionX, settings.PositionY, settings.Scale, dpi);

    private static void AssertPlacedBounds(NativeOverlayResizeFrame frame, Rectangle monitor, double dpi) =>
        Assert.Equal(frame.Bounds, Place(frame.Settings, monitor, dpi));

    private static byte[] Pixels(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }
}
