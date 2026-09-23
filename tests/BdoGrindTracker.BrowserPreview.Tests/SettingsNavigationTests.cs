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
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class SettingsNavigationTests
{
    [Theory]
    [InlineData(null, "appearance")]
    [InlineData("unknown", "appearance")]
    [InlineData("appearance", "appearance")]
    [InlineData("capture", "capture")]
    [InlineData("silver", "silver")]
    [InlineData("diagnostics", "diagnostics")]
    [InlineData("updates", "updates")]
    public async Task CategoryShowsOnlyItsControlsAndKeepsOtherCategoriesReachable(string? section, string expected)
    {
        await Render(section, (_, _, _, navigation, markup) =>
        {
            var html = markup();
            var markers = new Dictionary<string, string>
            {
                ["appearance"] = "id=\"interface-language\"",
                ["capture"] = "Spielsprache in Black Desert",
                ["silver"] = "Dein effektiver Markterlös",
                ["diagnostics"] = "id=\"automatic-debug-logging\"",
                ["updates"] = "id=\"updates-heading\"",
            };
            foreach (var (category, marker) in markers)
            {
                Assert.Contains($"href=\"settings/{category}\"", html);
                Assert.Equal(category == expected, html.Contains(marker, StringComparison.Ordinal));
            }
            var captureContainer = Regex.Match(html,
                "<div(?<attributes>[^>]*)>\\s*<section class=\"panel settings-panel capture-configuration\"");
            Assert.True(captureContainer.Success);
            Assert.Equal(expected != "capture", Regex.IsMatch(captureContainer.Groups["attributes"].Value, @"\bhidden(?:\s|=|$)"));
            Assert.Null(navigation.LastPath);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("capture", "_autoPause", "12", 12)]
    [InlineData("silver", "_familyFame", "7200", 7200)]
    [InlineData("diagnostics", "_debugLogRetentionHours", "24", 24)]
    public async Task SwitchingCategoryCommitsTheEditedNumberBeforeNavigating(string section, string field, string value, int expected)
    {
        await Render(section, async (page, fields, tracker, navigation, _) =>
        {
            SetField(fields, field, value);

            await Invoke(page, "SelectSection", "appearance");

            Assert.Equal(expected, Number(tracker.Preferences, section));
            Assert.Equal("/settings/appearance", navigation.LastPath);
        });
    }

    [Theory]
    [InlineData("capture", "_autoPause", "0", "zwischen 1 und 60")]
    [InlineData("silver", "_familyFame", "-1", "ab 0")]
    [InlineData("diagnostics", "_debugLogRetentionHours", "169", "zwischen 1 und 168")]
    public async Task InvalidNumberKeepsTheCategoryOpenUntilItIsCorrected(string section, string field, string value, string error)
    {
        await Render(section, async (page, fields, tracker, navigation, markup) =>
        {
            var original = Number(tracker.Preferences, section);
            SetField(fields, field, value);

            await Invoke(page, "SelectSection", "appearance");

            Assert.Null(navigation.LastPath);
            Assert.Equal(original, Number(tracker.Preferences, section));
            Assert.Contains("role=\"alert\"", markup());
            Assert.Contains(error, markup());

            SetField(fields, field, "8");
            await Invoke(page, "SelectSection", "appearance");

            Assert.Equal(8, Number(tracker.Preferences, section));
            Assert.Equal("/settings/appearance", navigation.LastPath);
        });
    }

    private static int Number(TrackerPreferences preferences, string section) => section switch
    {
        "capture" => preferences.AutoPauseMinutes,
        "silver" => preferences.FamilyFame,
        "diagnostics" => preferences.DebugLogRetentionHours,
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    private static async Task Render(string? section,
        Func<TrackerSettings, TrackerPreferenceSettings, PreviewTrackerSession, TestNavigation, Func<string>, Task> test)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        var activator = new CapturingActivator();
        var navigation = new TestNavigation();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IComponentActivator>(activator).AddSingleton<NavigationManager>(navigation)
            .AddSingleton<IAppUpdates>(new DisabledAppUpdates("test", "Vorschau"))
            .AddSingleton<IJSRuntime, NoJavaScript>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<TrackerSettings>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(TrackerSettings.Section)] = section }));
            var page = activator.Components.OfType<TrackerSettings>().Single();
            var fields = activator.Components.OfType<TrackerPreferenceSettings>().Single();
            await test(page, fields, tracker, navigation, () => WebUtility.HtmlDecode(rendered.ToHtmlString()));
        });
    }

    private static void SetField(object component, string name, object value) =>
        component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static Task Invoke(ComponentBase component, string name, params object?[] arguments) =>
        ((IHandleEvent)component).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(async () =>
        {
            var result = component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments);
            if (result is Task task) await task;
        })), null);

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
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/settings");
        protected override void NavigateToCore(string uri, bool forceLoad) => LastPath = ToAbsoluteUri(uri).AbsolutePath;
        protected override void NavigateToCore(string uri, NavigationOptions options) => NavigateToCore(uri, options.ForceLoad);
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
}
