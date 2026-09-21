using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeOverlayTextRenderingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void BlackDesertTitleHasFineCoverageAndSolidStemsAtMonitorScale(double scale)
    {
        var settings = new OverlaySettings
        {
            Width = 316, Height = 94, Widgets = [], ShowBorder = true,
            BackgroundOpacity = 0, Interaction = "passthrough"
        };
        var snapshot = new OverlaySnapshot { ThemeId = AppThemes.BlackDesert };
        var size = new Size((int)(320 * scale), (int)(128 * scale));
        using var renderer = new NativeOverlayRenderer();
        using var title = renderer.Render(size, settings, snapshot, out _, "Rotation Monitor");
        using var empty = renderer.Render(size, settings, snapshot, out _, "");

        var titlePixels = ChangedPixels(title, empty);

        Assert.NotEmpty(titlePixels);
        // Ignore the shadow. The old DrawString raster had only about 56
        // bright-ink coverage levels: merely finding one gray edge also passed
        // for its visibly coarse title. Require finer coverage and solid stems.
        var foreground = titlePixels.Where(pixel => pixel.Color.R > 160).ToArray();
        var coverageLevels = foreground.Where(pixel => pixel.Color.A is > 0 and < 255)
            .Select(pixel => pixel.Color.A).Distinct().Count();
        Assert.True(coverageLevels >= 70,
            $"Only {coverageLevels} partial-coverage levels at scale {scale}.");
        Assert.Contains(foreground, pixel => pixel.Color.A == 255);

        var titleInterior = new RectangleF((float)(2 * scale), (float)(2 * scale),
            (float)(316 * scale), (float)(30 * scale));
        Assert.All(titlePixels, pixel => Assert.True(titleInterior.Contains(pixel.Position),
            $"Title ink at {pixel.Position} lies outside the title bar at scale {scale}."));
    }

    [Theory]
    [InlineData(96, 1)]
    [InlineData(96, 1.5)]
    [InlineData(316, 1)]
    [InlineData(316, 2)]
    public void LongAccentedTitleFitsTheTitleBarAtNarrowWidthsAndMonitorScales(int width, double scale)
    {
        var settings = new OverlaySettings
        {
            Width = width, Height = 94, Widgets = [], ShowBorder = true,
            BackgroundOpacity = 0, Interaction = "passthrough"
        };
        var snapshot = new OverlaySnapshot { ThemeId = AppThemes.BlackDesert };
        var size = new Size((int)((width + 4) * scale), (int)(128 * scale));
        using var renderer = new NativeOverlayRenderer();
        using var title = renderer.Render(size, settings, snapshot, out _,
            "Rotation Monitor – ÄÖÜß, Caphras und seltene Gegenstände");
        using var empty = renderer.Render(size, settings, snapshot, out _, "");

        var titlePixels = ChangedPixels(title, empty);
        Assert.NotEmpty(titlePixels);
        var titleInterior = new RectangleF((float)(2 * scale), (float)(2 * scale),
            (float)(width * scale), (float)(30 * scale));
        Assert.All(titlePixels, pixel => Assert.True(titleInterior.Contains(pixel.Position),
            $"Title ink at {pixel.Position} escapes the {width}px title bar at scale {scale}."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTitleLeavesTheTransparentHeaderWithoutText(string title)
    {
        var settings = new OverlaySettings
        {
            Width = 316, Height = 94, Widgets = [], ShowBorder = true,
            BackgroundOpacity = 0, Interaction = "passthrough"
        };
        var snapshot = new OverlaySnapshot { ThemeId = AppThemes.BlackDesert };
        using var renderer = new NativeOverlayRenderer();
        using var rendered = renderer.Render(new(320, 128), settings, snapshot, out _, title);
        using var empty = renderer.Render(new(320, 128), settings, snapshot, out _, "");

        Assert.Empty(ChangedPixels(rendered, empty));
        Assert.Equal(0, rendered.GetPixel(20, 16).A);
    }

    private static List<(Point Position, Color Color)> ChangedPixels(Bitmap actual, Bitmap empty)
    {
        var changed = new List<(Point Position, Color Color)>();
        for (var y = 0; y < actual.Height; y++)
        for (var x = 0; x < actual.Width; x++)
        {
            var pixel = actual.GetPixel(x, y);
            if (pixel.ToArgb() != empty.GetPixel(x, y).ToArgb())
                changed.Add((new Point(x, y), pixel));
        }
        return changed;
    }
}
