using System.Globalization;
using System.Net;
using System.Reflection;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class SecondaryLocalizationTests
{
    [Fact]
    public void PartialObservationsKeepTheirMeaningAndUseTheRequestedLanguageIndependently()
    {
        var englishAgris = new AgrisPresentation(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(55), TimeSpan.FromMinutes(1), "en");
        var germanAgris = new AgrisPresentation(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(55), TimeSpan.FromMinutes(1), "de");
        Assert.Equal("≈ under 1 min *", englishAgris.Duration);
        Assert.Contains("Not detected: under 1 min", englishAgris.Description);
        Assert.Equal("≈ unter 1 Min. *", germanAgris.Duration);

        var english = new ExperiencePresentation(-.123m, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
            61, 62, 62, .579m, "en");
        var german = new ExperiencePresentation(-.123m, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
            61, 62, 62, .579m, "de");
        Assert.Equal("≈ -0.123 %", english.Gain);
        Assert.Equal("≈ -0.246 %", english.Hourly);
        Assert.True(english.IsLoss);
        Assert.Contains("Partially tracked: 15 min of 30 min", english.Description);
        Assert.Contains("Current: Lvl. 62 · 0.579 %.", english.Description);
        Assert.Equal("≈ -0,123 %", german.Gain);
        Assert.Contains("Teilweise erfasst: 15 Min. von 30 Min.", german.Description);
        Assert.Equal("Experience not tracked.", new ExperiencePresentation(null, null, TimeSpan.FromHours(1), language: "en").Description);
    }

    [Fact]
    public void RelativeDatesUseTheSelectedLanguageWithoutChangingProcessCulture()
    {
        var original = CultureInfo.CurrentCulture;
        var now = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", HistoryPresentation.FormatTimeAgo(now.AddSeconds(-10), now, "en"));
        Assert.Equal("5 min ago", HistoryPresentation.FormatTimeAgo(now.AddMinutes(-5), now, "en"));
        Assert.Equal("2 hr ago", HistoryPresentation.FormatTimeAgo(now.AddHours(-2), now, "en"));
        Assert.Equal("yesterday", HistoryPresentation.FormatTimeAgo(now.AddHours(-25), now, "en"));
        Assert.Equal("5 days ago", HistoryPresentation.FormatTimeAgo(now.AddDays(-5), now, "en"));
        Assert.Equal("vor 5 Tagen", HistoryPresentation.FormatTimeAgo(now.AddDays(-5), now, "de"));
        Assert.Same(original, CultureInfo.CurrentCulture);
    }

    [Fact]
    public async Task CalendarLanguageChangesPreserveNumericInputAndTranslateExistingFeedback()
    {
        await using var tracker = new PreviewTrackerSession();
        var goals = new GrindGoalStore(null);
        var activator = new CapturingActivator();
        await using var provider = Services(tracker, goals, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var calendar = await renderer.RenderComponentAsync<GrindGoals>(ParameterView.Empty);
            var component = activator.Components.OfType<GrindGoals>().Single();
            var amount = typeof(GrindGoals).GetField("_millions", BindingFlags.Instance | BindingFlags.NonPublic)!;
            amount.SetValue(component, 1250.5m);
            typeof(GrindGoals).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);

            await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "en" });
            var english = WebUtility.HtmlDecode(calendar.ToHtmlString());
            Assert.Contains("value=\"1250.5\"", english);
            Assert.Contains("Daily goal saved.", english);
            Assert.Contains("<span>Mon</span>", english);
            Assert.Contains("<span>Sun</span>", english);
            Assert.Contains("Previous month", english);
            Assert.Equal(1_250_500_000m, goals.Goals[DateOnly.FromDateTime(DateTime.Today)]);

            await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
            var german = WebUtility.HtmlDecode(calendar.ToHtmlString());
            Assert.Contains("value=\"1250.5\"", german);
            Assert.Contains("Tagesziel gespeichert.", german);
            Assert.Contains("<span>Mo</span>", german);
            Assert.Contains("Vorheriger Monat", german);
            Assert.Equal(1250.5m, amount.GetValue(component));
        });
    }

    [Fact]
    public async Task GarmothConfirmationActionsAndAccessibleNamesSwitchTogether()
    {
        await using var tracker = new PreviewTrackerSession();
        var activator = new CapturingActivator();
        await using var provider = Services(tracker, new GrindGoalStore(null), activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var dashboard = await renderer.RenderComponentAsync<GarmothDashboard>(ParameterView.Empty);
            var component = activator.Components.OfType<GarmothDashboard>().Single();
            await (Task)typeof(GarmothDashboard).GetMethod("AutoUploadChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [new ChangeEventArgs { Value = true }])!;
            await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "en" });
            var english = WebUtility.HtmlDecode(dashboard.ToHtmlString());
            Assert.Contains("Upload saved session?", english);
            Assert.Contains("Upload all?", english);
            Assert.Contains("Upload 0 sessions", english);
            Assert.Contains("Upload now", english);
            Assert.Contains("aria-label=\"Close upload dialog\"", english);
            Assert.Contains("aria-label=\"Close batch upload dialog\"", english);
            Assert.Contains("Add an API key first.", english);
            Assert.DoesNotContain("Jetzt hochladen", english);

            await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
            Assert.Contains("Jetzt hochladen", dashboard.ToHtmlString());
            Assert.Contains("Hinterlege zuerst einen API-Schl", WebUtility.HtmlDecode(dashboard.ToHtmlString()));
        });
    }

    private static ServiceProvider Services(PreviewTrackerSession tracker, GrindGoalStore goals, CapturingActivator activator) =>
        new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker).AddSingleton(goals)
            .AddSingleton<IComponentActivator>(activator).AddSingleton<IJSRuntime, NoJavaScript>().BuildServiceProvider();

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

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
}
