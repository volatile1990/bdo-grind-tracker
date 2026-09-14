using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class NativeOverlayChromeGeometryTests
{
    private static readonly OverlayWindowChrome Chrome = OverlayWindowChrome.For(AppThemes.BlackDesert, true);

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void ResizingAFramedWindowChangesOnlyTheContentCanvas(int dpi)
    {
        var monitor = new Rectangle(-1920, -200, 3840, 2160);
        var original = new OverlaySettings { Width = 400, Height = 296, SnapToGrid = true };
        var bounds = Place(original, monitor, dpi);
        var factor = dpi / 96d;
        var resize = new NativeOverlayResize(original, bounds, monitor, dpi, Chrome);

        var frame = resize.Update(new Size((int)(80 * factor), (int)(64 * factor)));

        Assert.Equal(480, frame.Settings.Width);
        Assert.Equal(360, frame.Settings.Height);
        Assert.Equal(original.Widgets, frame.Settings.Widgets);
        Assert.Equal(bounds.Location, frame.Bounds.Location);
        Assert.Equal(new Size((int)(484 * factor), (int)(394 * factor)), frame.Bounds.Size);
        Assert.Equal(frame.Bounds, Place(frame.Settings, monitor, dpi));
        Assert.Equal(bounds, resize.Update(Size.Empty).Bounds);
    }

    [Fact]
    public void MonitorFittingIncludesTheTitleBarAndResizePersistsTheVisibleOuterSize()
    {
        var monitor = new Rectangle(0, 0, 1000, 700);
        var original = new OverlaySettings { Width = 1600, Height = 1200, Scale = 2, SnapToGrid = false };
        var bounds = Place(original, monitor, 96);
        Assert.True(monitor.Contains(bounds));
        Assert.Equal(700, bounds.Height);

        var frame = new NativeOverlayResize(original, bounds, monitor, 96, Chrome).Update(new(-100, -50));

        Assert.True(monitor.Contains(frame.Bounds));
        Assert.Equal(frame.Bounds, Place(frame.Settings, monitor, 96));
        Assert.Equal(original.Widgets, frame.Settings.Widgets);
        Assert.Equal(bounds.Width - 100, frame.Bounds.Width);
        Assert.Equal(bounds.Height - 50, frame.Bounds.Height);
    }

    [Fact]
    public void FittedShortCanvasDoesNotJumpUntilTheTitleAndMinimumContentHeightFit()
    {
        var monitor = new Rectangle(0, 0, 800, 600);
        var original = new OverlaySettings { Width = 1600, Height = 64, Scale = 2, SnapToGrid = false };
        var bounds = Place(original, monitor, 96);
        var resize = new NativeOverlayResize(original, bounds, monitor, 96, Chrome);

        var frame = resize.Update(new(-50, 1));

        Assert.Equal(bounds, frame.Bounds);
        Assert.Equal(original.Width, frame.Settings.Width);
        Assert.Equal(original.Height, frame.Settings.Height);
    }

    [Theory]
    [InlineData(AppThemes.BlackDesert)]
    [InlineData(AppThemes.Cats)]
    public void RenamingRepaintsOnlyWhenTheSelectedThemeShowsAWindowTitle(string themeId)
    {
        var settings = new OverlaySettings { Widgets = [] };
        var snapshot = new OverlaySnapshot { ThemeId = themeId };
        var cache = new NativeOverlayRenderState();
        cache.Remember(settings, snapshot, new(364, 294), "Loot");
        Assert.False(cache.Matches(settings, snapshot, new(364, 294), "Loot heute"));
        Assert.True(cache.Matches(settings, snapshot, new(364, 294), "Loot"));

        cache.Remember(settings with { ShowBorder = false }, snapshot, new(360, 260), "Loot");
        Assert.True(cache.Matches(settings with { ShowBorder = false }, snapshot, new(360, 260), "Loot heute"));
        snapshot = snapshot with { ThemeId = AppThemes.Grindcrest };
        cache.Remember(settings, snapshot, new(360, 260), "Loot");
        Assert.True(cache.Matches(settings, snapshot, new(360, 260), "Loot heute"));
    }

    [Fact]
    public void CatWindowUsesTheExistingChromeResizeMathAndLightKeepsTheContentSize()
    {
        var cats = OverlayWindowChrome.For(AppThemes.Cats, true);
        Assert.Equal(Chrome, cats);
        Assert.Equal(default, OverlayWindowChrome.For(AppThemes.Cats, false));
        var light = OverlayWindowChrome.For(AppThemes.Light, true);
        Assert.False(light.HasTitleBar);
        Assert.Equal(400, light.OuterWidth(400));
        Assert.Equal(296, light.OuterHeight(296));
        var settings = new OverlaySettings { Width = 400, Height = 296, SnapToGrid = false };
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var bounds = Place(settings, monitor, 144);
        var frame = new NativeOverlayResize(settings, bounds, monitor, 144, cats).Update(new(120, 96));
        Assert.Equal(480, frame.Settings.Width);
        Assert.Equal(360, frame.Settings.Height);
        Assert.Equal(frame.Bounds, Place(frame.Settings, monitor, 144));
        Assert.Equal(settings.Widgets, frame.Settings.Widgets);
    }

    private static Rectangle Place(OverlaySettings settings, Rectangle monitor, int dpi) =>
        NativeOverlayGeometry.Place(monitor, Chrome.OuterWidth(settings.Width), Chrome.OuterHeight(settings.Height),
            settings.PositionX, settings.PositionY, settings.Scale, dpi);
}
