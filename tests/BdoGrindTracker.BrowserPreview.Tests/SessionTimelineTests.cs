using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Reflection;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class SessionTimelineTests
{
    [Fact]
    public async Task TheTimelineShowsRotationsRareDropsAndTheSilverCurveByDefault()
    {
        await using var tracker = new PreviewTrackerSession();
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var (collapsed, markup) = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<SessionTimeline>(ParameterView.Empty);
            var closed = WebUtility.HtmlDecode(rendered.ToHtmlString());
            Invoke(activator.Components.OfType<SessionTimeline>().Single(), "ToggleOpen");
            return (closed, WebUtility.HtmlDecode(rendered.ToHtmlString()));
        });

        // Closed, the section costs nothing but its summary.
        Assert.Contains("session-timeline", collapsed);
        Assert.DoesNotContain("session-timeline-track", collapsed);
        Assert.Contains("Show specific rotation", markup);
        // The default layers are on, the optional ones are off.
        foreach (var layer in SessionTimelineLayers.Default) Assert.Contains($"session-timeline-layer is-on", markup);
        Assert.Contains("session-timeline-silver", markup);
        Assert.DoesNotContain("session-timeline-trash", markup);
        // Session length, average and fastest rotation below the axis.
        Assert.Contains("Session", markup);
        Assert.Contains("session-timeline-summary", markup);
    }

    [Fact]
    public async Task SelectingARotationZoomsToItAndHighlightsIt()
    {
        await using var tracker = new PreviewTrackerSession();
        var observed = DateTimeOffset.UtcNow;
        Change(tracker, tracker.State with
        {
            Elapsed = TimeSpan.FromMinutes(60), ObservedAt = observed, SpotId = LootSpotCatalog.MagaiaId,
            Rotation = new RotationMonitorSnapshot
            {
                SpotId = LootSpotCatalog.MagaiaId, HasProfile = true, SupportsSpecialEvents = true, SessionSpecialEvents = 5,
                SessionRotations =
                [
                    new(1900, 30, StartedAt: observed.AddMinutes(-55), SpecialEventSeconds: [100, 900, 1500]),
                    new(1800, 30, StartedAt: observed.AddMinutes(-20), SpecialEventSeconds: [200, 1200]),
                ],
            },
        });
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<SessionTimeline>(ParameterView.Empty);
            var component = activator.Components.OfType<SessionTimeline>().Single();
            Invoke(component, "ToggleOpen");
            var spans = SessionRotationStats.Spans(tracker.State.Rotation, tracker.State.Elapsed, tracker.State.ObservedAt);
            Assert.Equal(2, spans.Count);
            // The faster rotation is the second one; it is marked as the fastest.
            Assert.Equal([false, true], spans.Select(span => span.IsFastest));

            Invoke(component, "Focus", spans[1]);
            var zoomed = WebUtility.HtmlDecode(rendered.ToHtmlString());
            Assert.Contains("is-selected", zoomed);
            Assert.True(Field<double>(component, "_zoom") > 1);

            Invoke(component, "ResetView");
            Assert.Equal(1, Field<double>(component, "_zoom"));
        });
    }

    [Fact]
    public async Task NearbyDropsOfDifferentItemsStandSideBySideAndTheSameItemIsCounted()
    {
        await using var tracker = new PreviewTrackerSession();
        Change(tracker, tracker.State with
        {
            Elapsed = TimeSpan.FromMinutes(60),
            DropHistory = [new(TimeSpan.FromSeconds(600), "Item A", 1), new(TimeSpan.FromSeconds(603), "Item A", 1),
                new(TimeSpan.FromSeconds(606), "Item B", 1), new(TimeSpan.FromSeconds(3000), "Item C", 1)],
        });
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        var markup = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<SessionTimeline>(ParameterView.Empty);
            var component = activator.Components.OfType<SessionTimeline>().Single();
            // Items chosen in the loot selector are marked like rare drops.
            foreach (var item in new[] { "Item A", "Item B", "Item C" }) Invoke(component, "ToggleItem", item, true);
            Invoke(component, "ToggleOpen");
            return WebUtility.HtmlDecode(rendered.ToHtmlString());
        });

        // Two rows: A twice and B next to it at ten minutes, C alone at fifty.
        Assert.Equal(2, Regex.Count(markup, "class=\"session-timeline-drops\""));
        Assert.Equal(3, Regex.Count(markup, "class=\"session-timeline-drop( is-stacked)? ?\""));
        Assert.Equal(1, Regex.Count(markup, "session-timeline-drop is-stacked"));
        Assert.Contains("session-timeline-drop-count\">2<", markup);
    }

    [Fact]
    public async Task EventsThatChangeNothingDoNotRebuildTheTimeline()
    {
        await using var tracker = new PreviewTrackerSession();
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<SessionTimeline>(ParameterView.Empty);
            var component = activator.Components.OfType<SessionTimeline>().Single();
            Invoke(component, "ToggleOpen");
            var worth = Field<decimal[]>(component, "_worth");
            Assert.NotEmpty(worth);

            // A pointer moving without a drag and a wheel without Shift skip the render they would cause.
            Invoke(component, "PointerMove", new PointerEventArgs { ClientX = 300 });
            Assert.False(ShouldRender(component));
            Assert.True(ShouldRender(component));
            Invoke(component, "Wheel", new WheelEventArgs { DeltaY = 100 });
            Assert.False(ShouldRender(component));
            Invoke(component, "Wheel", new WheelEventArgs { DeltaY = 100, ShiftKey = true });
            Assert.True(ShouldRender(component));

            // Zooming changes the window, not the worth of the drops: they are not valued again.
            Invoke(component, "Refresh");
            Assert.Same(worth, Field<decimal[]>(component, "_worth"));
        });
    }

    [Fact]
    public async Task EveryCoordinateIsWrittenInvariantlyUnderAGermanCulture()
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.DefaultThreadCurrentCulture);
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("de-DE");
        try
        {
            await using var tracker = new PreviewTrackerSession();
            var activator = new CapturingActivator();
            await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
                .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

            var markup = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var rendered = await renderer.RenderComponentAsync<SessionTimeline>(ParameterView.Empty);
                var component = activator.Components.OfType<SessionTimeline>().Single();
                Invoke(component, "ToggleOpen");
                Invoke(component, "Toggle", "trash", true);
                Invoke(component, "Toggle", "special", true);
                return WebUtility.HtmlDecode(rendered.ToHtmlString());
            });

            // A decimal comma would turn "12.5,60" into "12,5,60" and every path into a scribble.
            var points = Regex.Matches(markup, "points=\"([^\"]*)\"").Select(match => match.Groups[1].Value).ToArray();
            Assert.NotEmpty(points);
            foreach (var token in points.SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                Assert.Equal(1, token.Count(character => character == ','));
            foreach (var attribute in new[] { "x", "y", "width", "height", "x1", "x2", "y1", "y2", "cx", "cy" })
                Assert.DoesNotContain(",", Regex.Matches(markup, attribute + "=\"([^\"]*)\"")
                    .Select(match => match.Groups[1].Value).DefaultIfEmpty("").Aggregate((a, b) => a + b), StringComparison.Ordinal);
        }
        finally { (CultureInfo.CurrentCulture, CultureInfo.DefaultThreadCurrentCulture) = previous; }
    }

    [Fact]
    public async Task DraggingMovesTheViewByTheShareOfTheTrackThePointerCrossed()
    {
        await using var tracker = new PreviewTrackerSession();
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<SessionTimeline>(ParameterView.Empty);
            var component = activator.Components.OfType<SessionTimeline>().Single();
            Invoke(component, "ToggleOpen");
            Set(component, "_trackWidth", 800d);
            Set(component, "_zoom", 4d);
            Set(component, "_center", .5);

            Invoke(component, "PointerMove", new PointerEventArgs { ClientX = 700 });
            // Without a pressed pointer nothing moves.
            Assert.Equal(.5, Field<double>(component, "_center"));

            Set(component, "_dragFrom", (double?)700);
            Invoke(component, "PointerMove", new PointerEventArgs { ClientX = 300 });

            // Half the track at four times zoom moves the view by an eighth of the session.
            Assert.Equal(.625, Field<double>(component, "_center"), 6);
        });
    }

    private static void Change(PreviewTrackerSession tracker, TrackerState state) =>
        typeof(PreviewTrackerSession).GetMethod("Change", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tracker, [state, false]);

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

    private static void Invoke(SessionTimeline component, string method, params object[] arguments) =>
        typeof(SessionTimeline).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments);

    private static bool ShouldRender(SessionTimeline component) =>
        (bool)typeof(SessionTimeline).GetMethod("ShouldRender", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, [])!;

    private static T Field<T>(SessionTimeline component, string name) =>
        (T)typeof(SessionTimeline).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;

    private static void Set(SessionTimeline component, string name, object value) =>
        typeof(SessionTimeline).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
