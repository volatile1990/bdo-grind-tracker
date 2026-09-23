using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class DebugLoggingSettingsTests
{
    [Theory]
    [InlineData("de", "Debuglogs automatisch aufzeichnen", "Standard: 3 Stunden.", "ohne Bilder", "Debuglog-Fehler:")]
    [InlineData("en", "Record debug logs automatically", "Default: 3 hours.", "without images", "Debug log error:")]
    public async Task SettingsShowDefaultRetentionLocalPathAndDebugLogFailuresInBothLanguages(
        string language, string label, string defaultHint, string imageHint, string errorLabel)
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = language });
        typeof(PreviewTrackerSession).GetProperty(nameof(PreviewTrackerSession.State))!
            .SetValue(tracker, tracker.State with { DebugLogError = "Test: storage unavailable" });
        await using var provider = Services(tracker);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var markup = await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<TrackerPreferenceSettings>()).ToHtmlString()));

        Assert.Contains(label, markup);
        Assert.Contains(defaultHint, markup);
        Assert.Contains(imageHint, markup);
        Assert.Contains(((ITrackerSession)tracker).DebugLogsDirectory, markup);
        Assert.Contains(errorLabel, markup);
        Assert.Contains("Test: storage unavailable", markup);
        Assert.Contains("role=\"alert\"", markup);
        Assert.DoesNotContain("checked", Input(markup, "automatic-debug-logging"));
        var duration = Input(markup, "debug-log-retention-hours");
        Assert.Contains("value=\"3\"", duration);
        Assert.Contains("min=\"1\"", duration);
        Assert.Contains("max=\"168\"", duration);
    }

    [Fact]
    public async Task AutomaticLoggingAndRetentionCanChangeDuringARunningSessionAndSurviveReopeningSettings()
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.ToggleTrackingAsync();
        var sessionId = tracker.State.SessionId;
        var activator = new CapturingActivator();
        await using var provider = Services(tracker, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<TrackerPreferenceSettings>();
            var component = activator.Components.OfType<TrackerPreferenceSettings>().Single();
            var markup = rendered.ToHtmlString();
            Assert.DoesNotContain("disabled", Input(markup, "automatic-debug-logging"));
            Assert.DoesNotContain("disabled", Input(markup, "debug-log-retention-hours"));

            SetField(component, "_automaticDebugLogging", true);
            await Invoke(component, "AutomaticDebugLoggingChanged");
            SetField(component, "_debugLogRetentionHours", "12");
            await Invoke(component, "SaveDebugLogRetention");

            Assert.True(tracker.Preferences.AutomaticDebugLogging);
            Assert.Equal(12, tracker.Preferences.DebugLogRetentionHours);
            Assert.True(tracker.State.IsRunning);
            Assert.Equal(sessionId, tracker.State.SessionId);

            var reopened = await renderer.RenderComponentAsync<TrackerPreferenceSettings>();
            Assert.Contains("checked", Input(reopened.ToHtmlString(), "automatic-debug-logging"));
            Assert.Contains("value=\"12\"", Input(reopened.ToHtmlString(), "debug-log-retention-hours"));

            SetField(component, "_automaticDebugLogging", false);
            await Invoke(component, "AutomaticDebugLoggingChanged");
            Assert.False(tracker.Preferences.AutomaticDebugLogging);
            Assert.Equal(12, tracker.Preferences.DebugLogRetentionHours);
            Assert.True(tracker.State.IsRunning);
            Assert.Equal(sessionId, tracker.State.SessionId);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedToggleSaveRestoresThePersistedLoggingState(bool enabled)
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { AutomaticDebugLogging = enabled });
        var activator = new CapturingActivator();
        await using var provider = Services(tracker, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<TrackerPreferenceSettings>();
            var component = activator.Components.OfType<TrackerPreferenceSettings>().Single();
            // Reuse the preview's rejection path without introducing disk writes or a full session stub.
            typeof(PreviewTrackerSession).GetProperty(nameof(PreviewTrackerSession.Preferences))!
                .SetValue(tracker, tracker.Preferences with { ThemeId = "invalid-theme" });
            SetField(component, "_automaticDebugLogging", !enabled);

            await EventCallback.Factory.Create(component, () => Invoke(component, "AutomaticDebugLoggingChanged")).InvokeAsync();

            Assert.Equal(enabled, tracker.Preferences.AutomaticDebugLogging);
            Assert.Equal(enabled, Input(rendered.ToHtmlString(), "automatic-debug-logging").Contains("checked"));
            Assert.Contains("Please select a valid theme from the list.", WebUtility.HtmlDecode(rendered.ToHtmlString()));
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("169")]
    [InlineData("1.5")]
    [InlineData("invalid")]
    public async Task InvalidRetentionDoesNotReplaceSavedHoursAndCanBeCorrected(string input)
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { AutomaticDebugLogging = true, DebugLogRetentionHours = 6 });
        var activator = new CapturingActivator();
        await using var provider = Services(tracker, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<TrackerPreferenceSettings>();
            var component = activator.Components.OfType<TrackerPreferenceSettings>().Single();
            SetField(component, "_debugLogRetentionHours", input);
            await Invoke(component, "SaveDebugLogRetention");

            Assert.False(await component.CommitAsync());
            Assert.Equal(6, tracker.Preferences.DebugLogRetentionHours);
            Assert.True(tracker.Preferences.AutomaticDebugLogging);
            Assert.Contains("Enter a whole number between 1 and 168.", WebUtility.HtmlDecode(rendered.ToHtmlString()));

            SetField(component, "_debugLogRetentionHours", "168");
            await Invoke(component, "SaveDebugLogRetention");
            Assert.True(await component.CommitAsync());
            Assert.Equal(168, tracker.Preferences.DebugLogRetentionHours);
            Assert.DoesNotContain("field-error", rendered.ToHtmlString());
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(169)]
    public async Task PreviewRejectsInvalidRetentionWithoutChangingPreferencesOrSession(int hours)
    {
        var tracker = new PreviewTrackerSession(empty: true);
        var preferences = tracker.Preferences;
        var state = tracker.State;

        var result = await tracker.SavePreferencesAsync(preferences with
        {
            AutomaticDebugLogging = true,
            DebugLogRetentionHours = hours,
        });

        Assert.False(result.Succeeded);
        Assert.Contains("1 und 168", result.Error);
        Assert.Same(preferences, tracker.Preferences);
        Assert.Same(state, tracker.State);
    }

    private static string Input(string markup, string id)
    {
        var match = Regex.Match(markup, $"<input[^>]*id=\"{id}\"[^>]*>");
        Assert.True(match.Success);
        return match.Value;
    }

    private static void SetField(TrackerPreferenceSettings component, string name, object value) =>
        typeof(TrackerPreferenceSettings).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);

    private static Task Invoke(TrackerPreferenceSettings component, string method) =>
        (Task)typeof(TrackerPreferenceSettings).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, null)!;

    private static ServiceProvider Services(PreviewTrackerSession tracker, CapturingActivator? activator = null)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IAppUpdates>(new DisabledAppUpdates("test", "Preview"))
            .AddSingleton<NavigationManager, StaticNavigation>();
        if (activator is not null) services.AddSingleton<IComponentActivator>(activator);
        return services.BuildServiceProvider();
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
}
