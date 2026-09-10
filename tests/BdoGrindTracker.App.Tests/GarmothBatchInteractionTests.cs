using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothBatchInteractionTests
{
    [Fact]
    public async Task CancelingSingleLiveUploadConsumesTheConfirmationWithoutSending()
    {
        var session = new Session();
        session.StartLive();
        await Render(session, async (dashboard, _, js) =>
        {
            await Invoke(dashboard, "AskUpload", FirstSessionRow(dashboard));

            Assert.Equal(1, session.PauseCalls);
            Assert.Contains(js.Calls, call => call.Identifier == "grindcrest.showDialog" &&
                call.Arguments.Contains("garmoth-upload-confirm"));
            Assert.Empty(session.Uploads);
            await Invoke(dashboard, "CloseUploadDialog");
            await Invoke(dashboard, "ConfirmUpload");

            Assert.Empty(session.Uploads);
        });
    }

    [Fact]
    public async Task ConfirmingSingleLiveUploadSendsTheFrozenRemainderOnceDespiteAnUploadedHistoryCheckpoint()
    {
        var session = new Session();
        session.StartLive();
        session.History = [Entry(0) with
        {
            SessionId = session.State.SessionId, Duration = TimeSpan.FromMinutes(62),
            Totals = new() { ["Black Crystal Fragment"] = 102 },
            GarmothUploadBlocked = true, GarmothUploadedAt = EntryTime,
        }];
        await Render(session, async (dashboard, _, _) =>
        {
            await Invoke(dashboard, "AskUpload", FirstSessionRow(dashboard));
            var frozen = session.State.CurrentGarmothUpload;
            Assert.Empty(session.Uploads);

            // A newer valuation must not silently replace the reviewed amount.
            var frozenDraft = frozen.Draft!;
            var repriced = frozenDraft with { TotalSilver = frozenDraft.TotalSilver + 10_000 };
            session.State = session.State with
            {
                CurrentGarmothUpload = frozen with { Draft = repriced, Payload = GarmothSessionPayload.Create(repriced) },
            };
            await Invoke(dashboard, "ConfirmUpload");
            await Invoke(dashboard, "ConfirmUpload");

            var sent = Assert.Single(session.Uploads);
            Assert.Same(frozen, sent);
            Assert.Equal(TimeSpan.FromMinutes(2), sent.Draft!.ActiveDuration);
            Assert.Equal(2, sent.Draft.Totals["Black Crystal Fragment"]);
            Assert.Equal(frozen.Payload!.Total, sent.Payload!.Total);
            Assert.True(session.State.IsSubmitted);
        });
    }

    [Fact]
    public async Task AskingPausesAndFreezesTheLiveSessionButCancelNeverUploads()
    {
        var session = new Session();
        session.StartLive();
        session.AfterPause = () => session.ChangeLiveQuantity(7);
        await Render(session, async (dashboard, _, js) =>
        {
            await Invoke(dashboard, "AskAllUploads");

            Assert.Equal(1, session.PauseCalls);
            Assert.False(session.State.IsRunning);
            Assert.Equal(1, session.RefreshCalls);
            Assert.Contains(js.Calls, call => call.Identifier == "grindcrest.showDialog" &&
                call.Arguments.Contains("garmoth-all-upload-confirm"));
            Assert.Empty(session.Uploads);

            await Invoke(dashboard, "CloseAllUploadsDialog");
            await Invoke(dashboard, "ConfirmAllUploads");
            Assert.Empty(session.Uploads);
        });
    }

    [Fact]
    public async Task ConfirmationUsesTheLootSnapshotAfterThePauseHasFinished()
    {
        var session = new Session();
        session.StartLive();
        session.AfterPause = () => session.ChangeLiveQuantity(7);
        await Render(session, async (dashboard, _, _) =>
        {
            await Invoke(dashboard, "AskAllUploads");
            Assert.Empty(session.Uploads);
            await Invoke(dashboard, "ConfirmAllUploads");
            Assert.Equal(7, Assert.Single(session.Uploads).Draft!.Totals["Black Crystal Fragment"]);
        });
    }

    [Fact]
    public async Task AllIncludesEveryReadySessionBeyondFiltersAndPagesAndOnlyTheLiveRemainderOnce()
    {
        var session = new Session();
        session.StartLive();
        var live = Entry(0) with
        {
            SessionId = session.State.SessionId, Duration = TimeSpan.FromMinutes(62),
            Totals = new() { ["Black Crystal Fragment"] = 102 },
            GarmothUploadBlocked = true, GarmothUploadedAt = EntryTime,
        };
        var ready = Enumerable.Range(1, 10).Select(Entry).ToArray();
        var blocked = Entry(11) with { GarmothUploadBlocked = true };
        var transferred = Entry(12) with { GarmothUploadedAt = EntryTime };
        var missingClass = Entry(13) with { CharacterClass = null };
        var tooShort = Entry(14) with { Duration = TimeSpan.FromSeconds(59) };
        session.History = [live, .. ready, blocked, transferred, missingClass, tooShort];

        await Render(session, async (dashboard, markup, _) =>
        {
            Assert.Single(Regex.Matches(markup(), $"data-session-id=\"{live.SessionId}\""));
            Set(dashboard, "_search", "A spot that does not exist");
            Set(dashboard, "_statusFilter", "blocked");
            Set(dashboard, "_page", 2);

            await Invoke(dashboard, "AskAllUploads");
            Assert.Empty(session.Uploads);
            await Invoke(dashboard, "ConfirmAllUploads");

            Assert.Equal(11, session.Uploads.Count);
            Assert.Equal(11, session.Uploads.Select(upload => upload.Draft!.SourceSessionId).Distinct().Count());
            var uploadedLive = Assert.Single(session.Uploads, upload => upload.Draft!.SourceSessionId == live.SessionId);
            Assert.Equal(TimeSpan.FromMinutes(2), uploadedLive.Draft!.ActiveDuration);
            Assert.Equal(2, uploadedLive.Draft.Totals["Black Crystal Fragment"]);
            Assert.All(ready, entry => Assert.Contains(session.Uploads,
                upload => upload.Draft!.SourceSessionId == entry.SessionId));
            Assert.DoesNotContain(session.Uploads, upload => upload.Draft!.SourceSessionId == blocked.SessionId ||
                upload.Draft.SourceSessionId == transferred.SessionId || upload.Draft.SourceSessionId == missingClass.SessionId ||
                upload.Draft.SourceSessionId == tooShort.SessionId);
        });
    }

    [Fact]
    public async Task LiveSessionAppearsInTheSharedListBeforeItsFirstHistoryCheckpoint()
    {
        var session = new Session();
        session.StartLive();
        Assert.Empty(session.History);
        await Render(session, (dashboard, markup, _) =>
        {
            Assert.Single(Regex.Matches(markup(), $"data-session-id=\"{session.State.SessionId}\""));
            Assert.Contains("Alles hochladen", markup());
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedConfirmedSessionPreventsAnyBatchUpload(bool changeLive)
    {
        var session = new Session();
        session.StartLive();
        session.History = [Entry(1), Entry(2)];
        await Render(session, async (dashboard, _, _) =>
        {
            await Invoke(dashboard, "AskAllUploads");
            if (changeLive) session.ChangeLiveQuantity(3);
            else session.History = [session.History[0], session.History[1] with
            {
                Totals = new() { ["Black Crystal Fragment"] = 99 },
            }];

            await Invoke(dashboard, "ConfirmAllUploads");
            Assert.Empty(session.Uploads);
        });
    }

    [Fact]
    public async Task FirstUploadErrorStopsTheBatchAndPreservesCompletedSessions()
    {
        var session = new Session { History = [Entry(1), Entry(2), Entry(3)] };
        session.Respond = _ => Task.FromResult(session.Uploads.Count == 2
            ? new TrackerCommandResult("Upload-Ergebnis unklar. Bitte in Garmoth prüfen.")
            : TrackerCommandResult.Success);
        await Render(session, async (dashboard, markup, _) =>
        {
            await Invoke(dashboard, "AskAllUploads");
            await Invoke(dashboard, "ConfirmAllUploads");

            Assert.Equal(2, session.Uploads.Count);
            var completed = Assert.Single(session.History, entry => entry.GarmothUploadedAt is not null);
            Assert.Equal(session.Uploads[0].Draft!.SourceSessionId, completed.SessionId);
            Assert.True(completed.GarmothUploadBlocked);
            Assert.Contains("Upload-Ergebnis unklar", markup());
            Assert.Contains(session.History, entry => !session.Uploads.Any(upload => upload.Draft!.SourceSessionId == entry.SessionId));
        });
    }

    [Fact]
    public async Task ChangingTheNextSessionDuringTheFirstRequestStopsBeforeItsUpload()
    {
        var session = new Session { History = [Entry(1), Entry(2)] };
        session.Respond = preview =>
        {
            session.History = session.History.Select(entry => entry.SessionId == preview.Draft!.SourceSessionId
                ? entry : entry with { Totals = new() { ["Black Crystal Fragment"] = 99 } }).ToArray();
            return Task.FromResult(TrackerCommandResult.Success);
        };
        await Render(session, async (dashboard, markup, _) =>
        {
            await Invoke(dashboard, "AskAllUploads");
            await Invoke(dashboard, "ConfirmAllUploads");

            var sent = Assert.Single(session.Uploads);
            var completed = Assert.Single(session.History, entry => entry.GarmothUploadedAt is not null);
            Assert.Equal(sent.Draft!.SourceSessionId, completed.SessionId);
            var changed = Assert.Single(session.History, entry => entry.SessionId != completed.SessionId);
            Assert.Equal(99, changed.Totals["Black Crystal Fragment"]);
            Assert.Null(changed.GarmothUploadedAt);
            Assert.False(changed.GarmothUploadBlocked);
            Assert.Contains("geändert", markup());
        });
    }

    [Fact]
    public async Task RepeatedConfirmationWhileRequestIsPendingDoesNotDuplicateTheBatch()
    {
        var session = new Session { History = [Entry(1), Entry(2)] };
        var response = new TaskCompletionSource<TrackerCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Respond = _ => session.Uploads.Count == 1 ? response.Task : Task.FromResult(TrackerCommandResult.Success);
        await Render(session, async (dashboard, _, _) =>
        {
            await Invoke(dashboard, "AskAllUploads");
            var first = Invoke(dashboard, "ConfirmAllUploads");
            try
            {
                Assert.False(first.IsCompleted);
                Assert.Single(session.Uploads);
                await Invoke(dashboard, "ConfirmAllUploads");
                Assert.Single(session.Uploads);
            }
            finally
            {
                response.TrySetResult(TrackerCommandResult.Success);
                await first;
            }
            Assert.Equal(2, session.Uploads.Count);
            Assert.Equal(2, session.Uploads.Select(upload => upload.Draft!.SourceSessionId).Distinct().Count());
            await Invoke(dashboard, "ConfirmAllUploads");
            Assert.Equal(2, session.Uploads.Count);
        });
    }

    [Fact]
    public async Task FailedPausePreventsTheConfirmationAndReportsTheFailure()
    {
        var session = new Session { PauseError = "Die letzten Drops konnten nicht gesichert werden." };
        session.StartLive();
        await Render(session, async (dashboard, markup, js) =>
        {
            await Invoke(dashboard, "AskAllUploads");
            Assert.Equal(1, session.PauseCalls);
            Assert.DoesNotContain(js.Calls, call => call.Identifier == "grindcrest.showDialog");
            Assert.Empty(session.Uploads);
            Assert.Contains(session.PauseError, markup());
        });
    }

    private static readonly DateTimeOffset EntryTime = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static LootHistoryEntry Entry(int hoursAgo) => new()
    {
        SessionId = Guid.NewGuid(), StartedAt = EntryTime.AddHours(-hoursAgo), UpdatedAt = EntryTime,
        Duration = TimeSpan.FromMinutes(2), SpotId = LootSpotCatalog.HermesiaId,
        CharacterClass = "Warrior · Awakening", Totals = new() { ["Black Crystal Fragment"] = 2 },
        SilverBeforeTax = 321_078, SilverAfterTax = 321_078, SilverIsComplete = true,
    };

    private static async Task Render(Session session, Func<GarmothDashboard, Func<string>, RecordingJs, Task> test)
    {
        var activator = new CapturingActivator();
        var js = new RecordingJs();
        using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(session)
            .AddSingleton<IJSRuntime>(js).AddSingleton<NavigationManager>(new StaticNavigation())
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<GarmothDashboard>();
            var component = activator.Components.OfType<GarmothDashboard>().Single();
            string Markup()
            {
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
                return WebUtility.HtmlDecode(root.ToHtmlString());
            }
            await test(component, Markup, js);
        });
    }

    private static object FirstSessionRow(object component)
    {
        var property = component.GetType().GetProperty("SessionRows", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(property);
        var rows = Assert.IsAssignableFrom<System.Collections.IEnumerable>(property.GetValue(component));
        return rows.Cast<object>().First();
    }

    private static async Task Invoke(object component, string method, params object?[] arguments)
    {
        var handler = component.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(handler);
        if (handler.Invoke(component, arguments) is Task task) await task;
    }

    private static void Set(object component, string field, object value) =>
        component.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            Components.Add(component);
            return component;
        }
    }

    private sealed class RecordingJs : IJSRuntime
    {
        public List<(string Identifier, object?[] Arguments)> Calls { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        { Calls.Add((identifier, args ?? [])); return ValueTask.FromResult(default(TValue)!); }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("https://0.0.0.1/", "https://0.0.0.1/garmoth");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class Session : ITrackerSession
    {
        public event Action? Changed { add { } remove { } }
        public TrackerState State { get; set; } = new() { HasApiKey = true };
        public TrackerPreferences Preferences { get; } = new();
        public IReadOnlyList<TrackerMonitor> Monitors { get; } = [];
        public IReadOnlyList<LootHistoryEntry> History { get; set; } = [];
        public LootPriceSnapshot Prices { get; } = LootPriceCatalog.FixedSnapshot("eu");
        public List<GarmothUploadPreview> Uploads { get; } = [];
        public string? PauseError { get; set; }
        public Action? AfterPause { get; set; }
        public int PauseCalls { get; private set; }
        public int RefreshCalls { get; private set; }
        public Func<GarmothUploadPreview, Task<TrackerCommandResult>> Respond { get; set; } = _ => Success();

        public void StartLive()
        {
            State = State with
            {
                SessionId = Guid.NewGuid(), HasSession = true, IsRunning = true, CanPause = true,
                SpotId = LootSpotCatalog.HermesiaId, CharacterLabel = "Warrior · Awakening",
                CharacterClassId = "warrior-awakening", Elapsed = TimeSpan.FromMinutes(2),
            };
            ChangeLiveQuantity(2);
        }

        public void ChangeLiveQuantity(long quantity)
        {
            var totals = new Dictionary<string, long> { ["Black Crystal Fragment"] = quantity };
            var preview = GarmothUploadPreview.Create(Guid.NewGuid(), State.SessionId, State.SpotId,
                State.CharacterLabel, State.Elapsed, totals, EntryTime, Prices, Preferences.Tax);
            Assert.True(preview.IsReady, preview.Error);
            State = State with
            {
                Loot = new(totals, quantity, 1), CurrentGarmothUpload = preview,
                Silver = SilverValuation.Calculate(totals, Prices, Preferences.Tax),
            };
        }

        private static Task<TrackerCommandResult> Success() => Task.FromResult(TrackerCommandResult.Success);
        public Task<TrackerCommandResult> PauseAsync()
        {
            PauseCalls++;
            if (PauseError is not null) return Task.FromResult(new TrackerCommandResult(PauseError));
            State = State with { IsRunning = false, CanPause = false };
            AfterPause?.Invoke();
            return Success();
        }

        public async Task<TrackerCommandResult> UploadConfirmedAsync(GarmothUploadPreview preview)
        {
            Assert.False(State.IsBusy);
            Assert.True(preview.IsReady);
            Uploads.Add(preview);
            State = State with { IsBusy = true };
            try
            {
                var result = await Respond(preview);
                if (result.Succeeded)
                {
                    var id = preview.Draft!.SourceSessionId;
                    History = History.Select(entry => entry.SessionId == id
                        ? entry with { GarmothUploadBlocked = true, GarmothUploadedAt = EntryTime } : entry).ToArray();
                    if (State.HasSession && id == State.SessionId)
                        State = State with { IsSubmitted = true, CurrentGarmothUpload = GarmothUploadPreview.Unavailable("Session abgeschlossen.") };
                }
                return result;
            }
            finally { State = State with { IsBusy = false }; }
        }

        public Task<TrackerCommandResult> UploadAsync() => throw new InvalidOperationException("Use the confirmed preview.");
        public Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId) => throw new InvalidOperationException("Use the confirmed preview.");
        public Task<TrackerCommandResult> ToggleTrackingAsync() => Success();
        public Task<TrackerCommandResult> NewSessionAsync() => Success();
        public Task<TrackerCommandResult> SetDemoAsync(bool enabled) => Success();
        public Task<TrackerCommandResult> InstallOcrLanguageAsync() => Success();
        public Task<TrackerCommandResult> RecheckOcrLanguageAsync() => Success();
        public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity) => Success();
        public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals, string? characterClass = null) => Success();
        public Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId) => Success();
        public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null, bool resumeAutomaticUpload = false) => Task.FromResult(new PreferenceSaveResult());
        public Task RefreshPricesAsync() { RefreshCalls++; return Task.CompletedTask; }
        public Task TickAsync() => Task.CompletedTask;
        public Task PrepareUpdateRestartAsync() => Task.CompletedTask;
        public Task RunPreparedUpdateAsync(Func<Task> install) => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
