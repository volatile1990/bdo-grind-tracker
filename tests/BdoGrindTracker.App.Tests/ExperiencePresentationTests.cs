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

public sealed class ExperiencePresentationTests
{
    [Theory]
    [InlineData("live", false)]
    [InlineData("live", true)]
    [InlineData("chronological", false)]
    [InlineData("chronological", true)]
    [InlineData("spot", false)]
    [InlineData("spot", true)]
    public async Task PartialExperienceUsesWholeActiveSessionTimeAndKeepsLossesSignedAcrossViews(string view, bool loss)
    {
        var gain = loss ? -.123m : .123m;
        var session = Session(gain, TimeSpan.FromMinutes(15));
        var markup = await Render(session, view);
        var sign = loss ? "-" : "+";

        Assert.Equal("≈ " + sign + "0,123 %", Text(markup, "experience-gain"));
        Assert.Equal("≈ " + sign + "0,246 % / h", Text(markup, "experience-hourly"));
        Assert.Contains("Teilweise erfasst: 15 Min. von 30 Min.", markup);
        Assert.Contains("Nettozuwachs in Prozentpunkten der Levelanzeige.", markup);
        Assert.Equal(loss, markup.Contains("is-loss", StringComparison.Ordinal));
        Assert.Contains(view == "live" ? "≈ 12 Min. aktiv" : "Agris ≈ 12 Min.", markup);
        if (view == "live")
        {
            Assert.Contains("Aktuell: Lvl. 61 · 0,579 %.", markup);
            var trackingButton = Regex.Match(markup, "<button class=\"button primary\"(?<attributes>[^>]*)>");
            Assert.True(trackingButton.Success);
            Assert.DoesNotContain("disabled", trackingButton.Groups["attributes"].Value);
        }
        if (view == "spot") Assert.Contains("class=\"session-experience-column\" scope=\"col\">ERFAHRUNG", markup);
    }

    [Theory]
    [InlineData("live")]
    [InlineData("chronological")]
    [InlineData("spot")]
    public async Task UnknownExperienceNeverCreatesZeroGainOrAZeroHourlyRate(string view)
    {
        var session = Session(null, null);
        var markup = await Render(session, view);

        Assert.Equal("—", Text(markup, "experience-gain"));
        Assert.DoesNotContain("class=\"experience-hourly\"", markup);
        Assert.DoesNotContain("+0,000 %", markup);
        if (view != "live") Assert.Contains("Erfahrung nicht erfasst.", markup);
    }

    [Fact]
    public void KnownUnchangedExperienceIsZeroAndNotUnknown()
    {
        var presentation = new ExperiencePresentation(0, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        Assert.True(presentation.HasObservation);
        Assert.Equal("+0,000 %", presentation.Gain);
        Assert.Equal("+0,000 %", presentation.Hourly);
        Assert.False(presentation.IsLoss);
    }

    [Fact]
    public void LevelTransitionIsPercentagePointsAndNotAnAbsoluteExperienceCount()
    {
        var presentation = new ExperiencePresentation(1.234m, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30), 61, 62);

        Assert.Equal("+1,234 %", presentation.Gain);
        Assert.Equal("+2,468 %", presentation.Hourly);
        Assert.Contains("Lvl. 61 → 62", presentation.Description);
        Assert.Contains("Levelwechsel in Prozentpunkten summiert", presentation.Description);
    }

    [Fact]
    public void MissingIntervalAndInvalidHourlyTimeNeverBecomeAnInventedRate()
    {
        Assert.Equal("—", new ExperiencePresentation(null, TimeSpan.Zero, TimeSpan.FromMinutes(5)).Gain);
        Assert.Equal("—", new ExperiencePresentation(0, TimeSpan.Zero, TimeSpan.FromMinutes(5)).Gain);
        Assert.Equal("—", new ExperiencePresentation(.123m, TimeSpan.FromMinutes(1), TimeSpan.Zero).Hourly);
        Assert.Equal("—", new ExperiencePresentation(decimal.MaxValue, TimeSpan.FromTicks(1), TimeSpan.FromTicks(1)).Hourly);
    }

    [Fact]
    public async Task PausedLiveSessionPreservesGainAndUsesTheSameCompletedActiveTime()
    {
        var session = Session(.123m, TimeSpan.FromMinutes(30));
        var active = await Render(session, "live");
        session.State = session.State with { IsRunning = false, CanPause = false };
        var paused = await Render(session, "live");

        Assert.Equal("+0,123 %", Text(paused, "experience-gain"));
        Assert.Equal("+0,246 % / h", Text(paused, "experience-hourly"));
        Assert.Equal(Text(active, "experience-gain"), Text(paused, "experience-gain"));
        Assert.Equal(Text(active, "experience-hourly"), Text(paused, "experience-hourly"));
    }

    private static string Text(string markup, string className)
    {
        var match = Regex.Match(markup, "<(?<tag>span|strong|small) class=\"" + className + "\"[^>]*>(?<content>.*?)</\\k<tag>>", RegexOptions.Singleline);
        Assert.True(match.Success, "Missing experience content: " + className);
        return WebUtility.HtmlDecode(Regex.Replace(match.Groups["content"].Value, "<[^>]+>", ""));
    }

    private static SnapshotSession Session(decimal? gain, TimeSpan? observed)
    {
        var elapsed = TimeSpan.FromMinutes(30);
        var entry = new LootHistoryEntry
        {
            SessionId = Guid.NewGuid(), SpotId = LootSpotCatalog.HermesiaId,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-1), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1).Add(elapsed),
            Duration = elapsed, CharacterClass = "Warrior · Awakening",
            Totals = new() { ["Black Crystal Fragment"] = 100 }, SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = true,
            AgrisActiveDuration = TimeSpan.FromMinutes(12), AgrisObservedDuration = elapsed,
            ExperienceGainedPercentagePoints = gain, ExperienceObservedDuration = observed,
            ExperienceStartLevel = gain is null ? null : 61, ExperienceEndLevel = gain is null ? null : 61,
        };
        return new()
        {
            State = new()
            {
                HasSession = true, IsRunning = true, CanPause = true, AnalyzerAvailable = true,
                SessionId = Guid.NewGuid(), Elapsed = elapsed, SpotId = LootSpotCatalog.HermesiaId,
                Agris = new(AgrisStatus.Active), AgrisActiveDuration = TimeSpan.FromMinutes(12), AgrisObservedDuration = elapsed,
                Experience = new(61, .579m), ExperienceGainedPercentagePoints = gain,
                ExperienceObservedDuration = observed ?? TimeSpan.Zero,
                ExperienceStartLevel = gain is null ? null : 61, ExperienceEndLevel = gain is null ? null : 61,
            },
            History = [entry],
        };
    }

    private static async Task<string> Render(SnapshotSession session, string view)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(session)
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<NavigationManager, StaticNavigation>()
            .AddSingleton<IComponentActivator>(new HistoryActivator(view == "chronological"));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var parameters = view == "spot"
            ? ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(HistoryDashboard.SpotId)] = LootSpotCatalog.HermesiaId })
            : ParameterView.Empty;
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = view == "live" ? await renderer.RenderComponentAsync<LiveDashboard>()
                : await renderer.RenderComponentAsync<HistoryDashboard>(parameters);
            return WebUtility.HtmlDecode(rendered.ToHtmlString());
        });
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
        public TrackerState State { get; set; } = new() { AnalyzerAvailable = true };
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
