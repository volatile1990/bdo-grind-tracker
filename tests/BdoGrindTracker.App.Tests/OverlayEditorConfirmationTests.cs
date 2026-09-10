using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayEditorConfirmationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelingLayoutConfirmationPreservesTheLayoutAndDiscardsThePendingAction(bool clear)
    {
        var overlay = new RecordingOverlay();
        var original = overlay.Settings;
        await Render(overlay, async (editor, markup, js) =>
        {
            await Ask(editor, clear);

            Assert.Empty(overlay.Saves);
            Assert.Same(original, overlay.Settings);
            Assert.Contains(js.Calls, call => call.Identifier == "grindcrest.showDialog" &&
                call.Arguments.Contains("overlay-layout-confirm"));
            var dialog = DialogMarkup(markup());
            Assert.Contains("class=\"modal confirm-modal\"", dialog);
            Assert.Contains($"<h2 id=\"overlay-layout-title\">{(clear ? "Layout leeren?" : "Vorlage anwenden?")}</h2>", dialog);
            Assert.Contains("Abbrechen</button>", dialog);
            Assert.DoesNotContain(js.Calls, call => call.Identifier is "confirm" or "alert" or "prompt");

            // Both the cancel button and Escape discard the same pending action.
            await Invoke(editor, "CloseLayoutConfirmation");
            await Invoke(editor, "ConfirmLayoutChange");

            Assert.Empty(overlay.Saves);
            Assert.Same(original, overlay.Settings);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmingChangesTheLayoutOnceAndPreservesDisplayAndBehaviorSettings(bool clear)
    {
        var overlay = new RecordingOverlay(new()
        {
            Enabled = true, Interaction = "passthrough", Visibility = "always",
            PositionX = .6, PositionY = .4, Scale = 1.3, BackgroundOpacity = .35,
            ShowBorder = false, SnapToGrid = false, CaptureExcluded = false, HotkeysEnabled = true,
        });
        var original = overlay.Settings;
        await Render(overlay, async (editor, _, _) =>
        {
            await Ask(editor, clear);
            Assert.Empty(overlay.Saves);

            await Invoke(editor, "ConfirmLayoutChange");
            await Invoke(editor, "ConfirmLayoutChange");

            var saved = Assert.Single(overlay.Saves);
            Assert.Equal(original with { Width = saved.Width, Height = saved.Height, Widgets = saved.Widgets }, saved);
            if (clear)
            {
                Assert.Empty(saved.Widgets);
                Assert.Equal(original.Width, saved.Width);
                Assert.Equal(original.Height, saved.Height);
            }
            else
            {
                AssertDashboard(saved);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedConfirmationWhileSavingDoesNotApplyTheChangeTwice(bool clear)
    {
        var response = new TaskCompletionSource<OverlaySaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var overlay = new RecordingOverlay { SaveResponse = () => response.Task };
        await Render(overlay, async (editor, _, _) =>
        {
            await Ask(editor, clear);
            var first = Invoke(editor, "ConfirmLayoutChange");
            try
            {
                Assert.False(first.IsCompleted);
                Assert.Single(overlay.Saves);
                await Invoke(editor, "ConfirmLayoutChange").WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Single(overlay.Saves);
            }
            finally
            {
                response.TrySetResult(new());
                await first;
            }
            await Invoke(editor, "ConfirmLayoutChange");
            Assert.Single(overlay.Saves);
        });
    }

    [Fact]
    public async Task ApplyingAPresetToAnEmptyLayoutDoesNotAskToReplaceAnything()
    {
        var overlay = new RecordingOverlay(new() { Widgets = [] });
        await Render(overlay, async (editor, _, js) =>
        {
            await Invoke(editor, "ApplyPreset", "dashboard");

            AssertDashboard(Assert.Single(overlay.Saves));
            Assert.DoesNotContain(js.Calls, call => call.Identifier == "grindcrest.showDialog" ||
                call.Identifier is "confirm" or "alert" or "prompt");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSaveKeepsTheConfirmationOpenWithItsErrorAndCanBeRetried(bool clear)
    {
        const string error = "Das Overlay konnte nicht gespeichert werden.";
        var attempts = 0;
        var overlay = new RecordingOverlay
        {
            SaveResponse = () => Task.FromResult(++attempts == 1 ? new OverlaySaveResult(error) : new OverlaySaveResult()),
        };
        var original = overlay.Settings;
        await Render(overlay, async (editor, markup, js) =>
        {
            await Ask(editor, clear);
            await Invoke(editor, "ConfirmLayoutChange");

            Assert.Single(overlay.Saves);
            Assert.Same(original, overlay.Settings);
            var dialog = DialogMarkup(markup());
            Assert.Contains($"<div class=\"notice error-notice\" role=\"alert\">{error}</div>", dialog);
            Assert.Contains($"<h2 id=\"overlay-layout-title\">{(clear ? "Layout leeren?" : "Vorlage anwenden?")}</h2>", dialog);
            Assert.DoesNotContain(js.Calls, call => call.Identifier == "grindcrest.closeDialog");

            await Invoke(editor, "ConfirmLayoutChange");

            Assert.Equal(2, overlay.Saves.Count);
            if (clear) Assert.Empty(overlay.Settings.Widgets);
            else AssertDashboard(overlay.Settings);
            Assert.DoesNotContain(error, DialogMarkup(markup()));
            Assert.Single(js.Calls, call => call.Identifier == "grindcrest.closeDialog" &&
                call.Arguments.Contains("overlay-layout-confirm"));
            await Invoke(editor, "ConfirmLayoutChange");
            Assert.Equal(2, overlay.Saves.Count);
        });
    }

    private static string DialogMarkup(string markup)
    {
        var dialog = Regex.Match(markup, "<dialog\\b[^>]*\\bid=\"overlay-layout-confirm\"[\\s\\S]*?</dialog>");
        Assert.True(dialog.Success, "The overlay confirmation must render an app dialog.");
        return dialog.Value;
    }

    private static void AssertDashboard(OverlaySettings settings)
    {
        var expected = OverlayCatalog.Preset("dashboard");
        Assert.Equal(expected.Width, settings.Width);
        Assert.Equal(expected.Height, settings.Height);
        Assert.Equal(expected.Widgets.Select(widget => (widget.Kind, widget.X, widget.Y, widget.Width, widget.Height)),
            settings.Widgets.Select(widget => (widget.Kind, widget.X, widget.Y, widget.Width, widget.Height)));
    }

    private static Task Ask(OverlayEditor editor, bool clear) => clear
        ? Invoke(editor, "ClearLayout")
        : Invoke(editor, "ApplyPreset", "dashboard");

    private static async Task Render(RecordingOverlay overlay,
        Func<OverlayEditor, Func<string>, RecordingJs, Task> test)
    {
        var activator = new CapturingActivator();
        var js = new RecordingJs();
        using var provider = new ServiceCollection().AddLogging().AddSingleton<IOverlayService>(overlay)
            .AddSingleton<IJSRuntime>(js).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<OverlayEditor>();
            var editor = activator.Components.OfType<OverlayEditor>().Single();
            string Markup()
            {
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(editor, null);
                return WebUtility.HtmlDecode(root.ToHtmlString());
            }
            await test(editor, Markup, js);
        });
    }

    private static async Task Invoke(OverlayEditor editor, string method, params object?[] arguments)
    {
        var handler = typeof(OverlayEditor).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(handler);
        if (handler.Invoke(editor, arguments) is Task task) await task;
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

    private sealed class RecordingJs : IJSRuntime
    {
        public List<(string Identifier, object?[] Arguments)> Calls { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class RecordingOverlay(OverlaySettings? settings = null) : IOverlayService
    {
        public event Action? Changed { add { } remove { } }
        public OverlaySettings Settings { get; private set; } = OverlayLayout.Normalize(settings);
        public OverlayRuntimeState State { get; private set; } = new();
        public OverlaySnapshot Snapshot { get; } = new();
        public List<OverlaySettings> Saves { get; } = [];
        public Func<Task<OverlaySaveResult>> SaveResponse { get; init; } = () => Task.FromResult(new OverlaySaveResult());

        public async Task<OverlaySaveResult> SaveAsync(OverlaySettings value)
        {
            Saves.Add(value);
            var result = await SaveResponse();
            if (result.Succeeded) Settings = OverlayLayout.Normalize(value);
            return result;
        }

        public Task<OverlaySaveResult> SavePositionAsync(double x, double y, double? width = null, double? height = null) =>
            throw new NotSupportedException();
        public Task SetPreviewAsync(bool enabled)
        {
            State = State with { Previewing = enabled };
            return Task.CompletedTask;
        }
        public Task ResetPositionAsync() => throw new NotSupportedException();
        public Task ToggleTrackingAsync() => throw new NotSupportedException();
        public void UpdateRuntime(OverlayRuntimeState state) => State = state;
        public void Dispose() { }
    }
}
