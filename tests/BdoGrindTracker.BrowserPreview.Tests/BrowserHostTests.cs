using System.Net;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;
using BdoGrindTracker.App.Theming;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class BrowserHostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BrowserHostTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("/")]
    [InlineData("/history")]
    [InlineData("/settings")]
    [InlineData("/settings/appearance")]
    [InlineData("/settings/capture")]
    [InlineData("/settings/silver")]
    [InlineData("/settings/diagnostics")]
    [InlineData("/settings/updates")]
    [InlineData("/garmoth")]
    [InlineData("/overlay")]
    [InlineData("/grind-goals")]
    public async Task DirectNavigationLoadsTheInteractiveBrowserHost(string path)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Grindcrest · Browser preview", html);
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("_framework/blazor.web.js", html);
        Assert.Contains("href=\"grind-goals.css\"", html);
        Assert.DoesNotContain("blazor.webview.js", html);
    }

    [Theory]
    [InlineData("/app.css", "text/css")]
    [InlineData("/themes.css", "text/css")]
    [InlineData("/overlay-widgets.css", "text/css")]
    [InlineData("/overlay-editor.css", "text/css")]
    [InlineData("/browser-preview.css", "text/css")]
    [InlineData("/grind-goals.css", "text/css")]
    [InlineData("/app.js", "text/javascript")]
    [InlineData("/overlay-editor.js", "text/javascript")]
    [InlineData("/assets/branding/grindcrest-header.png", "image/png")]
    [InlineData("/assets/spot-backgrounds/aphrodon.jpg", "image/jpeg")]
    [InlineData("/assets/spot-backgrounds/gavinya-coastal-cliff.webp", "image/webp")]
    [InlineData("/assets/icons/ancient-spirit-dust.png", "image/png")]
    [InlineData("/assets/themes/cats/kitten-lounge.png", "image/png")]
    [InlineData("/assets/themes/cats/sidebar-napping-kitten.png", "image/png")]
    [InlineData("/assets/themes/cats/workspace-playful-kitten.png", "image/png")]
    [InlineData("/assets/themes/cats/settings-ribbon-kitten.png", "image/png")]
    public async Task SharedAssetsAreServedByTheRealHost(string path, string mediaType)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task PreviewLootUsesTheBundledItemIcons()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var overlay = scope.ServiceProvider.GetRequiredService<IOverlayService>();
        using var client = _factory.CreateClient();
        Assert.NotEmpty(overlay.Snapshot.Drops);
        foreach (var drop in overlay.Snapshot.Drops)
        {
            Assert.StartsWith("assets/icons/", drop.IconPath);
            using var response = await client.GetAsync("/" + drop.IconPath);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task BrowserSessionsKeepTrackingAndOverlayEditsIndependent()
    {
        await using var first = _factory.Services.CreateAsyncScope();
        await using var second = _factory.Services.CreateAsyncScope();
        var tracker = first.ServiceProvider.GetRequiredService<ITrackerSession>();
        var untouchedTracker = second.ServiceProvider.GetRequiredService<ITrackerSession>();
        var overlays = first.ServiceProvider.GetRequiredService<IOverlayService>();
        var untouchedOverlays = second.ServiceProvider.GetRequiredService<IOverlayService>();
        Assert.IsType<PreviewTrackerSession>(tracker);

        await tracker.ToggleTrackingAsync();
        Assert.True(tracker.State.IsRunning);
        Assert.False(untouchedTracker.State.IsRunning);
        Assert.True((await overlays.CreateOverlayAsync("Browser-Test")).Succeeded);
        await overlays.SetPreviewAsync(true);
        Assert.Equal(2, overlays.Overlays.Count);
        Assert.True(overlays.State.Previewing);
        Assert.Single(untouchedOverlays.Overlays);
        Assert.False(untouchedOverlays.State.Previewing);

        var updates = first.ServiceProvider.GetRequiredService<IAppUpdates>();
        await updates.CheckAsync();
        Assert.False(updates.State.Enabled);
        Assert.IsType<DisabledAppUpdates>(updates);
    }

    [Fact]
    public async Task ThemeChangesReachAllOverlayLayoutsWithoutChangingSessionOrOtherTabs()
    {
        await using var first = _factory.Services.CreateAsyncScope();
        await using var second = _factory.Services.CreateAsyncScope();
        var tracker = first.ServiceProvider.GetRequiredService<ITrackerSession>();
        var overlays = first.ServiceProvider.GetRequiredService<IOverlayService>();
        var secondTracker = second.ServiceProvider.GetRequiredService<ITrackerSession>();
        var goals = first.ServiceProvider.GetRequiredService<GrindGoalStore>();
        var today = DateOnly.FromDateTime(DateTime.Today);
        goals.Set([today], 1_250_000_000m);
        Assert.True((await overlays.SaveAsync(overlays.Settings with
        {
            Widgets = [OverlayCatalog.CreateWidget("daily-goal"),
                OverlayCatalog.CreateWidget("rotation-monitor") with { RotationComparison = "ideal", RotationColors = "minimal" }],
        })).Succeeded);
        await overlays.CreateOverlayAsync("Zweites Overlay");
        var layouts = overlays.Overlays.ToArray();
        var sessionId = tracker.State.SessionId;
        var totals = tracker.State.Loot.Totals;
        Assert.Equal(AppThemes.Grindcrest, overlays.Snapshot.ThemeId);

        foreach (var themeId in new[] { AppThemes.BlackDesert, AppThemes.Light, AppThemes.Cats, AppThemes.Grindcrest })
        {
            var changed = await tracker.SavePreferencesAsync(tracker.Preferences with { ThemeId = themeId });
            Assert.True(changed.Succeeded);
            Assert.Equal(themeId, overlays.Snapshot.ThemeId);
            Assert.Equal(layouts, overlays.Overlays);
            Assert.Equal(sessionId, tracker.State.SessionId);
            Assert.Equal(totals, tracker.State.Loot.Totals);
            Assert.Equal(1_250_000_000m, overlays.Snapshot.DailyGoal.Target);
            Assert.Equal(1_250_000_000m, goals.Goals[today]);
            Assert.Equal(AppThemes.Grindcrest, secondTracker.Preferences.ThemeId);
            Assert.Empty(second.ServiceProvider.GetRequiredService<GrindGoalStore>().Goals);
        }
    }
}
