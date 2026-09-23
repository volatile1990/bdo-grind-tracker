using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class GrindGoalPreviewTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public GrindGoalPreviewTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task SavingAndRemovingTheCalendarGoalUpdatesTheSameBrowserOverlayOnly()
    {
        await using var first = _factory.Services.CreateAsyncScope();
        await using var second = _factory.Services.CreateAsyncScope();
        var tracker = first.ServiceProvider.GetRequiredService<ITrackerSession>();
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        var goals = first.ServiceProvider.GetRequiredService<GrindGoalStore>();
        var overlay = first.ServiceProvider.GetRequiredService<IOverlayService>();
        var otherOverlay = second.ServiceProvider.GetRequiredService<IOverlayService>();
        await overlay.SaveAsync(overlay.Settings with { Widgets = [OverlayCatalog.CreateWidget("daily-goal")] });
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging()
            .AddSingleton(tracker).AddSingleton(goals).AddSingleton(overlay)
            .AddSingleton<IComponentActivator>(activator).AddSingleton<IJSRuntime, NoJavaScript>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var calendar = await renderer.RenderComponentAsync<GrindGoals>(ParameterView.Empty);
            var preview = await renderer.RenderComponentAsync<BrowserOverlayPreview>(ParameterView.Empty);
            var component = activator.Components.OfType<GrindGoals>().Single();
            Assert.Contains("Monatsübersicht", WebUtility.HtmlDecode(calendar.ToHtmlString()));
            Assert.Contains("Kein Tagesziel", preview.ToHtmlString());

            typeof(GrindGoals).GetField("_millions", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(component, 1250m);
            Invoke(component, "Save");

            var today = DateOnly.FromDateTime(DateTime.Today);
            Assert.Equal(1_250_000_000m, goals.Goals[today]);
            Assert.Equal(1_250_000_000m, overlay.Snapshot.DailyGoal.Target);
            Assert.Contains("aria-label=\"Fortschritt Tagesziel\"", preview.ToHtmlString());
            Assert.DoesNotContain("Kein Tagesziel", preview.ToHtmlString());
            Assert.Empty(second.ServiceProvider.GetRequiredService<GrindGoalStore>().Goals);
            Assert.Null(otherOverlay.Snapshot.DailyGoal.Target);

            // Navigating back to the calendar keeps the circuit's in-memory goal.
            var reopened = await renderer.RenderComponentAsync<GrindGoals>(ParameterView.Empty);
            Assert.Contains("value=\"1250\"", reopened.ToHtmlString());
            Invoke(component, "Remove");
            Assert.Empty(goals.Goals);
            Assert.Null(overlay.Snapshot.DailyGoal.Target);
            Assert.Contains("Kein Tagesziel", preview.ToHtmlString());
        });
    }

    [Theory]
    [InlineData(null, "calendar")]
    [InlineData("unknown", "calendar")]
    [InlineData("calendar", "calendar")]
    [InlineData("summary", "summary")]
    public async Task MenuQueryShowsItsSectionAndKeepsBothSectionsReachable(string? section, string expected)
    {
        await Render(section, (_, _, markup) =>
        {
            var html = markup();
            Assert.Contains("href=\"grind-goals\"", html);
            Assert.Contains("href=\"grind-goals?section=summary\"", html);
            var planner = Regex.Match(html, "<section class=\"menu-panel gg-planner\"(?<attributes>[^>]*)>");
            var summary = Regex.Match(html, "<div class=\"gg-summary-view\"(?<attributes>[^>]*)>");
            Assert.True(planner.Success);
            Assert.True(summary.Success);
            Assert.Equal(expected != "calendar", Regex.IsMatch(planner.Groups["attributes"].Value, @"\bhidden(?:\s|=|$)"));
            Assert.Equal(expected != "summary", Regex.IsMatch(summary.Groups["attributes"].Value, @"\bhidden(?:\s|=|$)"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task SwitchingSectionsRetainsSelectedDateAndUnsavedGoal()
    {
        await Render(null, async (page, goals, markup) =>
        {
            var date = new DateOnly(2026, 2, 18);
            Invoke(page, "ChangeDay", date);
            SetField(page, "_millions", 1725m);

            typeof(GrindGoals).GetProperty(nameof(GrindGoals.Section))!.SetValue(page, "summary");
            await page.SetParametersAsync(ParameterView.Empty);
            Assert.Contains("value=\"2026-02-18\"", markup());
            Assert.Empty(goals.Goals);

            typeof(GrindGoals).GetProperty(nameof(GrindGoals.Section))!.SetValue(page, null);
            await page.SetParametersAsync(ParameterView.Empty);
            Assert.Contains("value=\"1725\"", markup());
            Invoke(page, "Save");
            Assert.Equal(1_725_000_000m, goals.Goals[date]);
            Assert.Single(goals.Goals);
        });
    }

    [Fact]
    public async Task SummaryDayNavigationLoadsTheNewDaysGoalAndMatchingMonth()
    {
        await Render("summary", (page, goals, _) =>
        {
            var date = new DateOnly(2026, 2, 1);
            goals.Set([date], 1_250_000_000m);
            Invoke(page, "ChangeDay", date.AddDays(-1));
            Invoke(page, "MoveDay", 1);
            Assert.Equal(date, Field<DateOnly>(page, "_day"));
            Assert.Equal(date, Field<DateOnly>(page, "_month"));
            Assert.Equal(1250m, Field<decimal>(page, "_millions"));
            return Task.CompletedTask;
        });
    }

    private static async Task Render(string? section, Func<GrindGoals, GrindGoalStore, Func<string>, Task> test)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        var goals = new GrindGoalStore(null);
        var activator = new CapturingActivator(section);
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton(goals).AddSingleton<IComponentActivator>(activator)
            .AddSingleton<IJSRuntime, NoJavaScript>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<GrindGoals>(ParameterView.Empty);
            var page = activator.Components.OfType<GrindGoals>().Single();
            await test(page, goals, () => WebUtility.HtmlDecode(rendered.ToHtmlString()));
        });
    }

    private static void Invoke(GrindGoals component, string method, params object[] args) =>
        typeof(GrindGoals).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);

    private static void SetField(GrindGoals component, string name, object value) =>
        typeof(GrindGoals).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static T Field<T>(GrindGoals component, string name) =>
        (T)typeof(GrindGoals).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;

    private sealed class CapturingActivator(string? section = null) : IComponentActivator
    {
        internal List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is GrindGoals)
                componentType.GetProperty(nameof(GrindGoals.Section))!.SetValue(component, section);
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
