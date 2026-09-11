using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeOverlayResizeTests
{
    private static readonly Rectangle LargeMonitor = new(-1920, -200, 5120, 2880);

    [Theory]
    [InlineData(160, 0, 560, 300)]
    [InlineData(0, 120, 400, 420)]
    [InlineData(160, -120, 560, 180)]
    [InlineData(-160, 120, 240, 420)]
    [InlineData(-160, -120, 240, 180)]
    public void CornerDragChangesEachAxisIndependently(int dx, int dy, int width, int height)
    {
        var original = InitialLayout();
        var bounds = new Rectangle(-1700, 0, 400, 300);
        var resize = new NativeOverlayResize(original, bounds, LargeMonitor, 96);

        var frame = resize.Update(new Size(dx, dy));

        Assert.Equal(new Rectangle(bounds.Location, new Size(width, height)), frame.Bounds);
        AssertLayout(OverlayLayout.ResizeCanvas(original, width, height), frame.Settings);
        Assert.Equal(original.Scale, frame.Settings.Scale);
        Assert.Same(frame, resize.Current);
        AssertPlacedBounds(frame, LargeMonitor, 96);
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("dashboard")]
    [InlineData("loot")]
    [InlineData("loot-strip")]
    public void NativeDragMatchesEditorCanvasAndKeepsContentReferencesAndPreferences(string preset)
    {
        var original = OverlayCatalog.Preset(preset) with { SnapToGrid = false };
        var bounds = new Rectangle(-1700, 0, (int)original.Width, (int)original.Height);
        var target = new Size(bounds.Width + 168, bounds.Height - 48);

        var frame = new NativeOverlayResize(original, bounds, LargeMonitor, 96).Update(new Size(168, -48));

        AssertLayout(OverlayLayout.ResizeCanvas(original, target.Width, target.Height), frame.Settings);
        for (var index = 0; index < original.Widgets.Count; index++)
        {
            var before = original.Widgets[index];
            var after = frame.Settings.Widgets[index];
            Assert.Equal(before.Width, after.ContentWidth);
            Assert.Equal(before.Height, after.ContentHeight);
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

        Assert.Equal(new Size(320, 340), second.Bounds.Size);
        AssertLayout(OverlayLayout.ResizeCanvas(original, 320, 340), second.Settings);

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
    public void RepeatedGesturesRoundTripWithoutChangingContentPreferencesOrReferenceSizes()
    {
        var original = InitialLayout();
        var layout = original;
        var bounds = new Rectangle(-1700, 0, 400, 300);
        foreach (var target in new[] { new Size(200, 128), new Size(1000, 900), new Size(400, 300) })
        {
            var frame = new NativeOverlayResize(layout, bounds, LargeMonitor, 96)
                .Update(Size.Subtract(target, bounds.Size));
            layout = frame.Settings;
            bounds = frame.Bounds;
            AssertPlacedBounds(frame, LargeMonitor, 96);
        }

        Assert.Equal(new Size(400, 300), bounds.Size);
        Assert.Equal(original.Scale, layout.Scale);
        for (var index = 0; index < original.Widgets.Count; index++)
        {
            var before = original.Widgets[index];
            var after = layout.Widgets[index];
            Assert.Equal(before.X, after.X, 8);
            Assert.Equal(before.Y, after.Y, 8);
            Assert.Equal(before.Width, after.Width, 8);
            Assert.Equal(before.Height, after.Height, 8);
            Assert.Equal(before.Width, after.ContentWidth);
            Assert.Equal(before.Height, after.ContentHeight);
            Assert.Equal(before.FontScale, after.FontScale);
            Assert.Equal(before.ItemSize, after.ItemSize);
            Assert.Equal(before.ItemNames, after.ItemNames);
            var initialContent = OverlayContentLayout.Create(before, OverlaySnapshot.Demo);
            var restoredContent = OverlayContentLayout.Create(after, OverlaySnapshot.Demo);
            Assert.Equal(initialContent.Scale, restoredContent.Scale, 8);
            Assert.Equal(initialContent.LayoutWidget.Width, restoredContent.LayoutWidget.Width, 8);
            Assert.Equal(initialContent.LayoutWidget.Height, restoredContent.LayoutWidget.Height, 8);
        }
    }

    [Theory]
    [InlineData(1, 96, 504, 196)]
    [InlineData(1.25, 144, 448, 224)]
    [InlineData(.75, 192, 464, 216)]
    public void PhysicalPointerDistanceRespectsConfiguredScaleAndDpi(double scale, double dpi,
        double expectedWidth, double expectedHeight)
    {
        var original = OverlayLayout.ResizeCanvas(InitialLayout(), 384, 256) with
        {
            Scale = scale, PositionX = .1, PositionY = .15,
        };
        var bounds = Place(original, LargeMonitor, dpi);

        var frame = new NativeOverlayResize(original, bounds, LargeMonitor, dpi).Update(new Size(120, -60));

        Assert.Equal(expectedWidth, frame.Settings.Width, 8);
        Assert.Equal(expectedHeight, frame.Settings.Height, 8);
        Assert.Equal(scale, frame.Settings.Scale);
        Assert.Equal(new Rectangle(bounds.Location, new Size(bounds.Width + 120, bounds.Height - 60)), frame.Bounds);
        AssertLayout(OverlayLayout.ResizeCanvas(original, expectedWidth, expectedHeight), frame.Settings);
        AssertPlacedBounds(frame, LargeMonitor, dpi);
    }

    [Fact]
    public void APreviouslyMonitorFittedCanvasKeepsTheDraggedSizeWhenSaved()
    {
        var monitor = new Rectangle(-1280, 200, 1280, 720);
        var original = OverlayLayout.ResizeCanvas(InitialLayout(), 1600, 1200) with
        {
            Scale = 2, PositionX = 0, PositionY = 0,
        };
        var bounds = Place(original, monitor, 192);
        Assert.Equal(new Size(960, 720), bounds.Size);

        var frame = new NativeOverlayResize(original, bounds, monitor, 192).Update(new Size(-160, -120));

        Assert.Equal(new Rectangle(bounds.Location, new Size(800, 600)), frame.Bounds);
        Assert.Equal(200, frame.Settings.Width);
        Assert.Equal(150, frame.Settings.Height);
        Assert.Equal(2, frame.Settings.Scale);
        AssertLayout(OverlayLayout.ResizeCanvas(original, 200, 150), frame.Settings);
        AssertPlacedBounds(frame, monitor, 192);
    }

    [Theory]
    [InlineData(-10000, -10000, 160, 64)]
    [InlineData(10000, 10000, 1600, 1200)]
    [InlineData(-10000, 10000, 160, 1200)]
    [InlineData(10000, -10000, 1600, 64)]
    public void EditorCanvasLimitsApplyIndependently(int dx, int dy, int width, int height)
    {
        var bounds = new Rectangle(-1700, 0, 400, 300);
        var frame = new NativeOverlayResize(InitialLayout(), bounds, LargeMonitor, 96)
            .Update(new Size(dx, dy));

        Assert.Equal(new Rectangle(bounds.Location, new Size(width, height)), frame.Bounds);
        Assert.Equal(width, frame.Settings.Width);
        Assert.Equal(height, frame.Settings.Height);
        AssertPlacedBounds(frame, LargeMonitor, 96);
    }

    [Fact]
    public void ReachingTheMonitorEdgeKeepsTheOppositeCornerFixed()
    {
        var monitor = new Rectangle(-1920, -200, 1920, 1080);
        var bounds = new Rectangle(-600, 500, 400, 300);

        var frame = new NativeOverlayResize(InitialLayout(), bounds, monitor, 96)
            .Update(new Size(10000, 10000));

        Assert.Equal(new Rectangle(-600, 500, 600, 380), frame.Bounds);
        Assert.Equal(bounds.Location, frame.Bounds.Location);
        Assert.Equal(monitor.Right, frame.Bounds.Right);
        Assert.Equal(monitor.Bottom, frame.Bounds.Bottom);
        AssertPlacedBounds(frame, monitor, 96);
    }

    [Fact]
    public void ExtremeDpiAndSmallMonitorStillGiveValidCanvasAndIdenticalSavedBounds()
    {
        var monitor = new Rectangle(-1280, 200, 1280, 720);
        var original = InitialLayout() with { Scale = 2, PositionX = 0, PositionY = 0 };
        var bounds = Place(original, monitor, 768);

        var frame = new NativeOverlayResize(original, bounds, monitor, 768)
            .Update(new Size(-10000, -10000));

        Assert.Equal(original.Width, frame.Settings.Width);
        Assert.Equal(original.Height, frame.Settings.Height);
        Assert.Equal(bounds, frame.Bounds);
        Assert.Equal(2, frame.Settings.Scale);
        Assert.True(monitor.Contains(frame.Bounds));
        Assert.True(frame.Bounds.Width > 0 && frame.Bounds.Height > 0);
        AssertPlacedBounds(frame, monitor, 768);
    }

    [Theory]
    [InlineData(160, 1200, 0, -1)]
    [InlineData(1600, 64, -1, 0)]
    public void AlreadyFittedThinCanvasDoesNotInflateAnUntouchedAxis(int width, int height, int dx, int dy)
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var original = OverlayLayout.ResizeCanvas(InitialLayout(), width, height) with
        {
            Scale = 2, PositionX = 0, PositionY = 0,
        };
        var bounds = Place(original, monitor, 192);
        var frame = new NativeOverlayResize(original, bounds, monitor, 192).Update(new Size(dx, dy));

        Assert.Equal(bounds, frame.Bounds);
        AssertLayout(original, frame.Settings);
        AssertPlacedBounds(frame, monitor, 192);
    }

    [Fact]
    public void FittedThinCanvasCanResizeOnceThePointerReachesTheEditorMinimum()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var original = OverlayLayout.ResizeCanvas(InitialLayout(), 160, 1200) with
        {
            Scale = 2, PositionX = 0, PositionY = 0,
        };
        var bounds = Place(original, monitor, 192);
        var frame = new NativeOverlayResize(original, bounds, monitor, 192).Update(new Size(496, -480));

        Assert.Equal(new Size(640, 600), frame.Bounds.Size);
        Assert.Equal(160, frame.Settings.Width);
        Assert.Equal(150, frame.Settings.Height);
        AssertPlacedBounds(frame, monitor, 192);
    }

    [Theory]
    [InlineData(false, 413, 287)]
    [InlineData(true, 416, 288)]
    public void OptionalGridMatchesTheEditorEightPixelCanvasSnap(bool snap, int width, int height)
    {
        var original = InitialLayout() with { SnapToGrid = snap };
        var frame = new NativeOverlayResize(original, new Rectangle(-1700, 0, 400, 300), LargeMonitor, 96)
            .Update(new Size(13, -13));

        Assert.Equal(width, frame.Settings.Width);
        Assert.Equal(height, frame.Settings.Height);
        Assert.Equal(new Size(width, height), frame.Bounds.Size);
        AssertPlacedBounds(frame, LargeMonitor, 96);
    }

    [Fact]
    public void GridDoesNotAlterAnUntouchedAxisWithANonGridSize()
    {
        var original = InitialLayout() with { SnapToGrid = true };
        var frame = new NativeOverlayResize(original, new Rectangle(-1700, 0, 400, 300), LargeMonitor, 96)
            .Update(new Size(13, 0));

        Assert.Equal(416, frame.Settings.Width);
        Assert.Equal(300, frame.Settings.Height);
        Assert.Equal(300, frame.Bounds.Height);
    }

    [Theory]
    [InlineData(240, -100)]
    [InlineData(-160, 140)]
    public void UnequalAxisPreviewRendersTheSamePixelsAndControlsAsTheEditorLayout(int dx, int dy)
    {
        var original = InitialLayout();
        var frame = new NativeOverlayResize(original, new Rectangle(-1700, 0, 400, 300), LargeMonitor, 96)
            .Update(new Size(dx, dy));
        var editor = OverlayLayout.ResizeCanvas(original, 400 + dx, 300 + dy);
        var snapshot = OverlaySnapshot.Demo with { CanToggleTracking = true };
        using var renderer = new NativeOverlayRenderer();
        using var nativeImage = renderer.Render(frame.Bounds.Size, frame.Settings, snapshot, out var nativeActions);
        using var editorImage = renderer.Render(frame.Bounds.Size, editor, snapshot, out var editorActions);

        Assert.Equal(Pixels(editorImage), Pixels(nativeImage));
        Assert.Equal(editorActions.Count, nativeActions.Count);
        foreach (var (key, rectangle) in editorActions)
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
        Assert.Equal(expected.Scale, actual.Scale);
        Assert.Equal(expected.Widgets.Count, actual.Widgets.Count);
        for (var index = 0; index < expected.Widgets.Count; index++)
            Assert.Equal(expected.Widgets[index], actual.Widgets[index]);
    }

    private static Rectangle Place(OverlaySettings settings, Rectangle monitor, double dpi) =>
        NativeOverlayGeometry.Place(monitor, settings.Width, settings.Height,
            settings.PositionX, settings.PositionY, settings.Scale, dpi);

    private static void AssertPlacedBounds(NativeOverlayResizeFrame frame, Rectangle monitor, double dpi) =>
        Assert.Equal(frame.Bounds, Place(frame.Settings, monitor, dpi));

    private static int[] Pixels(Bitmap image) => Enumerable.Range(0, image.Height)
        .SelectMany(y => Enumerable.Range(0, image.Width).Select(x => image.GetPixel(x, y).ToArgb())).ToArray();
}
