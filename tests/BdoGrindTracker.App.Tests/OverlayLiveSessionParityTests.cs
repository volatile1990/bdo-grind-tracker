using System.Net;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
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

public sealed class OverlayLiveSessionParityTests
{
    [Theory]
    [InlineData(13000, "Unter Average", OverlayMetricTone.Muted)]
    [InlineData(13946, "Average Tier", OverlayMetricTone.Default)]
    [InlineData(16300, "High Tier", OverlayMetricTone.Positive)]
    [InlineData(18500, "Top Tier", OverlayMetricTone.Accent)]
    public async Task GrindRatingUsesIdenticalActiveSessionTotalsInLiveAndLateOpenedOverlay(
        long trash, string expected, OverlayMetricTone tone)
    {
        var state = ActiveState() with
        {
            SpotId = LootSpotCatalog.MagaiaId, Elapsed = TimeSpan.FromHours(1),
            Loot = new(new Dictionary<string, long> { ["Elion Follower's Helmet"] = trash, ["Caphras Stone"] = 90000 }, trash + 90000, 100),
            GrindBenchmark = new(LootSpotCatalog.MagaiaId, 13946, 16300, 18500,
                new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), "https://garmoth.com/grind-tracker/best-grind-spots/215", "Loot-Scroll Lv.2 · ohne Agris"),
            GrindBenchmarkStatus = "Garmoth ist nicht erreichbar. Gespeicherte Referenzen werden verwendet.",
        };
        await using var tracker = new SnapshotSession(state);
        using var overlay = new OverlayService(tracker);
        var metric = overlay.Snapshot.Metrics["grind-rating"];
        var markup = await RenderDashboardAsync(tracker);
        var label = Regex.Match(markup, "<span class=\"grind-rating-label\">(?<value>.*?)</span>");

        Assert.True(label.Success);
        Assert.Equal(expected, PlainText(label.Groups["value"].Value));
        Assert.Equal(expected, metric.Value);
        Assert.Equal(tone, metric.Tone);
        Assert.False(metric.IsWarning);
        Assert.Null(metric.Detail);
        Assert.Equal(new LiveSessionPresentation(state).GrindRating.Description, metric.Tooltip);
        Assert.Contains("Average ab 13.946 · High ab 16.300 · Top ab 18.500", metric.Tooltip);
        Assert.Contains("Loot-Scroll Lv.2 · ohne Agris", WebUtility.HtmlDecode(markup));
        Assert.Contains(state.GrindBenchmarkStatus, metric.Tooltip);
        Assert.Contains(state.GrindBenchmarkStatus, WebUtility.HtmlDecode(markup));
        Assert.Contains("Stand 10.09.2026", metric.Tooltip);
        Assert.Contains("Stand 10.09.2026", WebUtility.HtmlDecode(markup));

        tracker.SetState(state with { IsRunning = false, CanPause = false });
        Assert.Equal(metric, overlay.Snapshot.Metrics["grind-rating"]);
        using var reopened = new OverlayService(tracker);
        Assert.Equal(metric, reopened.Snapshot.Metrics["grind-rating"]);

