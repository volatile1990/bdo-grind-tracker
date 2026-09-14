using System.Net;
using System.Reflection;
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

    private static void Invoke(GrindGoals component, string method) =>
        typeof(GrindGoals).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);

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
