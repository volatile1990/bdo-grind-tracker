namespace BdoGrindTracker.App.Capture;

internal interface IGrindStandbyCapture : IDisposable
{
    bool IsGameForeground { get; }
    CapturedDesktopBitmap Capture(CancellationToken cancellationToken);
    void SetBurst(bool enabled);
    void Suspend();
}

/// <summary>
/// On-demand window snapshots for automatic start. Between standby samples there
/// is no native capture session; only a short confirmation burst retains one.
/// </summary>
internal sealed class GrindStandbyCapture : IGrindStandbyCapture
{
    private readonly object _sync = new();
    private readonly object _cancellationSync = new();
    private readonly PassiveWindowCapture _capture;
    private readonly IGameForegroundMonitor _foreground;
    private CancellationTokenSource? _inFlight;
    private Rectangle? _region;
    private bool _burst;
    private volatile bool _disposed;

    internal GrindStandbyCapture() : this(new PassiveWindowCapture(), new NativeGameForegroundMonitor()) { }

    internal GrindStandbyCapture(PassiveWindowCapture capture, IGameForegroundMonitor foreground)
    {
        _capture = capture;
        _foreground = foreground;
        _foreground.Changed += OnForegroundChanged;
    }

    public bool IsGameForeground => !_disposed && _foreground.IsGameForeground;

    public CapturedDesktopBitmap Capture(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_foreground.IsGameForeground) StopCaptureCore();
            EnsureForeground();
            using var captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_cancellationSync) _inFlight = captureCancellation;
            var succeeded = false;
            Bitmap? capturedBitmap = null;
            try
            {
                _region ??= _capture.PrepareCapture();
                EnsureForeground();
                captureCancellation.Token.ThrowIfCancellationRequested();
                var captured = _capture.Capture(_region.Value, captureCancellation.Token);
                capturedBitmap = captured.Bitmap;
                try
                {
                    // Reject a focus transition during native acquisition/readback,
                    // including switching away and back before the call completes.
                    captureCancellation.Token.ThrowIfCancellationRequested();
                    EnsureForeground();
                    succeeded = true;
                    return captured;
                }
                catch
                {
                    captured.Bitmap.Dispose();
                    throw;
                }
            }
            finally
            {
                lock (_cancellationSync) _inFlight = null;
                if (!_burst || !succeeded || !_foreground.IsGameForeground)
                {
                    try { StopCaptureCore(); }
                    catch
                    {
                        // If native teardown fails there is no caller to own a
                        // successfully acquired result from this throwing call.
                        if (succeeded) capturedBitmap?.Dispose();
                        throw;
                    }
                }
            }
        }
    }

    public void SetBurst(bool enabled)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _burst = enabled;
            if (!enabled) StopCaptureCore();
        }
    }

    public void Suspend()
    {
        CancelCurrentCapture();
        lock (_sync)
        {
            if (_disposed) return;
            _burst = false;
            StopCaptureCore();
        }
    }

    private void OnForegroundChanged(object? sender, EventArgs args) => Suspend();

    private void CancelCurrentCapture()
    {
        lock (_cancellationSync) _inFlight?.Cancel();
    }

    private void EnsureForeground()
    {
        if (!_foreground.IsGameForeground)
            throw new InvalidOperationException("Automatisches Tracking wartet, bis Black Desert im Vordergrund ist.");
    }

    private void StopCaptureCore()
    {
        _region = null;
        _capture.StopCapture();
    }

    public void Dispose()
    {
        _foreground.Changed -= OnForegroundChanged;
        CancelCurrentCapture();
        try
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _region = null;
                _capture.Dispose();
            }
        }
        finally { _foreground.Dispose(); }
    }
}
