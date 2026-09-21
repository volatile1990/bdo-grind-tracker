using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class AutoStartPreferenceTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"SettingsVersion\": 6, \"AutoPauseMinutes\": 12 }")]
    public void AutomaticGrindDetectionRequiresExplicitOptInForNewAndExistingSettings(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        settings.UpgradeDefaults();

        Assert.False(settings.AutoStartGrinding);
        Assert.False(new TrackerPreferences().AutoStartGrinding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticGrindDetectionPreferenceSurvivesUpgradeAndJsonRoundTrip(bool enabled)
    {
        var settings = new AppSettings { AutoStartGrinding = enabled, AutoPauseMinutes = 12 };
        settings.UpgradeDefaults();

        var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        reloaded.UpgradeDefaults();

        Assert.Equal(enabled, reloaded.AutoStartGrinding);
        Assert.Equal(12, reloaded.AutoPauseMinutes);
    }

    [Fact]
    public void LegacyManualSuspensionIsIgnoredWithoutDisablingTheOptIn()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            "{ \"AutoStartGrinding\": true, \"AutoStartSuspended\": true }")!;
        settings.UpgradeDefaults();

        var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        reloaded.UpgradeDefaults();

        Assert.True(reloaded.AutoStartGrinding);
        Assert.DoesNotContain("AutoStartSuspended", JsonSerializer.Serialize(reloaded));
    }

    [Fact]
    public async Task LiveSessionKeepsDetectionEnabledAfterPausingUntilTheSwitchIsTurnedOff()
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        // Render the same controls as a real session rather than the demo state.
        typeof(PreviewTrackerSession).GetProperty(nameof(PreviewTrackerSession.State))!
            .SetValue(tracker, tracker.State with { IsDemo = false });
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging()
            .AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IAppUpdates>(new DisabledAppUpdates("test", "Vorschau"))
            .AddSingleton<IJSRuntime, NoJavaScript>()
            .AddSingleton<NavigationManager, StaticNavigation>()
            .AddSingleton<IComponentActivator>(activator)
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<LiveDashboard>(ParameterView.Empty);
            var component = activator.Components.OfType<LiveDashboard>().Single();
            string Markup() => WebUtility.HtmlDecode(rendered.ToHtmlString());
            string Checkbox() => Regex.Match(Markup(), "<input[^>]*id=\"auto-start-grinding\"[^>]*>").Value;
            Assert.NotEmpty(Checkbox());
            Assert.DoesNotContain("checked", Checkbox());
            Assert.Contains("Grind automatisch erkennen", Markup());
            Assert.Contains("live-auto-start", Markup());
            Assert.DoesNotContain("Automatik wieder aktivieren", Markup());

            await Invoke(component, "AutoStartChanged", true);

            Assert.True(tracker.Preferences.AutoStartGrinding);
            Assert.False(tracker.State.IsRunning);
            Assert.False(tracker.State.HasSession);
            Assert.Contains("checked", Checkbox());
            Assert.DoesNotContain("Vordergrund", Markup());
            Assert.DoesNotContain("live-auto-start-status", Markup());

            await tracker.ToggleTrackingAsync();
            await tracker.PauseAsync();
            var sessionId = tracker.State.SessionId;
            Assert.True(tracker.Preferences.AutoStartGrinding);
            Assert.Contains("checked", Checkbox());
            Assert.NotNull(tracker.State.AutoStartStatus);
            Assert.False(tracker.State.IsRunning);
            Assert.Equal(sessionId, tracker.State.SessionId);
            Assert.DoesNotContain("Automatik wieder aktivieren", Markup());

            await Invoke(component, "AutoStartChanged", false);
            Assert.False(tracker.Preferences.AutoStartGrinding);
            Assert.DoesNotContain("checked", Checkbox());
            Assert.Null(tracker.State.AutoStartStatus);
            Assert.Equal(sessionId, tracker.State.SessionId);

            // External preference changes must remain visible in the live session.
            await tracker.SavePreferencesAsync(tracker.Preferences with { AutoStartGrinding = true });
            Assert.Contains("checked", Checkbox());

            var settings = await renderer.RenderComponentAsync<TrackerSettings>(ParameterView.Empty);
            Assert.DoesNotContain("auto-start-grinding", settings.ToHtmlString());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewPauseManualStartAndNewSessionPreserveAutomaticDetection(bool createNewSession)
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { AutoStartGrinding = true });
        await tracker.ToggleTrackingAsync();
        await tracker.ToggleTrackingAsync();
        Assert.True(tracker.Preferences.AutoStartGrinding);
        Assert.NotNull(tracker.State.AutoStartStatus);

        if (createNewSession) await tracker.NewSessionAsync();
        else await tracker.ToggleTrackingAsync();

        Assert.True(tracker.Preferences.AutoStartGrinding);
        Assert.NotNull(tracker.State.AutoStartStatus);
        Assert.Equal(!createNewSession, tracker.State.IsRunning);
    }

    private static Task Invoke(LiveDashboard component, string method, params object[] args) =>
        (Task)typeof(LiveDashboard).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, args)!;

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
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/live");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
