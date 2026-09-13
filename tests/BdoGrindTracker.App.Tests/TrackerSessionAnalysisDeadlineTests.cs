using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task LateWorkerBlocksReconfigurationThenResumeAddsFreshProjectionToPreservedManualBaseline()
    {
        var capture = new PassiveCaptureSession(_ => new Bitmap(2, 2),
            frameInterval: TimeSpan.FromDays(1), analysisTimeout: TimeSpan.FromMilliseconds(500));
        var pending = new TaskCompletionSource<FrameAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new SyntheticAnalyzer();
        long total = 0, revision = 0, nextDrop = 100;
        var resets = 0;
        var configured = 0;
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        analyzer.OnReset = () => { resets++; total = 0; revision = 0; };
        analyzer.ConfigureLanguage = _ => configured++;
        FrameAnalysisResult Projection(long version, long quantity) => new([], [], 1, "deadline-resume", 0, 0, 0, 0, null)
        {
            SpotId = LootSpotCatalog.HermesiaId,
            LootProjection = new(version, new Dictionary<string, long> { ["Black Crystal Fragment"] = quantity },
                1, DateTimeOffset.UtcNow),
        };
        Task<FrameAnalysisResult> Recognize()
        {
            total += nextDrop;
            var result = Projection(++revision, total);
            analyzer.CompletionResult = result;
            observed.TrySetResult();
            return Task.FromResult(result);
        }
        analyzer.Analyze = Recognize;
        await using var fixture = new Fixture(autoUpload: false, analyzer: analyzer, suppliedCapture: capture);
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.True((await fixture.Service.UpdateLootQuantityAsync(fixture.Service.State.SessionId,
            "Black Crystal Fragment", 125, 100)).Succeeded);
        analyzer.Analyze = () => pending.Task;
        try
        {
            Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
            await WaitUntilAsync(() => analyzer.Calls == 2);
            Assert.False((await fixture.Service.PauseAsync().WaitAsync(TimeSpan.FromSeconds(3))).Succeeded);
            var beforeRecheck = configured;
            Assert.False((await fixture.Service.RecheckOcrLanguageAsync()).Succeeded);
            Assert.Equal(beforeRecheck, configured);
            Assert.Equal(1, resets);
        }
        finally { pending.TrySetResult(Projection(99, 999)); }
        await WaitUntilAsync(() => !capture.HasPendingAnalysis);
        Assert.Equal(125, fixture.Service.State.Loot.TotalQuantity);

        nextDrop = 15;
        observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        analyzer.Analyze = Recognize;
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(2, resets);
        Assert.Equal(140, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(140, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
    }

    [Fact]
    public async Task AnalysisDeadlineSavesCommittedLootAndDefersNativeResourcesUntilLateWorkerFinishes()
    {
        Bitmap? captured = null;
        var capture = new PassiveCaptureSession(_ => captured = new Bitmap(2, 2),
            frameInterval: TimeSpan.FromDays(1), analysisTimeout: TimeSpan.FromMilliseconds(500));
        await using var fixture = new Fixture(autoUpload: false, suppliedCapture: capture);
        fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 10));
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await WaitUntilAsync(() => fixture.Analyzer.Calls == 1);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var completions = fixture.Analyzer.CompletionCalls;
        var pending = new TaskCompletionSource<FrameAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Analyzer.Analyze = () => pending.Task;
        fixture.Analyzer.CompletionResult = Analysis(("Black Crystal Fragment", 99));
        try
        {
            Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
            await WaitUntilAsync(() => fixture.Analyzer.Calls == 2);
            var stopped = await fixture.Service.PauseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(stopped.Error);
            Assert.Contains("Texterkennung", stopped.Error);
            Assert.False(fixture.Service.State.IsRunning);
            Assert.False(fixture.Service.State.IsBusy);
            Assert.True(capture.HasPendingAnalysis);
            Assert.True(capture.AnalysisFailed);
            Assert.False(fixture.Analyzer.Disposed);
            Assert.Equal(completions, fixture.Analyzer.CompletionCalls);
            Assert.Equal(10, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
            Assert.NotNull(captured);
            captured.GetPixel(0, 0); // The timed-out native worker still owns this input.
            Assert.False((await fixture.Service.ToggleTrackingAsync()).Succeeded);
            Assert.False((await fixture.Service.NewSessionAsync()).Succeeded);

            await fixture.Service.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(fixture.Service.State.ShutdownFailed);
            Assert.False(fixture.Analyzer.Disposed);
            captured.GetPixel(0, 0);
        }
        finally { pending.TrySetResult(Analysis(("Black Crystal Fragment", 500))); }
        await WaitUntilAsync(() => fixture.Analyzer.Disposed);
        Assert.False(capture.HasPendingAnalysis);
        Assert.Throws<ArgumentException>(() => captured!.GetPixel(0, 0));
        Assert.Equal(10, fixture.Service.State.Loot.TotalQuantity);
        Assert.Equal(10, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
        Assert.Equal(completions, fixture.Analyzer.CompletionCalls);
    }

    [Fact]
    public async Task FaultedAnalysisNeverFlushesPartiallyMutatedAnalyzerIntoSavedLoot()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Analyzer.NextResult = Analysis(("Black Crystal Fragment", 10));
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await WaitUntilAsync(() => fixture.Analyzer.Calls == 1);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var completions = fixture.Analyzer.CompletionCalls;
        fixture.Analyzer.Analyze = () => Task.FromException<FrameAnalysisResult>(new InvalidOperationException("broken OCR"));
        fixture.Analyzer.CompletionResult = Analysis(("Black Crystal Fragment", 99));
        Assert.True((await fixture.Service.ToggleTrackingAsync()).Succeeded);
        await WaitUntilAsync(() => fixture.Analyzer.Calls == 2);
        await fixture.Service.PauseAsync();
        await fixture.Service.ShutdownAsync();
        Assert.Equal(10, Assert.Single(fixture.HistoryStore.Load()).Totals["Black Crystal Fragment"]);
        Assert.Equal(completions, fixture.Analyzer.CompletionCalls);
        Assert.True(fixture.Analyzer.Disposed);
    }
}
