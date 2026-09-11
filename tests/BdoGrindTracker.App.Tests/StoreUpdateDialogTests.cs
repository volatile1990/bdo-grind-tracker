using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Tests;

public sealed class StoreUpdateDialogTests
{
    [Fact]
    public async Task AvailableStoreUpdateOpensOnceAndNeverStartsInstallationWithoutClick()
    {
        var updates = new RecordingUpdates();
        await Render<StoreUpdateDialog>(updates, new Session(), async (dialog, markup, js, _) =>
        {
            await AfterRender(dialog);
            await AfterRender(dialog);
            updates.Publish(updates.State);
            await AfterRender(dialog);

            Assert.Equal(1, js.Shows);
            Assert.Contains("Update verfügbar", markup());
            Assert.Contains("Jetzt aktualisieren", markup());
            Assert.Contains("Später", markup());
            Assert.DoesNotContain("Grindcrest 1.1.0", markup());
            Assert.Equal(0, updates.Installs);
            Assert.Equal(0, updates.Downloads);
        });
    }

    [Theory]
    [InlineData(false, true, (int)UpdatePhase.Available)]
    [InlineData(true, false, (int)UpdatePhase.Available)]
    [InlineData(true, true, (int)UpdatePhase.Idle)]
    [InlineData(true, true, (int)UpdatePhase.Checking)]
    [InlineData(true, true, (int)UpdatePhase.Downloading)]
    [InlineData(true, true, (int)UpdatePhase.ReadyToRestart)]
    [InlineData(true, true, (int)UpdatePhase.Restarting)]
    [InlineData(true, true, (int)UpdatePhase.Installed)]
    [InlineData(true, true, (int)UpdatePhase.Error)]
    public async Task AutomaticPromptRequiresAnEnabledAvailableStoreOffer(bool store, bool enabled, int phaseValue)
    {
        var phase = (UpdatePhase)phaseValue;
        var updates = new RecordingUpdates();
        updates.Publish(updates.State with { UsesStore = store, Enabled = enabled, Phase = phase });
        await Render<StoreUpdateDialog>(updates, new Session(), async (dialog, _, js, _) =>
        {
            await AfterRender(dialog);
            Assert.Equal(0, js.Shows);
            Assert.Equal(0, updates.Installs);
        });
    }

    [Fact]
    public async Task DismissedUnknownOfferDoesNotReopenAfterProgressOrRecheckingButCanOpenManually()
    {
        var updates = new RecordingUpdates();
        await Render<StoreUpdateDialog>(updates, new Session(), async (dialog, _, js, _) =>
        {
            await AfterRender(dialog);
            await Invoke(dialog, "CloseAsync");
            foreach (var phase in new[] { UpdatePhase.Downloading, UpdatePhase.Error, UpdatePhase.Checking, UpdatePhase.Available })
            {
                updates.Publish(updates.State with { Phase = phase });
                await AfterRender(dialog);
            }
            Assert.Equal(1, js.Shows);
            await dialog.OpenAsync();
            Assert.Equal(2, js.Shows);
            Assert.Equal(0, updates.Installs);
        });
    }

