using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"UiLanguage\":null}")]
    [InlineData("{\"UiLanguage\":\"\"}")]
    [InlineData("{\"UiLanguage\":\"fr\"}")]
    public void MissingOrInvalidPreferencesUseEnglishAndPreserveGameLanguage(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        settings.GameLanguage = "de";
        settings.UpgradeDefaults();
        Assert.Equal("en", settings.UiLanguage);
        Assert.Equal("de", settings.GameLanguage);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void LanguageSurvivesSettingsRoundTrip(string language)
    {
        var settings = new AppSettings { UiLanguage = language, GameLanguage = "de" };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        restored.UpgradeDefaults();
        Assert.Equal(language, restored.UiLanguage);
        Assert.Equal("de", restored.GameLanguage);
    }

    [Fact]
    public void ResourcesPreserveFormatArgumentsAndUnknownText()
    {
        Assert.NotEmpty(AppText.English);
        foreach (var (source, english) in AppText.English)
        {
            Assert.False(string.IsNullOrWhiteSpace(english), source);
            Assert.Equal(CompositeFormat.Parse(source).MinimumArgumentCount,
                CompositeFormat.Parse(english).MinimumArgumentCount);
        }
        Assert.Equal("external error/path.json", AppText.Translate("external error/path.json", "en"));
        Assert.Equal("Settings", AppText.Translate("Einstellungen", "invalid"));
    }

    [Fact]
    public void FormatsNumbersDurationsAndStatusArgumentsForEachLanguage()
    {
        Assert.Equal("1,234,567", Presentation.Number(1234567, "en"));
        Assert.Equal("1.234.567", Presentation.Number(1234567, "de"));
        Assert.Equal("1.50 B", Presentation.Silver(1500000000, "en"));
        Assert.Equal("1,50 Mrd.", Presentation.Silver(1500000000, "de"));
        Assert.Equal("1 hr 05 min", Presentation.ShortDuration(TimeSpan.FromMinutes(65), "en"));
        Assert.Equal("1 Std. 05 Min.", Presentation.ShortDuration(TimeSpan.FromMinutes(65), "de"));
        Assert.Equal("Session for Ash Forest saved.", AppText.Translate("Session für Ash Forest gespeichert.", "en"));
        Assert.Equal("EU · Prices as of 12:30", AppText.Translate("EU · Preisstand 12:30", "en"));
        Assert.Equal("Automatically paused: no new drops for 3 minutes. Idle time has been deducted.",
            AppText.Translate("Automatisch pausiert: seit 3 Minuten kein neuer Drop. Die Zeit ohne Drops wurde abgezogen.", "en"));
        Assert.Equal("Startup · 3 / 5 offerings", AppText.Translate("Startup · 3 / 5 Opfergaben", "en"));
        Assert.Equal("AFK ended · incomplete startup (3 / 5 offerings), rotation not counted · waiting for first event",
            AppText.Translate("AFK beendet · Startup unvollständig (3 / 5 Opfergaben), Rotation nicht gezählt · warte auf erstes Ereignis", "en"));
        Assert.Equal("Paused · startup discarded (3 / 5 offerings), does not count as a complete rotation",
            AppText.Translate("Pausiert · Startup verworfen (3 / 5 Opfergaben), zählt nicht als vollständige Rotation", "en"));
    }

    [Fact]
    public async Task SwitchingLanguageRerendersOpenSettingsAndKeepsOtherSessionsIndependent()
    {
        var tracker = new PreviewTrackerSession();
        var untouched = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        await tracker.ToggleTrackingAsync();
        var sessionId = tracker.State.SessionId;
        var originalCulture = CultureInfo.CurrentCulture;
        await using var services = CreateServices(tracker);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<TrackerSettings>(ParameterView.Empty);
            var diagnostics = await renderer.RenderComponentAsync<TrackerSettings>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(TrackerSettings.Section)] = "diagnostics" }));
            Assert.Contains("Einstellungen", rendered.ToHtmlString());
            Assert.Contains("id=\"interface-language\"", rendered.ToHtmlString());
            Assert.DoesNotContain("Rotation-Monitor-Diagnose aufzeichnen", rendered.ToHtmlString());
            Assert.Contains("Rotation-Monitor-Diagnose aufzeichnen", diagnostics.ToHtmlString());

            await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "en" });
            var english = WebUtility.HtmlDecode(rendered.ToHtmlString());
            Assert.Contains("<h1>Settings</h1>", english);
            Assert.DoesNotContain("<h1>Einstellungen</h1>", english);
            Assert.Contains("English", english);
            var englishDiagnostics = WebUtility.HtmlDecode(diagnostics.ToHtmlString());
            Assert.Contains("Record rotation monitor diagnostics", englishDiagnostics);
            Assert.Contains("Record loot diagnostics", englishDiagnostics);
            Assert.Contains("every 3 seconds", englishDiagnostics);
            Assert.DoesNotContain("Rotation-Monitor-Diagnose aufzeichnen", english);
            Assert.DoesNotContain("Rotation-Monitor-Diagnose aufzeichnen", englishDiagnostics);
            Assert.Equal("en", services.GetRequiredService<IOverlayService>().Snapshot.UiLanguage);

            await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
            Assert.Contains("<h1>Einstellungen</h1>", rendered.ToHtmlString());
            Assert.Contains("Rotation-Monitor-Diagnose aufzeichnen", diagnostics.ToHtmlString());
        });
        Assert.Equal("en", untouched.Preferences.UiLanguage);
        Assert.Equal("auto", tracker.Preferences.GameLanguage);
        Assert.Equal(sessionId, tracker.State.SessionId);
        Assert.True(tracker.State.IsRunning);
        Assert.Same(originalCulture, CultureInfo.CurrentCulture);
    }

    [Theory]
    [InlineData(typeof(LiveDashboard), "Live-Session")]
    [InlineData(typeof(HistoryDashboard), "Verlauf")]
    [InlineData(typeof(GarmothDashboard), "Verbindung")]
    [InlineData(typeof(GrindGoals), "Vorheriger Monat")]
    [InlineData(typeof(OverlayEditor), "Bausteine")]
    public async Task PrimaryPagesRenderInEnglishByDefault(Type componentType, string germanText)
    {
        var tracker = new PreviewTrackerSession();
        Assert.Equal("en", tracker.Preferences.UiLanguage);
        await using var services = CreateServices(tracker);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            WebUtility.HtmlDecode((await renderer.RenderComponentAsync(componentType, ParameterView.Empty)).ToHtmlString()));
        Assert.NotEmpty(html);
        Assert.DoesNotContain(germanText, html);
    }

    private static ServiceProvider CreateServices(PreviewTrackerSession tracker) => new ServiceCollection().AddLogging()
        .AddSingleton<ITrackerSession>(tracker)
        .AddSingleton(new GrindGoalStore(null))
        .AddSingleton<IOverlayService>(sp => new OverlayService(tracker, goals: sp.GetRequiredService<GrindGoalStore>()))
        .AddSingleton<IAppUpdates>(new DisabledAppUpdates("test", "Vorschau"))
        .AddSingleton<IJSRuntime, NoJavaScript>()
        .AddSingleton<NavigationManager, StaticNavigation>()
        .BuildServiceProvider();

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/settings");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
