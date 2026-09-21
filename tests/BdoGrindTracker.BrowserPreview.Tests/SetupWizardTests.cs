using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class SetupWizardTests
{
    [Fact]
    public void SetupLabelsAndInstructionsHaveEnglishTranslations()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "BdoGrindTracker.App", "Components", "SetupWizard.razor")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName, "src", "BdoGrindTracker.App", "Components", "SetupWizard.razor"));
        var directKeys = Regex.Matches(source, "\\b(?:T|F)\\(\"([^\"]+)\"").Select(match => match.Groups[1].Value);
        var stepKeys = new[] { "Steps", "Titles" }.SelectMany(name =>
            (string[])typeof(SetupWizard).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
        foreach (var key in directKeys.Concat(stepKeys).Distinct())
            Assert.True(AppText.English.ContainsKey(key), $"Missing English setup translation: {key}");
    }

    [Theory]
    [InlineData(false, "/", "/setup")]
    [InlineData(true, "/", null)]
    [InlineData(false, "/setup", null)]
    public async Task FirstRunOpensSetupOnlyWhenNeeded(bool completed, string initialPath, string? expectedPath)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { SetupCompleted = completed });
        var navigation = new TestNavigation(initialPath);
        var app = new TrackerApp();
        typeof(TrackerComponentBase).GetProperty("Tracker", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, tracker);
        typeof(TrackerApp).GetProperty("Navigation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, navigation);
        typeof(TrackerApp).GetProperty("JS", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, new NoJavaScript());
        var afterRender = typeof(TrackerApp).GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)afterRender.Invoke(app, [true])!;

        Assert.Equal(expectedPath, navigation.LastPath);
        var navigationCount = navigation.NavigationCount;
        await (Task)afterRender.Invoke(app, [false])!;
        Assert.Equal(navigationCount, navigation.NavigationCount);
        Assert.Equal(completed, tracker.Preferences.SetupCompleted);
        Assert.False(tracker.State.HasSession);
    }

    [Fact]
    public async Task CompletionKeepsChosenSettingsWithoutStartingTrackingOrEnablingUploads()
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { SetupCompleted = false });
        var sessionId = tracker.State.SessionId;
        var activator = new CapturingActivator();
        var navigation = new TestNavigation();
        await using var provider = Services(tracker, activator, navigation);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<SetupWizard>(ParameterView.Empty);
            var wizard = activator.Components.OfType<SetupWizard>().Single();
            Assert.Equal("en", tracker.Preferences.UiLanguage);
            Assert.Contains("id=\"interface-language\"", rendered.ToHtmlString());
            await Invoke(Fields(wizard), "ThemeChanged", AppThemes.Light);
            await Invoke(wizard, "Next");
            Assert.False(tracker.Preferences.SetupCompleted);

            await Invoke(wizard, "Next");
            Assert.Contains("capture-configuration", rendered.ToHtmlString());
            SetField(Fields(wizard), "_autoPause", "12");
            SetField(Fields(wizard), "_gameLanguage", "de");
            await Invoke(Fields(wizard), "GameLanguageChanged");
            await Invoke(wizard, "Next");
            Assert.Equal(12, tracker.Preferences.AutoPauseMinutes);

            SetField(Fields(wizard), "_familyFame", "7200");
            SetField(Fields(wizard), "_region", "na");
            await Invoke(Fields(wizard), "RegionChanged");
            await Invoke(wizard, "Next");
            Assert.False(tracker.Preferences.SetupCompleted);
            Assert.Equal(7200, tracker.Preferences.FamilyFame);

            // Returning to a previous step must restore the saved settings.
            await Invoke(wizard, "Back");
            Assert.Contains("value=\"7200\"", rendered.ToHtmlString());
            await Invoke(wizard, "Next");
            await Invoke(wizard, "Complete");

            Assert.True(tracker.Preferences.SetupCompleted);
            Assert.Equal("/", navigation.LastPath);
        });

        Assert.Equal(AppThemes.Light, tracker.Preferences.ThemeId);
        Assert.Equal("na", tracker.Preferences.MarketRegion);
        Assert.Equal("de", tracker.Preferences.GameLanguage);
        Assert.False(tracker.Preferences.AutoUpload);
        Assert.False(tracker.Preferences.RecordLoot);
        Assert.False(tracker.State.HasApiKey);
        Assert.False(tracker.State.HasSession);
        Assert.False(tracker.State.IsRunning);
        Assert.Equal(sessionId, tracker.State.SessionId);
        Assert.Empty(tracker.History);
    }

    [Theory]
    [InlineData(2, "_autoPause", "", "7")]
    [InlineData(2, "_autoPause", "0", "7")]
    [InlineData(2, "_autoPause", "61", "7")]
    [InlineData(2, "_autoPause", "1.5", "7")]
    [InlineData(3, "_familyFame", "-1", "7000")]
    [InlineData(3, "_familyFame", "2147483648", "7000")]
    public async Task InvalidNumbersKeepTheCurrentStepUntilCorrected(int step, string field, string invalid, string valid)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { SetupCompleted = false });
        var activator = new CapturingActivator();
        var navigation = new TestNavigation();
        await using var provider = Services(tracker, activator, navigation);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<SetupWizard>(ParameterView.Empty);
            var wizard = activator.Components.OfType<SetupWizard>().Single();
            for (var i = 0; i < step; i++) await Invoke(wizard, "Next");
            var previous = tracker.Preferences;
            SetField(Fields(wizard), field, invalid);

            await Invoke(wizard, "Next");

            Assert.Equal(step, GetField<int>(wizard, "_step"));
            Assert.Equal(previous.AutoPauseMinutes, tracker.Preferences.AutoPauseMinutes);
            Assert.Equal(previous.FamilyFame, tracker.Preferences.FamilyFame);
            Assert.False(tracker.Preferences.SetupCompleted);
            Assert.Contains("role=\"alert\"", rendered.ToHtmlString());
            await Invoke(wizard, "Leave");
            Assert.Null(navigation.LastPath);
            Assert.Equal(step, GetField<int>(wizard, "_step"));

            SetField(Fields(wizard), field, valid);
            await Invoke(wizard, "Next");

            Assert.Equal(step + 1, GetField<int>(wizard, "_step"));
            Assert.Equal(int.Parse(valid), field == "_autoPause"
                ? tracker.Preferences.AutoPauseMinutes : tracker.Preferences.FamilyFame);
        });
    }

    [Fact]
    public async Task ResumeLaterSavesPendingNumbersAndKeepsSetupIncomplete()
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { SetupCompleted = false });
        var activator = new CapturingActivator();
        var navigation = new TestNavigation();
        await using var provider = Services(tracker, activator, navigation);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<SetupWizard>(ParameterView.Empty);
            var wizard = activator.Components.OfType<SetupWizard>().Single();
            await Invoke(wizard, "Next");
            await Invoke(wizard, "Next");
            SetField(Fields(wizard), "_autoPause", "9");

            await Invoke(wizard, "Leave");

            Assert.Equal("/", navigation.LastPath);
            Assert.Equal(9, tracker.Preferences.AutoPauseMinutes);
            Assert.False(tracker.Preferences.SetupCompleted);
            Assert.False(tracker.State.HasSession);
        });
    }

    [Fact]
    public async Task LanguageSwitchTranslatesDropLogGuidanceWithoutChangingGameLanguage()
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { SetupCompleted = false, GameLanguage = "de" });
        var activator = new CapturingActivator();
        await using var provider = Services(tracker, activator, new TestNavigation());
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<SetupWizard>(ParameterView.Empty);
            var wizard = activator.Components.OfType<SetupWizard>().Single();
            await Invoke(Fields(wizard), "UiLanguageChanged", "de");
            await Invoke(wizard, "Next");
            var german = WebUtility.HtmlDecode(rendered.ToHtmlString());
            Assert.Contains("Droplog", german);
            Assert.Contains("verdeckt", german);
            Assert.Contains("Overlay", german);

            await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "en" });
            var english = WebUtility.HtmlDecode(rendered.ToHtmlString());
            Assert.DoesNotContain("verdeckt", english);
            Assert.Contains("Keep the drop log unobstructed.", english);
            Assert.Contains("overlays", english, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("de", tracker.Preferences.GameLanguage);
            Assert.False(tracker.Preferences.SetupCompleted);
        });
    }

    private static TrackerPreferenceSettings Fields(SetupWizard wizard) => GetField<TrackerPreferenceSettings>(wizard, "_fields");
    private static T GetField<T>(object component, string name) =>
        (T)component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
    private static void SetField(object component, string name, object value) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    // Use the same event boundary as a click so HtmlRenderer also sees the resulting render.
    private static Task Invoke(ComponentBase component, string name, params object?[] args) =>
        ((IHandleEvent)component).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(async () =>
        {
            var result = component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            if (result is Task task) await task;
        })), null);

    private static ServiceProvider Services(PreviewTrackerSession tracker, CapturingActivator activator, TestNavigation navigation) =>
        new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IComponentActivator>(activator).AddSingleton<NavigationManager>(navigation)
            .AddSingleton<IJSRuntime, NoJavaScript>().BuildServiceProvider();

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

    private sealed class TestNavigation : NavigationManager
    {
        public string? LastPath { get; private set; }
        public int NavigationCount { get; private set; }
        public TestNavigation(string path = "/setup") => Initialize("http://localhost/", "http://localhost" + path);
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            LastPath = ToAbsoluteUri(uri).AbsolutePath;
            NavigationCount++;
        }
        protected override void NavigateToCore(string uri, NavigationOptions options) => NavigateToCore(uri, options.ForceLoad);
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
}