        tracker.SetState(state with { Loot = new(new Dictionary<string, long> { ["Elion Follower's Helmet"] = 18500 }, 18500, 100) });
        Assert.Equal("Top Tier", overlay.Snapshot.Metrics["grind-rating"].Value);
        Assert.Equal("Top Tier", reopened.Snapshot.Metrics["grind-rating"].Value);
        Assert.Equal(0, tracker.CommandCalls);
    }

    [Theory]
    [InlineData(LootScrollStatus.Unknown, null, true, false, "Nicht erkannt", false)]
    [InlineData(LootScrollStatus.Active, null, true, false, "Aktiv", false)]
    [InlineData(LootScrollStatus.Active, 1, true, false, "Aktiv · Lvl. 1", false)]
    [InlineData(LootScrollStatus.Active, 2, true, false, "Aktiv · Lvl. 2", false)]
    [InlineData(LootScrollStatus.Inactive, null, true, false, "Inaktiv", true)]
    [InlineData(LootScrollStatus.Inactive, null, false, false, "Inaktiv", false)]
    [InlineData(LootScrollStatus.Inactive, null, true, true, "Inaktiv", false)]
    public async Task LootScrollStatusAndWarningsMatchTheLiveSessionWithoutBlockingTracking(
        LootScrollStatus status, int? level, bool running, bool demo, string expected, bool warning)
    {
        var state = ActiveState() with
        {
            IsRunning = running, IsDemo = demo,
            LootScroll = new(status, level, DateTimeOffset.UtcNow),
        };
        await using var tracker = new SnapshotSession(state);
        using var overlay = new OverlayService(tracker);
        var markup = await RenderDashboardAsync(tracker);

        Assert.Same(state.LootScroll, overlay.Snapshot.LootScroll);
        var metric = overlay.Snapshot.Metrics["loot-scroll"];
        Assert.Equal(expected, metric.Value);
        Assert.Equal(warning, metric.IsWarning);
        var detail = Regex.Match(markup, "<dt>Loot-Scroll</dt><dd[^>]*>(?<value>.*?)</dd>");
        Assert.True(detail.Success);
        Assert.Equal(expected, PlainText(detail.Groups["value"].Value));
        Assert.Equal(warning, markup.Contains("Loot-Scroll ist nicht aktiv.", StringComparison.Ordinal));
        Assert.Equal(warning, markup.Contains("loot-scroll-notice\" role=\"status\" aria-live=\"polite\"", StringComparison.Ordinal));
        Assert.True(overlay.Snapshot.CanToggleTracking);
        var trackingButton = Regex.Match(markup, "<button class=\"button primary\"(?<attributes>[^>]*)>");
        Assert.True(trackingButton.Success);
        Assert.DoesNotContain("disabled", trackingButton.Groups["attributes"].Value);
        Assert.Equal(0, tracker.CommandCalls);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("paused")]
    [InlineData("long")]
    [InlineData("partial")]
    [InlineData("unknown")]
    [InlineData("zero-elapsed")]
    [InlineData("partial-zero-elapsed")]
    public async Task EveryOverlayHeadlineMatchesTheRenderedLiveSession(string scenario)
    {
        var state = ActiveState();
        state = scenario switch
        {
            "paused" => state with { IsRunning = false, CanPause = false },
            "long" => state with { Elapsed = TimeSpan.FromHours(27) + TimeSpan.FromMinutes(15) },
            "partial" => PartiallyPriced(state),
            "unknown" => state with
            {
                Loot = new(new Dictionary<string, long> { ["Unpriced drop"] = 3 }, 3, 1),
                Silver = new(0, 0, 0, ["Unpriced drop"], [], false),
            },
            "zero-elapsed" => state with { Elapsed = TimeSpan.Zero },
            "partial-zero-elapsed" => PartiallyPriced(state) with { Elapsed = TimeSpan.Zero },
            _ => state,
        };
        await using var tracker = new SnapshotSession(state);
        using var overlay = new OverlayService(tracker);

        await AssertDashboardParityAsync(tracker, overlay.Snapshot);

        Assert.Equal(0, tracker.CommandCalls);
    }

    [Fact]
    public async Task ManualCorrectionUpdatesDashboardOverlayAndCurrentSampleWithoutStartingANewTimeframe()
    {
        var state = ActiveState() with
        {
            SilverHistory = Array.AsReadOnly(new[]
            {
                new SessionSilverSample(TimeSpan.FromMinutes(10), 600_000_000m),
                new SessionSilverSample(TimeSpan.FromMinutes(20), 700_000_000m),
                new SessionSilverSample(TimeSpan.FromMinutes(30), 800_000_000m),
            }),
        };
        await using var tracker = new SnapshotSession(state);
        using var overlay = new OverlayService(tracker);
        var before = overlay.Snapshot;
        await AssertDashboardParityAsync(tracker, before);

        var correctedTotals = new Dictionary<string, long> { ["Black Crystal Fragment"] = 960 };
        var correctedSilver = SilverValuation.Calculate(correctedTotals, tracker.Prices, tracker.Preferences.Tax);
        var corrected = state with
        {
            Loot = new(correctedTotals, 960, state.Loot.ConfirmedEventCount),
            Silver = correctedSilver,
            ManualLootItems = ["Black Crystal Fragment"],
            SilverHistory = Array.AsReadOnly(new[]
            {
                state.SilverHistory[0], state.SilverHistory[1],
                new SessionSilverSample(state.Elapsed, correctedSilver.AfterTax * 2),
            }),
        };
        tracker.SetState(corrected);

        await AssertDashboardParityAsync(tracker, overlay.Snapshot);
        Assert.Equal(before.Metrics["duration"], overlay.Snapshot.Metrics["duration"]);
        Assert.NotEqual(before.Metrics["silver-hour"].Value, overlay.Snapshot.Metrics["silver-hour"].Value);
        Assert.Equal(960, Assert.Single(overlay.Snapshot.Drops).Quantity);
        Assert.Same(corrected.SilverHistory, overlay.Snapshot.SilverHistory);
        Assert.Equal(800_000_000m, state.SilverHistory[^1].SilverPerHour);
        Assert.Equal(0, tracker.CommandCalls);
    }

    [Fact]
    public async Task LaterOverlayHasTheEntireSessionHistoryAndOverlayActionsCannotChangeSessionMetrics()
    {
        var firstSamples = Array.AsReadOnly(new[]
        {
            new SessionSilverSample(TimeSpan.FromMinutes(5), 610_000_000m),
            new SessionSilverSample(TimeSpan.FromMinutes(10), 650_000_000m),
            new SessionSilverSample(TimeSpan.FromMinutes(15), 630_000_000m),
        });
        var state = ActiveState() with { Elapsed = TimeSpan.FromMinutes(15), SilverHistory = firstSamples };
        await using var tracker = new SnapshotSession(state);
        using var earlyOverlay = new OverlayService(tracker);
        Assert.Same(firstSamples, earlyOverlay.Snapshot.SilverHistory);

        var completeSamples = Array.AsReadOnly(firstSamples.Concat(new[]
        {
            new SessionSilverSample(TimeSpan.FromMinutes(20), 700_000_000m),
            new SessionSilverSample(TimeSpan.FromMinutes(25), 730_000_000m),
            new SessionSilverSample(TimeSpan.FromMinutes(30), 740_000_000m),
        }).ToArray());
        state = state with { Elapsed = TimeSpan.FromMinutes(30), SilverHistory = completeSamples };
        tracker.SetState(state);
        using var lateOverlay = new OverlayService(tracker);
        var before = earlyOverlay.Snapshot;

        AssertSamePresentation(before, lateOverlay.Snapshot);
        Assert.Same(completeSamples, lateOverlay.Snapshot.SilverHistory);
        await AssertDashboardParityAsync(tracker, lateOverlay.Snapshot);

        foreach (var overlay in new[] { earlyOverlay, lateOverlay })
        {
            await overlay.SetPreviewAsync(true);
            Assert.True((await overlay.SaveAsync(OverlayCatalog.Preset("loot") with { Enabled = true })).Succeeded);
            Assert.True((await overlay.SavePositionAsync(.5, .4)).Succeeded);
            overlay.UpdateRuntime(new() { Previewing = true, IsVisible = true, Status = "Overlay sichtbar" });
            await overlay.SetPreviewAsync(false);
            Assert.True((await overlay.SaveAsync(overlay.Settings with { Enabled = false })).Succeeded);
            AssertSamePresentation(before, overlay.Snapshot);
        }

        Assert.Same(state, tracker.State);
        Assert.Same(completeSamples, tracker.State.SilverHistory);
        Assert.Equal(0, tracker.CommandCalls);

        tracker.SetState(state with { IsRunning = false, CanPause = false });
        AssertSamePresentation(earlyOverlay.Snapshot, lateOverlay.Snapshot);
        Assert.Same(completeSamples, lateOverlay.Snapshot.SilverHistory);
        await AssertDashboardParityAsync(tracker, lateOverlay.Snapshot);
    }

    private static async Task AssertDashboardParityAsync(ITrackerSession tracker, OverlaySnapshot overlay)
    {
        var markup = await RenderDashboardAsync(tracker);
        var cards = Regex.Matches(markup, "<article class=\"metric-card[^\"]*\">(?<content>.*?)</article>", RegexOptions.Singleline)
            .Select(match => match.Groups["content"].Value).ToArray();
        Assert.Equal(4, cards.Length);
        var kinds = new[] { "duration", "trash", "silver", "silver-hour" };
        for (var index = 0; index < cards.Length; index++)
        {
            var value = Regex.Match(cards[index], "<strong class=\"metric-value[^\"]*\">(?<value>.*?)</strong>", RegexOptions.Singleline);
            Assert.True(value.Success, $"Missing live-session metric: {kinds[index]}");
            Assert.Equal(PlainText(value.Groups["value"].Value), overlay.Metrics[kinds[index]].Value);
        }
        var trashHourly = Regex.Match(cards[1], "<span class=\"teal\">(?<value>.*?)</span>", RegexOptions.Singleline);
        Assert.True(trashHourly.Success);
        Assert.Equal(PlainText(trashHourly.Groups["value"].Value), overlay.Metrics["trash-hour"].Value);
        Assert.Equal(overlay.Metrics["silver-hour"].Value, overlay.Metrics["chart"].Value);
        var status = Regex.Match(markup, "<span class=\"live-session-status\"><span[^>]*></span>(?<value>.*?)</span>", RegexOptions.Singleline);
        Assert.True(status.Success);
        Assert.Equal(PlainText(status.Groups["value"].Value), overlay.Metrics["status"].Value);
        Assert.Equal(overlay.Metrics["status"].Value, overlay.Metrics["controls"].Value);
    }

    private static void AssertSamePresentation(OverlaySnapshot expected, OverlaySnapshot actual)
    {
        Assert.Equal(expected.Metrics.OrderBy(pair => pair.Key), actual.Metrics.OrderBy(pair => pair.Key));
        Assert.Equal(expected.Drops, actual.Drops);
        Assert.Equal(expected.RareDrops, actual.RareDrops);
        Assert.Equal(expected.SilverHistory, actual.SilverHistory);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.IsRunning, actual.IsRunning);
        Assert.Equal(expected.TrackingButtonLabel, actual.TrackingButtonLabel);
        Assert.Equal(expected.CanToggleTracking, actual.CanToggleTracking);
    }

    private static string PlainText(string markup) =>
        WebUtility.HtmlDecode(Regex.Replace(markup, "<[^>]+>", "")).Trim();

    private static TrackerState ActiveState()
    {
        var totals = new Dictionary<string, long> { ["Black Crystal Fragment"] = 2_387 };
        return new()
        {
            SessionId = Guid.NewGuid(), HasSession = true, IsRunning = true, CanPause = true, AnalyzerAvailable = true,
            SpotId = LootSpotCatalog.HermesiaId, CharacterClassId = "warrior-awakening", CharacterLabel = "Warrior · Awakening",
            Elapsed = TimeSpan.FromMinutes(30), Loot = new(totals, 2_387, 100),
            Silver = SilverValuation.Calculate(totals, LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default),
        };
    }

    private static TrackerState PartiallyPriced(TrackerState state) => state with
    {
        Loot = new(new Dictionary<string, long> { ["Black Crystal Fragment"] = 2_387, ["Unpriced drop"] = 1 }, 2_388, 101),
        Silver = state.Silver with { MissingItems = ["Unpriced drop"] },
    };

    private static async Task<string> RenderDashboardAsync(ITrackerSession tracker)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tracker);
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, StaticNavigation>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<LiveDashboard>(ParameterView.Empty)).ToHtmlString());
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("Reading live-session metrics must not invoke JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("https://0.0.0.1/", "https://0.0.0.1/");
        protected override void NavigateToCore(string uri, bool forceLoad) =>
            throw new InvalidOperationException("Reading live-session metrics must not navigate.");
    }

    private sealed class SnapshotSession(TrackerState state) : ITrackerSession
    {
        public event Action? Changed;
        public TrackerState State { get; private set; } = state;
        public TrackerPreferences Preferences { get; } = new() { MonitorDeviceName = "synthetic", GameLanguage = "de" };
        public IReadOnlyList<TrackerMonitor> Monitors { get; } = [new("synthetic", "Testbildschirm", new(0, 0, 1920, 1080), true)];
        public IReadOnlyList<LootHistoryEntry> History => [];
        public LootPriceSnapshot Prices { get; } = LootPriceCatalog.FixedSnapshot("eu");
        public int CommandCalls { get; private set; }
        public void SetState(TrackerState next) { State = next; Changed?.Invoke(); }
        private Task<TrackerCommandResult> Command() { CommandCalls++; return Task.FromResult(TrackerCommandResult.Success); }
        public Task<TrackerCommandResult> ToggleTrackingAsync() => Command();
        public Task<TrackerCommandResult> PauseAsync() => Command();
        public Task<TrackerCommandResult> InstallOcrLanguageAsync() => Command();
        public Task<TrackerCommandResult> RecheckOcrLanguageAsync() => Command();
        public Task<TrackerCommandResult> NewSessionAsync() => Command();
        public Task<TrackerCommandResult> SetDemoAsync(bool enabled) => Command();
        public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null, bool resumeAutomaticUpload = false)
        { CommandCalls++; return Task.FromResult(new PreferenceSaveResult()); }
        public Task<TrackerCommandResult> UploadAsync() => Command();
        public Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId) => Command();
        public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals, string? characterClass = null) => Command();
        public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity) => Command();
        public Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId) => Command();
        public Task RefreshPricesAsync() => Command();
        public Task TickAsync() => Command();
        public Task PrepareUpdateRestartAsync() => Task.CompletedTask;
        public Task RunPreparedUpdateAsync(Func<Task> install) => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
