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

public sealed class OverlayPersonalizationInteractionTests
{
    [Fact]
    public Task GlobalShortcutsAreVisibleBeforeCollapsedSettings() => Render(async (editor, overlay, markup, js) =>
    {
        var html = markup();
        Assert.True(overlay.Settings.HotkeysEnabled);
        Assert.True(html.IndexOf("oe-shortcuts", StringComparison.Ordinal) < html.IndexOf("oe-behavior", StringComparison.Ordinal));
        Assert.Contains("Global aktiv", html);
        Assert.Contains("<kbd>Strg+Alt+O</kbd>", html);
        Assert.Contains("<kbd>Strg+Alt+L</kbd>", html);
        Assert.DoesNotContain("Automatisch lokal gespeichert", html);
        overlay.UpdateRuntime(new() { HotkeyStatus = "Strg+Alt+O wird bereits verwendet." });
        Assert.Contains("Strg+Alt+O wird bereits verwendet.", markup());
        await Task.CompletedTask;
    });

    [Fact]
    public Task CancelingShortcutEditsKeepsExistingBindings() => Render(async (editor, overlay, markup, js) =>
    {
        var before = overlay.Settings;
        await Invoke(editor, "OpenHotkeyEditor");
        Set(editor, "_toggleOverlayDraft", new OverlayHotkey { Modifiers = OverlayHotkeyModifiers.None, Key = "F8" });
        await Invoke(editor, "CloseHotkeyEditor");
        await Invoke(editor, "SaveHotkeys");
        Assert.Same(before, overlay.Settings);
    });

    [Fact]
    public Task DuplicatesAreRejectedAndSwappingShortcutsSavesBothActionsTogether() => Render(async (editor, overlay, markup, js) =>
    {
        var before = overlay.Settings;
        await Invoke(editor, "OpenHotkeyEditor");
        Set(editor, "_toggleOverlayDraft", OverlayHotkey.DefaultToggleInteraction);
        await Invoke(editor, "SaveHotkeys");
        Assert.Same(before, overlay.Settings);
        Assert.Contains("unterschiedliche Tastenkürzel", markup());
        Set(editor, "_toggleInteractionDraft", OverlayHotkey.DefaultToggleOverlay);
        await Invoke(editor, "SaveHotkeys");
        Assert.Equal(OverlayHotkey.DefaultToggleInteraction, overlay.Settings.ToggleOverlayHotkey);
        Assert.Equal(OverlayHotkey.DefaultToggleOverlay, overlay.Settings.ToggleInteractionHotkey);
        Assert.True(overlay.Settings.HotkeysEnabled);
        Assert.Contains(js.Calls, call => call == ("grindcrest.closeDialog", "overlay-hotkeys-edit"));
    });

    [Fact]
    public Task GlobalHotkeysCanStillBeExplicitlyDisabled() => Render(async (editor, overlay, markup, js) =>
    {
        await Invoke(editor, "OpenHotkeyEditor");
        Set(editor, "_hotkeysEnabledDraft", false);
        await Invoke(editor, "SaveHotkeys");
        Assert.False(overlay.Settings.HotkeysEnabled);
        Assert.Contains("Deaktiviert", markup());
    });

    [Fact]
    public Task SavingANamedTemplateDoesNotChangeTheOverlayAndCancelDoesNotOverwriteIt() => Render(async (editor, overlay, markup, js) =>
    {
        var before = overlay.Settings;
        await Invoke(editor, "OpenTemplateSave", new object?[] { null });
        Set(editor, "_templateNameDraft", "  Mein Grind  ");
        await Invoke(editor, "SaveTemplate");
        var template = Assert.Single(overlay.Templates);
        Assert.Equal("Mein Grind", template.Name);
        Assert.Same(before, overlay.Settings);
        Assert.Contains("Eigene Vorlagen", markup());
        Assert.Contains("Mein Grind</option>", markup());

        await Invoke(editor, "OpenTemplateSave", new object?[] { null });
        Set(editor, "_templateNameDraft", "mein grind");
        Assert.Contains("Vorlage überschreiben", markup());
        await Invoke(editor, "CloseTemplateSave");
        await Invoke(editor, "SaveTemplate");
        Assert.Same(template, Assert.Single(overlay.Templates));
    });

    [Fact]
    public Task TemplateReplacementIsExplicitAndUsesTheCurrentLayout() => Render(async (editor, overlay, markup, js) =>
    {
        Assert.True((await overlay.SaveTemplateAsync("Mein Grind", OverlayCatalog.Preset("loot"))).Succeeded);
        var template = Assert.Single(overlay.Templates);
        await Invoke(editor, "OpenTemplateSave", template);
        Assert.Contains("Die Vorlage „Mein Grind“ wird durch das aktuelle Layout ersetzt.", markup());
        await Invoke(editor, "SaveTemplate");
        var updated = Assert.Single(overlay.Templates);
        Assert.Equal(template.Id, updated.Id);
        Assert.Equal(overlay.Settings.Width, updated.Layout.Width);
        Assert.Equal(overlay.Settings.Widgets.Select(w => w.Kind), updated.Layout.Widgets.Select(w => w.Kind));
    });

