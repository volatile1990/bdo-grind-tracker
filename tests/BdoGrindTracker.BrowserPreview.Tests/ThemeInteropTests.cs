using System.Reflection;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Theming;
using Microsoft.JSInterop;

namespace BdoGrindTracker.BrowserPreview.Tests;

public sealed class ThemeInteropTests
{
    [Fact]
    public async Task IndependentOverlayThemeNeverChangesTheDocumentTheme()
    {
        await using var tracker = new PreviewTrackerSession();
        await tracker.SavePreferencesAsync(tracker.Preferences with { ThemeId = AppThemes.Light });
        var js = new DelayedJavaScript();
        var component = CreateComponent(tracker, js);
        var initial = AfterRender(component);
        js.Calls[0].Acknowledge();
        await initial;

        await tracker.SavePreferencesAsync(tracker.Preferences with { OverlayThemeId = AppThemes.Obsidian });
        await AfterRender(component);

        Assert.Single(js.Calls);
        Assert.Equal(AppThemes.Light, js.DocumentTheme);
        Assert.Equal(AppThemes.Obsidian, tracker.Preferences.EffectiveOverlayThemeId);
    }

    [Fact]
    public async Task RapidThemeSwitchesBeforePriorAcknowledgementsKeepTheLatestDocumentTheme()
    {
        var tracker = new PreviewTrackerSession();
        var js = new DelayedJavaScript();
        var component = CreateComponent(tracker, js);
        var initial = AfterRender(component);
        js.Calls[0].Acknowledge();
        await initial;

        var pendingRenders = new List<Task>();
        foreach (var themeId in new[] { AppThemes.BlackDesert, AppThemes.Light, AppThemes.Cats, AppThemes.Grindcrest })
        {
            await tracker.SavePreferencesAsync(tracker.Preferences with { ThemeId = themeId });
            var pending = AfterRender(component);
            pendingRenders.Add(pending);
            Assert.False(pending.IsCompleted);
            Assert.Equal(themeId, js.DocumentTheme);
        }
        Assert.Equal(new[] { AppThemes.Grindcrest, AppThemes.BlackDesert, AppThemes.Light, AppThemes.Cats, AppThemes.Grindcrest },
            js.Calls.Select(call => call.Theme));
        Assert.Equal(AppThemes.Grindcrest, js.DocumentTheme);

        // Reverse the acknowledgements: completion of the obsolete request must
        // neither replace the latest theme nor force another request on render.
        for (var index = pendingRenders.Count - 1; index >= 0; index--)
        {
            js.Calls[index + 1].Acknowledge();
            await pendingRenders[index];
        }
        await AfterRender(component);
        Assert.Equal(5, js.Calls.Count);
        Assert.Equal(AppThemes.Grindcrest, js.DocumentTheme);
    }

    [Fact]
    public async Task FailedThemeInteropCanRetryOnTheNextRender()
    {
        var tracker = new PreviewTrackerSession();
        var js = new DelayedJavaScript();
        var component = CreateComponent(tracker, js);
        var first = AfterRender(component);
        js.Calls[0].Fail(new JSDisconnectedException("Connection interrupted"));
        await first;

        var retried = AfterRender(component);
        Assert.Equal(2, js.Calls.Count);
        js.Calls[1].Acknowledge();
        await retried;
        await AfterRender(component);
        Assert.Equal(2, js.Calls.Count);
    }

    private static TrackerApp CreateComponent(ITrackerSession tracker, IJSRuntime js)
    {
        var component = new TrackerApp();
        typeof(TrackerComponentBase).GetProperty("Tracker", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, tracker);
        typeof(TrackerApp).GetProperty("JS", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, js);
        return component;
    }

    private static Task AfterRender(TrackerApp component) =>
        (Task)typeof(TrackerApp).GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, [false])!;

    private sealed class DelayedJavaScript : IJSRuntime
    {
        public List<PendingCall> Calls { get; } = [];
        public string? DocumentTheme { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "grindcrest.setLanguage") return ValueTask.FromResult(default(TValue)!);
            Assert.Equal("grindcrest.setTheme", identifier);
            DocumentTheme = Assert.IsType<string>(Assert.Single(args!));
            var call = new PendingCall(DocumentTheme);
            Calls.Add(call);
            return new ValueTask<TValue>(AwaitAcknowledgement<TValue>(call));
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        private static async Task<TValue> AwaitAcknowledgement<TValue>(PendingCall call)
        {
            await call.Completion.Task;
            return default!;
        }
    }

    private sealed class PendingCall(string theme)
    {
        public string Theme { get; } = theme;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Acknowledge() => Completion.SetResult();
        public void Fail(Exception exception) => Completion.SetException(exception);
    }
}
