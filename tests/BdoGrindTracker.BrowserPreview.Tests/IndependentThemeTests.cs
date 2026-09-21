using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class IndependentThemeTests
{
    [Theory]
    [InlineData("{ \"SettingsVersion\": 6, \"ThemeId\": \"cats\" }")]
    [InlineData("{ \"ThemeId\": \"cats\", \"OverlayThemeId\": null }")]
    [InlineData("{ \"ThemeId\": \"cats\", \"OverlayThemeId\": \"\" }")]
    [InlineData("{ \"ThemeId\": \"cats\", \"OverlayThemeId\": \"retired-theme\" }")]
    public void OlderOrInvalidOverlaySettingsFollowTheExistingMainTheme(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        settings.UpgradeDefaults();

        Assert.Equal(7, settings.SettingsVersion);
        Assert.Equal(AppThemes.Cats, settings.ThemeId);
        Assert.Null(settings.OverlayThemeId);
        Assert.Equal(AppThemes.Cats, new TrackerPreferences
        {
            ThemeId = settings.ThemeId, OverlayThemeId = settings.OverlayThemeId,
        }.EffectiveOverlayThemeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AppThemes.Grindcrest)]
    [InlineData(AppThemes.BlackDesert)]
    [InlineData(AppThemes.Light)]
    [InlineData(AppThemes.Cats)]
    [InlineData(AppThemes.Obsidian)]
    [InlineData(AppThemes.Kamasylvia)]
    [InlineData(AppThemes.Valencia)]
    public void IndependentOverlayThemeSurvivesSettingsRoundTrip(string? overlayTheme)
    {
        var settings = new AppSettings { ThemeId = AppThemes.Light, OverlayThemeId = overlayTheme };
        settings.UpgradeDefaults();
        var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        reloaded.UpgradeDefaults();

        Assert.Equal(AppThemes.Light, reloaded.ThemeId);
        Assert.Equal(overlayTheme, reloaded.OverlayThemeId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-theme")]
    public async Task PreviewRejectsInvalidOverlayThemeAtomically(string theme)
    {
        await using var tracker = new PreviewTrackerSession();
        var previous = tracker.Preferences;
        var state = tracker.State;
        var result = await tracker.SavePreferencesAsync(previous with { OverlayThemeId = theme, ThemeId = AppThemes.Light });

        Assert.False(result.Succeeded);
        Assert.Same(previous, tracker.Preferences);
        Assert.Same(state, tracker.State);
    }

    [Fact]
    public async Task BothSelectorsAreIndependentAndFollowOptionTracksLaterMainThemeChanges()
    {
        await using var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        using var overlay = new OverlayService(tracker);
        var activator = new CapturingActivator();
        await using var provider = Services(tracker, overlay, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<TrackerPreferenceSettings>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(TrackerPreferenceSettings.Section)] = "appearance" }));
            var component = activator.Components.OfType<TrackerPreferenceSettings>().Single();
            var markup = WebUtility.HtmlDecode(rendered.ToHtmlString());
            var main = Select(markup, "appearance-theme");
            var overlaySelect = Select(markup, "overlay-appearance-theme");
            Assert.Equal(7, Regex.Matches(main, "<option\\b").Count);
            Assert.Equal(8, Regex.Matches(overlaySelect, "<option\\b").Count);
            Assert.Contains(">Wie Hauptfenster</option>", overlaySelect);

            await Change(component, "OverlayThemeChanged", AppThemes.Obsidian);
            await Change(component, "ThemeChanged", AppThemes.Valencia);
            Assert.Equal(AppThemes.Valencia, tracker.Preferences.ThemeId);
            Assert.Equal(AppThemes.Obsidian, overlay.Snapshot.ThemeId);
            markup = WebUtility.HtmlDecode(rendered.ToHtmlString());
            Assert.Contains("value=\"valencia\"", Select(markup, "appearance-theme").Split('>')[0]);
            Assert.Contains("value=\"obsidian\"", Select(markup, "overlay-appearance-theme").Split('>')[0]);

            await Change(component, "OverlayThemeChanged", "");
            Assert.Null(tracker.Preferences.OverlayThemeId);
            Assert.Equal(AppThemes.Valencia, overlay.Snapshot.ThemeId);
            await Change(component, "ThemeChanged", AppThemes.Kamasylvia);
            Assert.Equal(AppThemes.Kamasylvia, overlay.Snapshot.ThemeId);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditorAndBrowserPreviewUseOverlayThemeForLiveAndDemoData(bool demo)
    {
        await using var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with
        {
            ThemeId = AppThemes.Light, OverlayThemeId = AppThemes.Kamasylvia,
        });
        using var overlay = new OverlayService(tracker);
        var activator = new CapturingActivator();
        await using var provider = Services(tracker, overlay, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var editor = await renderer.RenderComponentAsync<OverlayEditor>(ParameterView.Empty);
            var component = activator.Components.OfType<OverlayEditor>().Single();
            typeof(OverlayEditor).GetField("_demo", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, demo);
            // Updating preferences rerenders the editor and regenerates its demo snapshot.
            await tracker.SavePreferencesAsync(tracker.Preferences with { OverlayThemeId = AppThemes.Valencia });
            Assert.Contains("class=\"oe-stage-viewport\" data-theme=\"valencia\"", editor.ToHtmlString());
            Assert.All(activator.Components.OfType<OverlayWidgetPreview>(), widget => Assert.Equal(AppThemes.Valencia, widget.Snapshot.ThemeId));

            var preview = await renderer.RenderComponentAsync<BrowserOverlayPreview>(ParameterView.Empty);
            Assert.Contains("data-theme=\"valencia\"", preview.ToHtmlString());
            Assert.Equal(AppThemes.Light, tracker.Preferences.ThemeId);
        });
    }

    private static string Select(string markup, string id) =>
        Regex.Match(markup, $"<select[^>]*id=\"{id}\"[^>]*>.*?</select>", RegexOptions.Singleline).Value;

    private static Task Change(TrackerPreferenceSettings component, string method, string value) =>
        (Task)typeof(TrackerPreferenceSettings).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, [value])!;

    private static ServiceProvider Services(ITrackerSession tracker, IOverlayService overlay, CapturingActivator activator) =>
        new ServiceCollection().AddLogging().AddSingleton(tracker).AddSingleton(overlay)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();

    private sealed class CapturingActivator : IComponentActivator
    {
        internal List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            Components.Add(component);
            return component;
        }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
