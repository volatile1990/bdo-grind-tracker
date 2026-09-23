using System.Net;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class OverlayWindowAppearanceTests
{
    [Theory]
    [InlineData(AppThemes.Grindcrest, true)]
    [InlineData(AppThemes.Light, true)]
    [InlineData(AppThemes.Cats, false)]
    [InlineData("unknown", true)]
    [InlineData(null, true)]
    [InlineData(AppThemes.BlackDesert, false)]
    public void FramelessAndOriginalThemesKeepTheExistingCanvasSize(string? theme, bool border)
    {
        var chrome = OverlayWindowChrome.For(theme, border);
        Assert.False(chrome.HasTitleBar);
        Assert.Equal(360, chrome.OuterWidth(360));
        Assert.Equal(260, chrome.OuterHeight(260));
    }

    [Theory]
    [InlineData(AppThemes.BlackDesert)]
    [InlineData(AppThemes.Cats)]
    public async Task EditorAndBrowserAddTheSameTitleOutsideTheSavedWidgetCanvas(string theme)
    {
        var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { ThemeId = theme });
        using var overlay = new OverlayService(tracker);
        await overlay.RenameOverlayAsync(overlay.SelectedOverlayId, "Meine Drops");
        var settings = overlay.Settings;
        var services = new ServiceCollection().AddLogging().AddSingleton<IOverlayService>(overlay)
            .AddSingleton<NavigationManager, OverlayTestNavigation>().AddSingleton<IJSRuntime, NoJavaScript>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var editor = await renderer.RenderComponentAsync<OverlayEditor>(ParameterView.Empty);
            var browser = await renderer.RenderComponentAsync<BrowserOverlayPreview>(ParameterView.Empty);
            foreach (var markup in new[] { editor.ToHtmlString(), browser.ToHtmlString() })
            {
                Assert.Contains("class=\"overlay-window-title\">Meine Drops", markup);
                Assert.Contains("width:364px;height:294px", markup);
                Assert.Contains("inset:32px 2px 2px 2px", markup);
                Assert.Contains("data-width=\"360\" data-height=\"260\"", markup);
                Assert.Contains("data-chrome-x=\"4\" data-chrome-y=\"34\"", markup);
            }
            Assert.Equal(settings, overlay.Settings);
            await tracker.SavePreferencesAsync(tracker.Preferences with { ThemeId = AppThemes.Grindcrest });
            Assert.DoesNotContain("overlay-window-titlebar", browser.ToHtmlString());
            Assert.Contains("width:360px;height:260px", browser.ToHtmlString());
            Assert.Equal(settings, overlay.Settings);
        });
    }

    [Fact]
    public async Task InventoryFinishesItsLastRowWithoutInventingDropsOrChangingCapacity()
    {
        var widget = OverlayCatalog.CreateWidget("drop-grid") with { Width = 200, Height = 300 };
        var snapshot = new OverlaySnapshot { ThemeId = AppThemes.BlackDesert,
            Drops = Enumerable.Range(1, 7).Select(i => new OverlayLootItem("item" + i, "Item " + i, "42", Quantity: 42)).ToArray() };
        var view = OverlayLootPresentation.Create(widget, snapshot);
        Assert.Equal(3, view.Columns);
        Assert.Equal(2, OverlayLootPresentation.EmptySlotCount(widget, snapshot, view));
        Assert.Equal(7, view.VisibleItems.Count);
        Assert.Equal(0, view.HiddenCount);
        Assert.Equal(0, OverlayLootPresentation.EmptySlotCount(widget,
            snapshot with { ThemeId = AppThemes.Grindcrest }, view));
        Assert.Equal(0, OverlayLootPresentation.EmptySlotCount(widget with { ItemView = "strip" }, snapshot, view));
        await using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var markup = await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<OverlayWidgetPreview>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Widget"] = widget, ["Snapshot"] = snapshot }))).ToHtmlString()));
        Assert.Equal(2, Regex.Matches(markup, "is-empty-slot\" aria-hidden=\"true\"").Count);
        Assert.Equal(7, Regex.Matches(markup, "class=\"overlay-item-quantity\"").Count);
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
