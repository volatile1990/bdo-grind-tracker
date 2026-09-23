using System.Net;
using System.Reflection;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class ModulePreviewTests
{
    [Fact]
    public async Task HoveringAModuleShowsItInItsDefaultSizeWithSampleData()
    {
        await using var tracker = new PreviewTrackerSession();
        using var overlay = new OverlayService(tracker);
        var activator = new CapturingActivator();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IOverlayService>(overlay).AddSingleton<IJSRuntime, NoJavaScript>()
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var editor = await renderer.RenderComponentAsync<OverlayEditor>(ParameterView.Empty);
            var component = activator.Components.OfType<OverlayEditor>().Single();
            Assert.DoesNotContain("oe-module-preview", editor.ToHtmlString());

            Show(component, "rotation-monitor");
            var markup = WebUtility.HtmlDecode(editor.ToHtmlString());
            var module = OverlayCatalog.Find("rotation-monitor")!;
            Assert.Contains("oe-module-preview", markup);
            Assert.Contains($"width:{module.Width}px;height:{module.Height}px", markup);
            Assert.Contains("Rotation Monitor · ", markup);
            // The example session fills it, even though the canvas shows live data.
            var preview = activator.Components.OfType<OverlayWidgetPreview>().Last();
            Assert.Equal("rotation-monitor", preview.Widget.Kind);
            Assert.Equal(LootSpotCatalog.MagaiaId, preview.Snapshot.Rotation.SpotId);
            Assert.NotEmpty(preview.Snapshot.Rotation.Events);

            Show(component, null);
            Assert.DoesNotContain("oe-module-preview", editor.ToHtmlString());
        });
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

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    private static void Show(OverlayEditor editor, string? kind) =>
        typeof(OverlayEditor).GetMethod(kind is null ? "HideModulePreview" : "ShowModulePreview",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, kind is null ? [] : [kind]);
}
