using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class WindowTraySettingsTests
{
    [Theory]
    [InlineData("de", "Fenster & Tray", "Beim Minimieren", "Normal minimieren", "Beim Schließen (X)", "Programm beenden", "In den Tray minimieren", "Im Tray läuft die Erfassung weiter.", "Beim ersten Schließen fragen")]
    [InlineData("en", "Window & Tray", "When minimizing", "Minimize normally", "When closing (X)", "Exit the app", "Minimize to tray", "Tracking continues in the tray.", "Ask on first close")]
    public async Task WindowSettingsExplainBothChoicesAndBackgroundTrackingInEachLanguage(
        string language, string title, string minimizeLabel, string minimizeOption, string closeLabel, string closeOption,
        string trayOption, string trackingHint, string askOption)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = language });
        await Render(tracker, (_, markup, _) =>
        {
            var html = markup();
            Assert.Contains($"<h2>{title}</h2>", html);
            Assert.Contains(minimizeLabel, html);
            Assert.Contains(closeLabel, html);
            Assert.Contains(minimizeOption, Select(html, "window-minimize-behavior"));
            Assert.Contains(closeOption, Select(html, "window-close-behavior"));
            Assert.Contains(trayOption, Select(html, "window-minimize-behavior"));
            Assert.Contains(trayOption, Select(html, "window-close-behavior"));
            Assert.Contains(askOption, Select(html, "window-close-behavior"));
            Assert.Matches("<option[^>]*value=\"ask\"[^>]*disabled", Select(html, "window-close-behavior"));
            Assert.Contains(trackingHint, html);
            AssertSelected(html, "window-minimize-behavior", "minimize");
            AssertSelected(html, "window-close-behavior", "ask");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EachChoiceSavesIndependentlyDuringTrackingAndReopensWithTheSavedValues()
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.ToggleTrackingAsync();
        var sessionId = tracker.State.SessionId;
        var gameLanguage = tracker.Preferences.GameLanguage;
        await Render(tracker, async (component, markup, reopen) =>
        {
            await Change(component, "MinimizeBehaviorChanged", "tray");
            Assert.True(tracker.Preferences.MinimizeToTray);
            Assert.False(tracker.Preferences.CloseToTray);
            Assert.False(tracker.Preferences.CloseBehaviorConfigured);
            AssertSelected(markup(), "window-minimize-behavior", "tray");
            AssertSelected(markup(), "window-close-behavior", "ask");

            await Change(component, "CloseBehaviorChanged", "tray");
            Assert.True(tracker.Preferences.MinimizeToTray);
            Assert.True(tracker.Preferences.CloseToTray);
            Assert.True(tracker.Preferences.CloseBehaviorConfigured);
            var reopened = await reopen();
            AssertSelected(reopened, "window-minimize-behavior", "tray");
            AssertSelected(reopened, "window-close-behavior", "tray");
            Assert.DoesNotContain("value=\"ask\"", Select(reopened, "window-close-behavior"));

            await Change(component, "MinimizeBehaviorChanged", "minimize");
            Assert.False(tracker.Preferences.MinimizeToTray);
            Assert.True(tracker.Preferences.CloseToTray);
            AssertSelected(markup(), "window-minimize-behavior", "minimize");
            AssertSelected(markup(), "window-close-behavior", "tray");

            await Change(component, "CloseBehaviorChanged", "exit");
            Assert.False(tracker.Preferences.MinimizeToTray);
            Assert.False(tracker.Preferences.CloseToTray);
            Assert.True(tracker.Preferences.CloseBehaviorConfigured);
            AssertSelected(markup(), "window-close-behavior", "exit");
        });
        Assert.True(tracker.State.IsRunning);
        Assert.Equal(sessionId, tracker.State.SessionId);
        Assert.Equal(gameLanguage, tracker.Preferences.GameLanguage);
    }

    [Theory]
    [InlineData("exit", false)]
    [InlineData("tray", true)]
    public async Task ChoosingCloseBehaviorInSettingsConfiguresTheFirstCloseWithoutChangingMinimize(string choice, bool useTray)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await Render(tracker, async (component, markup, reopen) =>
        {
            Assert.False(tracker.Preferences.CloseBehaviorConfigured);
            await Change(component, "CloseBehaviorChanged", choice);

            Assert.True(tracker.Preferences.CloseBehaviorConfigured);
            Assert.Equal(useTray, tracker.Preferences.CloseToTray);
            Assert.False(tracker.Preferences.MinimizeToTray);
            AssertSelected(markup(), "window-close-behavior", choice);
            AssertSelected(await reopen(), "window-close-behavior", choice);
            Assert.DoesNotContain("value=\"ask\"", Select(markup(), "window-close-behavior"));
        });
    }

    [Theory]
    [InlineData("MinimizeBehaviorChanged", false)]
    [InlineData("MinimizeBehaviorChanged", true)]
    [InlineData("CloseBehaviorChanged", false)]
    [InlineData("CloseBehaviorChanged", true)]
    public async Task RejectedSaveKeepsThePreviouslySavedWindowChoicesVisible(string method, bool useTray)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with
        {
            MinimizeToTray = useTray, CloseToTray = useTray, CloseBehaviorConfigured = true,
        });
        await Render(tracker, async (component, markup, _) =>
        {
            // The existing preview validation path rejects the update without writing preferences.
            typeof(PreviewTrackerSession).GetProperty(nameof(PreviewTrackerSession.Preferences))!
                .SetValue(tracker, tracker.Preferences with { ThemeId = "invalid-theme" });

            var regularBehavior = method == "MinimizeBehaviorChanged" ? "minimize" : "exit";
            await Change(component, method, useTray ? regularBehavior : "tray");

            Assert.Equal(useTray, tracker.Preferences.MinimizeToTray);
            Assert.Equal(useTray, tracker.Preferences.CloseToTray);
            Assert.True(tracker.Preferences.CloseBehaviorConfigured);
            AssertSelected(markup(), "window-minimize-behavior", useTray ? "tray" : "minimize");
            AssertSelected(markup(), "window-close-behavior", useTray ? "tray" : "exit");
            Assert.Contains("role=\"alert\"", markup());
            Assert.Contains("Please select a valid theme from the list.", markup());
        });
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("tray")]
    public async Task RejectedInitialChoiceKeepsTheFirstClosePromptPending(string choice)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await Render(tracker, async (component, markup, _) =>
        {
            typeof(PreviewTrackerSession).GetProperty(nameof(PreviewTrackerSession.Preferences))!
                .SetValue(tracker, tracker.Preferences with { ThemeId = "invalid-theme" });

            await Change(component, "CloseBehaviorChanged", choice);

            Assert.False(tracker.Preferences.CloseBehaviorConfigured);
            Assert.False(tracker.Preferences.CloseToTray);
            AssertSelected(markup(), "window-close-behavior", "ask");
            Assert.Contains("role=\"alert\"", markup());
        });
    }

    [Fact]
    public async Task ChoiceSavedByNativePromptUpdatesAlreadyOpenSettings()
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await Render(tracker, async (_, markup, _) =>
        {
            AssertSelected(markup(), "window-close-behavior", "ask");
            await tracker.SavePreferencesAsync(tracker.Preferences with
            {
                CloseBehaviorConfigured = true, CloseToTray = true,
            });

            AssertSelected(markup(), "window-close-behavior", "tray");
            Assert.DoesNotContain("value=\"ask\"", Select(markup(), "window-close-behavior"));
            AssertSelected(markup(), "window-minimize-behavior", "minimize");
        });
    }

    [Theory]
    [InlineData("de", "Grindcrest öffnen", "Beenden")]
    [InlineData("en", "Open Grindcrest", "Exit")]
    public void NativeTrayMenuUsesTheSelectedInterfaceLanguage(string language, string open, string exit)
    {
        Assert.Equal(open, AppText.Translate("Grindcrest öffnen", language));
        Assert.Equal(exit, AppText.Translate("Beenden", language));
    }

    [Theory]
    [InlineData("Was soll beim Schließen passieren?", "What should happen when you close the window?")]
    [InlineData("Deine Auswahl wird gespeichert und gilt künftig für das X. Du kannst sie unter Einstellungen → Fenster & Tray ändern.", "Your choice will be saved for future clicks on X. You can change it under Settings → Window & Tray.")]
    [InlineData("Grindcrest vollständig schließen und die Session speichern.", "Close Grindcrest completely and save the session.")]
    [InlineData("Das Fenster verbergen und die Erfassung im Hintergrund weiterlaufen lassen.", "Hide the window and continue tracking in the background.")]
    [InlineData("Auswahl konnte nicht gespeichert werden", "Your choice could not be saved")]
    [InlineData("Grindcrest bleibt geöffnet. Bitte versuche es erneut.", "Grindcrest will stay open. Please try again.")]
    public void FirstClosePromptAndSaveFailureExplainTheBehaviorInBothLanguages(string german, string english)
    {
        Assert.Equal(german, AppText.Translate(german, "de"));
        Assert.Equal(english, AppText.Translate(german, "en"));
    }

    private static async Task Render(PreviewTrackerSession tracker,
        Func<TrackerPreferenceSettings, Func<string>, Func<Task<string>>, Task> test)
    {
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(TrackerPreferenceSettings.Section)] = "window" });
            var rendered = await renderer.RenderComponentAsync<TrackerPreferenceSettings>(parameters);
            var component = activator.Components.OfType<TrackerPreferenceSettings>().Single();
            await test(component, () => WebUtility.HtmlDecode(rendered.ToHtmlString()), async () =>
                WebUtility.HtmlDecode((await renderer.RenderComponentAsync<TrackerPreferenceSettings>(parameters)).ToHtmlString()));
        });
    }

    private static string Select(string markup, string id)
    {
        var match = Regex.Match(markup, $"<select[^>]*id=\"{id}\"[^>]*>.*?</select>", RegexOptions.Singleline);
        Assert.True(match.Success);
        return match.Value;
    }

    private static void AssertSelected(string markup, string id, string value) =>
        Assert.Contains($"value=\"{value}\"", Select(markup, id).Split('>')[0]);

    private static Task Change(TrackerPreferenceSettings component, string method, string value) =>
        ((IHandleEvent)component).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(() =>
            (Task)typeof(TrackerPreferenceSettings).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [value])!)), null);

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            Components.Add(component);
            return component;
        }
    }
}
