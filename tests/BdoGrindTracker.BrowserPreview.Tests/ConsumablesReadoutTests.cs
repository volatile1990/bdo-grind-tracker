using System.Net;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core.Buffs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class ConsumablesReadoutTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("de", "1.100 Silber *")]
    [InlineData("en", "1,100 silver *")]
    public async Task RestoredTentDurationsShareOneTileInReadoutAndOverlay(string language, string cost)
    {
        var value = new BuffLedgerSnapshot(
            [Use("tent-body-enhancement-60", "Body Enhancement (60 min)", 100),
             Use("tent-body-enhancement-180", "Body Enhancement (180 min)", 200),
             Use("tent-body-enhancement-300", "Body Enhancement (300 min)", 300),
             Use("automatic-tent-body-enhancement", "Body Enhancement", null),
             Use("tent-adventures-boon-120", "Adventure's Boon (120 min)", 500)], [], []);

        var html = Compact(await Render(value, language));

        Assert.Equal(2, Tiles(html).Count);
        AssertCount(Tile(html, "tent-body-enhancement-300"), 4);
        AssertCount(Tile(html, "tent-adventures-boon-300"), 1);
        Assert.DoesNotContain("data-buff-id=\"tent-body-enhancement-60\"", html);
        Assert.DoesNotContain("data-buff-id=\"tent-body-enhancement-180\"", html);
        Assert.Contains(cost, html);

        var overlay = new OverlayMetrics().Update(new TrackerState { Buffs = value }, new() { UiLanguage = language });
        var body = Assert.Single(overlay.Consumables.Items, item => item.Id == "tent-body-enhancement-300");
        Assert.Equal(4, body.Count);
        Assert.Equal(600m, body.KnownCost);
        Assert.Equal(1, body.UnpricedCount);
        Assert.Equal(cost, overlay.Consumables.Cost);
        Assert.Equal("tent-body-enhancement-60", value.Consumptions[0].BuffId);
        Assert.Equal(100m, value.Consumptions[0].Cost);
    }

    [Theory]
    [InlineData("de", "Arznei der Harmonie", "Cron-Mahlzeit: Einfach", "600 Silber")]
    [InlineData("en", "Harmony Draught", "Simple Cron Meal", "600 silver")]
    public async Task UsesAreGroupedAsIconsWithAccessibleNamesAndVisibleCornerCounts(
        string language, string harmonyName, string cronName, string cost)
    {
        var value = new BuffLedgerSnapshot(
            [Use("harmony-draught", "Harmony Draught", 100),
             Use("harmony-draught", "Harmony Draught", 200) with { ConsumedAt = At.AddMinutes(20) },
             Use("simple-cron-meal", "Simple Cron Meal", 300)], [], []);

        var html = Compact(await Render(value, language));
        var tiles = Tiles(html);
        Assert.Equal(2, tiles.Count);
        var harmony = Tile(html, "harmony-draught");
        var cron = Tile(html, "simple-cron-meal");
        AssertCount(harmony, 2);
        AssertCount(cron, 1);
        Assert.Contains("assets/buffs/client-", harmony);
        Assert.Contains("assets/buffs/client-", cron);
        Assert.Contains("<img ", harmony);
        Assert.Contains("<img ", cron);
        Assert.Contains(harmonyName, Attribute(harmony, "aria-label"));
        Assert.Contains(cronName, Attribute(cron, "aria-label"));
        Assert.Contains("2", Attribute(harmony, "aria-label"));
        Assert.Equal(Attribute(harmony, "title"), Attribute(harmony, "aria-label"));
        Assert.Contains("tabindex=\"0\"", harmony);
        Assert.Contains(cost, html);
    }

    [Fact]
    public async Task BuffsWithTheSameSavedNameKeepDistinctVariantIconsAndCounts()
    {
        var value = new BuffLedgerSnapshot(
            [Use("harmony-draught", "Harmony", 100),
             Use("immortal-harmony-draught", "Harmony", 500)], [], []);

        var html = Compact(await Render(value));

        Assert.Equal(2, Tiles(html).Count);
        AssertCount(Tile(html, "harmony-draught"), 1);
        AssertCount(Tile(html, "immortal-harmony-draught"), 1);
        Assert.Contains("Unsterblich:", Tile(html, "immortal-harmony-draught"));
        Assert.Contains("600 Silber", html);
    }

    [Fact]
    public async Task ActiveBuffsAndRuntimeEstimatesDoNotInventConsumptionOrCosts()
    {
        var value = new BuffLedgerSnapshot([], [new("simple-cron-meal", "Simple Cron Meal", 9692,
                TimeSpan.FromMinutes(20), 88_888, TimeSpan.Zero, false)],
            [new("simple-cron-meal", "Simple Cron Meal", 9692, TimeSpan.FromMinutes(60), At,
                new(99_999, "eu", At, false), true)]);

        var html = Compact(await Render(value));

        Assert.Empty(Tiles(html));
        Assert.Contains("Noch keine verbrauchten Items", html);
        Assert.Contains("0 Silber", html);
        Assert.DoesNotContain("99.999", html);
        Assert.DoesNotContain("88.888", html);
    }

    [Fact]
    public async Task InitialBookingCountsOnceAndStoredPricesAreNeverReplacedByActivePrices()
    {
        var value = new BuffLedgerSnapshot(
            [Use("simple-cron-meal", "Simple Cron Meal", 100) with { IsSessionStart = true },
             Use("simple-cron-meal", "Simple Cron Meal", 150)],
            [new("simple-cron-meal", "Simple Cron Meal", 9692, TimeSpan.FromMinutes(20),
                88_888, TimeSpan.Zero, false)],
            [new("simple-cron-meal", "Simple Cron Meal", 9692, TimeSpan.FromMinutes(60), At,
                new(99_999, "eu", At, false), true)]);

        var html = Compact(await Render(value));

        AssertCount(Tile(html, "simple-cron-meal"), 2);
        Assert.Contains("250 Silber", html);
        Assert.Contains("Bei erster Erkennung aktiv: 1", html);
        Assert.DoesNotContain("Grindstart", html);
        Assert.DoesNotContain("99.999", html);
        Assert.DoesNotContain("88.888", html);
    }

    [Theory]
    [InlineData("de", "Preis fehlt")]
    [InlineData("en", "Price missing")]
    public async Task EntirelyUnpricedUsesRemainVisibleWithoutClaimingZeroCost(string language, string missing)
    {
        var value = new BuffLedgerSnapshot(
            [Use("automatic-perfume-of-courage", "Perfume of Courage (variant unknown)", null)], [], []);

        var html = Compact(await Render(value, language));

        AssertCount(Tile(html, "automatic-perfume-of-courage"), 1);
        Assert.Contains("has-missing-prices", html);
        Assert.Contains(missing, html);
        Assert.DoesNotContain("0 Silber", html);
        Assert.DoesNotContain("0 silver", html);
    }

    [Theory]
    [InlineData("de", "150 Silber *", "Bekannte Kosten")]
    [InlineData("en", "150 silver *", "Known cost")]
    public async Task MixedKnownAndUnknownPricesShowTheKnownSubtotalAndIncompleteMarker(
        string language, string subtotal, string label)
    {
        var value = new BuffLedgerSnapshot(
            [Use("simple-cron-meal", "Simple Cron Meal", 150),
             Use("harmony-draught", "Harmony Draught", null)], [], []);

        var html = Compact(await Render(value, language));

        Assert.Equal(2, Tiles(html).Count);
        Assert.Contains(label, html);
        Assert.Contains(subtotal, html);
        Assert.Contains("has-missing-prices", html);
    }

    [Fact]
    public async Task MissingObservationIsDistinctFromAnObservedSessionWithNoUses()
    {
        var html = Compact(await Render(null));

        Assert.Empty(Tiles(html));
        Assert.Contains("—", html);
        Assert.DoesNotContain("0 Silber", html);
    }

    [Fact]
    public async Task LegacyConsumptionsWithoutBundledIconsKeepTheirCountAndAccessibleName()
    {
        var value = new BuffLedgerSnapshot(
            [Use("automatic-harmony-draught", "Harmony Draught (variant unknown)", null)], [], []);

        var tile = Tile(Compact(await Render(value)), "automatic-harmony-draught");

        AssertCount(tile, 1);
        Assert.Contains("asset-fallback", tile);
        Assert.Contains("Arznei der Harmonie", Attribute(tile, "aria-label"));
        Assert.DoesNotContain("src=\"\"", tile);
    }

    [Fact]
    public async Task CompactBuffReadoutHasNoDetailedPanelOrDisclosure()
    {
        var value = new BuffLedgerSnapshot([Use("simple-cron-meal", "Simple Cron Meal", 123)], [], []);

        var html = await Render(value);

        Assert.DoesNotContain("<details", html);
        Assert.DoesNotContain("<summary>", html);
        Assert.DoesNotContain("Buffprotokoll", html);
        Assert.DoesNotContain("buff-summary", html);
        AssertCount(Tile(html, "simple-cron-meal"), 1);
        Assert.Contains("123 Silber", html);
    }

    [Fact]
    public async Task LiveDashboardRendersOnlyOneCompactHeaderSummary()
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "de", SetupCompleted = true });
        typeof(PreviewTrackerSession).GetProperty(nameof(PreviewTrackerSession.State))!.SetValue(tracker,
            tracker.State with
            {
                HasSession = true, IsRunning = true,
                Buffs = new([Use("simple-cron-meal", "Simple Cron Meal", 321)], [], []),
            });
        var html = await RenderComponent<LiveDashboard>(tracker, []);

        Assert.Single(Regex.Matches(html, "class=\"consumables-readout\""));
        Assert.DoesNotContain("buff-summary", html);
        var compactAt = html.IndexOf("class=\"consumables-readout\"", StringComparison.Ordinal);
        var bodyAt = html.IndexOf("class=\"live-content-grid\"", StringComparison.Ordinal);
        Assert.InRange(compactAt, 0, bodyAt - 1);
        Assert.DoesNotContain("buff-summary", html[bodyAt..]);
        Assert.DoesNotContain("consumables-details", html);
    }

    [Fact]
    public async Task OverlayShowsEveryConsumptionAndCostWhenLabelsAndIconsAreDisabled()
    {
        var items = Enumerable.Range(1, 10).Select(i => new ConsumableItem("consumable-" + i,
            "Consumable " + i, "assets/buffs/client-test.png", i, i * 100, 0,
            $"Consumable {i}\nCount: {i}\nCost: {i * 100} silver")).ToArray();
        var snapshot = new OverlaySnapshot
        {
            UiLanguage = "en",
            Consumables = new(items, "5,500 silver", "Cost: 5,500 silver", false),
        };
        var widget = OverlayCatalog.CreateWidget("consumables") with
        {
            ShowLabel = false, ShowIcon = false, ItemLimit = 1,
            ItemFilter = "selected", ItemNames = ["unrelated loot item"],
        };
        var html = await RenderOverlay(widget, snapshot);

        Assert.Contains("overlay-widget-consumables", html);
        Assert.Equal(10, Regex.Matches(html, "class=\"overlay-item-quantity\"").Count);
        Assert.DoesNotContain("overlay-widget-more", html);
        Assert.DoesNotContain("overlay-widget-label", html);
        Assert.DoesNotContain("<img ", html);
        Assert.Contains("overlay-consumables-cost", html);
        Assert.Contains("<strong>5,500 silver</strong>", html);
        Assert.Contains("title=\"Cost: 5,500 silver\"", html);
        foreach (var item in items)
        {
            Assert.Contains($"title=\"{item.Tooltip}\"", html);
            Assert.Matches($"class=\"overlay-item-quantity\"[^>]*>{item.Count}</strong>", html);
        }
    }

    [Theory]
    [InlineData("unobserved", "—")]
    [InlineData("empty", "0 silver")]
    [InlineData("unknown", "Price missing")]
    public async Task OverlayFooterRetainsUnknownAndZeroDistinctionsWithoutAVisibleLabel(
        string kind, string cost)
    {
        BuffLedgerSnapshot? value = kind switch
        {
            "empty" => new([], [], []),
            "unknown" => new([Use("simple-cron-meal", "Simple Cron Meal", null)], [], []),
            _ => null,
        };
        var snapshot = new OverlaySnapshot
        {
            UiLanguage = "en", Consumables = ConsumablesPresentation.Create(value, "en"),
        };
        var widget = OverlayCatalog.CreateWidget("consumables") with { ShowLabel = false, ShowIcon = false };

        var html = await RenderOverlay(widget, snapshot);

        Assert.Contains("overlay-consumables-cost", html);
        Assert.Contains("<strong>" + cost + "</strong>", html);
        if (kind == "unknown")
        {
            Assert.Contains("is-incomplete", html);
            Assert.DoesNotContain("0 silver", html);
        }
    }

    private static BuffConsumption Use(string id, string name, decimal? price) =>
        new(id, name, null, At, price is { } amount ? new(amount, "eu", At, false) : null);

    private static string Compact(string html)
    {
        Assert.DoesNotContain("<details", html);
        return html;
    }

    private static MatchCollection Tiles(string html) => Regex.Matches(html,
        "<li\\b(?=[^>]*class=\"[^\"]*\\bconsumable-item\\b)[^>]*>.*?</li>", RegexOptions.Singleline);

    private static string Tile(string html, string id) => Assert.Single(Tiles(html).Cast<Match>(),
        match => match.Value.Contains($"data-buff-id=\"{id}\"", StringComparison.Ordinal)).Value;

    private static void AssertCount(string tile, int count) => Assert.Matches(
        $"<strong\\b[^>]*class=\"consumable-count\"[^>]*>{count}</strong>", tile);

    private static string Attribute(string html, string attribute)
    {
        var match = Regex.Match(html, Regex.Escape(attribute) + "=\"([^\"]*)\"");
        Assert.True(match.Success, $"Missing {attribute}.");
        return match.Groups[1].Value;
    }

    private static async Task<string> Render(BuffLedgerSnapshot? value, string language = "de")
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = language });
        return await RenderComponent<ConsumablesReadout>(tracker, new()
        {
            [nameof(ConsumablesReadout.Value)] = value,
        });
    }

    private static async Task<string> RenderComponent<T>(PreviewTrackerSession tracker,
        Dictionary<string, object?> parameters) where T : IComponent
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<NavigationManager, StaticNavigation>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }

    private static async Task<string> RenderOverlay(OverlayWidget widget, OverlaySnapshot snapshot)
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = snapshot.UiLanguage });
        return await RenderComponent<OverlayWidgetPreview>(tracker, new()
        {
            [nameof(OverlayWidgetPreview.Widget)] = widget,
            [nameof(OverlayWidgetPreview.Snapshot)] = snapshot,
        });
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/live");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
