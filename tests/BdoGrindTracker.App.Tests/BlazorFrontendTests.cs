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
    public async Task SubmittedSessionCannotResumeOrUploadAgainFromTheLiveScreen()
    {
        var session = new SnapshotSession
        {
            State = ActiveState() with { IsRunning = false, IsSubmitted = true, UploadBlocked = true }
        };
        var markup = await RenderAsync<LiveDashboard>(session);
        Assert.True(IsDisabled(ButtonAttributes(markup, "Fortsetzen")));
        Assert.True(IsDisabled(ButtonAttributes(markup, "Session abgeschlossen")));
        Assert.False(IsDisabled(ButtonAttributes(markup, "Neue Session")));
    }

    [Fact]
    public async Task UnknownUploadOutcomeStillAllowsPausingButDisablesAnotherUpload()
    {
        var session = new SnapshotSession { State = ActiveState() with { UploadBlocked = true } };
        var markup = await RenderAsync<LiveDashboard>(session);
        Assert.False(IsDisabled(ButtonAttributes(markup, "Pausieren")));
        Assert.True(IsDisabled(ButtonAttributes(markup, "Session hochladen")));
        Assert.Contains("erneute Übertragung gesperrt", WebUtility.HtmlDecode(markup));
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

    [Fact]
    public async Task DemoExplainsItsLocalSampleDataAndCannotUpload()
    {
        var session = new SnapshotSession { State = ActiveState() with { HasSession = false, IsRunning = false, IsDemo = true } };
        var markup = await RenderAsync<LiveDashboard>(session);
        Assert.Contains("Beispieldaten werden weder im Verlauf gespeichert noch hochgeladen.", WebUtility.HtmlDecode(markup));
        Assert.True(IsDisabled(ButtonAttributes(markup, "Session hochladen")));
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

    private static async Task<string> RenderAsync<TComponent>(ITrackerSession session) where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(session);
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<TComponent>();
            return rendered.ToHtmlString();
        });
    }

    private static string ButtonAttributes(string markup, string label)
    {
        var button = Regex.Matches(markup, "<button(?<attributes>[^>]*)>(?<content>.*?)</button>", RegexOptions.Singleline)
            .First(match => WebUtility.HtmlDecode(Regex.Replace(match.Groups["content"].Value, "<[^>]+>", "")).Trim() == label);
        return button.Groups["attributes"].Value;
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

    private sealed class SnapshotSession : ITrackerSession
    {
        public event Action? Changed { add { } remove { } }
        public TrackerState State { get; init; } = new();
        public TrackerPreferences Preferences { get; } = new() { MonitorDeviceName = "synthetic" };
        public IReadOnlyList<TrackerMonitor> Monitors { get; } = [new("synthetic", "Testbildschirm", new(0, 0, 1920, 1080), true)];
        public IReadOnlyList<LootHistoryEntry> History => [];
        public LootPriceSnapshot Prices { get; } = LootPriceCatalog.FixedSnapshot("eu");
        public int CommandCalls { get; private set; }
        private Task Command() { CommandCalls++; return Task.CompletedTask; }
        public Task ToggleTrackingAsync() => Command();
        public Task PauseAsync() => Command();
        public Task NewSessionAsync() => Command();
        public Task SetDemoAsync(bool enabled) => Command();
        public Task SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null) => Command();
        public Task UploadAsync() => Command();
        public Task UploadHistoryAsync(Guid sessionId) => Command();
        public Task UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals) => Command();
        public Task DeleteHistoryAsync(Guid sessionId) => Command();
        public Task RefreshPricesAsync() => Command();
        public Task TickAsync() => Command();
        public Task ShutdownAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
