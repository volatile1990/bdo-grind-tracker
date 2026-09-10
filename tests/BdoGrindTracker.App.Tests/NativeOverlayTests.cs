using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using System.Drawing.Imaging;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeOverlayTests
{
    public static IEnumerable<object[]> CatalogWidgets() => OverlayCatalog.Widgets.Select(widget => new object[] { widget.Kind });

    [Theory]
    [MemberData(nameof(CatalogWidgets))]
    public void EveryCatalogModuleRendersAtItsDefaultSizeIncludingOptionalModules(string kind)
    {
        var widget = OverlayCatalog.CreateWidget(kind, 0, 0);
        var settings = new OverlaySettings
        {
            Width = widget.Width, Height = widget.Height, Widgets = [widget],
            ShowBorder = false, BackgroundOpacity = 0, Interaction = "passthrough",
        };
        using var renderer = new NativeOverlayRenderer();
        using var image = renderer.Render(new((int)widget.Width, (int)widget.Height), settings, OverlaySnapshot.Demo, out var actions);

        Assert.Equal(PixelFormat.Format32bppPArgb, image.PixelFormat);
        Assert.Contains(Enumerable.Range(0, image.Height), y =>
            Enumerable.Range(0, image.Width).Any(x => image.GetPixel(x, y).A > 0));
        Assert.Equal(new RectangleF(0, 0, (float)widget.Width, (float)widget.Height), actions["widget:" + widget.Id]);
    }

    [Theory]
    [InlineData(OverlayMetricTone.Default, 237, 241, 245)]
    [InlineData(OverlayMetricTone.Muted, 154, 175, 190)]
    [InlineData(OverlayMetricTone.Positive, 125, 211, 181)]
    [InlineData(OverlayMetricTone.Accent, 242, 199, 108)]
    public void GrindRatingNativeTextUsesTheProjectedToneWithoutAWarningBackground(OverlayMetricTone tone, int red, int green, int blue)
    {
        var widget = OverlayCatalog.CreateWidget("grind-rating", 0, 0) with { ShowLabel = false, ShowIcon = false };
        var settings = new OverlaySettings
        {
            Width = widget.Width, Height = widget.Height, Widgets = [widget],
            ShowBorder = false, BackgroundOpacity = 0, Interaction = "passthrough",
        };
        var snapshot = new OverlaySnapshot { Metrics = new Dictionary<string, OverlayMetric>
            { ["grind-rating"] = new("Grind-Bewertung", "High Tier", Tone: tone) } };
        using var renderer = new NativeOverlayRenderer();
        using var image = renderer.Render(new((int)widget.Width, (int)widget.Height), settings, snapshot, out _);
        var expected = Color.FromArgb(red, green, blue).ToArgb();

        Assert.Equal(0, image.GetPixel(2, 2).A);
        Assert.Contains(Enumerable.Range(0, image.Height), y =>
            Enumerable.Range(0, image.Width).Any(x => image.GetPixel(x, y).ToArgb() == expected));
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("dashboard")]
    [InlineData("loot")]
    [InlineData("loot-strip")]
    public void ProportionallyShrunkCanvasRendersWithoutInvalidDrawingOrHitRegions(string preset)
    {
        using var renderer = new NativeOverlayRenderer();
        var layout = OverlayLayout.ResizeCanvas(OverlayCatalog.Preset(preset), 160, 64);
        using var image = renderer.Render(new Size(160, 64), layout,
            OverlaySnapshot.Demo with { CanToggleTracking = true }, out var actions);
        Assert.Equal(new Size(160, 64), image.Size);
        Assert.All(actions.Values, bounds =>
        {
            Assert.True(bounds.Width > 0 && bounds.Height > 0);
            Assert.InRange(bounds.Left, 0, 160);
            Assert.InRange(bounds.Top, 0, 64);
            Assert.InRange(bounds.Right, 0, 160.001f);
            Assert.InRange(bounds.Bottom, 0, 64.001f);
        });
    }

    [Fact]
    public void RelativePositionSurvivesMonitorOriginResolutionAndDpiChanges()
    {
        var firstMonitor = new Rectangle(-2560, -200, 2560, 1440);
        var first = NativeOverlayGeometry.Place(firstMonitor, 360, 260, .8, .2, 1.25, 144);
        Assert.True(firstMonitor.Contains(first));
        Assert.Equal(675, first.Width);
        var relative = NativeOverlayGeometry.RelativePosition(first, firstMonitor);
        var secondMonitor = new Rectangle(1920, 0, 1920, 1080);
        var second = NativeOverlayGeometry.Place(secondMonitor, 360, 260, relative.X, relative.Y, 1.25, 96);
        Assert.True(secondMonitor.Contains(second));
        Assert.Equal(450, second.Width);
        var restored = NativeOverlayGeometry.RelativePosition(second, secondMonitor);
        Assert.InRange(Math.Abs(restored.X - .8), 0, .001);
        Assert.InRange(Math.Abs(restored.Y - .2), 0, .001);
    }

    [Fact]
    public void OversizedOverlayFitsSmallMonitorWithoutChangingAspectRatio()
    {
        var monitor = new Rectangle(-1280, 200, 1280, 720);
        var bounds = NativeOverlayGeometry.Place(monitor, 1600, 1200, 1, 1, 2, 192);
        Assert.Equal(new Size(960, 720), bounds.Size);
        Assert.Equal(new Point(-960, 200), bounds.Location);
        Assert.True(monitor.Contains(bounds));
    }

    [Fact]
    public void InvalidNumbersAndRemovedMonitorCannotStrandWindowOffScreen()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var bounds = NativeOverlayGeometry.Place(monitor, double.NaN, double.PositiveInfinity,
            double.NaN, double.NegativeInfinity, double.NaN, double.NaN);
        Assert.True(monitor.Contains(bounds));
        Assert.Equal(new Size(360, 260), bounds.Size);
        var clamped = NativeOverlayGeometry.Clamp(new Rectangle(-3000, 1500, 360, 260), monitor);
        Assert.Equal(new Rectangle(0, 820, 360, 260), clamped);
    }

    [Theory]
    [InlineData(false, false, "always", true, true, false)]
    [InlineData(false, true, "game", false, false, true)]
    [InlineData(true, false, "game", false, true, false)]
    [InlineData(true, false, "game", true, false, true)]
    [InlineData(true, false, "session", false, true, true)]
    [InlineData(true, false, "session", true, false, false)]
    [InlineData(true, false, "always", false, false, true)]
    public void VisibilityHonorsOptInAndForegroundPolicy(bool enabled, bool preview, string visibility,
        bool foreground, bool session, bool expected) =>
        Assert.Equal(expected, NativeOverlayGeometry.ShouldShow(enabled, preview, visibility, foreground, session));

    [Theory]
    [InlineData("BlackDesert64", true)]
    [InlineData("BlackDesert64.bin", true)]
    [InlineData("blackdesert", true)]
    [InlineData("BlackDesertLauncher", false)]
    [InlineData("BlackDesert64 - Microsoft Edge", false)]
    [InlineData("Grindcrest", false)]
    public void WindowAssociationAcceptsGameExecutablesOnly(string name, bool expected) =>
        Assert.Equal(expected, NativeOverlayGameWindow.IsGameProcessName(name));

    [Fact]
    public void RenderKeepsTextOpaqueWhenBackgroundIsTransparentAndDisablesClickThroughActions()
    {
        using var renderer = new NativeOverlayRenderer();
        var settings = new OverlaySettings { BackgroundOpacity = 0, Interaction = "passthrough", ShowBorder = false };
        using var image = renderer.Render(new Size(360, 260), settings, OverlaySnapshot.Demo, out var actions);
        Assert.Equal(PixelFormat.Format32bppPArgb, image.PixelFormat);
        Assert.DoesNotContain(actions.Keys, key => key.StartsWith("toggle-tracking:", StringComparison.Ordinal));
        Assert.Equal(0, image.GetPixel(179, 170).A);
        Assert.Contains(Enumerable.Range(8, 72), y => Enumerable.Range(8, 168).Any(x => image.GetPixel(x, y).A > 200));
    }

    [Fact]
    public void ControlHitRegionsScaleWithTheDisplayedOverlay()
    {
        using var renderer = new NativeOverlayRenderer();
        var snapshot = OverlaySnapshot.Demo with { CanToggleTracking = true };
        using var image = renderer.Render(new Size(720, 520), new(), snapshot, out var actions);
        var button = Assert.Single(actions, pair => pair.Key.StartsWith("toggle-tracking:", StringComparison.Ordinal)).Value;
        Assert.True(button.Contains(new PointF(176, 416)));
        Assert.False(button.Contains(new PointF(10, 10)));
        using var disabled = renderer.Render(new Size(720, 520), new(), snapshot with { CanToggleTracking = false }, out var disabledActions);
        Assert.DoesNotContain(disabledActions.Keys, key => key.StartsWith("toggle-tracking:", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryPlacedControlKeepsItsOwnActionRegion()
    {
        using var renderer = new NativeOverlayRenderer();
        var settings = new OverlaySettings { Widgets =
            [OverlayCatalog.CreateWidget("controls", 8, 8), OverlayCatalog.CreateWidget("controls", 184, 8)] };
        using var image = renderer.Render(new Size(360, 260), settings,
            OverlaySnapshot.Demo with { CanToggleTracking = true }, out var actions);
        var buttons = actions.Where(pair => pair.Key.StartsWith("toggle-tracking:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.NotEqual(buttons[0].Key, buttons[1].Key);
        Assert.False(buttons[0].Value.IntersectsWith(buttons[1].Value));
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("dashboard")]
    [InlineData("loot")]
    public void BuiltInLayoutsRenderAtFractionalDpiWithoutWindowOrGameAccess(string name)
    {
        using var renderer = new NativeOverlayRenderer();
        var settings = OverlayCatalog.Preset(name);
        using var image = renderer.Render(new Size((int)(settings.Width * 1.5), (int)(settings.Height * 1.5)),
            settings, OverlaySnapshot.Demo, out _);
        Assert.Equal((int)(settings.Width * 1.5), image.Width);
        Assert.Equal((int)(settings.Height * 1.5), image.Height);
    }

    [Theory]
    [InlineData("drop-grid", "grid", true, .75)]
    [InlineData("drop-strip", "strip", true, 1.25)]
    [InlineData("drop-list", "list", true, 1.5)]
    [InlineData("drop-item", "card", true, 2)]
    [InlineData("drop-grid", "grid", false, 1.25)]
    [InlineData("drop-strip", "strip", false, 1.5)]
    [InlineData("drop-list", "list", false, 2)]
    [InlineData("drop-item", "card", false, .75)]
    public void LootPresentationsRenderWithIndependentIconChoiceAndFractionalDpi(
        string kind, string view, bool icons, double scale)
    {
        using var renderer = new NativeOverlayRenderer();
        var widget = OverlayCatalog.CreateWidget(kind, 0, 0) with
        {
            Width = 240, Height = 160, ItemView = view, ItemSize = 48,
            ShowIcon = icons, ShowLabel = false, ItemFilter = "all", ItemLimit = 6,
            ItemNames = kind == "drop-item" ? ["item-1"] : []
        };
        var settings = new OverlaySettings
        {
            Width = 240, Height = 160, Widgets = [widget], ShowBorder = false,
            Interaction = "passthrough", BackgroundOpacity = 0
        };
        using var image = renderer.Render(new Size((int)(240 * scale), (int)(160 * scale)),
            settings, LootSnapshot(6), out var actions);
        Assert.Equal(PixelFormat.Format32bppPArgb, image.PixelFormat);
        Assert.DoesNotContain(actions.Keys, key => key.StartsWith("toggle-tracking:", StringComparison.Ordinal));
        Assert.Contains(Enumerable.Range(0, image.Height), y =>
            Enumerable.Range(0, image.Width).Any(x => image.GetPixel(x, y).A > 200));
    }

    [Fact]
    public void ItemLimitShowsOverflowFooterInsteadOfSilentlyOmittingDrops()
    {
        using var renderer = new NativeOverlayRenderer();
        var widget = OverlayCatalog.CreateWidget("drop-grid", 0, 0) with
        {
            Width = 240, Height = 160, ItemView = "grid", ItemSize = 48,
            ShowLabel = false, ItemLimit = 2, ItemFilter = "all"
        };
        var settings = new OverlaySettings
        {
            Width = 240, Height = 160, Widgets = [widget], ShowBorder = false,
            Interaction = "passthrough", BackgroundOpacity = 0
        };
        var snapshot = LootSnapshot(6);
        var presentation = OverlayLootPresentation.Create(widget, snapshot);
        Assert.Equal(4, presentation.HiddenCount);
        using var image = renderer.Render(new Size(240, 160), settings, snapshot, out _);
        using var noOverflow = renderer.Render(new Size(240, 160), settings, LootSnapshot(2), out _);
        var footerPixels = Enumerable.Range(138, 14)
            .Sum(y => Enumerable.Range(10, 220).Count(x => image.GetPixel(x, y).A > 128));
        var emptyFooterPixels = Enumerable.Range(138, 14)
            .Sum(y => Enumerable.Range(10, 220).Count(x => noOverflow.GetPixel(x, y).A > 128));
        Assert.True(footerPixels > 10, "The native overlay must visibly disclose hidden items.");
        Assert.Equal(0, emptyFooterPixels);
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("strip")]
    [InlineData("list")]
    [InlineData("card")]
    public void ExtremelySmallLootWidgetsShowOverflowWithoutDrawingInvalidGeometry(string view)
    {
        using var renderer = new NativeOverlayRenderer();
        var widget = OverlayCatalog.CreateWidget("drop-grid", 0, 0) with
        {
            Width = 80, Height = 40, ItemView = view, ItemSize = 112,
            ShowLabel = true, FontScale = 2, ItemLimit = 24
        };
        var settings = new OverlaySettings { Width = 160, Height = 64, Widgets = [widget] };
        using var image = renderer.Render(new Size(240, 96), settings, LootSnapshot(24), out _);
        Assert.Equal(240, image.Width);
    }

    private static OverlaySnapshot LootSnapshot(int count) => new()
    {
        Drops = Enumerable.Range(1, count).Select(index =>
            new OverlayLootItem("item-" + index, "Beispielgegenstand " + index, (index * 127).ToString(),
                IsRare: index % 3 == 0, Quantity: index * 127)).ToArray()
    };
}
