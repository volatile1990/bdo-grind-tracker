using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Theming;
using BdoGrindTracker.App.Updates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class ThemePreferenceTests
{
    [Fact]
    public void NewPreferencesAndSettingsKeepGrindcrestAsTheDefault()
    {
        Assert.Equal(AppThemes.Grindcrest, new TrackerPreferences().ThemeId);
        Assert.Equal(AppThemes.Grindcrest, new AppSettings().ThemeId);
        Assert.Null(new TrackerPreferences().OverlayThemeId);
        Assert.Null(new AppSettings().OverlayThemeId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"SettingsVersion\": 6 }")]
    [InlineData("{ \"SettingsVersion\": 6, \"ThemeId\": null }")]
    [InlineData("{ \"SettingsVersion\": 6, \"ThemeId\": \"\" }")]
    [InlineData("{ \"SettingsVersion\": 6, \"ThemeId\": \"unknown-theme\" }")]
    public void MissingOrUnrecognizedStoredThemesMigrateToGrindcrest(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        settings.AutoPauseMinutes = 12;
        settings.MonitorDeviceName = "DISPLAY2";

        settings.UpgradeDefaults();

        Assert.Equal(AppThemes.Grindcrest, settings.ThemeId);
        Assert.Equal(12, settings.AutoPauseMinutes);
        Assert.Equal("DISPLAY2", settings.MonitorDeviceName);
    }

    [Theory]
    [InlineData(AppThemes.Grindcrest)]
    [InlineData(AppThemes.BlackDesert)]
    [InlineData(AppThemes.Light)]
    [InlineData(AppThemes.Cats)]
    [InlineData(AppThemes.Obsidian)]
    [InlineData(AppThemes.Kamasylvia)]
    [InlineData(AppThemes.Valencia)]
    public void ThemeSelectionSurvivesSettingsJsonRoundTrip(string themeId)
    {
        var settings = new AppSettings { ThemeId = themeId };
        settings.UpgradeDefaults();

        var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        reloaded.UpgradeDefaults();

        Assert.Equal(themeId, reloaded.ThemeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-theme")]
    public async Task PreviewRejectsUnknownThemesBeforeChangingAnyPreferences(string? themeId)
    {
        var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { ThemeId = AppThemes.BlackDesert });
        var previous = tracker.Preferences;
        var previousState = tracker.State;

        var result = await tracker.SavePreferencesAsync(previous with { ThemeId = themeId!, FamilyFame = 7200 });

        Assert.False(result.Succeeded);
        Assert.Contains("Theme", result.Error);
        Assert.Same(previous, tracker.Preferences);
        Assert.Same(previousState, tracker.State);
    }

    [Fact]
    public async Task SettingsCanSwitchAllThemesImmediatelyDuringAnActiveSession()
    {
        var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        await tracker.ToggleTrackingAsync();
        var sessionId = tracker.State.SessionId;
        var activator = new CapturingActivator();
        var services = new ServiceCollection().AddLogging()
            .AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IAppUpdates>(new DisabledAppUpdates("test", "Vorschau"))
            .AddSingleton<IJSRuntime, NoJavaScript>()
            .AddSingleton<NavigationManager, StaticNavigation>()
            .AddSingleton<IComponentActivator>(activator);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<TrackerSettings>(ParameterView.Empty);
            var component = activator.Components.OfType<TrackerPreferenceSettings>().Single();
            var markup = WebUtility.HtmlDecode(rendered.ToHtmlString());
            var select = Regex.Match(markup, "<select[^>]*id=\"appearance-theme\"[^>]*>(.*?)</select>", RegexOptions.Singleline);
            Assert.True(select.Success);
            Assert.Contains(">Grindcrest</option>", select.Value);
            Assert.Contains(">Black Desert</option>", select.Value);
            Assert.Contains(">Light</option>", select.Value);
            Assert.Contains(">Katzen</option>", select.Value);
            Assert.Contains(">Obsidian</option>", select.Value);
            Assert.Contains(">Kamasylvia</option>", select.Value);
            Assert.Contains(">Valencia</option>", select.Value);
            Assert.DoesNotContain("disabled", select.Value);

            foreach (var themeId in new[] { AppThemes.BlackDesert, AppThemes.Light, AppThemes.Cats, AppThemes.Obsidian,
                AppThemes.Kamasylvia, AppThemes.Valencia, AppThemes.Grindcrest })
            {
                await (Task)typeof(TrackerPreferenceSettings).GetMethod("ThemeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(component, [themeId])!;

                Assert.Equal(themeId, tracker.Preferences.ThemeId);
                Assert.True(tracker.State.IsRunning);
                Assert.Equal(sessionId, tracker.State.SessionId);
            }

            // A settings recovery can replace preferences while this page stays open.
            await tracker.SavePreferencesAsync(tracker.Preferences with { ThemeId = AppThemes.Cats });
            markup = WebUtility.HtmlDecode(rendered.ToHtmlString());
            Assert.Contains("value=\"cats\"", Regex.Match(markup, "<select[^>]*id=\"appearance-theme\"[^>]*>").Value);
            Assert.Contains("Katzenmotive", markup);
        });
    }

    [Theory]
    [InlineData(AppThemes.Grindcrest, "ursprüngliche Grindcrest-Design")]
    [InlineData(AppThemes.BlackDesert, "im Stil von Black Desert")]
    [InlineData(AppThemes.Light, "Helle Flächen")]
    [InlineData(AppThemes.Cats, "Katzenmotive")]
    [InlineData(AppThemes.Obsidian, "Fast schwarze Flächen")]
    [InlineData(AppThemes.Kamasylvia, "Dunkles Waldgrün")]
    [InlineData(AppThemes.Valencia, "Warme Sandflächen")]
    public async Task SettingsShowThePreviouslySelectedThemeWhenReopened(string themeId, string description)
    {
        var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { ThemeId = themeId, UiLanguage = "de" });
        var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IAppUpdates>(new DisabledAppUpdates("test", "Vorschau"))
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<NavigationManager, StaticNavigation>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var markup = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TrackerSettings>(ParameterView.Empty)).ToHtmlString());

        Assert.Contains($"value=\"{themeId}\"", Regex.Match(markup, "<select[^>]*id=\"appearance-theme\"[^>]*>").Value);
        Assert.Contains(description, WebUtility.HtmlDecode(markup));
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            Components.Add(component);
            return component;
        }
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/settings");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
