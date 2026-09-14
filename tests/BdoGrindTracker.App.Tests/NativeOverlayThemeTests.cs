using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeOverlayThemeTests
{
    [Fact]
    public void DefaultUnknownAndRestoredGrindcrestFramesHaveIdenticalPixels()
    {
        var settings = OverlayCatalog.Preset("dashboard");
        var size = new Size((int)settings.Width, (int)settings.Height);
        var snapshot = OverlaySnapshot.Demo;
        using var renderer = new NativeOverlayRenderer();
        using var original = renderer.Render(size, settings, snapshot, out _);
        using var explicitTheme = renderer.Render(size, settings, snapshot with { ThemeId = AppThemes.Grindcrest }, out _);
        using var unknownTheme = renderer.Render(size, settings, snapshot with { ThemeId = "unavailable-theme" }, out _);
        using var blackDesert = renderer.Render(size, settings, snapshot with { ThemeId = AppThemes.BlackDesert }, out _);
        using var restored = renderer.Render(size, settings, snapshot with { ThemeId = AppThemes.Grindcrest }, out _);

        var originalPixels = Pixels(original);
        Assert.Equal(originalPixels, Pixels(explicitTheme));
        Assert.Equal(originalPixels, Pixels(unknownTheme));
        Assert.NotEqual(originalPixels, Pixels(blackDesert));
        Assert.Equal(originalPixels, Pixels(restored));
    }

    [Theory]
    [InlineData(.75)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ThemeChromeOffsetsHitboxesWithoutChangingTheirSizeAtFractionalDpi(double dpi)
    {
        var controls = OverlayCatalog.CreateWidget("controls", 25, 37);
        var settings = new OverlaySettings
        {
            Width = 500, Height = 300, Interaction = "locked",
            Widgets = [OverlayLayout.ResizeWidget(controls, 300, 120), OverlayCatalog.CreateWidget("drop-grid", 12, 120)]
        };
        var snapshot = OverlaySnapshot.Demo with { CanToggleTracking = true };
        var size = new Size((int)(settings.Width * dpi), (int)(settings.Height * dpi));
        using var renderer = new NativeOverlayRenderer();
        using var grindcrest = renderer.Render(size, settings, snapshot, out var before);
        var chrome = OverlayWindowChrome.For(AppThemes.BlackDesert, settings.ShowBorder);
        var framedSize = new Size((int)(chrome.OuterWidth(settings.Width) * dpi), (int)(chrome.OuterHeight(settings.Height) * dpi));
        using var blackDesert = renderer.Render(framedSize, settings, snapshot with { ThemeId = AppThemes.BlackDesert }, out var after);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (key, original) in before)
        {
            var framed = after[key];
            Assert.InRange(framed.Width, original.Width - 1, original.Width + 1);
            Assert.InRange(framed.Height, original.Height - 1, original.Height + 1);
            Assert.InRange(framed.X, original.X + chrome.Left * dpi - 1, original.X + chrome.Left * dpi + 1);
            Assert.InRange(framed.Y, original.Y + chrome.Top * dpi - 1, original.Y + chrome.Top * dpi + 1);
        }
        Assert.Contains("toggle-tracking:" + controls.Id, after.Keys);

        using var passthrough = renderer.Render(size, settings with { Interaction = "passthrough" },
            snapshot with { ThemeId = AppThemes.BlackDesert }, out var passthroughActions);
        Assert.DoesNotContain(passthroughActions.Keys, key => key.StartsWith("toggle-tracking:", StringComparison.Ordinal));
    }

    [Fact]
    public void BlackDesertCanvasHasSquareNeutralPanelsAndHonorsBackgroundTransparency()
    {
        var settings = new OverlaySettings
        {
            Width = 160, Height = 64, Widgets = [], ShowBorder = false,
            BackgroundOpacity = 1, Interaction = "passthrough"
        };
        var snapshot = new OverlaySnapshot { ThemeId = AppThemes.BlackDesert };
        using var renderer = new NativeOverlayRenderer();
        using var blackDesert = renderer.Render(new(160, 64), settings, snapshot, out _);
        using var grindcrest = renderer.Render(new(160, 64), settings, snapshot with { ThemeId = AppThemes.Grindcrest }, out _);
        Assert.Equal(Color.FromArgb(37, 37, 38).ToArgb(), blackDesert.GetPixel(80, 32).ToArgb());
        Assert.Equal(255, blackDesert.GetPixel(2, 2).A);
        Assert.Equal(0, grindcrest.GetPixel(2, 2).A);

        using var transparent = renderer.Render(new(160, 64), settings with { BackgroundOpacity = 0 }, snapshot, out _);
        Assert.Equal(0, transparent.GetPixel(80, 32).A);
        using var movable = renderer.Render(new(160, 64), settings with { BackgroundOpacity = 0, Interaction = "move" }, snapshot, out _);
        Assert.Equal(1, movable.GetPixel(80, 32).A);
    }

    [Theory]
    [InlineData(AppThemes.BlackDesert)]
    [InlineData(AppThemes.Cats)]
    public void WindowTitleOccupiesAdditionalSpaceWithoutCoveringTheFirstWidget(string themeId)
    {
        var widget = OverlayCatalog.CreateWidget("controls", 0, 0) with
        {
            Width = 160, Height = 64, ShowLabel = false, ShowIcon = false
        };
        var settings = new OverlaySettings
        {
            Width = 160, Height = 64, Widgets = [widget], BackgroundOpacity = 0,
            ShowBorder = true, Interaction = "locked"
        };
        var snapshot = new OverlaySnapshot { ThemeId = themeId, CanToggleTracking = true };
        using var renderer = new NativeOverlayRenderer();
        using var framed = renderer.Render(new(164, 98), settings, snapshot, out var actions, "Mein Loot");
        using var renamed = renderer.Render(new(164, 98), settings, snapshot, out _, "Andere Drops");
        using var borderless = renderer.Render(new(160, 64), settings with { ShowBorder = false }, snapshot,
            out var borderlessActions, "Mein Loot");

        Assert.Equal(new RectangleF(2, 32, 160, 64), actions["widget:" + widget.Id]);
        var button = actions["toggle-tracking:" + widget.Id];
        var unframedButton = borderlessActions["toggle-tracking:" + widget.Id];
        unframedButton.Offset(2, 32);
        Assert.Equal(unframedButton, button);
        Assert.NotEqual(Region(framed, new(0, 0, 164, 32)), Region(renamed, new(0, 0, 164, 32)));
        Assert.Equal(Region(framed, new(4, 34, 156, 60)), Region(renamed, new(4, 34, 156, 60)));
        Assert.Equal(Region(borderless, new(2, 2, 156, 60)), Region(framed, new(4, 34, 156, 60)));
    }

    [Theory]
    [InlineData(AppThemes.BlackDesert, 1)]
    [InlineData(AppThemes.BlackDesert, 1.5)]
    [InlineData(AppThemes.BlackDesert, 2)]
    [InlineData(AppThemes.Cats, 1)]
    [InlineData(AppThemes.Cats, 1.5)]
    [InlineData(AppThemes.Cats, 2)]
    public void CroppedButtonsCannotBeClickedOnTheBottomOrRightWindowBorder(string themeId, double dpi)
    {
        var widget = OverlayCatalog.CreateWidget("controls", 120, 40) with
        {
            Width = 160, Height = 64, ShowLabel = false, ShowIcon = false
        };
        var settings = new OverlaySettings
        {
            Width = 160, Height = 64, Widgets = [widget], ShowBorder = true, Interaction = "locked"
        };
        var snapshot = new OverlaySnapshot { ThemeId = themeId, CanToggleTracking = true };
        var size = new Size((int)(164 * dpi), (int)(98 * dpi));
        var content = new RectangleF((float)(2 * dpi), (float)(32 * dpi), (float)(160 * dpi), (float)(64 * dpi));
        using var renderer = new NativeOverlayRenderer();
        using var clipped = renderer.Render(size, settings, snapshot, out var actions);
        var button = actions["toggle-tracking:" + widget.Id];
        Assert.True(button.Width > 0 && button.Height > 0);
        Assert.True(content.Contains(button));
        Assert.True(content.Contains(actions["widget:" + widget.Id]));
        Assert.False(button.Contains(content.Right + .5f, button.Top));
        Assert.False(button.Contains(button.Left, content.Bottom + .5f));

        using var hidden = renderer.Render(size, settings with { Widgets = [widget with { Y = 64 }] }, snapshot,
            out var hiddenActions);
        Assert.DoesNotContain("toggle-tracking:" + widget.Id, hiddenActions.Keys);
    }

    [Theory]
    [InlineData(AppThemes.BlackDesert, 1)]
    [InlineData(AppThemes.BlackDesert, 1.5)]
    [InlineData(AppThemes.BlackDesert, 2)]
    [InlineData(AppThemes.Cats, 1)]
    [InlineData(AppThemes.Cats, 1.5)]
    [InlineData(AppThemes.Cats, 2)]
    public void CroppedNewSessionButtonCannotBeClickedOnWindowChrome(string themeId, double dpi)
    {
        var widget = OverlayCatalog.CreateWidget("controls", 120, 12) with
        {
            Width = 160, Height = 64, ShowLabel = false, ShowIcon = false, ShowNewSession = true
        };
        var settings = new OverlaySettings
        {
            Width = 160, Height = 64, Widgets = [widget], ShowBorder = true, Interaction = "locked"
        };
        var snapshot = new OverlaySnapshot { ThemeId = themeId, CanToggleTracking = true, CanNewSession = true };
        var size = new Size((int)(164 * dpi), (int)(98 * dpi));
        var content = new RectangleF((float)(2 * dpi), (float)(32 * dpi), (float)(160 * dpi), (float)(64 * dpi));
        using var renderer = new NativeOverlayRenderer();
        using var clipped = renderer.Render(size, settings, snapshot, out var actions);
        var next = actions["new-session:" + widget.Id];
        var toggle = actions["toggle-tracking:" + widget.Id];
        Assert.Equal(3, actions.Count); // One widget occluder and two distinct controls.
        Assert.True(next.Width > 0 && next.Height > 0);
        Assert.True(content.Contains(next));
        Assert.False(next.IntersectsWith(toggle));
        Assert.False(next.Contains(content.Right + .5f, next.Top));
        Assert.False(next.Contains(next.Left, content.Bottom + .5f));

        using var hidden = renderer.Render(size, settings with { Widgets = [widget with { Y = 64 }] }, snapshot,
            out var hiddenActions);
        Assert.DoesNotContain("new-session:" + widget.Id, hiddenActions.Keys);
        using var disabled = renderer.Render(size, settings, snapshot with { CanNewSession = false }, out var disabledActions);
        Assert.DoesNotContain("new-session:" + widget.Id, disabledActions.Keys);
        using var passthrough = renderer.Render(size, settings with { Interaction = "passthrough" }, snapshot,
            out var passthroughActions);
        Assert.DoesNotContain("new-session:" + widget.Id, passthroughActions.Keys);
    }

    [Fact]
    public void BlackDesertWarningsKeepTheirBackgroundTransparentAtZeroOpacity()
    {
        var widget = OverlayCatalog.CreateWidget("loot-scroll", 0, 0) with { ShowLabel = false, ShowIcon = false };
        var settings = new OverlaySettings
        {
            Width = widget.Width, Height = widget.Height, Widgets = [widget],
            BackgroundOpacity = 0, ShowBorder = false, Interaction = "passthrough"
        };
        var snapshot = new OverlaySnapshot
        {
            ThemeId = AppThemes.BlackDesert,
            Metrics = new Dictionary<string, OverlayMetric> { [widget.Kind] = new("Loot-Scroll", "Inaktiv", IsWarning: true) }
        };
        using var renderer = new NativeOverlayRenderer();
        using var image = renderer.Render(new((int)widget.Width, (int)widget.Height), settings, snapshot, out _);
        Assert.Equal(0, image.GetPixel(2, 2).A);
        Assert.Contains(Color.FromArgb(228, 206, 145).ToArgb(), Pixels(image));
    }

    [Theory]
    [InlineData(AppThemes.Light)]
    [InlineData(AppThemes.Cats)]
    public void NewThemesResetTheirPaletteBeforeRestoringEitherOriginalTheme(string selected)
    {
        var settings = OverlayCatalog.Preset("dashboard");
        var snapshot = OverlaySnapshot.Demo;
        using var renderer = new NativeOverlayRenderer();
        foreach (var original in new[] { AppThemes.Grindcrest, AppThemes.BlackDesert })
        {
            var chrome = OverlayWindowChrome.For(original, settings.ShowBorder);
            var size = new Size((int)chrome.OuterWidth(settings.Width), (int)chrome.OuterHeight(settings.Height));
            using var before = renderer.Render(size, settings, snapshot with { ThemeId = original }, out _);
            var selectedChrome = OverlayWindowChrome.For(selected, settings.ShowBorder);
            var selectedSize = new Size((int)selectedChrome.OuterWidth(settings.Width), (int)selectedChrome.OuterHeight(settings.Height));
            using var changed = renderer.Render(selectedSize, settings, snapshot with { ThemeId = selected }, out _);
            using var restored = renderer.Render(size, settings, snapshot with { ThemeId = original }, out _);
            Assert.NotEqual(Pixels(before), Pixels(changed));
            Assert.Equal(Pixels(before), Pixels(restored));
        }
    }

    [Theory]
    [InlineData(AppThemes.Light, 245, 246, 248)]
    [InlineData(AppThemes.Cats, 37, 34, 31)]
    public void NewThemesRespectCanvasAndModuleBackgroundTransparency(string themeId, int red, int green, int blue)
    {
        var settings = new OverlaySettings
        {
            Width = 160, Height = 64, Widgets = [], ShowBorder = false,
            BackgroundOpacity = 1, Interaction = "passthrough"
        };
        var snapshot = new OverlaySnapshot { ThemeId = themeId };
        using var renderer = new NativeOverlayRenderer();
        using var solid = renderer.Render(new(160, 64), settings, snapshot, out _);
        Assert.Equal(Color.FromArgb(red, green, blue).ToArgb(), solid.GetPixel(8, 8).ToArgb());
        using var transparent = renderer.Render(new(160, 64), settings with { BackgroundOpacity = 0 }, snapshot, out _);
        Assert.All(Pixels(transparent), pixel => Assert.Equal(0, pixel));
        using var move = renderer.Render(new(160, 64), settings with { BackgroundOpacity = 0, Interaction = "move" }, snapshot, out _);
        Assert.Equal(1, move.GetPixel(8, 8).A);

        var widget = OverlayCatalog.CreateWidget("loot-scroll", 0, 0) with
        {
            Width = 160, Height = 64, ShowLabel = false, ShowIcon = false
        };
        snapshot = snapshot with { Metrics = new Dictionary<string, OverlayMetric>
            { [widget.Kind] = new("Loot-Scroll", "Inaktiv", IsWarning: true) } };
        using var warning = renderer.Render(new(160, 64), settings with { BackgroundOpacity = 0, Widgets = [widget] }, snapshot, out _);
        Assert.Equal(0, warning.GetPixel(2, 2).A);
        Assert.Contains(Pixels(warning), pixel => Color.FromArgb(pixel).A > 200);
    }

    [Fact]
    public void CatIllustrationIsBundledAndVisibleWithoutLeavingDecorationAtZeroOpacity()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "themes", "cats", "kitten-lounge.png")));
        var settings = new OverlaySettings
        {
            Width = 480, Height = 320, Widgets = [], ShowBorder = true,
            BackgroundOpacity = 1, Interaction = "passthrough"
        };
        var snapshot = new OverlaySnapshot { ThemeId = AppThemes.Cats };
        using var renderer = new NativeOverlayRenderer();
        using var decorated = renderer.Render(new(484, 354), settings, snapshot, out var actions, "");
        // The lower right must contain the illustration's detailed shading,
        // rather than silently falling back to a flat surface in native builds.
        Assert.True(Region(decorated, new(340, 182, 120, 130)).Distinct().Count() > 100);
        Assert.Empty(actions);
        using var transparent = renderer.Render(new(484, 354), settings with { BackgroundOpacity = 0 }, snapshot, out _, "");
        Assert.All(Pixels(transparent), pixel => Assert.Equal(0, pixel));
    }

    [Theory]
    [InlineData(OverlayMetricTone.Default, 36, 50, 68)]
    [InlineData(OverlayMetricTone.Muted, 82, 100, 120)]
    [InlineData(OverlayMetricTone.Accent, 54, 95, 145)]
    [InlineData(OverlayMetricTone.Positive, 35, 117, 87)]
    public void LightMetricTonesUseDarkReadableInk(OverlayMetricTone tone, int red, int green, int blue)
    {
        var widget = OverlayCatalog.CreateWidget("grind-rating", 0, 0) with { ShowLabel = false, ShowIcon = false };
        var settings = new OverlaySettings
        {
            Width = widget.Width, Height = widget.Height, Widgets = [widget], ShowBorder = false,
            BackgroundOpacity = 1, Interaction = "passthrough"
        };
        var snapshot = new OverlaySnapshot
        {
            ThemeId = AppThemes.Light,
            Metrics = new Dictionary<string, OverlayMetric> { [widget.Kind] = new("Bewertung", "High Tier", Tone: tone) }
        };
        using var renderer = new NativeOverlayRenderer();
        using var image = renderer.Render(new((int)widget.Width, (int)widget.Height), settings, snapshot, out _);
        Assert.Contains(Color.FromArgb(red, green, blue).ToArgb(), Pixels(image));
    }

    private static int[] Region(Bitmap image, Rectangle bounds) => Enumerable.Range(bounds.Y, bounds.Height)
        .SelectMany(y => Enumerable.Range(bounds.X, bounds.Width).Select(x => image.GetPixel(x, y).ToArgb())).ToArray();

    private static int[] Pixels(Bitmap image) => Enumerable.Range(0, image.Height)
        .SelectMany(y => Enumerable.Range(0, image.Width).Select(x => image.GetPixel(x, y).ToArgb())).ToArray();
}
