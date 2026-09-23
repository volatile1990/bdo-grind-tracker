using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class OverlayNavigationTests
{
    [Theory]
    [InlineData(null, "layout")]
    [InlineData("unknown", "layout")]
    [InlineData("layout", "layout")]
    [InlineData("display", "display")]
    [InlineData("windows", "windows")]
    [InlineData("shortcuts", "shortcuts")]
    [InlineData("templates", "templates")]
    public Task OnlySelectedCategoryIsVisibleWhileTheCanvasStaysMounted(string? section, string expected) =>
        Render(section, (_, _, navigation, markup) =>
        {
            AssertCategory(markup(), expected);
            Assert.Contains("class=\"oe-stage ", markup());
            Assert.Contains("aria-label=\"Overlay-Fenster auswählen\"", markup());
            Assert.Null(navigation.LastPath);
            return Task.CompletedTask;
        });

    [Fact]
    public Task SwitchingCategoriesKeepsTheEditedWindowAndWidgetSelected() =>
        Render(null, async (editor, overlay, navigation, markup) =>
        {
            await editor.AddModuleAt("clock", 16, 24);
            var widget = overlay.Settings.Widgets.Last();
            await editor.SelectWidget(widget.Id);
            var settings = overlay.Settings;
            var windowId = overlay.SelectedOverlayId;

            foreach (var section in new[] { "display", "windows", "shortcuts", "templates", "layout" })
            {
                await Invoke(editor, "SelectSection", section);

                Assert.Equal($"/overlay?section={section}", navigation.LastPath);
                AssertCategory(markup(), section);
                Assert.Equal(windowId, overlay.SelectedOverlayId);
                Assert.Equal(settings, overlay.Settings);
                Assert.Contains($"class=\"oe-widget is-selected\" data-widget-id=\"{widget.Id}\"", markup());
            }
        });

    [Fact]
    public Task ConfirmingAPresetReturnsToTheLayoutWithItsNewWidgets() =>
        Render("templates", async (editor, overlay, navigation, markup) =>
        {
            await editor.AddModuleAt("clock", 16, 24);
            var previous = overlay.Settings;

            await Invoke(editor, "ApplyPreset", "compact");

            Assert.Null(navigation.LastPath);
            Assert.Equal(previous, overlay.Settings);
            await Invoke(editor, "SelectSection", "display");
            AssertCategory(markup(), "templates");

            await Invoke(editor, "ConfirmLayoutChange");

            Assert.Equal("/overlay?section=layout", navigation.LastPath);
            AssertCategory(markup(), "layout");
            Assert.Equal(OverlayCatalog.Preset("compact").Widgets.Select(widget => widget.Kind),
                overlay.Settings.Widgets.Select(widget => widget.Kind));
        });

    private static void AssertCategory(string markup, string expected)
    {
        foreach (var section in new[] { "layout", "display", "windows", "shortcuts", "templates" })
        {
            Assert.Contains($"href=\"/overlay?section={section}\"", markup);
            var container = Regex.Match(markup, $"<div data-overlay-section=\"{section}\"(?<attributes>[^>]*)>");
            Assert.True(container.Success);
            Assert.Equal(section != expected, Regex.IsMatch(container.Groups["attributes"].Value, @"\bhidden(?:\s|=|$)"));
        }
    }

    private static async Task Render(string? section,
        Func<OverlayEditor, OverlayService, OverlayTestNavigation, Func<string>, Task> test)
    {
        await using var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
        using var overlay = new OverlayService(tracker);
        var activator = new CapturingActivator();
        var navigation = new OverlayTestNavigation();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IOverlayService>(overlay).AddSingleton<IComponentActivator>(activator)
            .AddSingleton<NavigationManager>(navigation).AddSingleton<IJSRuntime, NoJavaScript>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<OverlayEditor>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(OverlayEditor.Section)] = section }));
            var editor = activator.Components.OfType<OverlayEditor>().Single();
            await test(editor, overlay, navigation, () => WebUtility.HtmlDecode(rendered.ToHtmlString()));
        });
    }

    private static Task Invoke(OverlayEditor component, string name, params object?[] arguments) =>
        ((IHandleEvent)component).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(async () =>
        {
            var result = typeof(OverlayEditor).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments);
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

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
}
