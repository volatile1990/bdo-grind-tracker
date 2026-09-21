using System.Net;
using System.Reflection;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class CombatStatsUiTests
{
    [Theory]
    [InlineData(CombatStatsCategory.General, "Allgemein", "general", LootSpotCatalog.AphrodonId)]
    [InlineData(CombatStatsCategory.Edania, "Edania", "edania", LootSpotCatalog.AphrodonId)]
    [InlineData(CombatStatsCategory.Demihuman, "Halbmenschen", "demihuman", "stars-end")]
    [InlineData(CombatStatsCategory.Kamasylvian, "Kamasilvia", "kamasylvian", "dehkia-tunkuta")]
    public async Task EveryApplicableCategoryHasReadableTextAndAColorClass(CombatStatsCategory category, string label, string css, string spotId)
    {
        var markup = await Render<CombatStatsReadout>(new PreviewTrackerSession(empty: true), new()
        {
            [nameof(CombatStatsReadout.Value)] = Observation(1650, 425, category),
            [nameof(CombatStatsReadout.SpotId)] = spotId,
        });

        Assert.Contains("combat-stats-" + css, markup);
        Assert.Contains(">" + label + "</span>", markup);
        Assert.Contains("AP 1.650, DP 425 · " + label, markup);
        Assert.Contains("AP <strong>1.650</strong>", markup);
        Assert.Contains("DP <strong>425</strong>", markup);
        Assert.DoesNotContain("AAP", markup);
    }

    [Theory]
    [InlineData(CombatStatsCategory.Edania, "Edania")]
    [InlineData(CombatStatsCategory.General, "Allgemein")]
    public async Task LiveUsesApplicableFreshValuesBeforeItsPreviousSessionObservation(CombatStatsCategory category, string label)
    {
        var tracker = EmptyLive();
        SetState(tracker, tracker.State with
        {
            IsRunning = true,
            CombatStats = Observation(1650, 425, category),
            SessionCombatStats = Observation(1400, 410, CombatStatsCategory.General),
        });
        var markup = await Render<LiveDashboard>(tracker);

        Assert.Contains($"AP 1.650, DP 425 · {label} · Im Spiel erkannt", markup);
        Assert.DoesNotContain("AP 1.400", markup);
        Assert.DoesNotContain("Zuletzt erkannt", markup);
        Assert.DoesNotContain("Garmoth-Build", markup);
        Assert.DoesNotContain("initial-garmoth-build-url", markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingFreshValuesUseTheSessionsLastObservationWithAnExplicitLabel(bool running)
    {
        var tracker = EmptyLive();
        SetState(tracker, tracker.State with
        {
            IsRunning = running,
            CombatStats = CombatStatsState.Unknown,
            SessionCombatStats = Observation(1400, 410, CombatStatsCategory.Edania),
        });
        var markup = await Render<LiveDashboard>(tracker);

        Assert.Contains("AP 1.400, DP 410 · Edania · Zuletzt im Spiel erkannt", markup);
        Assert.Contains(">Zuletzt erkannt</span>", markup);
    }

    [Fact]
    public async Task UnknownOrPartialValuesNeverBecomeZeroStatsOrAnAssumedCategory()
    {
        var tracker = EmptyLive();
        SetState(tracker, tracker.State with
        {
            CombatStats = new(1650, null, CombatStatsCategory.Edania, DateTimeOffset.UtcNow),
            SessionCombatStats = null,
        });
        var markup = await Render<LiveDashboard>(tracker);

        Assert.Contains("AP/DP noch nicht erkannt", markup);
        Assert.DoesNotContain("AP 1.650", markup);
        Assert.DoesNotContain("DP <strong>0", markup);
        Assert.DoesNotContain("combat-stats-edania", markup);
        Assert.DoesNotContain("combat-stats-general", markup);
    }

    [Theory]
    [InlineData(false, CombatStatsCategory.Edania, "Edania")]
    [InlineData(true, CombatStatsCategory.Edania, "Edania")]
    [InlineData(false, CombatStatsCategory.General, "Allgemein")]
    [InlineData(true, CombatStatsCategory.General, "Allgemein")]
    public async Task HistoryShowsEachSessionsOwnApplicableValuesAndNeverFillsOldEntriesFromLive(
        bool chronological, CombatStatsCategory category, string label)
    {
        var tracker = WithHistory(out _, category);
        var parameters = chronological
            ? new Dictionary<string, object?> { [nameof(HistoryDashboard.QueryView)] = "all" }
            : new Dictionary<string, object?> { [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.AphrodonId };
        var markup = await Render<HistoryDashboard>(tracker, parameters);

        Assert.Contains($"AP 1.250, DP 405 · {label} · In dieser Session erfasst", markup);
        Assert.Contains("AP/DP nicht erfasst", markup);
        Assert.DoesNotContain("AP 9.876", markup);
        Assert.DoesNotContain("DP 8.765", markup);
        Assert.DoesNotContain("Garmoth-Build", markup);
        if (!chronological) Assert.Contains("class=\"session-combat-column\" scope=\"col\">AP / DP", markup);
    }

    [Fact]
    public async Task EditingALootSessionKeepsItsCapturedStatsVisibleAsReadOnlyContext()
    {
        var tracker = WithHistory(out var sessionId);
        var markup = await Render<HistoryDashboard>(tracker, new()
        {
            [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.AphrodonId,
            [nameof(HistoryDashboard.SessionId)] = sessionId,
            [nameof(HistoryDashboard.QueryEdit)] = "1",
        });

        Assert.Contains("Im Spiel erfasste Werte dieser Session", markup);
        Assert.Contains("AP 1.250, DP 405 · Edania", markup);
        Assert.DoesNotContain("AP 9.876", markup);
    }

    [Theory]
    [InlineData(CombatStatsCategory.Demihuman)]
    [InlineData(CombatStatsCategory.Kamasylvian)]
    public async Task EdaniaLiveHidesIncompatibleFreshAndPreviousSessionValues(CombatStatsCategory category)
    {
        var tracker = EmptyLive();
        SetState(tracker, tracker.State with
        {
            IsRunning = true,
            CombatStats = Observation(1650, 425, category),
            SessionCombatStats = Observation(1400, 410, category),
        });
        var markup = await Render<LiveDashboard>(tracker);

        Assert.Contains("AP/DP noch nicht erkannt", markup);
        Assert.DoesNotContain("combat-stats-values", markup);
        Assert.DoesNotContain("combat-stats-demihuman", markup);
        Assert.DoesNotContain("combat-stats-kamasylvian", markup);
    }

    [Theory]
    [InlineData(CombatStatsCategory.Demihuman, CombatStatsCategory.Edania, "Edania")]
    [InlineData(CombatStatsCategory.Kamasylvian, CombatStatsCategory.General, "Allgemein")]
    public async Task IncompatibleFreshValuesKeepACompatibleFallbackClearlyMarkedAsPrevious(
        CombatStatsCategory freshCategory, CombatStatsCategory previousCategory, string label)
    {
        var tracker = EmptyLive();
        SetState(tracker, tracker.State with
        {
            IsRunning = true,
            CombatStats = Observation(1650, 425, freshCategory),
            SessionCombatStats = Observation(1400, 410, previousCategory),
        });
        var markup = await Render<LiveDashboard>(tracker);

        Assert.Contains($"AP 1.400, DP 410 · {label} · Zuletzt im Spiel erkannt", markup);
        Assert.Contains(">Zuletzt erkannt</span>", markup);
        Assert.DoesNotContain("AP 1.650", markup);
    }

    [Theory]
    [InlineData(CombatStatsCategory.General, "unknown-spot", true)]
    [InlineData(CombatStatsCategory.Edania, "unknown-spot", false)]
    [InlineData(CombatStatsCategory.Demihuman, "unknown-spot", false)]
    [InlineData(CombatStatsCategory.Kamasylvian, "unknown-spot", false)]
    [InlineData(CombatStatsCategory.General, null, true)]
    [InlineData(CombatStatsCategory.Edania, null, false)]
    public async Task UnknownSpotAllowsOnlyGeneralValues(CombatStatsCategory category, string? spotId, bool visible)
    {
        var tracker = EmptyLive();
        SetState(tracker, tracker.State with
        {
            SpotId = spotId, IsRunning = true,
            CombatStats = Observation(1650, 425, category), SessionCombatStats = null,
        });
        // An unselected spot is a valid live state. An invented future ID is
        // tested at the readout boundary, without invoking the live loot catalog.
        var markup = spotId is null
            ? await Render<LiveDashboard>(tracker)
            : await Render<CombatStatsReadout>(tracker, new()
            {
                [nameof(CombatStatsReadout.Value)] = tracker.State.CombatStats,
                [nameof(CombatStatsReadout.SpotId)] = spotId,
            });

        if (visible)
        {
            Assert.Contains("AP 1.650, DP 425 · Allgemein", markup);
            Assert.Contains("combat-stats-general", markup);
        }
        else
        {
            Assert.Contains("AP/DP noch nicht erkannt", markup);
            Assert.DoesNotContain("combat-stats-values", markup);
        }
        Assert.DoesNotContain("combat-stats-edania", markup);
        Assert.DoesNotContain("combat-stats-demihuman", markup);
        Assert.DoesNotContain("combat-stats-kamasylvian", markup);
    }

    [Theory]
    [InlineData(CombatStatsCategory.Demihuman, "spot")]
    [InlineData(CombatStatsCategory.Kamasylvian, "spot")]
    [InlineData(CombatStatsCategory.Demihuman, "all")]
    [InlineData(CombatStatsCategory.Kamasylvian, "all")]
    [InlineData(CombatStatsCategory.Demihuman, "edit")]
    [InlineData(CombatStatsCategory.Kamasylvian, "edit")]
    public async Task IncompatibleLegacyStatsAreHiddenInEveryHistoryView(CombatStatsCategory category, string view)
    {
        var tracker = WithHistory(out var sessionId, category);
        var parameters = view == "all"
            ? new Dictionary<string, object?> { [nameof(HistoryDashboard.QueryView)] = "all" }
            : new Dictionary<string, object?> { [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.AphrodonId };
        if (view == "edit")
        {
            parameters[nameof(HistoryDashboard.SessionId)] = sessionId;
            parameters[nameof(HistoryDashboard.QueryEdit)] = "1";
        }
        var markup = await Render<HistoryDashboard>(tracker, parameters);

        Assert.Contains("AP/DP nicht erfasst", markup);
        Assert.DoesNotContain("combat-stats-values", markup);
        Assert.DoesNotContain("combat-stats-demihuman", markup);
        Assert.DoesNotContain("combat-stats-kamasylvian", markup);
        Assert.DoesNotContain("AP 9.876", markup);
    }

    private static CombatStatsState Observation(int ap, int dp, CombatStatsCategory category) =>
        new(ap, dp, category, new DateTimeOffset(2026, 9, 21, 10, 30, 0, TimeSpan.Zero));

    private static PreviewTrackerSession EmptyLive()
    {
        var tracker = new PreviewTrackerSession(empty: true);
        SetState(tracker, tracker.State with { IsDemo = false, AnalyzerAvailable = true, SpotId = LootSpotCatalog.AphrodonId });
        return tracker;
    }

    private static PreviewTrackerSession WithHistory(out Guid sessionId, CombatStatsCategory category = CombatStatsCategory.Edania)
    {
        var tracker = EmptyLive();
        SetState(tracker, tracker.State with
        {
            CombatStats = Observation(9876, 8765, CombatStatsCategory.Edania),
            SessionCombatStats = Observation(9876, 8765, CombatStatsCategory.Edania),
        });
        var history = (List<LootHistoryEntry>)typeof(PreviewTrackerSession)
            .GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tracker)!;
        var entry = new LootHistoryEntry
        {
            SessionId = Guid.NewGuid(), SpotId = LootSpotCatalog.AphrodonId,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-3), UpdatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            Duration = TimeSpan.FromHours(1), CharacterClass = "Warrior · Awakening",
            Totals = new() { ["Branch of Abundance"] = 100 }, SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = true,
            CombatStats = Observation(1250, 405, category),
        };
        sessionId = entry.SessionId;
        history.Add(entry);
        history.Add(entry with { SessionId = Guid.NewGuid(), StartedAt = entry.StartedAt.AddDays(-1), CombatStats = null });
        return tracker;
    }

    private static void SetState(PreviewTrackerSession tracker, TrackerState state) =>
        typeof(PreviewTrackerSession).GetProperty(nameof(PreviewTrackerSession.State))!.SetValue(tracker, state);

    private static async Task<string> Render<T>(PreviewTrackerSession tracker, Dictionary<string, object?>? parameters = null)
        where T : IComponent
    {
        var directParameters = new Dictionary<string, object?>(parameters ?? new());
        var queryParameters = directParameters.Where(pair => typeof(T).GetProperty(pair.Key)?
            .IsDefined(typeof(SupplyParameterFromQueryAttribute), inherit: true) == true)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var name in queryParameters.Keys) directParameters.Remove(name);
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(tracker)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<NavigationManager, StaticNavigation>()
            .AddSingleton<IComponentActivator>(new QueryParameterActivator(typeof(T), queryParameters)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(directParameters))).ToHtmlString()));
    }

    // Match the router's query state before lifecycle methods run. Query-supplied
    // properties are cascading parameters and cannot be passed in ParameterView.
    private sealed class QueryParameterActivator(Type target, IReadOnlyDictionary<string, object?> queryParameters) : IComponentActivator
    {
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (type == target)
                foreach (var (name, value) in queryParameters) type.GetProperty(name)!.SetValue(component, value);
            return component;
        }
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/history");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