    [Fact]
    public Task ApplyingSavedLayoutRequiresConfirmationAndPreservesWindowBehaviorAndHotkeys() => Render(async (editor, overlay, markup, js) =>
    {
        await Invoke(editor, "Change", new Func<OverlaySettings, OverlaySettings>(s => s with
        {
            Enabled = true, Interaction = "passthrough", Visibility = "always", CaptureExcluded = false,
            ToggleOverlayHotkey = new() { Modifiers = OverlayHotkeyModifiers.None, Key = "F8" },
            ToggleInteractionHotkey = new() { Modifiers = OverlayHotkeyModifiers.None, Key = "F9" },
        }));
        await overlay.SavePositionAsync(.7, .4);
        var before = overlay.Settings;
        var layout = OverlayCatalog.Preset("loot-strip") with { BackgroundOpacity = .3, ShowBorder = false, Scale = 1.4 };
        await overlay.SaveTemplateAsync("Meine Leiste", layout);
        Set(editor, "_selectedTemplateId", Assert.Single(overlay.Templates).Id);
        await Invoke(editor, "ApplySelectedTemplate");
        Assert.Same(before, overlay.Settings);
        Assert.Contains("Vorlage anwenden?", markup());
        Assert.Contains("Meine Leiste", markup());
        await Invoke(editor, "CloseLayoutConfirmation");
        Assert.Same(before, overlay.Settings);
        await Invoke(editor, "ApplySelectedTemplate");
        await Invoke(editor, "ConfirmLayoutChange");
        Assert.Equal(layout.Width, overlay.Settings.Width);
        Assert.Equal(layout.BackgroundOpacity, overlay.Settings.BackgroundOpacity);
        Assert.Equal(layout.Scale, overlay.Settings.Scale);
        Assert.Equal(before.PositionX, overlay.Settings.PositionX);
        Assert.Equal(before.PositionY, overlay.Settings.PositionY);
        Assert.Equal(before.Interaction, overlay.Settings.Interaction);
        Assert.Equal(before.Visibility, overlay.Settings.Visibility);
        Assert.Equal(before.Enabled, overlay.Settings.Enabled);
        Assert.Equal(before.CaptureExcluded, overlay.Settings.CaptureExcluded);
        Assert.Equal(before.ToggleOverlayHotkey, overlay.Settings.ToggleOverlayHotkey);
        Assert.Equal(before.ToggleInteractionHotkey, overlay.Settings.ToggleInteractionHotkey);
    });

    [Fact]
    public Task DeletingTemplateRequiresConfirmationAndKeepsTheCurrentLayout() => Render(async (editor, overlay, markup, js) =>
    {
        await overlay.SaveTemplateAsync("Lösch mich", overlay.Settings);
        Set(editor, "_selectedTemplateId", Assert.Single(overlay.Templates).Id);
        var before = overlay.Settings;
        await Invoke(editor, "RequestTemplateDelete");
        Assert.Single(overlay.Templates);
        await Invoke(editor, "CloseTemplateDelete");
        await Invoke(editor, "DeleteTemplate");
        Assert.Single(overlay.Templates);
        await Invoke(editor, "RequestTemplateDelete");
        await Invoke(editor, "DeleteTemplate");
        Assert.Empty(overlay.Templates);
        Assert.Same(before, overlay.Settings);
    });

    [Fact]
    public Task MissingExplicitReplacementTargetDoesNotSilentlyCreateAnotherTemplate() => Render(async (editor, overlay, markup, js) =>
    {
        await overlay.SaveTemplateAsync("Vorlage", overlay.Settings);
        var target = Assert.Single(overlay.Templates);
        await Invoke(editor, "OpenTemplateSave", target);
        await overlay.DeleteTemplateAsync(target.Id);
        await Invoke(editor, "SaveTemplate");
        Assert.Empty(overlay.Templates);
        Assert.Contains("nicht gefunden", markup());
        Assert.DoesNotContain(js.Calls, call => call == ("grindcrest.closeDialog", "overlay-template-save"));
    });

    private static async Task Render(Func<OverlayEditor, OverlayService, Func<string>, RecordingJs, Task> test)
    {
        await using var tracker = new PreviewTrackerSession();
        using var overlay = new OverlayService(tracker);
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
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
                return WebUtility.HtmlDecode(root.ToHtmlString());
            }
            await test(editor, overlay, Markup, js);
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
    private sealed class RecordingJs : IJSRuntime
    {
        public List<(string Identifier, string? Dialog)> Calls { get; } = [];
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args?.FirstOrDefault()?.ToString()));
            return ValueTask.FromResult(default(T)!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
}
