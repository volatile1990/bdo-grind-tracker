using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayScaledPreviewTests
{
    [Theory]
    [InlineData("duration")]
    [InlineData("spot")]
    [InlineData("grind-rating")]
    [InlineData("controls")]
    [InlineData("chart")]
    public async Task ResizedPreviewTransformsTheEntireContentAndRetainsTextAndIconPreferences(string kind)
    {
        var original = OverlayCatalog.CreateWidget(kind) with { Width = 300, Height = 200, FontScale = 1.35 };
        var resized = OverlayLayout.ResizeWidget(original, 600, 400);
        var markup = await Render(resized, OverlaySnapshot.Demo);

        Assert.Contains("class=\"overlay-widget-viewport\"", markup);
        Assert.Contains("data-content-width=\"300\"", markup);
        Assert.Contains("data-content-height=\"200\"", markup);
        Assert.Contains("width:300px;height:200px;transform:scale(2)", markup);
        Assert.Contains("--widget-font-scale:1.35", markup);
        Assert.Contains("<svg", markup);
        Assert.Contains("data-overlay-fit", markup);
        Assert.Contains(kind == "controls" ? "Pausieren" : OverlaySnapshot.Demo.Metrics[kind].Value, markup);
    }

    [Fact]
    public async Task UnequalResizeKeepsAUniformTransformAndExpandsTheVirtualLayout()
    {
        var original = OverlayCatalog.CreateWidget("duration") with { Width = 300, Height = 200 };
        var markup = await Render(OverlayLayout.ResizeWidget(original, 600, 100), OverlaySnapshot.Demo);

        Assert.Contains("width:1200px;height:200px;transform:scale(0.5)", markup);
        Assert.Contains("data-content-width=\"300\"", markup);
        Assert.Contains("data-content-height=\"200\"", markup);
        Assert.DoesNotContain("scaleX", markup);
        Assert.DoesNotContain("scaleY", markup);
    }

    [Fact]
    public async Task SmallMetricPreservesLongLabelValueAndDetailAsFitTargets()
    {
        var widget = OverlayLayout.ResizeWidget(OverlayCatalog.CreateWidget("spot") with
            { Width = 300, Height = 200, FontScale = 2 }, 40, 16);
        var snapshot = new OverlaySnapshot { Metrics = new Dictionary<string, OverlayMetric>
        {
            ["spot"] = new("Ein besonders langer Grindspotname", "Magaia Shattered Sanctuary der Familie", "Warrior · Awakening · Beispielcharakter"),
        } };
        var markup = await Render(widget, snapshot);

        Assert.Contains("Ein besonders langer Grindspotname", markup);
        Assert.Contains("Magaia Shattered Sanctuary der Familie", markup);
        Assert.Contains("Warrior · Awakening · Beispielcharakter", markup);
        Assert.Equal(3, Regex.Matches(markup, "data-overlay-fit").Count);
        Assert.Contains("data-min-content-height=", markup);
        Assert.Contains("transform:scale(0.08)", markup);
    }

    [Theory]
    [InlineData("grid", 6, 2)]
    [InlineData("strip", 6, 2)]
    [InlineData("list", 6, 2)]
    [InlineData("card", 1, 7)]
    public async Task SmallLootPreviewKeepsEveryItemUpToTheLimitAndUsesSharedEffectiveSizing(string mode, int visibleCount, int hiddenCount)
    {
        var widget = OverlayLayout.ResizeWidget(OverlayCatalog.CreateWidget("drop-grid") with
        {
            Width = 344, Height = 168, ItemLimit = 6, ItemView = mode, ItemSize = 112, FontScale = 2, ShowIcon = false,
        }, 80, 40);
        var snapshot = new OverlaySnapshot { Drops = Enumerable.Range(1, 8).Select(index =>
            new OverlayLootItem("item-" + index, "Vollständiger langer Gegenstandsname " + index, "9.223.372.036.854.775.807", Quantity: long.MaxValue)).ToArray() };
        var view = OverlayLootPresentation.Create(OverlayContentLayout.Create(widget, snapshot).LayoutWidget, snapshot);
        var markup = await Render(widget, snapshot);

        Assert.Equal(visibleCount, Regex.Matches(markup, "class=\"overlay-widget-item ").Count);
        Assert.Equal(visibleCount, Regex.Matches(markup, "class=\"overlay-item-name\" data-overlay-fit").Count);
        Assert.Equal(visibleCount, Regex.Matches(markup, "class=\"overlay-item-quantity\" data-overlay-fit").Count);
        Assert.Contains("+ " + hiddenCount + " weitere", markup);
        Assert.Contains("Maximale Items in den Moduleinstellungen erhöhen", markup);
        Assert.DoesNotContain("is-short-card", markup);
        Assert.DoesNotContain("Modul vergrößern", markup);
        Assert.Contains("--widget-font-scale:" + Css(view.FontScale), markup);
        Assert.Contains("--loot-item-size:" + Css(view.ItemSize) + "px", markup);
        Assert.Contains("--loot-gap:" + Css(view.Gap) + "px", markup);
        Assert.Contains("class=\"overlay-loot-viewport\"", markup);
        Assert.Contains("Vollständiger langer Gegenstandsname 1", markup);
        Assert.DoesNotContain("Vollständiger langer Gegenstandsname 8", markup);
    }

    [Fact]
    public async Task EmptyLootAndTrackingControlTextRemainFitTargetsWhenScaledDown()
    {
        var empty = await Render(OverlayLayout.ResizeWidget(OverlayCatalog.CreateWidget("drop-item"), 40, 24), new());
        var controls = await Render(OverlayLayout.ResizeWidget(OverlayCatalog.CreateWidget("controls") with
            { ShowLabel = false, ShowIcon = false }, 40, 24), new() { TrackingButtonLabel = "Tracking starten und fortsetzen" });

        Assert.Contains("class=\"overlay-widget-empty\" data-overlay-fit", empty);
        Assert.Contains("Items im Editor auswählen", empty);
        Assert.Contains("class=\"overlay-tracking-button\" data-overlay-fit", controls);
        Assert.Contains("Tracking starten und fortsetzen", controls);
        Assert.DoesNotContain("<svg", controls);
    }

    private static string Css(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

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
}
