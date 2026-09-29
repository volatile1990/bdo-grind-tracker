using System.Net;
using System.Net.Http;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public Task CompletedSessionPriceLookupLeavesNewSessionAndOverlayControlsAvailable() => RunOnHostContextAsync(async () =>
    {
        await using var fixture = new Fixture();
        var prices = new TaskCompletionSource<LootPriceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Prices.Fetch = _ => prices.Task;
        fixture.Begin();
        var completedId = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 10), ("Black Stone", 2));
        await fixture.Service.PauseAsync();

        var completion = fixture.Service.NewSessionAsync();
        try
        {
            Assert.True((await completion.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
            Assert.False(prices.Task.IsCompleted);
            Assert.Empty(fixture.Requests);
            Assert.False(fixture.Service.State.HasSession);
            Assert.False(fixture.Service.State.IsBusy);
            Assert.Contains(completedId, fixture.Service.State.PendingGarmothUploads);
            var overlay = new OverlayMetrics().Update(fixture.Service.State, fixture.Service.Preferences, fixture.Service.Prices);
            Assert.True(overlay.CanNewSession);
            Assert.True(overlay.CanToggleTracking);
            Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
            Assert.True(fixture.Service.State.IsRunning);
            Assert.NotEqual(completedId, fixture.Service.State.SessionId);
            Assert.True((await fixture.Service.PauseAsync()).Succeeded);
            Assert.Empty(fixture.Requests);
        }
        finally
        {
            prices.TrySetResult(fixture.Prices.GetCachedSnapshot("eu"));
            await completion;
            await WaitForAutomaticUploadsAsync(fixture);
        }

        AssertPayload(Assert.Single(fixture.Requests), 2, 10, 2);
        Assert.True(fixture.HistoryStore.Load().Single(entry => entry.SessionId == completedId).GarmothUploadBlocked);
    });

    [Fact]
    public Task MultipleCompletedSessionsUploadInOrderWithTheirOwnLootAndTax() => RunOnHostContextAsync(async () =>
    {
        await using var fixture = new Fixture();
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        fixture.Respond = () => ++sends == 1 ? firstResponse.Task : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        fixture.Begin();
        var firstId = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 10), ("Black Stone", 2));
        await fixture.Service.PauseAsync();

        try
        {
            Assert.True((await fixture.Service.NewSessionAsync().WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
            await WaitUntilAsync(() => fixture.Requests.Count == 1);
            fixture.Begin();
            var secondId = fixture.Service.State.SessionId;
            await fixture.ProcessAfter(TimeSpan.FromMinutes(3), ("Black Crystal Fragment", 20), ("Black Stone", 3));
            Assert.True((await fixture.Service.PauseAsync()).Succeeded);
            Assert.True((await fixture.Service.NewSessionAsync().WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);

            Assert.False(fixture.Service.State.IsBusy);
            Assert.Equal(2, fixture.Service.State.PendingGarmothUploads.Count);
            Assert.Contains(firstId, fixture.Service.State.PendingGarmothUploads);
            Assert.Contains(secondId, fixture.Service.State.PendingGarmothUploads);
            Assert.Single(fixture.Requests);
            Assert.False((await fixture.Service.UploadHistoryAsync(secondId)).Succeeded);
            Assert.False((await fixture.Service.DeleteHistoryAsync(secondId)).Succeeded);
            Assert.True((await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { ValuePack = true })).Succeeded);

            fixture.Begin();
            var currentId = fixture.Service.State.SessionId;
            await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 99));
            firstResponse.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK));
            await WaitForAutomaticUploadsAsync(fixture);

            var requests = fixture.Requests.ToArray();
            Assert.Equal(2, requests.Length);
            AssertPayload(requests[0], 2, 10, 2);
            AssertPayload(requests[1], 3, 20, 3);
            Assert.Equal(currentId, fixture.Service.State.SessionId);
            fixture.AssertTracking(TimeSpan.FromMinutes(1), 99);
            var history = fixture.HistoryStore.Load();
            Assert.True(history.Single(entry => entry.SessionId == firstId).GarmothUploadBlocked);
            Assert.True(history.Single(entry => entry.SessionId == secondId).GarmothUploadBlocked);
        }
        finally
        {
            firstResponse.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK));
            await WaitForAutomaticUploadsAsync(fixture);
        }
    });

    [Fact]
    public Task DelayedAutomaticRejectionDoesNotFailTheCompletedNewSessionCommand() => RunOnHostContextAsync(async () =>
    {
        await using var fixture = new Fixture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Respond = () => response.Task;
        fixture.Begin();
        var completedId = fixture.Service.State.SessionId;
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 10));
        await fixture.Service.PauseAsync();

        var completion = fixture.Service.NewSessionAsync();
        try
        {
            Assert.True((await completion.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
            await WaitUntilAsync(() => fixture.Requests.Count == 1);
            Assert.False(fixture.Service.State.IsBusy);
            Assert.False(fixture.Service.State.HasSession);
        }
        finally
        {
            response.TrySetResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            await WaitForAutomaticUploadsAsync(fixture);
        }

        Assert.True((await completion).Succeeded);
        Assert.True(fixture.Service.State.IsError);
        Assert.NotEqual(completedId, fixture.Service.State.SessionId);
        Assert.False(Assert.Single(fixture.HistoryStore.Load()).GarmothUploadBlocked);
        Assert.Single(fixture.Requests);
    });
}
