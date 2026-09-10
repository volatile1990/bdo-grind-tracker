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

public sealed class AgrisPresentationTests
{
    [Theory]
    [InlineData(AgrisStatus.Active, "Aktiv")]
    [InlineData(AgrisStatus.Inactive, "Inaktiv")]
    [InlineData(AgrisStatus.Unknown, "Nicht erkannt")]
    public async Task LiveStatusAndAccumulatedActivityAreShownSeparatelyWithoutBlockingTracking(AgrisStatus status, string expected)
    {
        var session = new SnapshotSession
        {
            State = LiveState() with
            {
                Agris = new(status), AgrisActiveDuration = TimeSpan.FromMinutes(12),
                AgrisObservedDuration = TimeSpan.FromHours(1),
            },
        };
        var markup = await Render<LiveDashboard>(session);
        var agris = Extract(markup, "agris-live-value");

        Assert.Contains("<span>" + expected + "</span>", agris);
        Assert.Contains("≈ 12 Min. aktiv", agris);
        Assert.DoesNotContain(" *", agris);
        Assert.Contains("Agris aktiv: ungefähr 12 Min.", agris);
        var trackingButton = Regex.Match(markup, "<button class=\"button primary\"(?<attributes>[^>]*)>");
        Assert.True(trackingButton.Success);
        Assert.DoesNotContain("disabled", trackingButton.Groups["attributes"].Value);
        Assert.Contains("Pausieren", markup);
    }

    [Fact]
    public async Task LiveSessionWithoutObservationsDoesNotInventZeroAgrisActivity()
    {
        var markup = await Render<LiveDashboard>(new SnapshotSession { State = LiveState() });
        var agris = Extract(markup, "agris-live-value");

        Assert.Contains("Nicht erkannt", agris);
        Assert.DoesNotContain("Min.", agris);
        Assert.DoesNotContain("aktiv", agris);
    }

    [Fact]
    public async Task LiveSessionMarksUnobservedTimeWithoutLosingItsKnownActiveDuration()
    {
        var session = new SnapshotSession
        {
            State = LiveState() with
            {
                Agris = new(AgrisStatus.Active), AgrisActiveDuration = TimeSpan.FromMinutes(12),
                AgrisObservedDuration = TimeSpan.FromMinutes(30),
            },
        };
        var markup = await Render<LiveDashboard>(session);
        var agris = Extract(markup, "agris-live-value");

        Assert.Contains("≈ 12 Min. * aktiv", agris);
        Assert.Contains("Nicht erkannt: 30 Min.", agris);
    }

    [Theory]
    [InlineData(true, "legacy")]
    [InlineData(false, "legacy")]
    [InlineData(true, "unobserved")]
    [InlineData(false, "unobserved")]
    [InlineData(true, "inactive")]
    [InlineData(false, "inactive")]
    [InlineData(true, "partial")]
    [InlineData(false, "partial")]
    [InlineData(true, "observed")]
    [InlineData(false, "observed")]
    public async Task ChronologicalAndSpotSessionsPreserveUnknownCoverageAndTheSavedActiveTime(bool chronological, string scenario)
    {
        var entry = Entry();
        entry = scenario switch
        {
            "unobserved" => entry with { AgrisActiveDuration = TimeSpan.Zero, AgrisObservedDuration = TimeSpan.Zero },
            "inactive" => entry with { AgrisActiveDuration = TimeSpan.Zero, AgrisObservedDuration = entry.Duration },
            "partial" => entry with { AgrisActiveDuration = TimeSpan.FromMinutes(12), AgrisObservedDuration = TimeSpan.FromMinutes(30) },
            "observed" => entry with { AgrisActiveDuration = TimeSpan.FromMinutes(12), AgrisObservedDuration = entry.Duration },
            _ => entry,
        };
        var session = new SnapshotSession { History = [entry] };
        var markup = await Render<HistoryDashboard>(session, chronological);
        var agris = Extract(markup, "session-agris-time");
        var expected = scenario switch
        {
            "inactive" => "Agris ≈ 0 Min.",
            "partial" => "Agris ≈ 12 Min. *",
            "observed" => "Agris ≈ 12 Min.",
            _ => "Agris —",
        };

        Assert.Equal(expected, WebUtility.HtmlDecode(Regex.Replace(agris, "<[^>]+>", "")));
        if (scenario is "legacy" or "unobserved")
        {
            Assert.Contains(scenario == "legacy" ? "Agris nicht erfasst." : "Agris nicht erkannt.", agris);
            Assert.DoesNotContain("0 Min.", agris);
        }
        else if (scenario == "partial") Assert.Contains("Nicht erkannt: 30 Min.", agris);
        else Assert.DoesNotContain("Nicht erkannt:", agris);
        Assert.Contains(chronological ? "chronological-session-card" : "spot-session-table", markup);
    }

