using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Grindcrest.Live;
using Xunit;

namespace Grindcrest.Live.Tests;

public sealed class PublisherTests
{
    [Fact]
    public async Task StopDrainsInFlightPutBeforeDeleteAndUsesSeparatePublicId()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new ConcurrentQueue<(HttpMethod Method, Guid Id)>();
        using var handler = new Handler(async request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var data = (await request.Content!.ReadFromJsonAsync<LiveSessionUpdate>())!;
                requests.Enqueue((request.Method, data.SessionId));
                entered.SetResult();
                await release.Task;
            }
            else requests.Enqueue((request.Method, Guid.Parse(request.RequestUri!.Segments.Last())));
            return new(HttpStatusCode.NoContent);
        });
        using var publisher = new LiveSessionPublisher(new("https://api.example/"), new string('a', 64), handler);
        var session = SessionApiTests.Sample(DateTimeOffset.UtcNow);
        publisher.Offer(session);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = publisher.StopAsync();
        publisher.Offer(session);
        Assert.False(stop.IsCompleted);
        release.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([HttpMethod.Put, HttpMethod.Delete], requests.Select(r => r.Method));
        Assert.NotEqual(session.SessionId, requests.First().Id);
        Assert.Equal(requests.First().Id, requests.Last().Id);
    }

    [Fact]
    public async Task PausePublishesImmediatelyAndSnapshotCannotMutate()
    {
        var firstPut = new TaskCompletionSource<LiveSessionUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPut = new TaskCompletionSource<LiveSessionUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                await canRead.Task;
                var value = (await request.Content!.ReadFromJsonAsync<LiveSessionUpdate>())!;
                if (!firstPut.TrySetResult(value)) secondPut.TrySetResult(value);
            }
            return new(HttpStatusCode.NoContent);
        });
        using var publisher = new LiveSessionPublisher(new("https://api.example/"), new string('b', 64), handler);
        var original = SessionApiTests.Sample(DateTimeOffset.UtcNow);
        publisher.Offer(original);
        original.Loot["Black Stone"] = 999;
        canRead.SetResult();
        var first = await firstPut.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(8, first.Loot["Black Stone"]);
        publisher.Offer(original with { Paused = true, ActiveSeconds = 100 });
        var paused = await secondPut.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(paused.Paused);
        Assert.Equal(first.SessionId, paused.SessionId);
        Assert.Equal(100, paused.ActiveSeconds);
        Assert.True(paused.Sequence > first.Sequence);
        await publisher.StopAsync();
    }

    [Theory]
    [InlineData("http://example.com", false)]
    [InlineData("https://api.example/path", true)]
    [InlineData("http://localhost:5080", true)]
    [InlineData("https://user:secret@api.example/", false)]
    [InlineData("https://api.example/?token=secret", false)]
    public void EndpointPolicyProtectsCredentials(string url, bool expected) =>
        Assert.Equal(expected, LiveEndpoint.TryParse(url, out _));

    [Fact]
    public async Task NetworkFailureRetriesLatestSnapshotAfterInterval()
    {
        var clock = new TestClock();
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource<LiveSessionUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        using var handler = new Handler(async request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    failed.SetResult();
                    throw new HttpRequestException("Test offline");
                }
                recovered.TrySetResult((await request.Content!.ReadFromJsonAsync<LiveSessionUpdate>())!);
            }
            return new(HttpStatusCode.NoContent);
        });
        using var publisher = new LiveSessionPublisher(new("https://api.example/"), new string('c', 64), handler, clock);
        var sample = SessionApiTests.Sample(clock.GetUtcNow());
        publisher.Offer(sample);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(15));
        publisher.Offer(sample with { ActiveSeconds = 135 });
        var update = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(135, update.ActiveSeconds);
        Assert.Equal(2, update.Sequence);
        await publisher.StopAsync();
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task DisposeDuringRequestDoesNotFaultWorker()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(async request =>
        {
            entered.TrySetResult();
            await release.Task;
            throw new ObjectDisposedException("Test shutdown");
        });
        using var publisher = new LiveSessionPublisher(new("https://api.example/"), new string('d', 64), handler);
        publisher.Offer(SessionApiTests.Sample(DateTimeOffset.UtcNow));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        publisher.Dispose();
        release.SetResult();
        await publisher.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
