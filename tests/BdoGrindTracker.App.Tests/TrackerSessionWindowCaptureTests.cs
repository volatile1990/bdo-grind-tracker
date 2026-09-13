using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task ResizingPausedGamePreservesAuthoritativeLootManualCorrectionAndSavedTotals()
    {
        var bounds = new Rectangle(100, 100, 8, 6);
        using var window = new PassiveWindowCapture(() => new(123, 456, Size.Empty),
            _ => new(bounds, bounds, bounds, false), (_, _) => new ServiceWindowSource());
        var capture = new PassiveCaptureSession(window, TimeSpan.FromDays(1));
        long automaticTotal = 0, revision = 0, nextDrop = 100;
        var resets = 0;
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new SyntheticAnalyzer
        {
            OnReset = () => { automaticTotal = 0; revision = 0; resets++; },
        };
        analyzer.Analyze = () =>
        {
            automaticTotal += nextDrop;
            var result = new FrameAnalysisResult([], [], 1, "resize-projection", 0, 0, 0, 0, null)
            {
                SpotId = LootSpotCatalog.HermesiaId,
                LootProjection = new(++revision,
                    new Dictionary<string, long> { ["Black Crystal Fragment"] = automaticTotal },
                    (int)revision, DateTimeOffset.UtcNow),
            };
            analyzer.CompletionResult = result;
            observed.TrySetResult();
            return Task.FromResult(result);
        };
        await using var fixture = new Fixture(autoUpload: false, analyzer: analyzer, suppliedCapture: capture);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(100, fixture.Service.State.Loot.TotalQuantity);
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
            "Black Crystal Fragment", 125, 100)).Succeeded);

        bounds = new Rectangle(200, 150, 9, 7);
        nextDrop = 15;
        observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);

        Assert.Equal(1, resets);
        Assert.Equal(140, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(140, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
        Assert.Equal(140, new CurrentSessionStore(Path.Combine(fixture.DirectoryPath,
            CurrentSessionStore.FileName)).Load()!.Totals["Black Crystal Fragment"]);
    }

    [Fact]
    public async Task WindowCaptureUsesClientCalibrationAndCanResumeAfterMovingToAnotherMonitor()
    {
        var geometry = new WindowCaptureGeometry(new Rectangle(-1000, 100, 8, 6),
            new Rectangle(-1000, 100, 8, 6), new Rectangle(-1000, 100, 8, 6), false);
        var sources = new List<ServiceWindowSource>();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validations = new List<Size>();
        var foregroundChecks = 0;
        using var window = new PassiveWindowCapture(() => new(123, 456, Size.Empty), _ => geometry,
            (_, _) => { var source = new ServiceWindowSource(); sources.Add(source); return source; });
        var capture = new PassiveCaptureSession(window, TimeSpan.FromDays(1));
        var analyzer = new SyntheticAnalyzer
        {
            ValidateSetup = size => validations.Add(size),
            Analyze = () => { observed.TrySetResult(); return Task.FromResult(Analysis()); },
        };
        await using var fixture = new Fixture(autoUpload: false, analyzer: analyzer, suppliedCapture: capture,
            lootScrollVisible: _ => { foregroundChecks++; return false; });
        Assert.True(fixture.Service.CapturesGameWindow);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(new Size(8, 6), Assert.Single(validations));
        Assert.True(Assert.Single(sources).Disposed);

        geometry = new(new Rectangle(2200, 100, 8, 6), new Rectangle(2200, 100, 8, 6),
            new Rectangle(2200, 100, 8, 6), false);
        observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(2, validations.Count);
        Assert.All(validations, size => Assert.Equal(new Size(8, 6), size));
        Assert.All(sources, source => Assert.True(source.Disposed));
        Assert.Equal(0, foregroundChecks);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WindowCaptureConsentPreservesSessionUntilAccepted(bool resume, bool accepted)
    {
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompts = 0;
        var sources = new List<ServiceWindowSource>();
        using var window = CreateServiceWindowCapture(() =>
        {
            var source = new ServiceWindowSource();
            sources.Add(source);
            return source;
        });
        var capture = new PassiveCaptureSession(window, TimeSpan.FromDays(1));
        await using var fixture = new Fixture(autoUpload: false, suppliedCapture: capture,
            prepareWindowCapture: () =>
            {
                if (++prompts == 1 && resume) return Task.FromResult(true);
                prompted.TrySetResult();
                return decision.Task;
            });
        if (resume)
        {
            fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 5));
            Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
            await WaitUntilAsync(() => fixture.Analyzer.Calls == 1);
            fixture.Time.Advance(TimeSpan.FromSeconds(30));
            Assert.True((await fixture.Service.PauseAsync()).Succeeded);
            Assert.Equal(5, fixture.Service.State.Loot.TotalQuantity);
            Assert.True(Assert.Single(sources).Disposed);
        }

        var before = fixture.Service.State;
        var previousSourceCount = sources.Count;
        var pending = fixture.Service.ToggleTrackingAsync();
        try
        {
            await prompted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Time.Advance(TimeSpan.FromMinutes(10));
            fixture.Service.RefreshPendingState();
            Assert.False(pending.IsCompleted);
            Assert.False(fixture.Service.State.IsRunning);
            Assert.False(fixture.Clock.IsRunning);
            Assert.False(fixture.Activity.IsRunning);
            Assert.Equal(resume, fixture.Service.State.HasSession);
            Assert.Equal(before.SessionId, fixture.Service.State.SessionId);
            Assert.Equal(before.Elapsed, fixture.Service.State.Elapsed);
            Assert.Equal(before.Loot.TotalQuantity, fixture.Service.State.Loot.TotalQuantity);
            Assert.Equal(previousSourceCount, sources.Count);
            Assert.Equal(before.RecordingPath, fixture.Service.State.RecordingPath);
            decision.SetResult(accepted);
            Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        }
        finally
        {
            decision.TrySetResult(false);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(resume ? 2 : 1, prompts);
        Assert.Equal(before.SessionId, fixture.Service.State.SessionId);
        Assert.Equal(before.Loot.TotalQuantity, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(before.Elapsed, fixture.Service.State.Elapsed);
        if (accepted)
        {
            await WaitUntilAsync(() => fixture.Analyzer.Calls == previousSourceCount + 1);
            Assert.True(fixture.Service.State.HasSession);
            Assert.True(fixture.Service.State.IsRunning);
            Assert.True(fixture.Clock.IsRunning);
            Assert.True(fixture.Clock.IsWaitingForFirstDrop);
            Assert.Equal(previousSourceCount + 1, sources.Count);
            fixture.Time.Advance(TimeSpan.FromSeconds(15));
            Assert.True((await fixture.Service.PauseAsync()).Succeeded);
            Assert.Equal(before.Elapsed, fixture.Service.State.Elapsed);
            Assert.All(sources, source => Assert.True(source.Disposed));
        }
        else
        {
            Assert.False(fixture.Service.State.IsRunning);
            Assert.Equal(resume, fixture.Service.State.HasSession);
            Assert.False(fixture.Clock.IsRunning);
            Assert.False(fixture.Activity.IsRunning);
            Assert.Equal(previousSourceCount, sources.Count);
        }
    }

    [Fact]
    public async Task DesktopCaptureStartsWithoutRequestingWindowCaptureConsent()
    {
        var prompts = 0;
        await using var fixture = new Fixture(autoUpload: false, prepareWindowCapture: () =>
        {
            prompts++;
            return Task.FromResult(false);
        });
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await WaitUntilAsync(() => fixture.Captures > 0);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.True(fixture.Service.State.HasSession);
        Assert.Equal(0, prompts);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
    }

    [Fact]
    public async Task ShutdownWhileWindowCaptureConsentIsPendingCannotStartCaptureAfterApproval()
    {
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sources = 0;
        using var window = CreateServiceWindowCapture(() =>
        {
            sources++;
            return new ServiceWindowSource();
        });
        var capture = new PassiveCaptureSession(window, TimeSpan.FromDays(1));
        await using var fixture = new Fixture(autoUpload: false, suppliedCapture: capture,
            prepareWindowCapture: () =>
            {
                prompted.TrySetResult();
                return decision.Task;
            });
        var pending = fixture.Service.ToggleTrackingAsync();
        Task? shutdown = null;
        try
        {
            await prompted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            shutdown = fixture.Service.ShutdownAsync();
            Assert.False(shutdown.IsCompleted);
            fixture.Time.Advance(TimeSpan.FromMinutes(10));
            decision.SetResult(true);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(fixture.Service.State.IsRunning);
            Assert.False(fixture.Service.State.HasSession);
            Assert.False(fixture.Clock.IsRunning);
            Assert.Equal(TimeSpan.Zero, fixture.Service.State.Elapsed);
            Assert.Equal(0, sources);
            Assert.Equal(0, fixture.Analyzer.Calls);
            Assert.True(fixture.Analyzer.Disposed);
            Assert.Empty(fixture.HistoryStore.Load());
        }
        finally
        {
            decision.TrySetResult(false);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            if (shutdown is not null) await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static PassiveWindowCapture CreateServiceWindowCapture(Func<IWindowFrameSource> createSource)
    {
        var bounds = new Rectangle(100, 100, 8, 6);
        return new PassiveWindowCapture(() => new(123, 456, Size.Empty),
            _ => new WindowCaptureGeometry(bounds, bounds, bounds, false), (_, _) => createSource());
    }

    private sealed class ServiceWindowSource : IWindowFrameSource
    {
        public bool Disposed { get; private set; }
        public CapturedDesktopBitmap Capture(Func<WindowCaptureGeometry> geometry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(new Bitmap(geometry().ClientBounds.Width, geometry().ClientBounds.Height), false, false);
        }
        public void Dispose() => Disposed = true;
    }
}
