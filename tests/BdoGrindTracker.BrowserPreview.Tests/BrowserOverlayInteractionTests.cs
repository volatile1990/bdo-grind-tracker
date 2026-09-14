using System.Net;
using System.Reflection;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Theming;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class BrowserOverlayInteractionTests
{
    [Fact]
    public async Task NewSessionButtonAsksBeforeChangingTheSession()
    {
        await using var tracker = new PreviewTrackerSession();
        using var overlay = new OverlayService(tracker);
        await overlay.SaveAsync(overlay.Settings with { Widgets = [OverlayCatalog.CreateWidget("controls")] });
        var js = new ConfirmationJavaScript();
        var activator = new CapturingActivator();
        await using var provider = Services(overlay, js, activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<BrowserOverlayPreview>(ParameterView.Empty);
            var controls = activator.Components.OfType<OverlayWidgetPreview>().Single();
            Assert.True(controls.OnNewSession.HasDelegate);
            var session = tracker.State.SessionId;
            var totals = tracker.State.Loot.Totals;

            await controls.OnNewSession.InvokeAsync();
            Assert.Equal(session, tracker.State.SessionId);
            Assert.Equal(totals, tracker.State.Loot.Totals);
            Assert.Equal("Neue Session beginnen? Die bisherige Session wird abgeschlossen.", Assert.Single(js.Confirmations));

            js.Confirmed = true;
            await controls.OnNewSession.InvokeAsync();
            Assert.NotEqual(session, tracker.State.SessionId);
            Assert.Empty(tracker.State.Loot.Totals);
            Assert.Equal(2, js.Confirmations.Count);
        });
    }

    [Fact]
    public async Task RotationOnlyPreviewAnimatesItsExampleWithoutChangingSessionData()
    {
        await using var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { ThemeId = AppThemes.Cats });
        using var overlay = new OverlayService(tracker);
        await overlay.SaveAsync(overlay.Settings with { Widgets = [OverlayCatalog.CreateWidget("rotation-monitor")] });
        await overlay.SetPreviewAsync(true);
        var state = tracker.State;
        var history = tracker.History;
        var snapshot = overlay.Snapshot;
        var activator = new CapturingActivator();
        await using var provider = Services(overlay, new ConfirmationJavaScript(), activator);
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<BrowserOverlayPreview>(ParameterView.Empty);
            var component = activator.Components.OfType<BrowserOverlayPreview>().Single();
            var widget = activator.Components.OfType<OverlayWidgetPreview>().Single();
            Assert.Contains("Rotationsbeispiel", WebUtility.HtmlDecode(rendered.ToHtmlString()));
            Assert.Contains("data-phase=\"drakania\"", rendered.ToHtmlString());
            Assert.Equal(LootSpotCatalog.HermesiaId, widget.Snapshot.Rotation.SpotId);
            Assert.Equal(350, widget.Snapshot.Rotation.Elapsed);
            Assert.Equal(AppThemes.Cats, widget.Snapshot.ThemeId);

            component.AdvanceRotationExample();
            Assert.Equal(351, widget.Snapshot.Rotation.Elapsed);
            Assert.Same(snapshot, overlay.Snapshot);
            Assert.Same(state, tracker.State);
            Assert.Equal(history, tracker.History);

            typeof(BrowserOverlayPreview).GetField("_rotationExample", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(component, false);
            await overlay.SetPreviewAsync(false);
            Assert.Same(overlay.Snapshot, widget.Snapshot);
            Assert.False(widget.Snapshot.Rotation.HasProfile);
            Assert.DoesNotContain("rotation-playhead\"", rendered.ToHtmlString());

            await overlay.SaveAsync(overlay.Settings with { Widgets = [OverlayCatalog.CreateWidget("duration")] });
            Assert.DoesNotContain("Rotationsbeispiel", WebUtility.HtmlDecode(rendered.ToHtmlString()));
        });
    }

    private static ServiceProvider Services(IOverlayService overlay, IJSRuntime js, CapturingActivator activator) =>
        new ServiceCollection().AddLogging().AddSingleton(overlay).AddSingleton(js)
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();

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

    private sealed class ConfirmationJavaScript : IJSRuntime
    {
        internal bool Confirmed { get; set; }
        internal List<string> Confirmations { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier != "confirm") return ValueTask.FromResult(default(TValue)!);
            Confirmations.Add((string)args![0]!);
            return ValueTask.FromResult((TValue)(object)Confirmed);
        }
    }
}
