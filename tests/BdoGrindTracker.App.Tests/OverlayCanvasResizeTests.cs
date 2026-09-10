using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayCanvasResizeTests
{
    [Theory]
    [InlineData("compact")]
    [InlineData("dashboard")]
    [InlineData("loot")]
    [InlineData("loot-strip")]
    public void SmallestCanvasPreservesPresetModuleProportionsAndMargins(string preset)
    {
        var original = OverlayCatalog.Preset(preset);

        var resized = OverlayLayout.ResizeCanvas(original, 160, 64);

        Assert.Equal(160, resized.Width);
        Assert.Equal(64, resized.Height);
        AssertProportions(original, resized);
        Assert.Equal(resized.Widgets, OverlayLayout.Normalize(resized).Widgets);
    }

    [Theory]
    [InlineData(800, 300)]
    [InlineData(400, 600)]
    [InlineData(800, 600)]
    [InlineData(197, 83)]
    public void EachAxisResizesIndependentlyWithoutSnappingFractionalCoordinates(double width, double height)
    {
        var original = new OverlaySettings
        {
            Width = 400,
            Height = 300,
            SnapToGrid = true,
            Widgets =
            [
                OverlayCatalog.CreateWidget("duration", 7.25, 11.5) with { Width = 160.5, Height = 71.25 },
                OverlayCatalog.CreateWidget("trash", 207.5, 218.25) with { Width = 184.5, Height = 73.75 },
            ],
        };

        var resized = OverlayLayout.ResizeCanvas(original, width, height);

        AssertProportions(original, resized);
        Assert.Equal(7.25, original.Widgets[0].X);
        Assert.Equal(400, original.Width);
        Assert.True(Assert.IsAssignableFrom<IList<OverlayWidget>>(resized.Widgets).IsReadOnly);
    }

    [Fact]
    public void NormalizingCanvasChangesDoesNotImplicitlyResizeModules()
    {
        var original = OverlayCatalog.Preset("compact");

        var normalized = OverlayLayout.Normalize(original with { Width = 720, Height = 520 });

        Assert.Equal(original.Widgets, normalized.Widgets);
    }

    [Fact]
    public void ResizePreservesContentPreferencesAndOverlayBehavior()
    {
        var original = new OverlaySettings
        {
            Width = 400, Height = 300,
            Enabled = true, Interaction = "passthrough", Visibility = "always",
            PositionX = .63, PositionY = .79, Scale = 1.4,
            BackgroundOpacity = .42, ShowBorder = false, SnapToGrid = false,
            CaptureExcluded = false, HotkeysEnabled = false,
            ToggleOverlayHotkey = new() { Modifiers = OverlayHotkeyModifiers.Shift, Key = "F8" },
            ToggleInteractionHotkey = new() { Modifiers = OverlayHotkeyModifiers.Control, Key = "F9" },
            Widgets =
            [
                OverlayCatalog.CreateWidget("drop-grid", 8, 9) with
                {
                    Width = 240, Height = 120, ShowLabel = false, ShowIcon = false,
                    FontScale = 1.75, ItemSize = 96, ItemLimit = 17, ItemSort = "quantity",
                    ItemView = "strip", ItemFilter = "selected", ItemNames = Array.AsReadOnly(new[] { "Test item" }),
                },
            ],
        };

        var resized = OverlayLayout.ResizeCanvas(original, 200, 150);

        Assert.Equal(original, resized with { Width = original.Width, Height = original.Height, Widgets = original.Widgets });
        var before = Assert.Single(original.Widgets);
        var after = Assert.Single(resized.Widgets);
        Assert.Equal(before, after with { X = before.X, Y = before.Y, Width = before.Width, Height = before.Height,
            ContentWidth = before.ContentWidth, ContentHeight = before.ContentHeight });
        Assert.Equal(before.Width, after.ContentWidth);
        Assert.Equal(before.Height, after.ContentHeight);
    }

    [Fact]
    public void ShrinkingAndGrowingDoesNotExpandModulesToIndividualEditorMinimums()
    {
        var original = new OverlaySettings
        {
            Width = 1600, Height = 1200,
            Widgets = [OverlayCatalog.CreateWidget("duration", 1504, 1144) with { Width = 80, Height = 40 }],
        };

        var small = OverlayLayout.ResizeCanvas(original, 160, 64);
        var widget = Assert.Single(small.Widgets);
        Assert.Equal(8, widget.Width);
        Assert.Equal(40d * 64 / 1200, widget.Height, 10);
        AssertProportions(original, small);

        var restored = OverlayLayout.ResizeCanvas(small, original.Width, original.Height);
        AssertProportions(original, restored);
    }

    [Fact]
    public void SavingAndLoadingKeepsShrunkenModulesAndTheirMargins()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Grindcrest.OverlayResize.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var resized = OverlayLayout.ResizeCanvas(OverlayCatalog.Preset("loot"), 160, 64);
            var store = new OverlaySettingsStore(folder);
            store.Save(resized);

            var restored = store.Load();

            AssertProportions(resized, restored);
            Assert.Contains(restored.Widgets, widget => widget.Width < 80 && widget.Height < 40);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, -10, 160, 64)]
    [InlineData(2000, 1400, 1600, 1200)]
    [InlineData(double.NaN, double.PositiveInfinity, 400, 300)]
    public void TargetDimensionsAreValidatedBeforeCalculatingProportions(double width, double height,
        double expectedWidth, double expectedHeight)
    {
        var original = new OverlaySettings
        {
            Width = 400, Height = 300,
            Widgets = [OverlayCatalog.CreateWidget("duration", 13, 17)],
        };

        var resized = OverlayLayout.ResizeCanvas(original, width, height);

        Assert.Equal(expectedWidth, resized.Width);
        Assert.Equal(expectedHeight, resized.Height);
        AssertProportions(original, resized);
    }

    [Fact]
    public void VerySmallModulesKeepPositiveBoundsWhenResizedFurther()
    {
        var original = new OverlaySettings
        {
            Width = 1600, Height = 1200,
            Widgets = [OverlayCatalog.CreateWidget("duration", 1599, 1199) with { Width = 1, Height = 1 }],
        };

        var resized = OverlayLayout.ResizeCanvas(original, 160, 64);
        var widget = Assert.Single(resized.Widgets);

        Assert.Equal(1, widget.Width);
        Assert.Equal(1, widget.Height);
        Assert.Equal(159, widget.X);
        Assert.Equal(63, widget.Y);
    }

    [Fact]
    public void InvalidSourceGeometryIsNormalizedBeforeResizing()
    {
        var original = new OverlaySettings
        {
            Width = double.NaN, Height = -4,
            Widgets = [OverlayCatalog.CreateWidget("duration", double.NaN, 100) with { Width = -3, Height = double.NaN }],
        };

        var resized = OverlayLayout.ResizeCanvas(original, 720, 128);
        var widget = Assert.Single(resized.Widgets);

        Assert.Equal(720, resized.Width);
        Assert.Equal(128, resized.Height);
        Assert.Equal(0, widget.X);
        Assert.Equal(0, widget.Y);
        Assert.Equal(2, widget.Width);
        Assert.Equal(128, widget.Height);
    }

    private static void AssertProportions(OverlaySettings original, OverlaySettings resized)
    {
        Assert.Equal(original.Widgets.Count, resized.Widgets.Count);
        for (var i = 0; i < original.Widgets.Count; i++)
        {
            var before = original.Widgets[i];
            var after = resized.Widgets[i];
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.X / original.Width, after.X / resized.Width, 10);
            Assert.Equal(before.Y / original.Height, after.Y / resized.Height, 10);
            Assert.Equal(before.Width / original.Width, after.Width / resized.Width, 10);
            Assert.Equal(before.Height / original.Height, after.Height / resized.Height, 10);
            Assert.Equal((original.Width - before.X - before.Width) / original.Width,
                (resized.Width - after.X - after.Width) / resized.Width, 10);
            Assert.Equal((original.Height - before.Y - before.Height) / original.Height,
                (resized.Height - after.Y - after.Height) / resized.Height, 10);
            Assert.InRange(after.X, 0, resized.Width - after.Width);
            Assert.InRange(after.Y, 0, resized.Height - after.Height);
        }
    }
}
