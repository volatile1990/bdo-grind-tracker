using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class NativeOverlayThemeCacheTests
{
    [Theory]
    [InlineData(AppThemes.BlackDesert)]
    [InlineData(AppThemes.Light)]
    [InlineData(AppThemes.Cats)]
    public void PausedAndEmptyOverlaysRepaintWhenThemeChanges(string themeId)
    {
        var cache = new NativeOverlayRenderState();
        var settings = new OverlaySettings { Widgets = [] };
        var original = new OverlaySnapshot();
        var themed = original with { ThemeId = themeId };
        cache.Remember(settings, original, new(360, 260));
        Assert.True(cache.Matches(settings, original, new(360, 260)));
        Assert.False(cache.Matches(settings, themed, new(360, 260)));
        cache.Remember(settings, themed, new(360, 260));
        Assert.True(cache.Matches(settings, themed, new(360, 260)));
        Assert.False(cache.Matches(settings, original, new(360, 260)));
    }

    [Fact]
    public void UnknownThemeUsesTheOriginalStyleWithoutNeedlessRepainting()
    {
        var cache = new NativeOverlayRenderState();
        var settings = new OverlaySettings();
        var snapshot = new OverlaySnapshot();
        cache.Remember(settings, snapshot, new(360, 260));
        Assert.True(cache.Matches(settings, snapshot with { ThemeId = "unavailable" }, new(360, 260)));
    }
}
