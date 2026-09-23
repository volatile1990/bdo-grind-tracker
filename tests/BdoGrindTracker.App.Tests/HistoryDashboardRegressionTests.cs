using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Tests;

public sealed class HistoryDashboardRegressionTests
{
    [Fact]
    public async Task SpotOverviewRendersEverySpotWithItsOwnRegionAndEmblem()
    {
        var markup = await RenderAsync(new() { History = [] }, "/history");

        Assert.Equal(LootSpotCatalog.Spots.Count, Regex.Matches(markup, "class=\"spot-card\"").Count);
        var decoded = WebUtility.HtmlDecode(markup);
        foreach (var spot in LootSpotCatalog.Spots)
        {
            var profile = Presentation.Profile(spot.Id)!;
            if (profile.BackgroundFileName is { } background)
                Assert.Contains("assets/spot-backgrounds/" + background, markup);
            Assert.Contains("assets/spot-icons/" + profile.IconFileName, markup);
            Assert.Contains("<h3>" + Presentation.SpotName(spot.Id) + "</h3>", decoded);
            Assert.Contains(profile.RegionName.ToUpperInvariant(), decoded);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CategoryLinksKeepFiltersAndStartAtTheOverview(bool allSessions)
    {
        var markup = await RenderAsync(new(), "/history/spots/" + LootSpotCatalog.HermesiaId
            + "?q=Temple%20%26%20Ruins&days=30&class=Warrior%20%C2%B7%20Awakening&page=99&edit=1"
            + (allSessions ? "&view=all" : ""));
        var navigation = Regex.Match(markup, "<nav[^>]*aria-label=\"Verlaufsbereiche\"[^>]*>(.*?)</nav>", RegexOptions.Singleline);
        Assert.True(navigation.Success);
        var links = Regex.Matches(navigation.Value, "<a\\b[^>]*href=\"([^\"]+)\"[^>]*>");
        Assert.Equal(2, links.Count);
        for (var index = 0; index < links.Count; index++)
        {
            var uri = new Uri("https://0.0.0.1" + links[index].Groups[1].Value);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            Assert.Equal("/history", uri.AbsolutePath);
            Assert.Equal("Temple & Ruins", query["q"]);
            Assert.Equal("30", query["days"]);
            Assert.Equal("Warrior · Awakening", query["class"]);
            Assert.False(query.ContainsKey("page"));
            Assert.False(query.ContainsKey("edit"));
            Assert.Equal(index == 1, query.ContainsKey("view"));
            Assert.Equal((index == 1) == allSessions, links[index].Value.Contains("aria-current=\"page\"", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectingTheCurrentCategoryFromASpotReturnsToTheOverview(bool allSessions)
    {
        var navigation = new RecordingNavigation();
        var component = new HistoryDashboard();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(HistoryDashboard).GetProperty("Navigation", flags)!.SetValue(component, navigation);
        typeof(HistoryDashboard).GetField("_spotId", flags)!.SetValue(component, LootSpotCatalog.HermesiaId);
        typeof(HistoryDashboard).GetField("_allSessions", flags)!.SetValue(component, allSessions);
        typeof(HistoryDashboard).GetField("_query", flags)!.SetValue(component, "Temple");
        typeof(HistoryDashboard).GetField("_days", flags)!.SetValue(component, 30);
        typeof(HistoryDashboard).GetField("_page", flags)!.SetValue(component, 2);

        typeof(HistoryDashboard).GetMethod("SetOverview", flags)!.Invoke(component, [allSessions]);

        var uri = new Uri(navigation.Uri);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("/history", uri.AbsolutePath);
        Assert.Equal("Temple", query["q"]);
        Assert.Equal("30", query["days"]);
        Assert.Equal(allSessions, query.ContainsKey("view"));
        Assert.False(query.ContainsKey("page"));
    }

    [Fact]
    public async Task NewlyAddedSpotShowsSuppliedBackgroundAndKnownCapWithoutInventedRecommendations()
    {
        var markup = WebUtility.HtmlDecode(await RenderAsync(new() { History = [] }, "/history/spots/tungrad-ruins"));

        Assert.Contains("Tungrad Ruins", markup);
        Assert.Contains("<span>AP-LIMIT</span><strong>1.395</strong>", markup);
        Assert.Contains("assets/spot-icons/tungrad-ruins.png", markup);
        Assert.DoesNotContain("EMPFOHLENE DP", markup);
        Assert.DoesNotContain("KRISTALL-EMPFEHLUNG", markup);
        Assert.Contains("assets/spot-backgrounds/tungrad-ruins.jpg", markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchWithoutMatchesClearsStatisticsCountAndContents(bool allSessions)
    {
        var markup = await RenderAsync(new(), "/history?q=zzzz-kein-spot" + (allSessions ? "&view=all" : ""));

        Assert.Contains("class=\"filter-count\">0 Sessions", markup);
        Assert.Contains("<span>SESSIONS</span><strong>0</strong>", markup);
        Assert.Contains("<span>GRINDZEIT</span><strong>0 Min.</strong>", markup);
        Assert.DoesNotContain("class=\"chronological-session-card\"", markup);
        Assert.DoesNotContain("class=\"spot-card\"", markup);
        Assert.Contains(allSessions ? "Keine passenden Sessions" : "Kein Grindspot gefunden", markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchClassAndTimeUseTheSameSessionsForEveryTotal(bool allSessions)
    {
        var now = DateTimeOffset.Now;
        var session = new HistorySession
        {
            History =
            [
                Entry(LootSpotCatalog.HermesiaId, 10, now.AddDays(-1), "Warrior · Awakening"),
                Entry(LootSpotCatalog.HermesiaId, 100, now.AddDays(-1), "Witch · Awakening"),
                Entry(LootSpotCatalog.HermesiaId, 1000, now.AddDays(-10), "Warrior · Awakening"),
                Entry(LootSpotCatalog.Spots.First(profile => profile.Id != LootSpotCatalog.HermesiaId).Id,
                    10000, now.AddDays(-1), "Warrior · Awakening")
            ]
        };
        var name = Presentation.SpotName(LootSpotCatalog.HermesiaId);
        var query = "/history?q=" + Uri.EscapeDataString("  " + name.ToUpperInvariant() + "  ")
            + "&days=7&class=" + Uri.EscapeDataString("Warrior · Awakening") + (allSessions ? "&view=all" : "");
        var markup = await RenderAsync(session, query);
        var expectedValue = SilverValuation.Calculate(session.History[0].Totals, session.Prices, session.Preferences.Tax);

        Assert.Contains("class=\"filter-count\">1 Sessions", markup);
        Assert.Contains("<span>SESSIONS</span><strong>1</strong>", markup);
        Assert.Contains("<span>GRINDZEIT</span><strong>1 Std. 00 Min.</strong>", markup);
        Assert.Contains("<strong class=\"gold\">" + Presentation.Silver(expectedValue.AfterTax) + "</strong>", markup);
        Assert.Single(Regex.Matches(markup, allSessions ? "class=\"chronological-session-card\"" : "class=\"spot-card\""));
    }

    [Fact]
    public async Task FilteredPaginationIsClampedToTheRemainingResults()
    {
        var markup = await RenderAsync(new(), "/history?view=all&page=999&q=" + Uri.EscapeDataString(Presentation.SpotName(LootSpotCatalog.HermesiaId)));

        Assert.Contains("1–1 von 1 Sessions", markup);
        Assert.Contains("Seite 1 von 1", markup);
    }

    [Fact]
    public async Task SpotRouteIgnoresTheHiddenOverviewSearch()
    {
        var markup = await RenderAsync(new(), "/history/spots/" + LootSpotCatalog.HermesiaId + "?q=zzzz-kein-spot");

        Assert.Contains("class=\"filter-count\">1 Sessions", markup);
        Assert.Contains("spot-session-table", markup);
    }

    [Fact]
    public async Task MatrixExplainsKeyboardReorderingAndDialogsHaveNames()
    {
        var markup = await RenderAsync(new(), "/history/spots/" + LootSpotCatalog.HermesiaId);

        Assert.Contains("aria-keyshortcuts=\"Alt+ArrowLeft Alt+ArrowRight\"", markup);
        Assert.Contains("Lootspalte 1 von", markup);
        Assert.Contains("aria-describedby=\"session-column-help\"", markup);
        Assert.Contains("Umschalt + Mausrad", markup);
        Assert.Contains("aria-label=\"Session bearbeiten\"", markup);
        foreach (var id in new[] { "history-edit", "history-confirm" })
        {
            Assert.Matches("<dialog[^>]*id=\"" + id + "\"[^>]*aria-labelledby=\"" + id + "-title\"", markup);
            Assert.Contains("<h2 id=\"" + id + "-title\">", markup);
        }
    }

    [Fact]
    public async Task DirectCorrectionRoutePreparesTheReferencedHistoricalSession()
    {
        var session = new HistorySession();
        var entry = session.History[0];
        var markup = await RenderAsync(session, $"/history/spots/{entry.SpotId}/{entry.SessionId}?edit=1");

        Assert.Contains("class=\"edit-loot-row\"", markup);
        Assert.Contains("value=\"10\"", markup);
        Assert.Contains("selected", markup);
    }

    [Fact]
    public async Task DirectCorrectionRouteRejectsANonexistentSession()
    {
        var markup = await RenderAsync(new(), $"/history/spots/{LootSpotCatalog.HermesiaId}/{Guid.NewGuid()}?edit=1");

        Assert.Contains("Diese Session ist nicht mehr vorhanden", markup);
        Assert.DoesNotContain("class=\"edit-loot-row\"", markup);
    }

    [Theory]
    [InlineData("/history?view=all")]
    [InlineData("/history/spots/hermesia")]
    public async Task LocallyModifiedTransferredSessionsStayVisiblyMarked(string path)
    {
        var session = new HistorySession();
        session.History = [session.History[0] with { GarmothUploadBlocked = true, GarmothLocallyModified = true }];
        var markup = await RenderAsync(session, path.Replace("/spots/hermesia", "/spots/" + LootSpotCatalog.HermesiaId));

        Assert.Contains("class=\"session-local-correction\"", markup);
        Assert.Contains("Lokal korrigiert; Garmoth unverändert", markup);
    }

    private static LootHistoryEntry Entry(string spotId, long quantity, DateTimeOffset at, string characterClass) => new()
    {
        SessionId = Guid.NewGuid(), SpotId = spotId, StartedAt = at, UpdatedAt = at.AddHours(1),
        Duration = TimeSpan.FromHours(1), CharacterClass = characterClass,
        Totals = new() { [Presentation.Profile(spotId)!.TrashItemName] = quantity },
        SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = true
    };

    private static async Task<string> RenderAsync(HistorySession session, string path)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITrackerSession>(session);
        services.AddSingleton<IAppUpdates>(new DisabledAppUpdates("test", "Updates deaktiviert."));
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager>(new StaticNavigation(path));
        services.AddSingleton<IComponentActivator>(new HistoryActivator(path));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<HistoryDashboard>();
            return WebUtility.HtmlDecode(rendered.ToHtmlString());
        });
    }

    // Supply the same values as the router without booting interactive browser services.
    private sealed class HistoryActivator(string path) : IComponentActivator
    {
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is not HistoryDashboard) return component;
            var uri = new Uri("https://0.0.0.1" + path);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            var properties = new Dictionary<string, string>
            {
                ["QueryView"] = "view", ["QuerySearch"] = "q", ["QueryDays"] = "days",
                ["QueryClass"] = "class", ["QueryPage"] = "page", ["QueryEdit"] = "edit"
            };
            foreach (var property in properties)
                if (query.TryGetValue(property.Value, out var value)) type.GetProperty(property.Key)!.SetValue(component, value.ToString());
            if (uri.AbsolutePath.StartsWith("/history/spots/", StringComparison.Ordinal))
            {
                type.GetProperty(nameof(HistoryDashboard.SpotId))!.SetValue(component, uri.AbsolutePath.Split('/')[3]);
                if (uri.AbsolutePath.Split('/') is { Length: 5 } parts && Guid.TryParse(parts[4], out var id))
                    type.GetProperty(nameof(HistoryDashboard.SessionId))!.SetValue(component, id);
            }
            return component;
        }
    }

    private sealed class RecordingNavigation : NavigationManager
    {
        public RecordingNavigation() => Initialize("https://0.0.0.1/", "https://0.0.0.1/history/spots/" + LootSpotCatalog.HermesiaId);
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation(string path) => Initialize("https://0.0.0.1/", "https://0.0.0.1" + path);
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("Static rendering must not invoke JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class HistorySession : ITrackerSession
    {
        public event Action? Changed { add { } remove { } }
        public TrackerState State { get; } = new() { AnalyzerAvailable = true };
        public TrackerPreferences Preferences { get; } = new() { UiLanguage = "de" };
        public IReadOnlyList<TrackerMonitor> Monitors { get; } = [];
        public IReadOnlyList<LootHistoryEntry> History { get; set; } = [Entry(LootSpotCatalog.HermesiaId, 10, DateTimeOffset.Now.AddDays(-1), "Warrior · Awakening")];
        public LootPriceSnapshot Prices { get; } = LootPriceCatalog.FixedSnapshot("eu");
        private static Task<TrackerCommandResult> Command() => throw new InvalidOperationException("Rendering must not invoke commands.");
        public Task<TrackerCommandResult> ToggleTrackingAsync() => Command();
        public Task<TrackerCommandResult> PauseAsync() => Command();
        public Task<TrackerCommandResult> NewSessionAsync() => Command();
        public Task<TrackerCommandResult> SetDemoAsync(bool enabled) => Command();
        public Task<TrackerCommandResult> InstallOcrLanguageAsync() => Command();
        public Task<TrackerCommandResult> RecheckOcrLanguageAsync() => Command();
        public Task<TrackerCommandResult> UploadAsync() => Command();
        public Task<TrackerCommandResult> UploadHistoryAsync(Guid id) => Command();
        public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid id, IReadOnlyDictionary<string, long> totals, string? characterClass = null) => Command();
        public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid id, string item, long quantity, long originalQuantity) => Command();
        public Task<TrackerCommandResult> DeleteHistoryAsync(Guid id) => Command();
        public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null, bool resumeAutomaticUpload = false) => throw new InvalidOperationException("Unexpected save.");
        public Task RefreshPricesAsync() => Command();
        public Task TickAsync() => Command();
        public Task RunPreparedUpdateAsync(Func<Task> install) => Command();
        public Task ShutdownAsync() => Command();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
