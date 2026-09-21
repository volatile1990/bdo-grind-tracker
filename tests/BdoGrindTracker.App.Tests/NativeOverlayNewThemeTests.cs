using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeOverlayNewThemeTests
{
    [Fact]
    public void EachNewThemeRendersDistinctCanvasAndWidgetColors()
    {
        var settings = new OverlaySettings
        {
            Width = 260, Height = 180, BackgroundOpacity = 1, ShowBorder = true,
            Widgets = [OverlayCatalog.CreateWidget("grind-rating", 12, 12),
                OverlayCatalog.CreateWidget("controls", 12, 96)]
        };
        var themes = new[] { AppThemes.Grindcrest, AppThemes.Obsidian, AppThemes.Kamasylvia, AppThemes.Valencia };
        var rendered = new List<byte[]>();
        var canvasColors = new HashSet<int>();
        using var renderer = new NativeOverlayRenderer();
        foreach (var theme in themes)
        {
            using var frame = renderer.Render(new(260, 180), settings,
                OverlaySnapshot.Demo with { ThemeId = theme }, out _);
            var pixels = Pixels(frame);
            Assert.DoesNotContain(rendered, previous => previous.SequenceEqual(pixels));
            rendered.Add(pixels);
            canvasColors.Add(frame.GetPixel(250, 170).ToArgb());
        }
        Assert.Equal(themes.Length, canvasColors.Count);
    }

    [Theory]
    [InlineData(AppThemes.Obsidian)]
    [InlineData(AppThemes.Kamasylvia)]
    [InlineData(AppThemes.Valencia)]
    public void SwitchingToNewThemeAndBackRestoresDefaultPixels(string selected)
    {
        var settings = OverlayCatalog.Preset("compact");
        var size = new Size((int)settings.Width, (int)settings.Height);
        var snapshot = OverlaySnapshot.Demo with { ThemeId = AppThemes.Grindcrest };
        using var renderer = new NativeOverlayRenderer();
        using var before = renderer.Render(size, settings, snapshot, out _);
        using var changed = renderer.Render(size, settings, snapshot with { ThemeId = selected }, out _);
        using var restored = renderer.Render(size, settings, snapshot, out _);
        using var unknown = renderer.Render(size, settings, snapshot with { ThemeId = "unknown-theme" }, out _);

        var expected = Pixels(before);
        Assert.NotEqual(expected, Pixels(changed));
        Assert.Equal(expected, Pixels(restored));
        Assert.Equal(expected, Pixels(unknown));
    }

    [Theory]
    [InlineData(AppThemes.Obsidian, 0x10, 0x12, 0x16)]
    [InlineData(AppThemes.Kamasylvia, 0x14, 0x23, 0x1e)]
    [InlineData(AppThemes.Valencia, 0xf3, 0xea, 0xdb)]
    public void NativeThemeSurfaceHonorsOpacityAndLeavesInvisibleBordersAtZero(string theme, int red, int green, int blue)
    {
        var settings = new OverlaySettings
        {
            Width = 160, Height = 64, Widgets = [], BackgroundOpacity = 1,
            ShowBorder = true, Interaction = "passthrough"
        };
        var snapshot = new OverlaySnapshot { ThemeId = theme };
        using var renderer = new NativeOverlayRenderer();
        using var opaque = renderer.Render(new(160, 64), settings, snapshot, out _);
        using var half = renderer.Render(new(160, 64), settings with { BackgroundOpacity = .5 }, snapshot, out _);
        using var transparent = renderer.Render(new(160, 64), settings with { BackgroundOpacity = 0 }, snapshot, out _);
        using var movable = renderer.Render(new(160, 64), settings with { BackgroundOpacity = 0, Interaction = "move" }, snapshot, out _);

        Assert.Equal(Color.FromArgb(red, green, blue).ToArgb(), opaque.GetPixel(80, 32).ToArgb());
        Assert.InRange(half.GetPixel(80, 32).A, 127, 128);
        Assert.All(Pixels(transparent), value => Assert.Equal((byte)0, value));
        Assert.Equal(1, movable.GetPixel(80, 32).A);
    }

    [Theory]
    [InlineData(AppThemes.Obsidian)]
    [InlineData(AppThemes.Kamasylvia)]
    [InlineData(AppThemes.Valencia)]
    public void NativeWarningTextStaysVisibleOnTransparentModules(string theme)
    {
        var widget = OverlayCatalog.CreateWidget("loot-scroll", 0, 0) with
        {
            Width = 160, Height = 64, ShowLabel = false, ShowIcon = false
        };
        var settings = new OverlaySettings
        {
            Width = 160, Height = 64, Widgets = [widget], BackgroundOpacity = 0,
            ShowBorder = false, Interaction = "passthrough"
        };
        var snapshot = new OverlaySnapshot
        {
            ThemeId = theme,
            Metrics = new Dictionary<string, OverlayMetric>
            {
                [widget.Kind] = new("Loot-Scroll", "Inaktiv", IsWarning: true)
            }
        };
        using var renderer = new NativeOverlayRenderer();
        using var frame = renderer.Render(new(160, 64), settings, snapshot, out _);

        Assert.Equal(0, frame.GetPixel(2, 2).A);
        var pixels = Pixels(frame);
        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), index => pixels[index * 4 + 3] > 200);
    }

    [Theory]
    [InlineData(AppThemes.Obsidian, .75)]
    [InlineData(AppThemes.Obsidian, 1.5)]
    [InlineData(AppThemes.Obsidian, 2)]
    [InlineData(AppThemes.Kamasylvia, .75)]
    [InlineData(AppThemes.Kamasylvia, 1.5)]
    [InlineData(AppThemes.Kamasylvia, 2)]
    [InlineData(AppThemes.Valencia, .75)]
    [InlineData(AppThemes.Valencia, 1.5)]
    [InlineData(AppThemes.Valencia, 2)]
    public void NewThemesKeepCanvasDimensionsAndControlHitboxesAtEveryScale(string theme, double dpi)
    {
        var controls = OverlayCatalog.CreateWidget("controls", 24, 16) with
        {
            Width = 220, Height = 80, ShowNewSession = true
        };
        var settings = new OverlaySettings
        {
            Width = 300, Height = 140, Widgets = [controls], ShowBorder = true, Interaction = "locked"
        };
        var snapshot = OverlaySnapshot.Demo with { CanToggleTracking = true, CanNewSession = true };
        var size = new Size((int)(settings.Width * dpi), (int)(settings.Height * dpi));
        var chrome = OverlayWindowChrome.For(theme, settings.ShowBorder);
        Assert.False(chrome.HasTitleBar);
        Assert.Equal(settings.Width, chrome.OuterWidth(settings.Width));
        Assert.Equal(settings.Height, chrome.OuterHeight(settings.Height));

        using var renderer = new NativeOverlayRenderer();
        using var original = renderer.Render(size, settings, snapshot with { ThemeId = AppThemes.Grindcrest }, out var before);
        using var changed = renderer.Render(size, settings, snapshot with { ThemeId = theme }, out var after);

        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.Contains("toggle-tracking:" + controls.Id, after.Keys);
        Assert.Contains("new-session:" + controls.Id, after.Keys);
        foreach (var (action, bounds) in before)
            Assert.Equal(bounds, after[action]);

        using var passthrough = renderer.Render(size, settings with { Interaction = "passthrough" },
            snapshot with { ThemeId = theme }, out var passthroughActions);
        Assert.DoesNotContain(passthroughActions.Keys, key => key.StartsWith("toggle-tracking:", StringComparison.Ordinal));
        Assert.DoesNotContain(passthroughActions.Keys, key => key.StartsWith("new-session:", StringComparison.Ordinal));
    }

    private static byte[] Pixels(Bitmap image)
    {
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            var pixels = new byte[data.Stride * image.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            image.UnlockBits(data);
        }
    }
}
