using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Updates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class WindowCloseDialogUiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ControllerIsOptionalForTheDialogAndTheExistingAppShell(bool appShell)
    {
        await using var harness = new UiHarness(registerController: false);
        if (appShell) await harness.Render<TrackerApp>();
        else await harness.Render<WindowCloseDialog>();
        await harness.AfterRender(firstRender: true);

        Assert.DoesNotContain("window-close-modal", await harness.Markup());
        Assert.Empty(harness.JavaScript.Calls);
        Assert.False(harness.Controller.IsAvailable);
    }

    [Theory]
    [InlineData("de", "Was soll beim Schließen passieren?", "Programm beenden", "In den Tray minimieren", "Abbrechen")]
    [InlineData("en", "What should happen when you close the window?", "Exit the app", "Minimize to tray", "Cancel")]
    public async Task ChoiceUsesLocalizedAppStylingAccessibleLabelsAndCancelAutofocus(
        string language, string title, string exit, string tray, string cancel)
    {
        await using var harness = new UiHarness();
        await harness.Tracker.SavePreferencesAsync(harness.Tracker.Preferences with { UiLanguage = language });
        await harness.Render<WindowCloseDialog>();
        Assert.False(harness.Controller.IsAvailable);
        Assert.Null(await harness.Controller.AskAsync(_ => Task.FromResult(TrackerCommandResult.Success)));
        await harness.AfterRender(firstRender: true);
        var answer = harness.Ask();
        var html = await harness.Markup();

        Assert.Contains("class=\"modal confirm-modal window-close-modal\"", html);
        Assert.Contains("data-managed-cancel", html);
        Assert.DoesNotContain("@oncancel", html);
        Assert.Contains("aria-labelledby=\"window-close-title\"", html);
        Assert.Contains("aria-describedby=\"window-close-message\"", html);
        Assert.Contains(title, html);
        Assert.Contains(exit, ChoiceButton(html, "exit"));
        Assert.Contains(tray, ChoiceButton(html, "tray"));
        Assert.Contains("<small>", ChoiceButton(html, "exit"));
        Assert.Contains("<svg", ChoiceButton(html, "tray"));
        Assert.Matches("<button[^>]*data-close-cancel[^>]*autofocus[^>]*>" + cancel + "</button>", html);
        await harness.AfterRender();
        await harness.AfterRender();
        Assert.Single(harness.JavaScript.Calls, call => call == "grindcrest.showDialog");
        Assert.True(harness.JavaScript.BrowserOpen);

        await harness.Click("Dismiss", harness.Controller.State!.Id);
        Assert.Null(await answer);
        await harness.AfterRender();
        Assert.False(harness.JavaScript.BrowserOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitAndTrayButtonsForwardTheChoiceOnlyOnce(bool closeToTray)
    {
        await using var harness = new UiHarness();
        await harness.RenderAndAttach();
        var choices = new List<bool>();
        var answer = harness.Controller.AskAsync(choice =>
        {
            choices.Add(choice);
            return Task.FromResult(TrackerCommandResult.Success);
        });
        await harness.Markup();
        var id = harness.Controller.State!.Id;

        await harness.Click("ChooseAsync", id, closeToTray);
        await harness.Click("ChooseAsync", id, !closeToTray);

        Assert.Equal(closeToTray, await answer);
        Assert.Equal(closeToTray, Assert.Single(choices));
        Assert.Null(harness.Controller.State);
    }

    [Fact]
    public async Task CancelLeavesPreferencesUntouchedAndStaleButtonsCannotAnswerANewerPrompt()
    {
        await using var harness = new UiHarness();
        await harness.RenderAndAttach();
        var preferences = harness.Tracker.Preferences;
        var first = harness.Ask();
        await harness.Markup();
        var oldId = harness.Controller.State!.Id;
        await harness.Click("Dismiss", oldId);
        Assert.Null(await first);
        Assert.Same(preferences, harness.Tracker.Preferences);
        var second = harness.Ask();
        await harness.Markup();
        var nextId = harness.Controller.State!.Id;

        await harness.Click("ChooseAsync", oldId, true);
        await harness.Click("Dismiss", oldId);

        Assert.Equal(nextId, harness.Controller.State!.Id);
        Assert.False(second.IsCompleted);
        await harness.Click("Dismiss", nextId);
        Assert.Null(await second);
        Assert.Same(preferences, harness.Tracker.Preferences);
    }

    [Fact]
    public async Task SavingDisablesEveryDismissAndChoiceControlAndFailedSaveAllowsRetry()
    {
        await using var harness = new UiHarness();
        await harness.RenderAndAttach();
        var saved = new TaskCompletionSource<TrackerCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = 0;
        var answer = harness.Controller.AskAsync(_ => ++saves == 1 ? saved.Task : Task.FromResult(TrackerCommandResult.Success));
        await harness.Markup();
        var id = harness.Controller.State!.Id;
        var choosing = harness.Click("ChooseAsync", id, true);
        var saving = await harness.Markup();

        Assert.Contains("aria-busy=\"true\"", saving);
        Assert.Contains("Saving", saving);
        var controls = Regex.Matches(saving, "<button\\b[^>]*>").ToArray();
        Assert.Equal(4, controls.Length);
        Assert.All(controls, button => Assert.Contains("disabled", button.Value));
        await harness.Click("Dismiss", id);
        await harness.Click("ChooseAsync", id, false);
        Assert.False(answer.IsCompleted);
        Assert.Equal(1, saves);

        saved.SetResult(new("Disk locked."));
        await choosing;
        var failed = await harness.Markup();
        Assert.Contains("Your choice could not be saved", failed);
        Assert.Contains("Grindcrest will stay open. Please try again.", failed);
        Assert.Contains("Disk locked.", failed);
        Assert.Contains("role=\"alert\"", failed);
        Assert.DoesNotContain("disabled", ChoiceButton(failed, "tray"));
        Assert.False(answer.IsCompleted);

        await harness.Click("ChooseAsync", id, true);
        Assert.True(await answer);
        Assert.Equal(2, saves);
    }

    [Fact]
    public async Task ShutdownErrorUsesTheSameLocalizedModalAndCanBeDismissed()
    {
        await using var harness = new UiHarness();
        await harness.RenderAndAttach();
        var error = harness.Controller.ShowErrorAsync("Auswahl konnte nicht gespeichert werden",
            "Grindcrest bleibt geöffnet. Bitte versuche es erneut.", "Disk locked.\nSession remains safe.");
        var html = await harness.Markup();

        Assert.Contains("window-close-modal", html);
        Assert.Contains("Your choice could not be saved", html);
        Assert.Contains("Grindcrest will stay open. Please try again.", html);
        Assert.Contains("Disk locked.\nSession remains safe.", html);
        Assert.DoesNotContain("data-close-choice", html);
        Assert.Matches("<button[^>]*data-close-cancel[^>]*autofocus[^>]*>Close</button>", html);
        await harness.AfterRender();
        await harness.Click("Dismiss", harness.Controller.State!.Id);
        await error;
        await harness.AfterRender();
        Assert.False(harness.JavaScript.BrowserOpen);
    }

    [Fact]
    public async Task FailedShowReleasesThePendingNativeCloseAndDoesNotAutomaticallyReattach()
    {
        await using var harness = new UiHarness();
        await harness.RenderAndAttach();
        var answer = harness.Ask();
        await harness.Markup();
        harness.JavaScript.FailNext = "grindcrest.showDialog";

        await harness.AfterRender();

        Assert.Null(await answer.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Null(harness.Controller.State);
        Assert.False(harness.Controller.IsAvailable);
        await harness.AfterRender();
        Assert.False(harness.Controller.IsAvailable);
        Assert.Null(await harness.Ask());
    }

    [Fact]
    public async Task FailedNativeCloseRetriesOnALaterRenderWithoutChangingTheCompletedAnswer()
    {
        await using var harness = new UiHarness();
        await harness.RenderAndAttach();
        var answer = harness.Ask();
        await harness.Markup();
        await harness.AfterRender();
        await harness.Click("Dismiss", harness.Controller.State!.Id);
        Assert.Null(await answer);
        harness.JavaScript.FailNext = "grindcrest.closeDialog";

        await harness.AfterRender();

        Assert.True(harness.JavaScript.BrowserOpen);
        Assert.Single(harness.JavaScript.Calls, call => call == "grindcrest.closeDialog");
        await harness.AfterRender();
        Assert.False(harness.JavaScript.BrowserOpen);
        Assert.Equal(2, harness.JavaScript.Calls.Count(call => call == "grindcrest.closeDialog"));
        Assert.Null(await answer);
    }

    [Fact]
    public async Task ADelayedFailedShowCannotCancelOrDisableANewerRequest()
    {
        await using var harness = new UiHarness();
        await harness.RenderAndAttach();
        var first = harness.Ask();
        await harness.Markup();
        var oldId = harness.Controller.State!.Id;
        var acknowledgement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.JavaScript.PendingShow = acknowledgement.Task;
        var showing = harness.AfterRender();
        Assert.False(showing.IsCompleted);
        await harness.Click("Dismiss", oldId);
        Assert.Null(await first);
        var next = harness.Ask();
        await harness.Markup();
        var nextId = harness.Controller.State!.Id;
        await harness.AfterRender();
        harness.JavaScript.PendingShow = null;

        acknowledgement.SetException(new JSException("Old acknowledgement failed."));
        await showing;

        Assert.True(harness.Controller.IsAvailable);
        Assert.Equal(nextId, harness.Controller.State!.Id);
        Assert.False(next.IsCompleted);
        Assert.Equal(2, harness.JavaScript.Calls.Count(call => call == "grindcrest.showDialog"));
        await harness.Click("Dismiss", nextId);
        Assert.Null(await next);
    }

    [Fact]
    public async Task DisposingTheRendererCancelsAnUnacknowledgedShowAndIgnoresLateActions()
    {
        await using var harness = new UiHarness();
        await harness.RenderAndAttach();
        var answer = harness.Ask();
        await harness.Markup();
        var id = harness.Controller.State!.Id;
        var acknowledgement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.JavaScript.PendingShow = acknowledgement.Task;
        var showing = harness.AfterRender();
        Assert.False(showing.IsCompleted);

        await harness.Renderer.Dispatcher.InvokeAsync(harness.Dialog.Dispose);
        await showing.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Null(await answer);
        Assert.False(harness.Controller.IsAvailable);
        await harness.Click("ChooseAsync", id, true);
        acknowledgement.SetResult();
        await harness.AfterRender();
        Assert.Single(harness.JavaScript.Calls);
        Assert.Null(harness.Controller.State);
    }

    private static string ChoiceButton(string html, string choice) =>
        Regex.Match(html, "<button[^>]*data-close-choice=\"" + choice + "\"[^>]*>[\\s\\S]*?</button>").Value;

    private sealed class UiHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly CapturingActivator _activator = new();
        private Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent _rendered;
        public PreviewTrackerSession Tracker { get; } = new(empty: true);
        public WindowCloseDialogController Controller { get; } = new();
        public RecordingJavaScript JavaScript { get; } = new();
        public HtmlRenderer Renderer { get; }
        public WindowCloseDialog Dialog => _activator.Components.OfType<WindowCloseDialog>().Single();

        public UiHarness(bool registerController = true)
        {
            var services = new ServiceCollection().AddLogging().AddSingleton<ITrackerSession>(Tracker)
                .AddSingleton<IJSRuntime>(JavaScript).AddSingleton<IComponentActivator>(_activator)
                .AddSingleton<NavigationManager, StaticNavigation>()
                .AddSingleton(new GrindGoalStore(null))
                .AddSingleton<IOverlayService>(provider => new OverlayService(Tracker, goals: provider.GetRequiredService<GrindGoalStore>()))
                .AddSingleton<IAppUpdates>(new DisabledAppUpdates("test", "Preview"))
                .AddSingleton<INavigationInterception, NoNavigationInterception>()
                .AddSingleton<IScrollToLocationHash, NoScrollToLocationHash>();
            if (registerController) services.AddSingleton(Controller);
            _services = services.BuildServiceProvider();
            Renderer = new(_services, _services.GetRequiredService<ILoggerFactory>());
        }

        public Task Render<T>() where T : IComponent => Renderer.Dispatcher.InvokeAsync(async () =>
            _rendered = await Renderer.RenderComponentAsync<T>());
        public async Task RenderAndAttach() { await Render<WindowCloseDialog>(); await AfterRender(firstRender: true); }
        public Task<bool?> Ask() => Controller.AskAsync(_ => Task.FromResult(TrackerCommandResult.Success));
        public Task<string> Markup() => Renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(_rendered.ToHtmlString()));
        public Task AfterRender(bool firstRender = false) => Renderer.Dispatcher.InvokeAsync(() =>
            (Task)typeof(WindowCloseDialog).GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Dialog, [firstRender])!);
        public Task Click(string method, params object?[] arguments) => Renderer.Dispatcher.InvokeAsync(() =>
            ((IHandleEvent)Dialog).HandleEventAsync(new EventCallbackWorkItem((Func<Task>)(async () =>
            {
                var result = typeof(WindowCloseDialog).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Dialog, arguments);
                if (result is Task task) await task;
            })), null));
        public async ValueTask DisposeAsync() { await Renderer.DisposeAsync(); await _services.DisposeAsync(); await Tracker.DisposeAsync(); }
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public List<IComponent> Components { get; } = [];
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            Components.Add(component);
            return component;
        }
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoNavigationInterception : INavigationInterception
    {
        public Task EnableNavigationInterceptionAsync() => Task.CompletedTask;
    }

    private sealed class NoScrollToLocationHash : IScrollToLocationHash
    {
        public Task RefreshScrollPositionForHash(string locationAbsolute) => Task.CompletedTask;
    }

    private sealed class RecordingJavaScript : IJSRuntime
    {
        public List<string> Calls { get; } = [];
        public bool BrowserOpen { get; private set; }
        public string? FailNext { get; set; }
        public Task? PendingShow { get; set; }

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            Assert.Equal("window-close-dialog", Assert.Single(args!));
            if (FailNext == identifier) { FailNext = null; throw new JSException("Browser unavailable."); }
            if (identifier == "grindcrest.showDialog")
            {
                BrowserOpen = true;
                if (PendingShow is { } pending) await pending;
            }
            else { Assert.Equal("grindcrest.closeDialog", identifier); BrowserOpen = false; }
            return default!;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
