using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class SessionShareUiTests
{
    private const string Png = "data:image/png;base64,c2Vzc2lvbg==";

    [Fact]
    public async Task ShareDialogConnectsBackdropClicksToItsExistingCloseAction()
    {
        await using var harness = new UiHarness();
        await harness.Render<SessionShareDialog>();
        var markup = await harness.Markup();

        Assert.Contains("data-backdrop-close", markup);
        Assert.Contains("data-dialog-close", markup);
        Assert.Contains("Close dialog", markup);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public async Task LiveShareIsAvailableForRunningAndDemoSessions(bool hasSession, bool demo, bool enabled)
    {
        await using var harness = new UiHarness();
        Change(harness.Tracker, harness.Tracker.State with { HasSession = hasSession, IsRunning = hasSession, IsDemo = demo });
        await harness.Render<LiveDashboard>();
        var button = Regex.Match(await harness.Markup(), "<button[^>]*class=\"[^\"]*session-share-trigger[^\"]*\"[^>]*>.*?</button>", RegexOptions.Singleline);

        Assert.True(button.Success);
        Assert.Contains("Share session", button.Value);
        Assert.Equal(!enabled, button.Value.Contains("disabled", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothHistoryViewsExposeShareForEveryVisibleSession(bool chronological)
    {
        await using var harness = new UiHarness(chronological);
        await harness.Render<HistoryDashboard>();
        var html = await harness.Markup();
        var shareButtons = Regex.Matches(html, "<button[^>]*>.*?</button>", RegexOptions.Singleline)
            .Where(button => button.Value.Contains(chronological ? ">Share session</button>" : "class=\"icon-button session-share-button\"", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(shareButtons);
        Assert.All(shareButtons, button => Assert.DoesNotContain("disabled", button.Value));
    }

    [Fact]
    public async Task SharingTheCurrentHistorySessionUsesFreshLiveValuesAndKeepsTheImageFrozen()
    {
        await using var harness = new UiHarness();
        var saved = harness.Tracker.History.First(entry => entry.SpotId == LootSpotCatalog.HermesiaId);
        var trashName = Presentation.Profile(saved.SpotId)!.TrashItemName;
        Change(harness.Tracker, harness.Tracker.State with
        {
            SessionId = saved.SessionId, HasSession = true, IsDemo = false, IsRunning = true, SpotId = saved.SpotId,
            Elapsed = TimeSpan.FromMinutes(17), Loot = new(new Dictionary<string, long> { [trashName] = 7654 }, 7654, 40),
        });
        await harness.Render<HistoryDashboard>();

        await harness.Call(harness.Component<HistoryDashboard>(), "ShareSession", saved);
        var snapshot = Assert.Single(harness.JavaScript.Snapshots);
        Assert.Equal("00:17:00", snapshot.Metrics[0].Value);
        Assert.Equal("7,654", snapshot.Metrics[1].Value);

        await harness.Renderer.Dispatcher.InvokeAsync(() => Change(harness.Tracker, harness.Tracker.State with
        {
            Elapsed = TimeSpan.FromMinutes(28), Loot = new(new Dictionary<string, long> { [trashName] = 99999 }, 99999, 70),
        }));
        Assert.Single(harness.JavaScript.Snapshots);
        Assert.Equal("7,654", snapshot.Metrics[1].Value);
        Assert.Contains(Png, await harness.Markup());
        Assert.True(harness.Tracker.State.IsRunning);
        Assert.Equal(saved, harness.Tracker.History.Single(entry => entry.SessionId == saved.SessionId));
    }

    [Fact]
    public async Task HistoricalShareUsesTheSelectedSavedSession()
    {
        await using var harness = new UiHarness();
        var saved = harness.Tracker.History.First(entry => entry.SpotId == LootSpotCatalog.HermesiaId);
        await harness.Render<HistoryDashboard>();

        // The table uses current prices; the shared historical result must retain the saved valuation.
        await harness.Call(harness.Component<HistoryDashboard>(), "ShareSession", saved with { SilverAfterTax = 123m });

        var snapshot = Assert.Single(harness.JavaScript.Snapshots);
        Assert.Equal(Presentation.SpotName(saved.SpotId, "en"), snapshot.Title);
        Assert.Equal(Presentation.Duration(saved.Duration), snapshot.Metrics[0].Value);
        Assert.Equal((saved.SilverIsComplete ? "" : "≈ ") + Presentation.Silver(saved.SilverAfterTax, "en"), snapshot.Metrics[2].Value);
        Assert.DoesNotContain(snapshot.Details, detail => detail.Label is "Snapshot" or "Price basis" or "Level" or "Loot scroll");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAndCopyUseThePreviewImageThroughTheAvailableExporter(bool native)
    {
        var exporter = native ? new RecordingExporter() : null;
        await using var harness = new UiHarness(exporter: exporter);
        await harness.Render<LiveDashboard>();
        await harness.Call(harness.Component<LiveDashboard>(), "ShareSession");
        var dialog = harness.Component<SessionShareDialog>();

        await harness.Call(dialog, "SaveAsync");
        await harness.Call(dialog, "CopyAsync");

        if (exporter is not null)
        {
            Assert.Equal((Png, harness.JavaScript.Snapshots[0].FileName), Assert.Single(exporter.Saved));
            Assert.Equal(Png, Assert.Single(exporter.Copied));
            Assert.DoesNotContain(harness.JavaScript.Calls, call => call.Name is "grindcrestSessionShare.download" or "grindcrestSessionShare.copy");
        }
        else
        {
            Assert.Equal(Png, Assert.Single(harness.JavaScript.Calls, call => call.Name == "grindcrestSessionShare.download").Arguments[0]);
            Assert.Equal(Png, Assert.Single(harness.JavaScript.Calls, call => call.Name == "grindcrestSessionShare.copy").Arguments[0]);
        }
        Assert.Contains("Image copied to clipboard.", await harness.Markup());
        Assert.Single(harness.JavaScript.Snapshots);
    }

    [Fact]
    public async Task CancelledNativeSaveDoesNotReportSuccessAndClipboardFailureKeepsThePngAvailable()
    {
        var exporter = new RecordingExporter { SaveCompleted = false, CopyFails = true };
        await using var harness = new UiHarness(exporter: exporter);
        await harness.Render<LiveDashboard>();
        await harness.Call(harness.Component<LiveDashboard>(), "ShareSession");
        var dialog = harness.Component<SessionShareDialog>();

        await harness.Call(dialog, "SaveAsync");
        Assert.DoesNotContain("Image saved.", await harness.Markup());
        await harness.Call(dialog, "CopyAsync");
        var failed = await harness.Markup();
        Assert.Contains("role=\"alert\"", failed);
        Assert.Contains(Png, failed);
        Assert.Matches("<button[^>]*class=\"button primary\"[^>]*>.*?Save PNG", failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateRenderResultsAreIgnoredAfterClosingOrDisposing(bool dispose)
    {
        await using var harness = new UiHarness();
        await harness.Render<SessionShareDialog>();
        var dialog = harness.Component<SessionShareDialog>();
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.JavaScript.PendingRender = pending.Task;
        var showing = harness.Renderer.Dispatcher.InvokeAsync(() => dialog.ShowAsync(SessionSharePresentation.FromLive(harness.Tracker.State, harness.Tracker.Preferences)));
        Assert.False(showing.IsCompleted);

        if (dispose) await harness.Renderer.Dispatcher.InvokeAsync(dialog.Dispose);
        else await harness.Call(dialog, "CloseAsync");
        pending.SetResult(Png);
        await showing;

        Assert.DoesNotContain(Png, await harness.Markup());
    }

    [Fact]
    public async Task FailedRenderingOffersRetryWithTheSameSnapshot()
    {
        await using var harness = new UiHarness();
        harness.JavaScript.PendingRender = Task.FromException<string>(new JSException("Canvas unavailable."));
        await harness.Render<LiveDashboard>();
        await harness.Call(harness.Component<LiveDashboard>(), "ShareSession");
        var snapshot = Assert.Single(harness.JavaScript.Snapshots);
        var failed = await harness.Markup();
        Assert.Contains("The image could not be created. Please try again.", failed);
        Assert.Contains("Try again", failed);

        harness.JavaScript.PendingRender = null;
        await harness.Call(harness.Component<SessionShareDialog>(), "RenderImageAsync");

        Assert.Same(snapshot, harness.JavaScript.Snapshots[1]);
        Assert.Contains(Png, await harness.Markup());
        Assert.DoesNotContain("The image could not be created.", await harness.Markup());
    }

    private static void Change(PreviewTrackerSession tracker, TrackerState state) =>
        typeof(PreviewTrackerSession).GetMethod("Change", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tracker, [state, false]);

    private sealed class UiHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly CapturingActivator _activator;
        private Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent _rendered;
        public PreviewTrackerSession Tracker { get; } = new();
        public RecordingJavaScript JavaScript { get; } = new();
        public HtmlRenderer Renderer { get; }

        public UiHarness(bool chronological = false, RecordingExporter? exporter = null)
        {
            _activator = new(chronological);
            var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(Tracker)
                .AddSingleton<IJSRuntime>(JavaScript).AddSingleton<IComponentActivator>(_activator)
                .AddSingleton<NavigationManager, StaticNavigation>();
            if (exporter is not null) services.AddSingleton<ISessionImageExporter>(exporter);
            _services = services.BuildServiceProvider();
            Renderer = new(_services, _services.GetRequiredService<ILoggerFactory>());
        }

        public Task Render<T>() where T : IComponent => Renderer.Dispatcher.InvokeAsync(async () =>
            _rendered = await Renderer.RenderComponentAsync<T>());
        public T Component<T>() where T : IComponent => _activator.Components.OfType<T>().Single();
        public Task<string> Markup() => Renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(_rendered.ToHtmlString()));
        public Task Call(object component, string method, params object[] arguments) => Renderer.Dispatcher.InvokeAsync(() =>
            (Task)component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments)!);
        public async ValueTask DisposeAsync() { await Renderer.DisposeAsync(); await _services.DisposeAsync(); }
    }

    private sealed class CapturingActivator(bool chronological) : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is HistoryDashboard)
                type.GetProperty(chronological ? nameof(HistoryDashboard.QueryView) : nameof(HistoryDashboard.SpotId))!
                    .SetValue(component, chronological ? "all" : LootSpotCatalog.HermesiaId);
            Components.Add(component);
            return component;
        }
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Sharing must not navigate.");
    }

    private sealed class RecordingJavaScript : IJSRuntime
    {
        public List<(string Name, object?[] Arguments)> Calls { get; } = [];
        public List<SessionShareImageData> Snapshots { get; } = [];
        public Task<string>? PendingRender { get; set; }
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            if (identifier != "grindcrestSessionShare.render") return default!;
            Snapshots.Add((SessionShareImageData)args![0]!);
            return (TValue)(object)(PendingRender is { } pending ? await pending : Png);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class RecordingExporter : ISessionImageExporter
    {
        public List<(string Image, string Name)> Saved { get; } = [];
        public List<string> Copied { get; } = [];
        public bool SaveCompleted { get; init; } = true;
        public bool CopyFails { get; init; }
        public Task<bool> SaveAsync(string pngDataUrl, string fileName) { Saved.Add((pngDataUrl, fileName)); return Task.FromResult(SaveCompleted); }
        public Task CopyAsync(string pngDataUrl)
        {
            if (CopyFails) throw new InvalidOperationException("Clipboard unavailable.");
            Copied.Add(pngDataUrl);
            return Task.CompletedTask;
        }
    }
}
