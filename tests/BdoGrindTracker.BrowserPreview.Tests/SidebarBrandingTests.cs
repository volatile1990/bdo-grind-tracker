using System.Net;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class SidebarBrandingTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task SidebarShowsOnlyBrandNameAndASeparatorAboveTheMenu(string language)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = language, SetupCompleted = true });
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton<ITrackerSession>(tracker)
            .AddSingleton(new GrindGoalStore(null))
            .AddSingleton<IOverlayService>(provider => new OverlayService(tracker,
                goals: provider.GetRequiredService<GrindGoalStore>()))
            .AddSingleton<IAppUpdates>(new DisabledAppUpdates("test", "Preview"))
            .AddSingleton<IJSRuntime, NoJavaScript>()
            .AddSingleton<NavigationManager, StaticNavigation>()
            .AddSingleton<INavigationInterception, NoNavigationInterception>()
            .AddSingleton<IScrollToLocationHash, NoScrollToLocationHash>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            WebUtility.HtmlDecode((await renderer.RenderComponentAsync<TrackerApp>()).ToHtmlString()));
        var sidebar = Regex.Match(html, @"<aside\b[\s\S]*?</aside>").Value;

        Assert.NotEmpty(sidebar);
        Assert.Contains("<strong>Grindcrest</strong>", sidebar);
        Assert.DoesNotContain("BLACK DESERT TRACKER", sidebar);
        Assert.DoesNotContain("DEIN ABENTEUER", sidebar);
        Assert.DoesNotContain("YOUR ADVENTURE", sidebar, StringComparison.OrdinalIgnoreCase);
        var brand = sidebar.IndexOf("class=\"brand\"", StringComparison.Ordinal);
        var separator = sidebar.IndexOf("class=\"sidebar-divider\"", StringComparison.Ordinal);
        var menu = sidebar.IndexOf("<nav>", StringComparison.Ordinal);
        Assert.True(brand >= 0 && brand < separator && separator < menu);
        Assert.Matches("class=\"sidebar-divider\"[^>]*aria-hidden=\"true\"", sidebar);
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class NoNavigationInterception : INavigationInterception
    {
        public Task EnableNavigationInterceptionAsync() => Task.CompletedTask;
    }

    private sealed class NoScrollToLocationHash : IScrollToLocationHash
    {
        public Task RefreshScrollPositionForHash(string locationAbsolute) => Task.CompletedTask;
    }
}
