using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Services;

internal sealed partial class TrackerSessionService
{
    private readonly IGarmothGrindBenchmarkProvider? _benchmarkProvider;
    private GarmothBenchmarkSnapshot _benchmarkSnapshot;
    private string _benchmarkStatus;
    private readonly List<Task> _benchmarkRefreshTasks = [];
    private CancellationTokenSource? _benchmarkRefreshCancellation;
    private long? _benchmarkRefreshHour;
    private long _benchmarkRefreshGeneration;

    private void RefreshGrindBenchmarksIfDue()
    {
        if (_benchmarkProvider is null || !_uiRunning || _shutdownStarted || _disposed) return;
        var activeHour = _sessionClock.Elapsed.Ticks / TimeSpan.TicksPerHour;
        // Auto-pause can subtract an idle tail and move Elapsed backwards.
        // Resuming that hour must not repeat an already attempted refresh.
        if (_benchmarkRefreshHour is { } refreshedHour && activeHour <= refreshedHour) return;
        CancelGrindBenchmarkRefresh();
        _benchmarkRefreshHour = activeHour;
        var cancellation = new CancellationTokenSource();
        _benchmarkRefreshCancellation = cancellation;
        _benchmarkStatus = "Garmoth-Daten werden aktualisiert …";
        _benchmarkRefreshTasks.RemoveAll(task => task.IsCompleted);
        _benchmarkRefreshTasks.Add(RefreshGrindBenchmarksAsync(_sessionId, _benchmarkRefreshGeneration, cancellation));
    }

    private async Task RefreshGrindBenchmarksAsync(Guid sessionId, long generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            // Keep the host context: only this continuation publishes UI state.
            // The provider fetches all spots before automatic spot detection is complete.
            var snapshot = await _benchmarkProvider!.RefreshAsync(cancellation.Token);
            if (!CanApplyGrindBenchmarks(sessionId, generation, cancellation)) return;
            _benchmarkSnapshot = snapshot;
            _benchmarkStatus = snapshot.Status;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!CanApplyGrindBenchmarks(sessionId, generation, cancellation)) return;
            _benchmarkStatus = "Garmoth nicht erreichbar · letzte verfügbare Referenzwerte";
        }
        finally
        {
            if (ReferenceEquals(_benchmarkRefreshCancellation, cancellation))
                _benchmarkRefreshCancellation = null;
            cancellation.Dispose();
        }
        if (!_shutdownStarted && !_disposed && sessionId == _sessionId && generation == _benchmarkRefreshGeneration)
            PublishState();
    }

    private bool CanApplyGrindBenchmarks(Guid sessionId, long generation, CancellationTokenSource cancellation) =>
        !_shutdownStarted && !_disposed && !cancellation.IsCancellationRequested &&
        sessionId == _sessionId && generation == _benchmarkRefreshGeneration;

    private void CancelGrindBenchmarkRefresh()
    {
        _benchmarkRefreshGeneration++;
        var cancellation = _benchmarkRefreshCancellation;
        _benchmarkRefreshCancellation = null;
        cancellation?.Cancel();
    }

    private void ResetGrindBenchmarkRefresh()
    {
        CancelGrindBenchmarkRefresh();
        _benchmarkRefreshHour = null;
        _benchmarkStatus = _benchmarkSnapshot.Status;
    }
}