    [Fact]
    public void PositiveSubMinuteActivityAndCoverageGapsDoNotLookLikeZeroMinutes()
    {
        var presentation = new AgrisPresentation(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(55), TimeSpan.FromMinutes(1));

        Assert.Equal("≈ unter 1 Min. *", presentation.Duration);
        Assert.Contains("Nicht erkannt: unter 1 Min.", presentation.Description);
    }

    private static string Extract(string markup, string className)
    {
        var match = Regex.Match(markup, "<(?<tag>dd|small) class=\"" + className + "\"[^>]*>.*?</\\k<tag>>", RegexOptions.Singleline);
        Assert.True(match.Success, "Missing Agris content: " + className);
        return match.Value;
    }

    private static TrackerState LiveState() => new()
    {
        HasSession = true, IsRunning = true, CanPause = true, AnalyzerAvailable = true,
        SessionId = Guid.NewGuid(), Elapsed = TimeSpan.FromHours(1),
        SpotId = LootSpotCatalog.HermesiaId,
    };

    private static LootHistoryEntry Entry() => new()
    {
        SessionId = Guid.NewGuid(), SpotId = LootSpotCatalog.HermesiaId,
        StartedAt = DateTimeOffset.UtcNow.AddDays(-1), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1).AddHours(1),
        Duration = TimeSpan.FromHours(1), CharacterClass = "Warrior · Awakening",
        Totals = new() { ["Black Crystal Fragment"] = 100 }, SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = true,
    };

    private static async Task<string> Render<T>(SnapshotSession session, bool chronological = false) where T : IComponent
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(session)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<NavigationManager, StaticNavigation>()
            .AddSingleton<IComponentActivator>(new HistoryActivator(chronological));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var parameters = typeof(T) == typeof(HistoryDashboard) && !chronological
            ? ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId })
            : ParameterView.Empty;
        return await renderer.Dispatcher.InvokeAsync(async () =>
            WebUtility.HtmlDecode((await renderer.RenderComponentAsync<T>(parameters)).ToHtmlString()));
    }

    private sealed class HistoryActivator(bool chronological) : IComponentActivator
    {
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is HistoryDashboard && chronological)
                type.GetProperty(nameof(HistoryDashboard.QueryView))!.SetValue(component, "all");
            return component;
        }
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("https://0.0.0.1/", "https://0.0.0.1/");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Rendering must not navigate.");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => throw new InvalidOperationException("Rendering must not invoke JavaScript.");
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    private sealed class SnapshotSession : ITrackerSession
    {
        public event Action? Changed { add { } remove { } }
        public TrackerState State { get; init; } = new() { AnalyzerAvailable = true };
        public TrackerPreferences Preferences { get; } = new();
        public IReadOnlyList<TrackerMonitor> Monitors => [];
        public IReadOnlyList<LootHistoryEntry> History { get; init; } = [];
        public LootPriceSnapshot Prices { get; } = LootPriceCatalog.FixedSnapshot("eu");
        private static Task<TrackerCommandResult> Command() => throw new InvalidOperationException("Rendering must not issue tracker commands.");
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
        public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null, bool resumeAutomaticUpload = false) => throw new InvalidOperationException("Rendering must not save preferences.");
        public Task RefreshPricesAsync() => Command();
        public Task TickAsync() => Command();
        public Task PrepareUpdateRestartAsync() => Command();
        public Task RunPreparedUpdateAsync(Func<Task> install) => Command();
        public Task ShutdownAsync() => Command();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
