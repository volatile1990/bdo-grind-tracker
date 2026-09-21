using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;
using BdoGrindTracker.Core.Buffs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class HistoryConsumablesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("de", "VERBRAUCHT", "2.500 Silber", "7.777 Silber", "0 Silber")]
    [InlineData("en", "CONSUMED", "2,500 silver", "7,777 silver", "0 silver")]
    public async Task SpotRowsShowOnlyTheirOwnSavedBuffCountsAndCostsIncludingLegacyUnknowns(
        string language, string heading, string firstCost, string secondCost, string zeroCost)
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = language });
        typeof(PreviewTrackerSession).GetProperty(nameof(PreviewTrackerSession.State))!.SetValue(tracker,
            tracker.State with
            {
                Buffs = new([Use("live-only", 999_999)], [], []),
                SessionId = Guid.NewGuid(), HasSession = true,
            });
        var first = Entry(0, new([Use("simple-cron-meal", 1000), Use("simple-cron-meal", 1500)], [], []));
        var second = Entry(1, new([Use("harmony-draught", 7777)], [], []));
        var legacy = Entry(2, null);
        var observedEmpty = Entry(3, BuffLedgerSnapshot.Empty);
        AddHistory(tracker, first, second, legacy, observedEmpty);

        var html = await Render(tracker);

        Assert.Contains($"class=\"session-consumables-column\" scope=\"col\">{heading}</th>", html);
        var firstCell = ConsumablesCell(html, first.SessionId);
        Assert.Contains("consumables-compact", firstCell);
        Assert.Contains("data-buff-id=\"simple-cron-meal\"", firstCell);
        Assert.Contains("<img ", firstCell);
        AssertCount(firstCell, 2);
        Assert.Contains(firstCost, firstCell);
        Assert.DoesNotContain("harmony-draught", firstCell);
        Assert.DoesNotContain(secondCost, firstCell);

        var secondCell = ConsumablesCell(html, second.SessionId);
        Assert.Contains("data-buff-id=\"harmony-draught\"", secondCell);
        AssertCount(secondCell, 1);
        Assert.Contains(secondCost, secondCell);
        Assert.DoesNotContain("simple-cron-meal", secondCell);
        Assert.DoesNotContain(firstCost, secondCell);

        var legacyCell = ConsumablesCell(html, legacy.SessionId);
        Assert.Contains("—</strong>", legacyCell);
        Assert.DoesNotContain("data-buff-id", legacyCell);
        Assert.DoesNotContain(zeroCost, legacyCell);
        var emptyCell = ConsumablesCell(html, observedEmpty.SessionId);
        Assert.Contains(zeroCost, emptyCell);
        Assert.DoesNotContain("data-buff-id", emptyCell);
        Assert.DoesNotContain("live-only", html);

        foreach (var cell in new[] { firstCell, secondCell, legacyCell, emptyCell })
        {
            Assert.DoesNotContain("<h2", cell);
            Assert.DoesNotContain("consumables-heading", cell);
            Assert.DoesNotContain("consumables-empty", cell);
            Assert.DoesNotContain("<details", cell);
        }
    }

    [Fact]
    public async Task SpotRowsKeepUnpricedBookingsVisibleWithoutInventingZeroCost()
    {
        var tracker = new PreviewTrackerSession(empty: true);
        await tracker.SavePreferencesAsync(tracker.Preferences with { UiLanguage = "en" });
        var unknown = Entry(0, new([Use("simple-cron-meal", null)], [], []));
        var partial = Entry(1, new([Use("harmony-draught", 1234), Use("harmony-draught", null)], [], []));
        AddHistory(tracker, unknown, partial);

        var html = await Render(tracker);

        var unknownCell = ConsumablesCell(html, unknown.SessionId);
        AssertCount(unknownCell, 1);
        Assert.Contains("Price missing", unknownCell);
        Assert.Contains("has-missing-prices", unknownCell);
        Assert.DoesNotContain("0 silver", unknownCell);
        var partialCell = ConsumablesCell(html, partial.SessionId);
        AssertCount(partialCell, 2);
        Assert.Contains("1,234 silver *", partialCell);
        Assert.Contains("has-missing-prices", partialCell);
    }

    private static BuffConsumption Use(string id, decimal? price) =>
        new(id, id, null, At, price is { } amount ? new(amount, "eu", At, false) : null);

    private static LootHistoryEntry Entry(int hoursAgo, BuffLedgerSnapshot? buffs) => new()
    {
        SessionId = Guid.NewGuid(), SpotId = LootSpotCatalog.HermesiaId,
        StartedAt = At.AddHours(-hoursAgo - 1), UpdatedAt = At.AddHours(-hoursAgo),
        Duration = TimeSpan.FromHours(1), CharacterClass = "Warrior · Awakening",
        Totals = new() { ["Black Crystal Fragment"] = 100 },
        SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = true, Buffs = buffs,
    };

    private static void AddHistory(PreviewTrackerSession tracker, params LootHistoryEntry[] entries) =>
        ((List<LootHistoryEntry>)typeof(PreviewTrackerSession)
            .GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tracker)!).AddRange(entries);

    private static string ConsumablesCell(string html, Guid sessionId)
    {
        var row = Regex.Match(html, $"<tr\\b[^>]*data-session-id=\"{sessionId}\"[^>]*>.*?</tr>", RegexOptions.Singleline);
        Assert.True(row.Success, $"Missing saved session row {sessionId}.");
        var cell = Regex.Match(row.Value, "<td class=\"session-consumables-column\">(.*?)</td>", RegexOptions.Singleline);
        Assert.True(cell.Success, $"Missing consumables in saved session {sessionId}.");
        return cell.Groups[1].Value;
    }

    private static void AssertCount(string html, int count) => Assert.Matches(
        $"<strong\\b[^>]*class=\"consumable-count\"[^>]*>{count}</strong>", html);

    private static async Task<string> Render(PreviewTrackerSession tracker)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<NavigationManager, StaticNavigation>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<HistoryDashboard>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId,
            }))).ToHtmlString()));
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/history/spots/hermesia");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
