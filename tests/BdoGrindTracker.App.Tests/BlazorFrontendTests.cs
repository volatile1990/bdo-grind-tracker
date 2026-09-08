using System.Net;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Tests;

public sealed class BlazorFrontendTests
{
    [Fact]
    public async Task SessionSilverUsesCurrentPricesInsteadOfSavedPartialDemoTotal()
    {
        var entry = HistoryEntry(Guid.NewGuid()) with
        {
            Totals = new Dictionary<string, long> { ["Black Stone"] = 100 },
            Duration = TimeSpan.FromHours(1), SilverAfterTax = 999999999, SilverIsComplete = false
        };
        var session = new SnapshotSession
        {
            History = [entry],
            Prices = new LootPriceSnapshot("eu", [new("Black Stone", 100, 0, LootPriceOrigin.LiveMarket, null)]),
            Preferences = new()
        };
        var markup = WebUtility.HtmlDecode(await RenderAsync<HistoryDashboard>(session,
            new Dictionary<string, object?> { [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId }));
        Assert.Contains("numeric gold\">6.500</td>", markup);
        Assert.DoesNotContain("* Enthält Sessions", markup);
        Assert.Equal(999999999, entry.SilverAfterTax);
        Assert.False(entry.SilverIsComplete);
    }

    [Fact]
    public async Task RunningSessionOffersPauseAndShowsExactLootWithoutStartingAnyActionDuringRender()
    {
        var session = new SnapshotSession { State = ActiveState() };
        var markup = await RenderAsync<LiveDashboard>(session);
        Assert.False(IsDisabled(ButtonAttributes(markup, "Pausieren")));
        Assert.Contains("1.582", markup);
        Assert.Contains("00:30:00", markup);
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task SubmittedSessionCannotResumeFromTheLiveScreen()
    {
        var session = new SnapshotSession
        {
            State = ActiveState() with { IsRunning = false, IsSubmitted = true, UploadBlocked = true }
        };
        var markup = await RenderAsync<LiveDashboard>(session);
        Assert.True(IsDisabled(ButtonAttributes(markup, "Fortsetzen")));
        Assert.False(IsDisabled(ButtonAttributes(markup, "Neue Session")));
        AssertNoGarmothControls(markup);
    }

    [Fact]
    public async Task LiveGeneralSettingsAndHistoryContainNoConnectionOrUploadControls()
    {
        var state = ActiveState() with { UploadBlocked = true };
        var session = new SnapshotSession { State = state, History = [HistoryEntry(Guid.NewGuid())] };
        var live = await RenderAsync<LiveDashboard>(session);
        Assert.False(IsDisabled(ButtonAttributes(live, "Pausieren")));
        AssertNoGarmothControls(live);
        AssertNoGarmothControls(await RenderAsync<TrackerSettings>(session));
        AssertNoGarmothControls(await RenderAsync<HistoryDashboard>(session, new Dictionary<string, object?>
        {
            [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId
        }));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task SpotHistoryRendersTheCompactLootMatrixWithPinnedSessionColumns()
    {
        var session = new SnapshotSession
        {
            History = [HistoryEntry(Guid.NewGuid())]
        };

        var markup = WebUtility.HtmlDecode(await RenderAsync<HistoryDashboard>(session,
            new Dictionary<string, object?>
            {
                [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId
            }));

        Assert.Contains("spot-session-table-scroll", markup);
        Assert.Contains("WIE LANG HER", markup);
        Assert.Contains("GRINDZEIT", markup);
        Assert.Contains("SILBER / H", markup);
        Assert.Contains("Black Crystal Fragment", markup);
        Assert.Contains("Aktionen für diese Session", markup);
        Assert.Contains("Klasse dieser Session", markup);
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task ManualColumnOrderRemainsWithoutLootPool()
    {
        var session = new SnapshotSession
        {
            State = ActiveState() with { Loot = ActiveState().Loot with { Totals = new Dictionary<string, long> { ["Black Stone"] = 2 } } },
            History = [HistoryEntry(Guid.NewGuid())],
            Preferences = new() { LootColumnOrders = new Dictionary<string, string[]> { [LootSpotCatalog.HermesiaId] = ["Black Stone", "Black Crystal Fragment"] } }
        };
        var markup = WebUtility.HtmlDecode(await RenderAsync<HistoryDashboard>(session,
            new Dictionary<string, object?> { [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId }));
        Assert.DoesNotContain("spot-loot-pool", markup);

        var headers = Regex.Matches(markup, "<th class=\"session-loot-column\"[^>]*title=\"([^\"]+)\"");
        Assert.StartsWith("Black Stone", headers[0].Groups[1].Value);
        Assert.StartsWith("Black Crystal Fragment", headers[1].Groups[1].Value);
        var live = WebUtility.HtmlDecode(await RenderAsync<LiveDashboard>(session));
        Assert.DoesNotContain("favorite-loot-row", live);
        Assert.DoesNotContain("★ FAVORIT", live);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CaptureSettingsStayLockedForBothRunningAndPausedSessions(bool running)
    {
        var session = new SnapshotSession { State = ActiveState() with { IsRunning = running } };
        var markup = await RenderAsync<TrackerSettings>(session);
        Assert.True(IsDisabled(FieldSelectAttributes(markup, "Spielbildschirm")));
        Assert.Equal(running, IsDisabled(FieldSelectAttributes(markup, "Charakterklasse")));
        Assert.True(IsDisabled(ToggleAttributes(markup, "Event-Loot mitzählen")));
        Assert.True(IsDisabled(ToggleAttributes(markup, "Diese Session aufzeichnen")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GarmothConnectionUsesAnEmptyMaskedInputForBothNewAndStoredKeys(bool hasSavedKey)
    {
        var session = new SnapshotSession { State = ActiveState() with { HasApiKey = hasSavedKey } };
        var markup = await RenderAsync<GarmothDashboard>(session);
        var input = Regex.Matches(WebUtility.HtmlDecode(markup), "<input(?<attributes>[^>]*)>")
            .Single(match => match.Groups["attributes"].Value.Contains("aria-label=\"Garmoth-API-Schlüssel\"", StringComparison.Ordinal));
        var attributes = input.Groups["attributes"].Value;
        Assert.Contains("type=\"password\"", attributes);
        var value = Regex.Match(attributes, "(?:^|\\s)value=\"(?<value>[^\"]*)\"");
        Assert.True(!value.Success || value.Groups["value"].Length == 0);
        var text = WebUtility.HtmlDecode(markup);
        Assert.Contains(hasSavedKey ? "Gespeichert" : "Nicht hinterlegt", text);
        Assert.Equal(0, session.CommandCalls);
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("busy")]
    [InlineData("blocked")]
    [InlineData("submitted")]
    public async Task GarmothCurrentUploadRespectsDemoBusyAndDuplicateGuards(string reason)
    {
        var state = ActiveState();
        state = reason switch
        {
            "demo" => state with { IsDemo = true },
            "busy" => state with { IsBusy = true },
            "blocked" => state with { UploadBlocked = true },
            _ => state with { IsRunning = false, IsSubmitted = true }
        };
        var session = new SnapshotSession { State = state };
        var markup = await RenderAsync<GarmothDashboard>(session);
        Assert.True(IsDisabled(AriaButtonAttributes(markup, "Aktuellen Session-Anteil hochladen")));
        Assert.Equal(0, session.CommandCalls);
    }

    [Fact]
    public async Task GarmothShowsAPartialSilverValueHonestlyWhileAllowingItsKnownAmount()
    {
        var totals = new Dictionary<string, long> { ["Black Crystal Fragment"] = 1_582, ["Black Stone"] = 2 };
        var state = ActiveState() with
        {
            Loot = new(totals, 1_584, 102),
            Silver = SilverValuation.Calculate(totals, LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default)
        };
        Assert.False(state.Silver.IsComplete);
        Assert.True(state.Silver.HasKnownValue);
        var session = new SnapshotSession { State = state };
        var markup = await RenderAsync<GarmothDashboard>(session);
        Assert.Contains("Teilbetrag", WebUtility.HtmlDecode(markup));
        Assert.False(IsDisabled(AriaButtonAttributes(markup, "Aktuellen Session-Anteil hochladen")));
    }

    [Fact]
    public async Task GarmothHistoryExcludesTheCurrentSessionAndDisablesAlreadyTransferredRows()
    {
        var state = ActiveState() with { IsRunning = false };
        var session = new SnapshotSession
        {
            State = state,
            History =
            [
                HistoryEntry(state.SessionId) with { CharacterClass = "CURRENT-ROW-MUST-NOT-REAPPEAR" },
                HistoryEntry(Guid.NewGuid()),
                HistoryEntry(Guid.NewGuid()) with { GarmothUploadBlocked = true },
                HistoryEntry(Guid.NewGuid()) with { GarmothUploadedAt = DateTimeOffset.UnixEpoch.AddHours(1) }
            ]
        };
        var markup = await RenderAsync<GarmothDashboard>(session);
        Assert.DoesNotContain("CURRENT-ROW-MUST-NOT-REAPPEAR", markup);
        var uploadButtons = AriaButtons(markup, "Gespeicherte Session hochladen").ToArray();
        Assert.Equal(3, uploadButtons.Length);
        Assert.Equal(2, uploadButtons.Count(IsDisabled));
        Assert.False(IsDisabled(AriaButtonAttributes(markup, "Aktuellen Session-Anteil hochladen")));
        Assert.Equal(0, session.CommandCalls);
    }

    private static TrackerState ActiveState()
    {
        var totals = new Dictionary<string, long> { ["Black Crystal Fragment"] = 1_582 };
        return new()
        {
            SessionId = Guid.NewGuid(), HasSession = true, IsRunning = true, AnalyzerAvailable = true,
            HasApiKey = true, SpotId = LootSpotCatalog.HermesiaId, Elapsed = TimeSpan.FromMinutes(30),
            CharacterClassId = "warrior-awakening", CharacterLabel = "Warrior · Awakening",
            Loot = new(totals, 1_582, 100),
            Silver = SilverValuation.Calculate(totals, LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default)
        };
    }

    private static LootHistoryEntry HistoryEntry(Guid id) => new()
    {
        SessionId = id, StartedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch.AddHours(1),
        Duration = TimeSpan.FromHours(1), SpotId = LootSpotCatalog.HermesiaId, CharacterClass = "Warrior · Awakening",
        Totals = new() { ["Black Crystal Fragment"] = 1_582 }, SilverBeforeTax = 1_582 * 160_539m,
        SilverAfterTax = 1_582 * 160_539m, SilverIsComplete = true
    };

    private static async Task<string> RenderAsync<TComponent>(ITrackerSession session,
        IDictionary<string, object?>? parameters = null) where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(session);
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, StaticNavigation>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<TComponent>(parameters is null
                ? ParameterView.Empty : ParameterView.FromDictionary(parameters));
            return rendered.ToHtmlString();
        });
    }

    private static string ButtonAttributes(string markup, string label)
    {
        var button = Regex.Matches(markup, "<button(?<attributes>[^>]*)>(?<content>.*?)</button>", RegexOptions.Singleline)
            .First(match => WebUtility.HtmlDecode(Regex.Replace(match.Groups["content"].Value, "<[^>]+>", "")).Trim() == label);
        return button.Groups["attributes"].Value;
    }

    private static IEnumerable<string> AriaButtons(string markup, string label) =>
        Regex.Matches(WebUtility.HtmlDecode(markup), "<button(?<attributes>[^>]*)>")
            .Where(match => match.Groups["attributes"].Value.Contains("aria-label=\"" + label + "\"", StringComparison.Ordinal))
            .Select(match => match.Groups["attributes"].Value);

    private static string AriaButtonAttributes(string markup, string label) => Assert.Single(AriaButtons(markup, label));

    private static void AssertNoGarmothControls(string markup)
    {
        Assert.DoesNotContain("type=\"password\"", markup);
        foreach (Match match in Regex.Matches(WebUtility.HtmlDecode(markup), "<button(?<attributes>[^>]*)>(?<content>.*?)</button>", RegexOptions.Singleline))
        {
            var control = match.Groups["attributes"].Value + " " + Regex.Replace(match.Groups["content"].Value, "<[^>]+>", "");
            Assert.DoesNotMatch("(?i)hochladen|API-Schlüssel|API-Key", control);
        }
        Assert.DoesNotContain("Stündlich automatisch hochladen", WebUtility.HtmlDecode(markup));
    }

    private static string FieldSelectAttributes(string markup, string label)
    {
        var match = Regex.Match(WebUtility.HtmlDecode(markup),
            "<span>" + Regex.Escape(label) + "</span>\\s*<select(?<attributes>[^>]*)>");
        Assert.True(match.Success, $"Missing settings select: {label}");
        return match.Groups["attributes"].Value;
    }

    private static string ToggleAttributes(string markup, string label)
    {
        var match = Regex.Match(WebUtility.HtmlDecode(markup),
            "<strong>" + Regex.Escape(label) + "</strong>(?:(?!</label>).)*?<input(?<attributes>[^>]*)>", RegexOptions.Singleline);
        Assert.True(match.Success, $"Missing settings switch: {label}");
        return match.Groups["attributes"].Value;
    }

    private static bool IsDisabled(string attributes) => Regex.IsMatch(attributes, @"\bdisabled(?:\s|=|$)");

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("Static component rendering must not invoke JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("https://0.0.0.1/", "https://0.0.0.1/");
        protected override void NavigateToCore(string uri, bool forceLoad) =>
            throw new InvalidOperationException("Static rendering must not navigate.");
    }

    private sealed class SnapshotSession : ITrackerSession
    {
        public event Action? Changed { add { } remove { } }
        public TrackerState State { get; init; } = new();
        public TrackerPreferences Preferences { get; init; } = new() { MonitorDeviceName = "synthetic" };
        public IReadOnlyList<TrackerMonitor> Monitors { get; } = [new("synthetic", "Testbildschirm", new(0, 0, 1920, 1080), true)];
        public IReadOnlyList<LootHistoryEntry> History { get; init; } = [];
        public LootPriceSnapshot Prices { get; init; } = LootPriceCatalog.FixedSnapshot("eu");
        public int CommandCalls { get; private set; }
        private Task Command() { CommandCalls++; return Task.CompletedTask; }
        public Task ToggleTrackingAsync() => Command();
        public Task PauseAsync() => Command();
        public Task NewSessionAsync() => Command();
        public Task SetDemoAsync(bool enabled) => Command();
        public Task SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null, bool resumeAutomaticUpload = false) => Command();
        public Task UploadAsync() => Command();
        public Task UploadHistoryAsync(Guid sessionId) => Command();
        public Task UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals,
            string? characterClass = null) => Command();
        public Task DeleteHistoryAsync(Guid sessionId) => Command();
        public Task RefreshPricesAsync() => Command();
        public Task TickAsync() => Command();
        public Task ShutdownAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
