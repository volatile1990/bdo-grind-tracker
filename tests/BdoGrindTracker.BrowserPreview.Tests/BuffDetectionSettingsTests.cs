using System.Net;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class BuffDetectionSettingsTests
{
    [Theory]
    [InlineData("de", "Buff-Erkennung", "Erweiterte Kalibrierung (optional)")]
    [InlineData("en", "Buff detection", "Advanced calibration (optional)")]
    public async Task SettingsDoNotOfferBuffDetectionOrCalibrationControls(
        string language, string heading, string optional)
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = language });
        await using var provider = Services(tracker);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var markup = await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<TrackerSettings>()).ToHtmlString()));

        Assert.DoesNotContain(heading, markup);
        Assert.DoesNotContain(optional, markup);
        Assert.DoesNotContain("buff-settings", markup);
        Assert.DoesNotContain("buff-details", markup);
        Assert.Null(tracker.Preferences.BuffRecognitionProfilePath);
    }

    [Fact]
    public async Task LegacyProfilePreferenceCannotRestoreTheRemovedSettingsCard()
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        // Inject a legacy snapshot even if the preference writer already clears
        // obsolete profile paths. Rendering must not expose an editing path.
        typeof(PreviewTrackerSession).GetProperty(nameof(PreviewTrackerSession.Preferences))!
            .SetValue(tracker, tracker.Preferences with { BuffRecognitionProfilePath = "D:/profiles/retained-profile.json" });
        await using var provider = Services(tracker);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var markup = await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<TrackerSettings>()).ToHtmlString()));

        Assert.DoesNotContain("Buff-Erkennung", markup);
        Assert.DoesNotContain("retained-profile.json", markup);
        Assert.DoesNotContain("buff-settings", markup);
        Assert.DoesNotContain("Profil auswählen", markup);
        Assert.DoesNotContain("Eigenes Profil per Screenshot erstellen", markup);
    }

    private static ServiceProvider Services(PreviewTrackerSession tracker)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IAppUpdates>(new DisabledAppUpdates("test", "Preview"))
            .AddSingleton<NavigationManager, StaticNavigation>();
        return services.BuildServiceProvider();
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/settings");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
