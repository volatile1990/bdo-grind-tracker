using System.Net;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayLocalizationTests
{
    [Fact]
    public async Task RetainedTemplateErrorFollowsLanguageChangesInTheOpenEditor()
    {
        await using var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "en" });
        using var overlay = new OverlayService(tracker);
        var failure = await overlay.SaveTemplateAsync("Missing", new(), "missing-template");
        const string source = "Die Vorlage wurde nicht gefunden. Bitte erneut auswählen.";
        Assert.Equal(source, failure.Error);
        Assert.Equal(source, overlay.TemplateError);
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton<ITrackerSession>(tracker).AddSingleton<IOverlayService>(overlay)
            .AddSingleton<IJSRuntime>(new NoopJs()).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<OverlayEditor>(ParameterView.Empty);
            Assert.Contains("The template was not found. Please select it again.", WebUtility.HtmlDecode(rendered.ToHtmlString()));
            await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de" });
            var german = WebUtility.HtmlDecode(rendered.ToHtmlString());
            Assert.Contains(source, german);
            Assert.DoesNotContain("The template was not found.", german);
            await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "en" });
            Assert.Contains("The template was not found. Please select it again.", WebUtility.HtmlDecode(rendered.ToHtmlString()));
            Assert.Equal(source, overlay.TemplateError);
        });
    }

    [Fact]
    public void CompoundOverlayErrorsTranslateWithoutChangingExternalDetails()
    {
        const string source = "Das Overlay konnte nicht gespeichert werden: external/path.json";
        Assert.Equal("Could not save the overlay: external/path.json", AppText.Translate(source, "en"));
        Assert.Equal(source, AppText.Translate(source, "de"));
        Assert.Equal("You can create up to 24 custom templates.",
            AppText.Translate("Es sind höchstens 24 eigene Vorlagen möglich.", "en"));
    }

    [Fact]
    public void UiLanguageChangesMetricsAndNumberFormattingWithoutChangingGameItemNames()
    {
        var projector = new OverlayMetrics();
        var state = new TrackerState
        {
            HasSession = true, IsRunning = true, SpotId = LootSpotCatalog.HermesiaId,
            Elapsed = TimeSpan.FromHours(1), DetectedGameLanguage = "de",
            Loot = new(new Dictionary<string, long> { ["Black Crystal Fragment"] = 1234 }, 1234, 20),
            Silver = new(50_000_000, 50_000_000, 1, [], [], false),
        };
        var german = projector.Update(state, new() { UiLanguage = "de", GameLanguage = "de" });
        var english = projector.Update(state, new() { UiLanguage = "en", GameLanguage = "de" });

        Assert.Equal("Aktive Zeit", german.Metrics["duration"].Label);
        Assert.Equal("Active time", english.Metrics["duration"].Label);
        Assert.Equal("1.234", german.Metrics["trash"].Value);
        Assert.Equal("1,234", english.Metrics["trash"].Value);
        Assert.Equal("Net silver", english.Metrics["silver"].Label);
        Assert.Equal("50.0 M", english.Metrics["silver"].Value);
        Assert.Equal("Pause", english.TrackingButtonLabel);
        Assert.Equal("Black Crystal Fragment", english.Drops[0].CanonicalName);
        Assert.Equal("Schwarzkristallfragment", english.Drops[0].Name);
        Assert.Equal("1,234", english.Drops[0].QuantityText);
        Assert.Equal(german.ItemCatalog, english.ItemCatalog);
    }

    [Fact]
    public async Task StandalonePreviewUsesSnapshotLanguageForControlsClockAndEmptyLoot()
    {
        var snapshot = new OverlaySnapshot { UiLanguage = "en" };
        var controls = await Render(OverlayCatalog.CreateWidget("controls") with { ShowNewSession = true }, snapshot);
        Assert.Contains("Start tracking", controls);
        Assert.Contains("New session", controls);
        Assert.DoesNotContain("Tracking starten", controls);
        var clock = await Render(OverlayCatalog.CreateWidget("clock"), snapshot);
        Assert.Contains("Local", clock);
        Assert.Contains("Clock", clock);
        var loot = await Render(OverlayCatalog.CreateWidget("rare-drops"), snapshot);
        Assert.Contains("Rare drops", loot);
        Assert.Contains("No rare drops yet", loot);
    }

    [Fact]
    public void NativeCacheInvalidatesLanguageChangesForWidgetsWithNoSessionMetrics()
    {
        var cache = new NativeOverlayRenderState();
        var settings = new OverlaySettings { Widgets = [OverlayCatalog.CreateWidget("clock")] };
        var snapshot = new OverlaySnapshot { UiLanguage = "de" };
        var size = new System.Drawing.Size(360, 260);
        cache.Remember(settings, snapshot, size);
        Assert.True(cache.Matches(settings, snapshot, size));
        Assert.False(cache.Matches(settings, snapshot with { UiLanguage = "en" }, size));
        cache.Remember(settings, snapshot with { UiLanguage = "en" }, size);
        Assert.False(cache.Matches(settings, snapshot, size));
    }

    [Fact]
    public void LocalizedShortcutLabelsDoNotChangeRegisteredKeyOrModifiers()
    {
        var hotkey = OverlayHotkey.DefaultToggleOverlay with { Key = "Delete" };
        Assert.Equal("Strg+Alt+Entf", hotkey.GetDisplayText("de"));
        Assert.Equal("Ctrl+Alt+Delete", hotkey.GetDisplayText("en"));
        Assert.Equal("Delete", hotkey.Key);
        Assert.Equal(OverlayHotkeyModifiers.Control | OverlayHotkeyModifiers.Alt, hotkey.Modifiers);
    }

    private static async Task<string> Render(OverlayWidget widget, OverlaySnapshot snapshot)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<OverlayWidgetPreview>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OverlayWidgetPreview.Widget)] = widget,
                [nameof(OverlayWidgetPreview.Snapshot)] = snapshot,
            }));
            return WebUtility.HtmlDecode(rendered.ToHtmlString());
        });
    }

    private sealed class NoopJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
