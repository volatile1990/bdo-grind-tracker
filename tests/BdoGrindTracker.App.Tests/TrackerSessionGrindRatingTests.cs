using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using System.Reflection;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task LiveStateUsesTheDetectedSpotReferenceAcrossPauseAndNewSession()
    {
        await using var fixture = new Fixture(autoUpload: false);
        Assert.Null(fixture.Service.State.GrindBenchmark);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 16_000));
        var live = fixture.Service.State;
        Assert.Equal(LootSpotCatalog.HermesiaId, live.SpotId);
        Assert.Equal(GarmothGrindBenchmarks.Find(live.SpotId), live.GrindBenchmark);
        Assert.Equal(GrindRatingTier.High, GrindRatingEvaluator.Evaluate(live.SpotId, 16_000,
            live.Elapsed, live.GrindBenchmark).Tier);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(live.GrindBenchmark, fixture.Service.State.GrindBenchmark);
        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Null(fixture.Service.State.GrindBenchmark);
        Assert.Empty(fixture.Service.State.Loot.Totals);
    }

    [Fact]
    public async Task SpotChangesNeverReuseThePreviousBenchmark()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var old = fixture.Service.State.GrindBenchmark;
        Assert.Equal(LootSpotCatalog.HermesiaId, old?.SpotId);
        SetField(fixture.Service, "_sessionSpotId", LootSpotCatalog.MagaiaId);
        fixture.Service.RefreshPendingState();
        Assert.Equal(LootSpotCatalog.MagaiaId, fixture.Service.State.GrindBenchmark?.SpotId);
        Assert.NotEqual(old, fixture.Service.State.GrindBenchmark);
        SetField(fixture.Service, "_sessionSpotId", "unknown");
        fixture.Service.RefreshPendingState();
        Assert.Null(fixture.Service.State.GrindBenchmark);
    }

    [Fact]
    public Task StartingTrackingRefreshesWithoutWaitingAndUsesAllFetchedSpots() => RunOnHostContextAsync(async () =>
    {
        var response = new TaskCompletionSource<GarmothBenchmarkSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new SyntheticBenchmarks { Fetch = _ => response.Task };
        await using var fixture = new Fixture(autoUpload: false, benchmarkProvider: provider);
        Assert.Empty(provider.Tokens);
        try
        {
            var result = await fixture.Service.ToggleTrackingAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(result.Succeeded);
            Assert.True(fixture.Service.State.IsRunning);
            Assert.Single(provider.Tokens);
            Assert.Contains("aktualisiert", fixture.Service.State.GrindBenchmarkStatus);
            await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 100));
            Assert.Equal(GarmothGrindBenchmarks.Find(LootSpotCatalog.HermesiaId), fixture.Service.State.GrindBenchmark);

            var refreshed = RefreshedBenchmarks(20_000);
            response.SetResult(refreshed);
            await AwaitBenchmarkRefreshesAsync(fixture.Service);
            Assert.Equal(refreshed.Find(LootSpotCatalog.HermesiaId), fixture.Service.State.GrindBenchmark);
            Assert.Equal(refreshed.Status, fixture.Service.State.GrindBenchmarkStatus);
            SetField(fixture.Service, "_sessionSpotId", LootSpotCatalog.MagaiaId);
            fixture.Service.RefreshPendingState();
            Assert.Equal(refreshed.Find(LootSpotCatalog.MagaiaId), fixture.Service.State.GrindBenchmark);
            Assert.Single(provider.Tokens);
        }
        finally { response.TrySetResult(GarmothBenchmarkSnapshot.Bundled); }
    });

    [Fact]
    public async Task PausesDoNotRefreshAndEachActiveHourDoes()
    {
        var provider = new SyntheticBenchmarks();
        await using var fixture = new Fixture(autoUpload: false, benchmarkProvider: provider);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        Assert.Single(provider.Tokens);
        await fixture.ProcessAfter(TimeSpan.Zero, ("Black Crystal Fragment", 1));
        await fixture.ProcessAfter(TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59), ("Black Crystal Fragment", 1));
        await fixture.Service.TickAsync();
        Assert.Single(provider.Tokens);
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 1));
        await fixture.Service.TickAsync();
        Assert.Equal(2, provider.Tokens.Count);
        await fixture.Service.TickAsync();
        Assert.Equal(2, provider.Tokens.Count);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        fixture.Time.Advance(TimeSpan.FromDays(1));
        await fixture.Service.TickAsync();
        Assert.Equal(2, provider.Tokens.Count);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        Assert.Equal(2, provider.Tokens.Count);
        await fixture.ProcessAfter(TimeSpan.Zero, ("Black Crystal Fragment", 1));
        await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 1));
        await fixture.Service.TickAsync();
        Assert.Equal(3, provider.Tokens.Count);
    }

    [Fact]
    public async Task RestoredSessionRefreshesOnFirstStartAtItsExistingHour()
    {
        var provider = new SyntheticBenchmarks();
        var restored = CurrentSessionStoreTests.Example() with { Duration = TimeSpan.FromMinutes(130) };
        await using var fixture = new Fixture(autoUpload: false, benchmarkProvider: provider, restoredSession: restored);
        Assert.Equal(restored.SessionId, fixture.Service.State.SessionId);
        await fixture.Service.TickAsync();
        Assert.Empty(provider.Tokens);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        Assert.Single(provider.Tokens);
        Assert.Equal(restored.Duration, fixture.Service.State.Elapsed);
        await fixture.Service.TickAsync();
        Assert.Single(provider.Tokens);
    }

    [Fact]
    public async Task AutomaticPauseClockCorrectionDoesNotRepeatAnEarlierHourRefresh()
    {
        var provider = new SyntheticBenchmarks();
        await using var fixture = new Fixture(autoUpload: false, benchmarkProvider: provider);
        await fixture.Service.ToggleTrackingAsync();
        await fixture.ProcessAfter(TimeSpan.Zero, ("Black Crystal Fragment", 1));
        await fixture.ProcessAfter(TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59), ("Black Crystal Fragment", 1));
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await fixture.Service.TickAsync();
        Assert.Equal(2, provider.Tokens.Count);
        fixture.Time.Advance(TimeSpan.FromMinutes(fixture.Service.Preferences.AutoPauseMinutes));
        await fixture.Service.TickAsync();
        Assert.False(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.Elapsed < TimeSpan.FromHours(1));
        await fixture.Service.ToggleTrackingAsync();
        await fixture.ProcessAfter(TimeSpan.Zero, ("Black Crystal Fragment", 1));
        await fixture.ProcessAfter(TimeSpan.FromSeconds(1), ("Black Crystal Fragment", 1));
        await fixture.Service.TickAsync();
        Assert.Equal(2, provider.Tokens.Count);
    }

    [Fact]
    public async Task FailedCaptureStartDoesNotFetchBenchmarks()
    {
        var provider = new SyntheticBenchmarks();
        await using var fixture = new Fixture(autoUpload: false, benchmarkProvider: provider,
            analyzer: new SyntheticAnalyzer { ValidateSetup = _ => throw new InvalidOperationException("No panel") });
        Assert.False((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        Assert.Empty(provider.Tokens);
    }

    [Fact]
    public async Task FailedRefreshRetainsCacheAndDoesNotStopTracking()
    {
        var cached = RefreshedBenchmarks(20_000);
        var provider = new SyntheticBenchmarks
        {
            Cached = cached,
            Fetch = _ => Task.FromException<GarmothBenchmarkSnapshot>(new InvalidDataException("Bad response")),
        };
        await using var fixture = new Fixture(autoUpload: false, benchmarkProvider: provider);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 100));
        Assert.True(fixture.Service.State.IsRunning);
        Assert.False(fixture.Service.State.IsError);
        Assert.Equal(cached.Find(LootSpotCatalog.HermesiaId), fixture.Service.State.GrindBenchmark);
        Assert.Contains("letzte verfügbare", fixture.Service.State.GrindBenchmarkStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NewSessionOrHourCancelsPendingRefreshAndIgnoresItsLateResponse(bool newSession) => RunOnHostContextAsync(async () =>
    {
        var first = new TaskCompletionSource<GarmothBenchmarkSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<GarmothBenchmarkSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new SyntheticBenchmarks { Fetch = _ => ++calls == 1 ? first.Task : second.Task };
        await using var fixture = new Fixture(autoUpload: false, benchmarkProvider: provider);
        try
        {
            await fixture.Service.ToggleTrackingAsync();
            var oldId = fixture.Service.State.SessionId;
            if (newSession)
            {
                await fixture.Service.PauseAsync();
                await fixture.Service.NewSessionAsync();
                Assert.NotEqual(oldId, fixture.Service.State.SessionId);
                await fixture.Service.ToggleTrackingAsync();
            }
            else
            {
                await fixture.ProcessAfter(TimeSpan.Zero, ("Black Crystal Fragment", 1));
                await fixture.ProcessAfter(TimeSpan.FromHours(1), ("Black Crystal Fragment", 100));
                await fixture.Service.TickAsync();
                Assert.Equal(oldId, fixture.Service.State.SessionId);
            }
            Assert.True(provider.Tokens[0].IsCancellationRequested);
            Assert.Equal(2, provider.Tokens.Count);
            await fixture.ProcessAfter(TimeSpan.FromMinutes(1), ("Black Crystal Fragment", 100));
            var expected = RefreshedBenchmarks(30_000);
            second.SetResult(expected);
            await WaitUntilAsync(() => fixture.Service.State.GrindBenchmark == expected.Find(LootSpotCatalog.HermesiaId));
            first.SetResult(RefreshedBenchmarks(40_000));
            await AwaitBenchmarkRefreshesAsync(fixture.Service);
            Assert.Equal(expected.Find(LootSpotCatalog.HermesiaId), fixture.Service.State.GrindBenchmark);
            Assert.Equal(expected.Status, fixture.Service.State.GrindBenchmarkStatus);
        }
        finally
        {
            first.TrySetResult(GarmothBenchmarkSnapshot.Bundled);
            second.TrySetResult(GarmothBenchmarkSnapshot.Bundled);
        }
    });

    [Fact]
    public Task ShutdownCancelsAndAwaitsTheRefreshBeforeDisposingTheProvider() => RunOnHostContextAsync(async () =>
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new SyntheticBenchmarks
        {
            Fetch = async token =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally
                {
                    cancelled.TrySetResult();
                    await release.Task;
                }
                return GarmothBenchmarkSnapshot.Bundled;
            },
        };
        await using var fixture = new Fixture(autoUpload: false, benchmarkProvider: provider);
        try
        {
            await fixture.Service.ToggleTrackingAsync();
            var shutdown = fixture.Service.ShutdownAsync();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(shutdown.IsCompleted);
            Assert.False(provider.Disposed);
            release.SetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(provider.Disposed);
            Assert.False(fixture.Service.State.IsRunning);
        }
        finally { release.TrySetResult(); }
    });

    private static Task AwaitBenchmarkRefreshesAsync(TrackerSessionService service)
    {
        var field = typeof(TrackerSessionService).GetField("_benchmarkRefreshTasks", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Task.WhenAll((List<Task>)field.GetValue(service)!).WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static GarmothBenchmarkSnapshot RefreshedBenchmarks(decimal average) => new(
        GarmothGrindBenchmarks.All.Select(benchmark => benchmark with
        {
            AverageTrashPerHour = average,
            HighTrashPerHour = average + 2_000,
            TopTrashPerHour = average + 4_000,
        }).ToArray(), $"Garmoth aktualisiert: {average}");

    private sealed class SyntheticBenchmarks : IGarmothGrindBenchmarkProvider
    {
        public GarmothBenchmarkSnapshot Cached { get; set; } = GarmothBenchmarkSnapshot.Bundled;
        public Func<CancellationToken, Task<GarmothBenchmarkSnapshot>> Fetch { get; set; } =
            _ => Task.FromResult(RefreshedBenchmarks(20_000));
        public List<CancellationToken> Tokens { get; } = [];
        public bool Disposed { get; private set; }
        public GarmothBenchmarkSnapshot GetCachedSnapshot() => Cached;
        public Task<GarmothBenchmarkSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return Fetch(cancellationToken);
        }
        public void Dispose() => Disposed = true;
    }
}
