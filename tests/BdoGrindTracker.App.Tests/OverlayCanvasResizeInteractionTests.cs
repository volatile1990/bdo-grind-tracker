using System.Net;
using System.Reflection;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayCanvasResizeInteractionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task CanvasDimensionFieldScalesEveryModuleAlongTheEditedAxis(bool width) => Render(async (editor, overlay, markup) =>
    {
        var ids = overlay.Settings.Widgets.Select(widget => widget.Id).ToArray();
        await Invoke(editor, "CanvasDimension", new ChangeEventArgs { Value = width ? "800" : "600" }, width);

        Assert.Equal(width ? 800 : 400, overlay.Settings.Width);
        Assert.Equal(width ? 300 : 600, overlay.Settings.Height);
        Assert.Equal(ids, overlay.Settings.Widgets.Select(widget => widget.Id));
        AssertGeometry(overlay.Settings.Widgets[0], width ? (40, 24, 240, 64) : (20, 48, 120, 128));
        AssertGeometry(overlay.Settings.Widgets[1], width ? (360, 112, 400, 160) : (180, 224, 200, 320));
        Assert.Contains(width ? "data-width=\"800\"" : "data-height=\"600\"", markup());
    });

    [Fact]
    public Task CanvasDragCommitScalesEveryModuleInBothDimensions() => Render(async (editor, overlay, markup) =>
    {
        var ids = overlay.Settings.Widgets.Select(widget => widget.Id).ToArray();
        await editor.CommitCanvasSize(800, 600);

        Assert.Equal(800, overlay.Settings.Width);
        Assert.Equal(600, overlay.Settings.Height);
        Assert.Equal(ids, overlay.Settings.Widgets.Select(widget => widget.Id));
        AssertGeometry(overlay.Settings.Widgets[0], (40, 48, 240, 128));
        AssertGeometry(overlay.Settings.Widgets[1], (360, 224, 400, 320));
        Assert.Contains("data-x=\"360\"", markup());
        Assert.Contains("data-y=\"224\"", markup());
    });

    [Theory]
    [InlineData(true, "159")]
    [InlineData(true, "1601")]
    [InlineData(false, "63")]
    [InlineData(false, "1201")]
    [InlineData(true, "NaN")]
    [InlineData(false, "Infinity")]
    [InlineData(true, "kein Wert")]
    public Task InvalidCanvasFieldDoesNotSaveOrChangeModules(bool width, string value) => Render(async (editor, overlay, markup) =>
    {
        var before = overlay.Settings;
        var changes = 0;
        overlay.Changed += () => changes++;
        await Invoke(editor, "CanvasDimension", new ChangeEventArgs { Value = value }, width);

        Assert.Same(before, overlay.Settings);
        Assert.Equal(0, changes);
        Assert.Contains("Die Änderung wurde nicht gespeichert.", markup());
    });

    [Theory]
    [InlineData(double.NaN, 600)]
    [InlineData(800, double.NaN)]
    [InlineData(double.PositiveInfinity, 600)]
    [InlineData(800, double.NegativeInfinity)]
    public Task NonFiniteCanvasDragCommitDoesNotSaveOrChangeModules(double width, double height) => Render(async (editor, overlay, markup) =>
    {
        var before = overlay.Settings;
        var changes = 0;
        overlay.Changed += () => changes++;
        await editor.CommitCanvasSize(width, height);

        Assert.Same(before, overlay.Settings);
        Assert.Equal(0, changes);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ResizingOneWidgetLeavesTheOtherWidgetsAndCanvasAlone(bool drag) => Render(async (editor, overlay, markup) =>
    {
        var before = overlay.Settings;
        var selected = before.Widgets[0];
        await editor.SelectWidget(selected.Id);
        if (drag) await editor.CommitWidgetGeometry(selected.Id, 28, 36, 168, 96);
        else
        {
            await Invoke(editor, "WidgetNumber", new ChangeEventArgs { Value = "168" }, "width");
            await Invoke(editor, "WidgetNumber", new ChangeEventArgs { Value = "96" }, "height");
        }

        Assert.Equal(before.Width, overlay.Settings.Width);
        Assert.Equal(before.Height, overlay.Settings.Height);
        Assert.Equal(before.Widgets[1], overlay.Settings.Widgets[1]);
        AssertGeometry(overlay.Settings.Widgets[0], drag ? (28, 36, 168, 96) : (20, 24, 168, 96));
        Assert.Equal(selected.Width, overlay.Settings.Widgets[0].ContentWidth);
        Assert.Equal(selected.Height, overlay.Settings.Widgets[0].ContentHeight);
    });

    [Fact]
    public Task AddingAModuleCanExtendTheCanvasWithoutScalingExistingModules() => Render(async (editor, overlay, markup) =>
    {
        var existing = Assert.Single(overlay.Settings.Widgets);
        await Invoke(editor, "AddModule", "duration");

        Assert.Equal(360, overlay.Settings.Width);
        Assert.Equal(176, overlay.Settings.Height);
        Assert.Equal(2, overlay.Settings.Widgets.Count);
        Assert.Equal(existing, overlay.Settings.Widgets[0]);
        AssertGeometry(overlay.Settings.Widgets[1], (8, 96, 168, 72));
    }, new OverlaySettings
    {
        Width = 360, Height = 96,
        Widgets = [OverlayCatalog.CreateWidget("drop-grid", 8, 8) with { Width = 344, Height = 80 }],
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LoadingATemplateOrPresetUsesItsSavedGeometryWithoutRescalingIt(bool custom) => Render(async (editor, overlay, markup) =>
    {
        await editor.CommitCanvasSize(800, 600);
        var expected = custom ? InitialLayout() with { Width = 640, Height = 480 } : OverlayCatalog.Preset("loot");
        if (custom)
        {
            Assert.True((await overlay.SaveTemplateAsync("Meine Geometrie", expected)).Succeeded);
            Set(editor, "_selectedTemplateId", Assert.Single(overlay.Templates).Id);
            await Invoke(editor, "ApplySelectedTemplate");
        }
        else await Invoke(editor, "ApplyPreset", "loot");

        Assert.Equal(800, overlay.Settings.Width);
        Assert.Equal(600, overlay.Settings.Height);
        await Invoke(editor, "ConfirmLayoutChange");

        Assert.Equal(expected.Width, overlay.Settings.Width);
        Assert.Equal(expected.Height, overlay.Settings.Height);
        Assert.Equal(expected.Widgets.Count, overlay.Settings.Widgets.Count);
        for (var index = 0; index < expected.Widgets.Count; index++)
        {
            var saved = expected.Widgets[index];
            var actual = overlay.Settings.Widgets[index];
            Assert.Equal(saved.Kind, actual.Kind);
            AssertGeometry(actual, (saved.X, saved.Y, saved.Width, saved.Height));
            Assert.Equal(saved.FontScale, actual.FontScale);
            Assert.Equal(saved.ItemSize, actual.ItemSize);
        }
    });

    private static void AssertGeometry(OverlayWidget widget, (double X, double Y, double Width, double Height) expected)
    {
        Assert.Equal(expected.X, widget.X, 6);
        Assert.Equal(expected.Y, widget.Y, 6);
        Assert.Equal(expected.Width, widget.Width, 6);
        Assert.Equal(expected.Height, widget.Height, 6);
    }

    private static OverlaySettings InitialLayout() => new()
    {
        Width = 400, Height = 300,
        Widgets =
        [
            OverlayCatalog.CreateWidget("duration", 20, 24) with { Width = 120, Height = 64, FontScale = 1.2 },
            OverlayCatalog.CreateWidget("drop-grid", 180, 112) with { Width = 200, Height = 160, ItemSize = 40 },
        ],
    };

    private static async Task Render(Func<OverlayEditor, OverlayService, Func<string>, Task> test, OverlaySettings? initial = null)
    {
        await using var tracker = new PreviewTrackerSession();
        using var overlay = new OverlayService(tracker);
        Assert.True((await overlay.SaveAsync(initial ?? InitialLayout())).Succeeded);
        var activator = new CapturingActivator();
        using var provider = new ServiceCollection().AddLogging().AddSingleton<IOverlayService>(overlay)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<OverlayEditor>();
            var editor = activator.Components.OfType<OverlayEditor>().Single();
            string Markup()
            {
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
                return WebUtility.HtmlDecode(root.ToHtmlString());
            }
            await test(editor, overlay, Markup);
        });
    }

    private static async Task Invoke(OverlayEditor editor, string name, params object?[] arguments)
    {
        var method = typeof(OverlayEditor).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        if (method.Invoke(editor, arguments) is Task task) await task;
    }

    private static void Set(OverlayEditor editor, string name, object value) =>
        typeof(OverlayEditor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, value);

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
