using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Theming;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayMergeIntegrationTests
{
    [Fact]
    public void RealSessionDemoKeepsTheSameDataAcrossUiLanguages()
    {
        var german = OverlayMetrics.DemoFor("de");
        var english = OverlayMetrics.DemoFor("en");

        Assert.Equal("de", german.UiLanguage);
        Assert.Equal("en", english.UiLanguage);
        Assert.Equal(DemoSession.Elapsed, english.SessionElapsed);
        Assert.Equal(german.SessionElapsed, english.SessionElapsed);
        Assert.Equal(german.SilverHistory, english.SilverHistory);
        Assert.Equal(german.SilverDrops, english.SilverDrops);
        Assert.NotEmpty(english.SilverDrops);
        Assert.Equal(german.Drops.Select(item => (item.CanonicalName, item.Name, item.Quantity)),
            english.Drops.Select(item => (item.CanonicalName, item.Name, item.Quantity)));
        Assert.Equal("Black Crystal Fragment", english.Drops[0].Name);
        Assert.Equal("24,438", english.Metrics["trash"].Value);
        Assert.Equal("24.438", german.Metrics["trash"].Value);
        Assert.Equal("6", english.Metrics["rotation-count"].Value);
        Assert.Equal("5.7 / h · Ø 10:35 · last 3", english.Metrics["rotations-hour"].Detail);
        Assert.StartsWith("Sample data · Hermesia session from ", english.Status);
        Assert.Equal("en", english.DailyGoal.UiLanguage);
        Assert.Equal(german.DailyGoal.Earned, english.DailyGoal.Earned);
    }

    [Fact]
    public void RotationModulesUseUiLanguageAndTheIndependentOverlayTheme()
    {
        var rotation = HermesiaRotationDemo.At(350) with
        {
            SessionRotations = [new(900, 20), new(600, 15), new(620, 25), new(640)],
        };
        var state = new TrackerState { HasSession = true, SpotId = LootSpotCatalog.HermesiaId, Rotation = rotation };
        var preferences = new TrackerPreferences
        {
            UiLanguage = "en", ThemeId = AppThemes.Light, OverlayThemeId = AppThemes.Kamasylvia,
        };
        var metrics = new OverlayMetrics();
        var snapshot = metrics.Update(state, preferences);

        Assert.Equal(AppThemes.Kamasylvia, snapshot.ThemeId);
        Assert.Equal("5.6 / h · Ø 10:40 · last 3", snapshot.Metrics["rotations-hour"].Detail);
        Assert.Equal("Latest 10:40", snapshot.Metrics["rotation-count"].Detail);
        Assert.Contains("walk back", snapshot.Metrics["rotations-hour"].Tooltip);
        Assert.DoesNotContain("Rückweg", snapshot.Metrics["rotations-hour"].Tooltip);
        Assert.Equal(AppThemes.Kamasylvia,
            metrics.Update(state, preferences with { ThemeId = AppThemes.Valencia }).ThemeId);
        Assert.Equal("No rotation profile for this spot",
            metrics.Update(state with { SpotId = null }, preferences).Metrics["rotation-count"].Detail);
        Assert.Equal("After the first complete rotation", metrics.Update(state with
        {
            Rotation = rotation with { SessionRotations = [] },
        }, preferences).Metrics["rotations-hour"].Detail);
    }

    [Theory]
    [InlineData(AppThemes.Obsidian)]
    [InlineData(AppThemes.Kamasylvia)]
    [InlineData(AppThemes.Valencia)]
    public void SectionChartsKeepTheirThemeWhenOnlyTheMainWindowThemeChanges(string overlayTheme)
    {
        var widget = OverlayCatalog.CreateWidget("chart") with { X = 0, Y = 0 };
        var settings = new OverlaySettings
        {
            Width = widget.Width, Height = widget.Height, Widgets = [widget], BackgroundOpacity = 1,
            ShowBorder = false,
        };
        var metrics = new OverlayMetrics();
        var preferences = new TrackerPreferences
        {
            UiLanguage = "en", ThemeId = AppThemes.Light, OverlayThemeId = overlayTheme,
        };
        var projected = metrics.Update(new(), preferences);
        var changedMain = metrics.Update(new(), preferences with { ThemeId = AppThemes.Cats });
        var snapshot = OverlayMetrics.DemoFor("en") with { ThemeId = projected.ThemeId };
        using var renderer = new NativeOverlayRenderer();
        var size = new Size((int)widget.Width, (int)widget.Height);
        using var before = renderer.Render(size, settings, snapshot, out _);
        using var after = renderer.Render(size, settings, snapshot with { ThemeId = changedMain.ThemeId }, out _);
        using var otherOverlay = renderer.Render(size, settings, snapshot with { ThemeId = AppThemes.Grindcrest }, out _);

        Assert.Equal(Pixels(before), Pixels(after));
        Assert.NotEqual(Pixels(before), Pixels(otherOverlay));
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
        finally { image.UnlockBits(data); }
    }
}
