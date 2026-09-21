using System.Reflection;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Tests;

public sealed class HistoryDashboardLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NavigationDuringWheelSetupDoesNotBindAColumnCallbackAfterDisposal(bool alreadyRendered)
    {
        var js = new ControlledJavaScript();
        var component = CreateComponent(js);
        if (alreadyRendered) await AfterRender(component);
        var previousBindings = js.ColumnReferences.Count;
        var wheel = js.DelayNext("grindcrest.enableHorizontalWheel", "spot-session-table-scroll");

        var rendering = AfterRender(component);
        Assert.False(rendering.IsCompleted);
        component.Dispose();
        wheel.SetResult();
        await rendering;

        Assert.Equal(previousBindings, js.ColumnReferences.Count);
        Assert.All(js.ColumnReferences, reference =>
            Assert.Throws<ObjectDisposedException>(() => reference.Value));
    }

    [Fact]
    public async Task LateRenderAfterDisposalDoesNotInvokeJavaScript()
    {
        var js = new ControlledJavaScript();
        var component = CreateComponent(js);
        component.Dispose();

        await AfterRender(component);

        Assert.Empty(js.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RouteChangeDuringWheelSetupSkipsTheObsoleteColumnBinding(bool anotherSpot)
    {
        var js = new ControlledJavaScript();
        using var component = CreateComponent(js);
        var wheel = js.DelayNext("grindcrest.enableHorizontalWheel", "spot-session-table-scroll");
        var rendering = AfterRender(component);
        SetField(component, "_spotId", anotherSpot
            ? LootSpotCatalog.Spots.First(spot => spot.Id != LootSpotCatalog.HermesiaId).Id
            : null);

        wheel.SetResult();
        await rendering;

        Assert.Empty(js.ColumnReferences);
        await AfterRender(component);
        Assert.Equal(anotherSpot ? 1 : 0, js.ColumnReferences.Count);
    }

    [Theory]
    [InlineData("grindcrest.closeDialog", "history-edit", 1)]
    [InlineData("grindcrest.closeDialog", "history-confirm", 2)]
    [InlineData("grindcrest.showDialog", "history-edit", 3)]
    public async Task NavigationDuringDialogSetupStopsRemainingRenderInterop(
        string delayedIdentifier, string delayedElement, int expectedCalls)
    {
        var js = new ControlledJavaScript();
        var component = CreateComponent(js);
        SetField(component, "_closeDialogsAfterRender", true);
        SetField(component, "_showEditAfterRender", true);
        var dialog = js.DelayNext(delayedIdentifier, delayedElement);

        var rendering = AfterRender(component);
        Assert.False(rendering.IsCompleted);
        component.Dispose();
        dialog.SetResult();
        await rendering;

        Assert.Equal(expectedCalls, js.Calls.Count);
        Assert.Empty(js.ColumnReferences);
    }

    [Fact]
    public async Task ActiveRendersReuseAReferenceAndDisposeReleasesIt()
    {
        var js = new ControlledJavaScript();
        var component = CreateComponent(js);

        await AfterRender(component);
        await AfterRender(component);

        Assert.Equal(2, js.ColumnReferences.Count);
        var reference = js.ColumnReferences[0];
        Assert.Same(reference, js.ColumnReferences[1]);
        Assert.Same(component, reference.Value);
        Assert.Equal(new[]
        {
            "grindcrest.enableHorizontalWheel", "grindcrest.enableColumnDrag",
            "grindcrest.enableHorizontalWheel", "grindcrest.enableColumnDrag"
        }, js.Calls);
        component.Dispose();
        component.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reference.Value);
    }

    [Theory]
    [InlineData("script")]
    [InlineData("disposed")]
    [InlineData("disconnected")]
    [InlineData("cancelled")]
    public async Task ActiveRenderDoesNotHideJavaScriptFailures(string failureKind)
    {
        var js = new ControlledJavaScript();
        using var component = CreateComponent(js);
        var wheel = js.DelayNext("grindcrest.enableHorizontalWheel", "spot-session-table-scroll");
        var rendering = AfterRender(component);
        var failure = InteropFailure(failureKind);

        wheel.SetException(failure);

        Assert.Same(failure, await Assert.ThrowsAsync(failure.GetType(), () => rendering));
        Assert.Empty(js.ColumnReferences);
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("disconnected")]
    [InlineData("cancelled")]
    public async Task PendingInteropShutdownFailureAfterDisposalDoesNotEscape(string failureKind)
    {
        var js = new ControlledJavaScript();
        var component = CreateComponent(js);
        await AfterRender(component);
        var wheel = js.DelayNext("grindcrest.enableHorizontalWheel", "spot-session-table-scroll");
        var rendering = AfterRender(component);

        component.Dispose();
        wheel.SetException(InteropFailure(failureKind));
        await rendering;

        Assert.Equal(3, js.Calls.Count);
        Assert.Single(js.ColumnReferences);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateColumnCallbacksAfterDisposalDoNotChangePreferences(bool reorder)
    {
        var tracker = new PreviewTrackerSession(empty: true);
        var component = CreateComponent(new ControlledJavaScript(), tracker);
        var preferences = tracker.Preferences;
        var spotId = LootSpotCatalog.HermesiaId;
        var trash = Presentation.Profile(spotId)!.TrashItemName;
        component.Dispose();

        if (reorder)
            await component.SaveColumnOrder(spotId, new[] { trash }
                .Concat(LootSpotCatalog.GetRequired(spotId).AllowedItems).Distinct(StringComparer.Ordinal).Reverse().ToArray());
        else
            await component.ToggleSessionFavorite(spotId, trash);

        Assert.Same(preferences, tracker.Preferences);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColumnCallbackDoesNotRenderWhenPreferenceSaveDisposesComponent(bool reorder)
    {
        var tracker = new PreviewTrackerSession(empty: true);
        using var component = CreateComponent(new ControlledJavaScript(), tracker);
        var spotId = LootSpotCatalog.HermesiaId;
        var trash = Presentation.Profile(spotId)!.TrashItemName;
        var disposedDuringSave = false;
        // A save notification can navigate away before the awaiting callback resumes.
        tracker.Changed += () => { disposedDuringSave = true; component.Dispose(); };

        if (reorder)
        {
            var order = new[] { trash }.Concat(LootSpotCatalog.GetRequired(spotId).AllowedItems)
                .Distinct(StringComparer.Ordinal).Reverse().ToArray();
            await component.SaveColumnOrder(spotId, order);
            Assert.Equal(order, tracker.Preferences.LootColumnOrders[spotId]);
        }
        else
        {
            await component.ToggleSessionFavorite(spotId, trash);
            Assert.Contains(trash, tracker.Preferences.FavoriteItems);
        }

        Assert.True(disposedDuringSave);
        // With no renderer attached, an obsolete StateHasChanged would throw.
    }

    private static Exception InteropFailure(string kind) => kind switch
    {
        "disposed" => new ObjectDisposedException("WebView"),
        "disconnected" => new JSDisconnectedException("WebView disconnected"),
        "cancelled" => new OperationCanceledException("WebView stopped"),
        _ => new JSException("Wheel setup failed")
    };

    private static HistoryDashboard CreateComponent(IJSRuntime js, ITrackerSession? tracker = null)
    {
        var component = new HistoryDashboard();
        typeof(TrackerComponentBase).GetProperty("Tracker", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, tracker ?? new PreviewTrackerSession());
        typeof(HistoryDashboard).GetProperty("JS", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, js);
        SetField(component, "_spotId", LootSpotCatalog.HermesiaId);
        return component;
    }

    private static void SetField(HistoryDashboard component, string name, object? value) =>
        typeof(HistoryDashboard).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);

    private static Task AfterRender(HistoryDashboard component) =>
        (Task)typeof(HistoryDashboard).GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, [false])!;

    private sealed class ControlledJavaScript : IJSRuntime
    {
        private string? _delayedIdentifier;
        private string? _delayedElement;
        private TaskCompletionSource? _completion;
        public List<string> Calls { get; } = [];
        public List<DotNetObjectReference<HistoryDashboard>> ColumnReferences { get; } = [];

        public TaskCompletionSource DelayNext(string identifier, string element)
        {
            _delayedIdentifier = identifier;
            _delayedElement = element;
            return _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "grindcrest.enableColumnDrag")
            {
                var reference = Assert.IsType<DotNetObjectReference<HistoryDashboard>>(args![1]);
                // JSRuntime serialization also rejects disposed DotNetObjectReferences.
                // Reading Value exercises that guard without needing a WebView process.
                _ = reference.Value;
                ColumnReferences.Add(reference);
            }
            if (identifier == _delayedIdentifier && Equals(args![0], _delayedElement))
            {
                _delayedIdentifier = null;
                return new(AwaitCompletion<TValue>(_completion!));
            }
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        private static async Task<TValue> AwaitCompletion<TValue>(TaskCompletionSource completion)
        {
            await completion.Task;
            return default!;
        }
    }
}
