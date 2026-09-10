using System.Net;
using System.Reflection;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
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

public sealed class LiveLootInteractionTests
{
    [Fact]
    public async Task DemoExplainsDiscardingSampleDataAndShowsManualProvenanceWithoutHistory()
    {
        var session = new Session();
        session.State = session.State with { IsDemo = true, HasSession = false, ManualLootItems = ["Black Stone"] };
        await Render<LiveDashboard>(session, null, (_, markup, _) =>
        {
            Assert.Contains("Die Demo wird beendet. Beispieldaten werden nicht im Verlauf gespeichert.", markup());
            Assert.DoesNotContain("Die aktuelle Session wird beendet und im Verlauf gesichert.", markup());
            Assert.Contains("Manuell korrigiert", markup());
            Assert.Empty(session.History);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PreviewPublishesReadOnlyManualItemsAndClearsThemForANewSession()
    {
        await using var session = new PreviewTrackerSession();
        var quantity = session.State.Loot.Totals["Black Stone"];
        await session.UpdateLootQuantityAsync(session.State.SessionId, "Black Stone", quantity + 5, quantity);
        Assert.Equal("Black Stone", Assert.Single(session.State.ManualLootItems));
        Assert.True(Assert.IsAssignableFrom<IList<string>>(session.State.ManualLootItems).IsReadOnly);
        await session.NewSessionAsync();
        Assert.Empty(session.State.ManualLootItems);
    }

    [Fact]
    public async Task NativeOverlayUsesTheDedicatedPauseEvenDuringAnUpload()
    {
        var session = new Session();
        session.State = session.State with { IsRunning = true, IsBusy = true, CanPause = true };
        using var overlay = new OverlayService(session);
        Assert.True(overlay.Snapshot.CanToggleTracking);
        await overlay.ToggleTrackingAsync();
        Assert.Equal(1, session.PauseCalls);
        Assert.Equal(0, session.ToggleCalls);
        Assert.False(session.State.IsRunning);
    }

    [Fact]
    public async Task NativeOverlayPropagatesThePauseFailureToItsCommandHost()
    {
        var session = new Session { PauseError = "Die Session ist noch nicht gesichert." };
        session.State = session.State with { IsRunning = true, CanPause = true };
        using var overlay = new OverlayService(session);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(overlay.ToggleTrackingAsync);
        Assert.Equal(session.PauseError, error.Message);
        Assert.Equal(1, session.PauseCalls);
    }

    [Fact]
    public async Task CommittedCorrectionClosesDespitePersistentTrackingErrorAndCannotBeAppliedTwice()
    {
        var session = new Session();
        await Render<LootQuantityEditor>(session, EditorParameters(session), async (editor, markup, _) =>
        {
            await Invoke(editor, "Begin");
            Set(editor, "_value", "15");
            await Invoke(editor, "Save");
            Assert.Equal(15, session.State.Loot.Totals["Black Stone"]);
            Assert.True(session.State.IsError);
            Assert.DoesNotContain("<form", markup());
            Assert.Contains("quantity-edit-trigger", markup());

            // A queued second submit from the removed form must be harmless.
            await Invoke(editor, "Save");
            Assert.Equal(1, session.CorrectionCalls);
            Assert.Equal(15, session.State.Loot.Totals["Black Stone"]);
        });
    }

    [Fact]
    public async Task FailedCorrectionKeepsEditorAndReportsTheCommandErrorBeforeSafeRetry()
    {
        var session = new Session { CorrectionError = "Die Datei konnte nicht gespeichert werden." };
        await Render<LootQuantityEditor>(session, EditorParameters(session), async (editor, markup, _) =>
        {
            await Invoke(editor, "Begin");
            Set(editor, "_value", "15");
            await Invoke(editor, "Save");
            Assert.Equal(10, session.State.Loot.Totals["Black Stone"]);
            Assert.Contains("<form", markup());
            Assert.Contains(session.CorrectionError, markup());
            session.CorrectionError = null;
            await Invoke(editor, "Save");
            Assert.Equal(15, session.State.Loot.Totals["Black Stone"]);
            Assert.DoesNotContain("<form", markup());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewSessionCanRecoverFromAnUnrelatedTrackingError(bool running)
    {
        var session = new Session();
        session.State = session.State with { IsRunning = running };
        await Render<LiveDashboard>(session, null, async (dashboard, markup, js) =>
        {
            await Invoke(dashboard, "ConfirmAction");
            Assert.Equal(running ? 1 : 0, session.PauseCalls);
            Assert.Equal(1, session.NewSessionCalls);
            Assert.Contains("grindcrest.closeDialog", js.Calls);
            Assert.Contains("aria-labelledby=\"live-confirm-title\"", markup());
        });
    }

    [Fact]
    public async Task FailedPausePreventsSessionResetAndKeepsTheConfirmationOpen()
    {
        var session = new Session { PauseError = "Die Session ist noch nicht gesichert." };
        session.State = session.State with { IsRunning = true };
        await Render<LiveDashboard>(session, null, async (dashboard, markup, js) =>
        {
            await Invoke(dashboard, "ConfirmAction");
            Assert.Equal(0, session.NewSessionCalls);
            Assert.DoesNotContain("grindcrest.closeDialog", js.Calls);
            Assert.Contains(session.PauseError, markup());
        });
    }

    [Fact]
    public async Task AddMissingItemUsesTheSpotCatalogAndPreservesDropsReceivedWhileOpen()
    {
        var session = new Session();
        var missingItem = LootSpotCatalog.GetRequired(LootSpotCatalog.AphrodonId).AllowedItems.First(name => name != "Black Stone");
        await Render<LootItemAdder>(session, new Dictionary<string, object?>
        {
            [nameof(LootItemAdder.SessionId)] = session.State.SessionId,
            [nameof(LootItemAdder.SpotId)] = session.State.SpotId,
            [nameof(LootItemAdder.Totals)] = session.State.Loot.Totals,
        }, async (adder, markup, _) =>
        {
            await Invoke(adder, "Open");
            Assert.DoesNotContain("<option value=\"Black Stone\"", markup());
            Assert.Contains("Spotkatalog durchsuchen", markup());
            Set(adder, "_selectedItem", "Not a catalog item");
            await Invoke(adder, "Save");
            Assert.Equal(0, session.CorrectionCalls);

            Set(adder, "_selectedItem", missingItem);
            Set(adder, "_quantity", "5");
            var totals = session.State.Loot.Totals.ToDictionary(pair => pair.Key, pair => pair.Value);
            totals[missingItem] = 2;
            session.State = session.State with { Loot = new(totals, 12, 2) };
            await Invoke(adder, "Save");
            Assert.Equal(7, session.State.Loot.Totals[missingItem]);
            Assert.Equal(0, session.LastOriginalQuantity);
            await Invoke(adder, "Save");
            Assert.Equal(1, session.CorrectionCalls);
        });
    }

    [Fact]
    public async Task CorrectedUploadedLootHasPersistentContextAndWarnsBeforeAnotherEdit()
    {
        var session = new Session();
        session.History = [new()
        {
            SessionId = session.State.SessionId, StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow, Duration = TimeSpan.FromHours(1), SpotId = LootSpotCatalog.AphrodonId,
            Totals = new() { ["Black Stone"] = 10 }, SilverBeforeTax = 0, SilverAfterTax = 0, SilverIsComplete = false,
            GarmothUploadedAt = DateTimeOffset.UtcNow, GarmothLocallyModified = true, ManualLootItems = ["Black Stone"],
        }];
        await Render<LootTable>(session, new Dictionary<string, object?>
        {
            [nameof(LootTable.EditableSessionId)] = session.State.SessionId,
            [nameof(LootTable.Totals)] = session.State.Loot.Totals,
        }, (_, markup, _) =>
        {
            Assert.Contains("Lokal korrigiert; Garmoth unverändert.", markup());
            Assert.Contains("Manuell korrigiert", markup());
            return Task.CompletedTask;
        });
        await Render<LootQuantityEditor>(session, EditorParameters(session), async (editor, markup, _) =>
        {
            await Invoke(editor, "Begin");
            Assert.Contains("Bereits übertragene Garmoth-Einträge bleiben unverändert.", markup());
        });
    }

    private static Dictionary<string, object?> EditorParameters(Session session) => new()
    {
        [nameof(LootQuantityEditor.SessionId)] = session.State.SessionId,
        [nameof(LootQuantityEditor.ItemName)] = "Black Stone",
        [nameof(LootQuantityEditor.Quantity)] = 10L,
        [nameof(LootQuantityEditor.DisplayQuantity)] = "10",
    };

    private static async Task Render<T>(Session session, Dictionary<string, object?>? parameters,
        Func<T, Func<string>, RecordingJs, Task> test) where T : IComponent
    {
        var activator = new CapturingActivator();
        var js = new RecordingJs();
        var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(session)
            .AddSingleton<IJSRuntime>(js).AddSingleton<NavigationManager>(new StaticNavigation())
            .AddSingleton<IComponentActivator>(activator);
        using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? []));
            var component = activator.Components.OfType<T>().First();
            string Markup()
            {
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
                return WebUtility.HtmlDecode(root.ToHtmlString());
            }
            await test(component, Markup, js);
        });
    }

    private static async Task Invoke(object component, string method)
    {
        var result = component.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
        if (result is Task task) await task;
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
        public List<string> Calls { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        { Calls.Add(identifier); return ValueTask.FromResult(default(TValue)!); }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("https://0.0.0.1/", "https://0.0.0.1/");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class Session : ITrackerSession
    {
        public event Action? Changed { add { } remove { } }
        public TrackerState State { get; set; } = new()
        {
            SessionId = Guid.NewGuid(), HasSession = true, IsError = true, AnalyzerAvailable = false,
            TrackingBlockedReason = "OCR nicht verfügbar.", Status = "OCR nicht verfügbar.",
            SpotId = LootSpotCatalog.AphrodonId, Elapsed = TimeSpan.FromMinutes(1),
            Loot = new(new Dictionary<string, long> { ["Black Stone"] = 10 }, 10, 1),
        };
        public TrackerPreferences Preferences { get; } = new();
        public IReadOnlyList<TrackerMonitor> Monitors { get; } = [];
        public IReadOnlyList<LootHistoryEntry> History { get; set; } = [];
        public LootPriceSnapshot Prices { get; } = LootPriceCatalog.FixedSnapshot("eu");
        public string? CorrectionError { get; set; }
        public string? PauseError { get; set; }
        public int CorrectionCalls { get; private set; }
        public int PauseCalls { get; private set; }
        public int NewSessionCalls { get; private set; }
        public int ToggleCalls { get; private set; }
        public long LastOriginalQuantity { get; private set; }
        private static Task<TrackerCommandResult> Success() => Task.FromResult(new TrackerCommandResult());
        public Task<TrackerCommandResult> PauseAsync()
        {
            PauseCalls++;
            if (PauseError is not null) return Task.FromResult(new TrackerCommandResult(PauseError));
            State = State with { IsRunning = false };
            return Success();
        }
        public Task<TrackerCommandResult> NewSessionAsync() { NewSessionCalls++; return Success(); }
        public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity)
        {
            CorrectionCalls++;
            LastOriginalQuantity = originalQuantity;
            if (CorrectionError is not null) return Task.FromResult(new TrackerCommandResult(CorrectionError));
            var totals = State.Loot.Totals.ToDictionary(pair => pair.Key, pair => pair.Value);
            totals[itemName] = totals.GetValueOrDefault(itemName) + quantity - originalQuantity;
            State = State with { Loot = new(totals, totals.Values.Sum(), State.Loot.ConfirmedEventCount) };
            return Success();
        }
        public Task<TrackerCommandResult> ToggleTrackingAsync() { ToggleCalls++; return Success(); }
        public Task<TrackerCommandResult> SetDemoAsync(bool enabled) => Success();
        public Task<TrackerCommandResult> InstallOcrLanguageAsync() => Success();
        public Task<TrackerCommandResult> RecheckOcrLanguageAsync() => Success();
        public Task<TrackerCommandResult> UploadAsync() => Success();
        public Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId) => Success();
        public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals, string? characterClass = null) => Success();
        public Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId) => Success();
        public Task<TrackerCommandResult> SaveSessionAsync() => Success();
        public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null, bool resumeAutomaticUpload = false) => Task.FromResult(new PreferenceSaveResult());
        public Task RefreshPricesAsync() => Task.CompletedTask;
        public Task TickAsync() => Task.CompletedTask;
        public Task PrepareUpdateRestartAsync() => Task.CompletedTask;
        public Task RunPreparedUpdateAsync(Func<Task> install) => Task.CompletedTask;
        public Task ShutdownAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