    [Fact]
    public async Task ADifferentKnownStoreOfferCanBeAnnouncedAgain()
    {
        var updates = new RecordingUpdates();
        updates.Publish(updates.State with { AvailableVersion = "1.1.0" });
        await Render<StoreUpdateDialog>(updates, new Session(), async (dialog, markup, js, _) =>
        {
            await AfterRender(dialog);
            Assert.Contains("Grindcrest 1.1.0", markup());
            await Invoke(dialog, "CloseAsync");
            updates.Publish(updates.State with { AvailableVersion = "1.2.0" });
            await AfterRender(dialog);
            Assert.Equal(2, js.Shows);
            Assert.Contains("Grindcrest 1.2.0", markup());
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunningOrBusySessionCannotBeInterruptedByPrompt(bool running, bool busy)
    {
        var updates = new RecordingUpdates();
        var session = new Session { State = new() { IsRunning = running, IsBusy = busy } };
        await Render<StoreUpdateDialog>(updates, session, async (dialog, markup, _, _) =>
        {
            await AfterRender(dialog);
            Assert.True(IsDisabled(markup(), "Jetzt aktualisieren"));
            Assert.Contains(running ? "Session zuerst pausieren" : "aktuelle Vorgang abgeschlossen", markup());
            await Invoke(dialog, "InstallAsync");
            Assert.Equal(0, updates.Installs);
            Assert.Equal(0, session.Commands);
            session.Publish(new());
            Assert.False(IsDisabled(markup(), "Jetzt aktualisieren"));
            await Invoke(dialog, "InstallAsync");
            Assert.Equal(1, updates.Installs);
            Assert.Equal(0, session.Commands);
        });
    }

    [Theory]
    [InlineData((int)UpdatePhase.Available)]
    [InlineData((int)UpdatePhase.ReadyToRestart)]
    public async Task OneClickCallsInstallDirectlyAndGuardsDuplicateClicks(int phaseValue)
    {
        var phase = (UpdatePhase)phaseValue;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updates = new RecordingUpdates { InstallAction = () => completion.Task };
        updates.Publish(updates.State with { Phase = phase });
        await Render<StoreUpdateDialog>(updates, new Session(), async (dialog, markup, js, _) =>
        {
            await dialog.OpenAsync();
            var first = Invoke(dialog, "InstallAsync");
            await Invoke(dialog, "InstallAsync");
            Assert.Equal(1, updates.Installs);
            Assert.Equal(0, updates.Downloads);
            Assert.True(IsDisabled(markup(), phase == UpdatePhase.Available ? "Jetzt aktualisieren" : "Update installieren"));
            updates.Publish(updates.State with { Phase = UpdatePhase.Restarting, DownloadPercent = 35, Message = "Update wird installiert." });
            await AfterRender(dialog);
            Assert.Contains("35 %", markup());
            Assert.Contains("Grindcrest wird aktualisiert", markup());
            Assert.Equal(1, js.Shows);
            completion.SetResult();
            await first;
        });
    }

    [Fact]
    public async Task FailedInstallationKeepsPromptAndAllowsRetryWithoutReopening()
    {
        var updates = new RecordingUpdates { InstallAction = () => throw new IOException("Session konnte nicht gespeichert werden.") };
        await Render<StoreUpdateDialog>(updates, new Session(), async (dialog, markup, js, _) =>
        {
            await AfterRender(dialog);
            await Invoke(dialog, "InstallAsync");
            Assert.Contains("Session konnte nicht gespeichert werden.", markup());
            Assert.False(IsDisabled(markup(), "Jetzt aktualisieren"));
            updates.InstallAction = () => Task.CompletedTask;
            await Invoke(dialog, "InstallAsync");
            Assert.Equal(2, updates.Installs);
            Assert.DoesNotContain("Session konnte nicht gespeichert werden.", markup());
            Assert.Equal(1, js.Shows);
        });
    }

    [Fact]
    public async Task ClosedOrDisposedPromptCannotInstallOrOpenFromLateEvents()
    {
        var updates = new RecordingUpdates();
        await Render<StoreUpdateDialog>(updates, new Session(), async (dialog, _, js, _) =>
        {
            await AfterRender(dialog);
            await Invoke(dialog, "CloseAsync");
            await Invoke(dialog, "InstallAsync");
            dialog.Dispose();
            updates.Publish(updates.State with { AvailableVersion = "1.2.0" });
            await AfterRender(dialog);
            await dialog.OpenAsync();
            Assert.Equal(1, js.Shows);
            Assert.Equal(0, updates.Installs);
            Assert.Equal(0, updates.Subscribers);
        });
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(false, false, 1)]
    public async Task SettingsInstallDirectlyWithoutDownloadAndRespectSessionGuard(bool running, bool busy, int installs)
    {
        var updates = new RecordingUpdates();
        var session = new Session { State = new() { IsRunning = running, IsBusy = busy } };
        await Render<AppUpdates>(updates, session, async (panel, markup, _, _) =>
        {
            Assert.Equal(installs == 0, IsDisabled(markup(), "Jetzt aktualisieren"));
            await Invoke(panel, "InstallStoreUpdate");
            Assert.Equal(installs, updates.Installs);
            Assert.Equal(0, updates.Downloads);
            Assert.Equal(0, session.Commands);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StoreBannerReopensGlobalPromptWhilePortableBannerStillNavigatesToSettings(bool store)
    {
        var updates = new RecordingUpdates();
        updates.Publish(updates.State with { UsesStore = store });
        await Render<AppUpdates>(updates, new Session(), async (banner, _, _, navigation) =>
        {
            var openings = 0;
            banner.ShowStoreUpdateDialog = EventCallback.Factory.Create(this, () => openings++);
            await Invoke(banner, "ViewUpdate");
            Assert.Equal(store ? 1 : 0, openings);
            Assert.Equal(store ? "https://0.0.0.1/" : "https://0.0.0.1/settings", navigation.Uri);
        }, new Dictionary<string, object?> { [nameof(AppUpdates.Compact)] = true });
    }

    private static async Task Render<T>(RecordingUpdates updates, Session session,
        Func<T, Func<string>, RecordingJs, TestNavigation, Task> test,
        IDictionary<string, object?>? parameters = null) where T : IComponent
    {
        var activator = new CapturingActivator();
        var js = new RecordingJs();
        var navigation = new TestNavigation();
        await using var provider = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(session)
            .AddSingleton<IAppUpdates>(updates).AddSingleton<IJSRuntime>(js)
            .AddSingleton<NavigationManager>(navigation).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<T>(parameters is null ? ParameterView.Empty : ParameterView.FromDictionary(parameters));
            var component = activator.Components.OfType<T>().Single();
            string Markup()
            {
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
                return WebUtility.HtmlDecode(rendered.ToHtmlString());
            }
            await test(component, Markup, js, navigation);
        });
    }

    // HtmlRenderer is deliberately static; drive the interactive lifecycle hook
    // after each render, using the same component and its real event handlers.
    private static Task AfterRender(StoreUpdateDialog dialog) => Invoke(dialog, "OnAfterRenderAsync", false);
    private static async Task Invoke<T>(T component, string method, params object?[] arguments)
    {
        var handler = typeof(T).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!;
        if (handler.Invoke(component, arguments) is Task task) await task;
    }
    private static bool IsDisabled(string markup, string label)
    {
        var button = Regex.Matches(markup, "<button(?<attrs>[^>]*)>(?<content>.*?)</button>", RegexOptions.Singleline)
            .Single(match => Regex.Replace(match.Groups["content"].Value, "<[^>]+>", "").Trim() == label);
        return Regex.IsMatch(button.Groups["attrs"].Value, @"\bdisabled(?:\s|=|$)");
    }

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
        public int Shows { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "grindcrest.showDialog") { Assert.Equal("store-update-dialog", Assert.Single(args!)); Shows++; }
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://0.0.0.1/", "https://0.0.0.1/");
        protected override void NavigateToCore(string uri, bool forceLoad) { Uri = ToAbsoluteUri(uri).AbsoluteUri; NotifyLocationChanged(false); }
    }
    private sealed class RecordingUpdates : IAppUpdates
    {
        private event Action? _changed;
        public int Subscribers { get; private set; }
        public event Action? Changed { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
        public UpdateState State { get; private set; } = new(true, false, "1.0.0", null, UpdatePhase.Available, 0,
            "Eine neue Version von Grindcrest ist verfügbar.") { UsesStore = true };
        public int Installs { get; private set; }
        public int Downloads { get; private set; }
        public Func<Task> InstallAction { get; set; } = () => Task.CompletedTask;
        public void Publish(UpdateState state) { State = state; _changed?.Invoke(); }
        public Task CheckAsync() => Task.CompletedTask;
        public Task DownloadAsync() { Downloads++; return Task.CompletedTask; }
        public Task SetBetaAsync(bool enabled) => Task.CompletedTask;
        public Task RequestRestartAsync() { Installs++; return InstallAction(); }
    }
    private sealed class Session : ITrackerSession
    {
        public event Action? Changed;
        public TrackerState State { get; set; } = new();
        public TrackerPreferences Preferences { get; } = new();
        public IReadOnlyList<TrackerMonitor> Monitors { get; } = [];
        public IReadOnlyList<LootHistoryEntry> History { get; } = [];
        public LootPriceSnapshot Prices { get; } = LootPriceCatalog.FixedSnapshot("eu");
        public int Commands { get; private set; }
        public void Publish(TrackerState state) { State = state; Changed?.Invoke(); }
        private Task<TrackerCommandResult> Command() { Commands++; return Task.FromResult(TrackerCommandResult.Success); }
        public Task<TrackerCommandResult> ToggleTrackingAsync() => Command();
        public Task<TrackerCommandResult> PauseAsync() => Command();
        public Task<TrackerCommandResult> NewSessionAsync() => Command();
        public Task<TrackerCommandResult> SetDemoAsync(bool enabled) => Command();
        public Task<TrackerCommandResult> InstallOcrLanguageAsync() => Command();
        public Task<TrackerCommandResult> RecheckOcrLanguageAsync() => Command();
        public Task<PreferenceSaveResult> SavePreferencesAsync(TrackerPreferences preferences, string? apiKey = null, bool resumeAutomaticUpload = false) { Commands++; return Task.FromResult(new PreferenceSaveResult()); }
        public Task<TrackerCommandResult> UploadAsync() => Command();
        public Task<TrackerCommandResult> UploadHistoryAsync(Guid sessionId) => Command();
        public Task<TrackerCommandResult> UpdateHistoryLootAsync(Guid sessionId, IReadOnlyDictionary<string, long> totals, string? characterClass = null) => Command();
        public Task<TrackerCommandResult> UpdateLootQuantityAsync(Guid sessionId, string itemName, long quantity, long originalQuantity) => Command();
        public Task<TrackerCommandResult> DeleteHistoryAsync(Guid sessionId) => Command();
        public Task RefreshPricesAsync() => Command();
        public Task TickAsync() => Command();
        public Task PrepareUpdateRestartAsync() => Command();
        public Task RunPreparedUpdateAsync(Func<Task> install) => Command();
        public Task ShutdownAsync() => Command();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
