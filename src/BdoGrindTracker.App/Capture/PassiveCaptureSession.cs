using System.Threading.Channels;

namespace BdoGrindTracker.App.Capture;

internal sealed class PassiveCaptureSession : IAsyncDisposable
{
    // FUN_1406cb3d0 returns Duration { secs: 0, nanos: 450_000_000 }
    // after every completed Companion frame (0x1406d0c79..0x1406d0c92).
    // The capture runner adds that duration to the frame-acquisition timestamp
    // and waits only for the remaining time (0x1406c729b..0x1406c7319).
    internal static readonly TimeSpan CompanionFrameInterval =
        TimeSpan.FromMilliseconds(450);

    // Target for live temporal tracking. Actual cadence also depends on capture
    // cost and bounded-queue backpressure, which are measured per frame below.
    internal static readonly TimeSpan LiveFrameInterval = TimeSpan.FromMilliseconds(200);

    // Four waiting frames plus the frame currently being analyzed. Capacity is
    // reserved before capture, so a slow OCR worker cannot grow bitmap memory.
    internal const int DefaultMaximumQueuedFrames = 4;

    private readonly Func<Rectangle, CancellationToken, CapturedDesktopBitmap> _captureFrame;
    private IDisposable? _captureOwner;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _frameInterval;
    private readonly int _maximumQueuedFrames;
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;
    private DateTimeOffset? _captureEpochUtc;
    private long _captureEpochTimestamp;
    private DateTimeOffset _lastCapturedAtUtc = DateTimeOffset.MinValue;

    public PassiveCaptureSession(PassiveScreenCapture capture, TimeSpan? frameInterval = null)
        : this(
            CreateCaptureDelegate(capture),
            TimeProvider.System,
            frameInterval,
            maximumQueuedFrames: DefaultMaximumQueuedFrames,
            captureOwner: capture)
    {
    }

    internal PassiveCaptureSession(
        Func<Rectangle, Bitmap> captureFrame,
        TimeProvider? timeProvider = null,
        TimeSpan? frameInterval = null,
        int maximumQueuedFrames = DefaultMaximumQueuedFrames)
        : this(
            WrapLegacyCapture(captureFrame),
            timeProvider,
            frameInterval,
            maximumQueuedFrames,
            captureOwner: null)
    {
    }

    internal PassiveCaptureSession(
        Func<Rectangle, CapturedDesktopBitmap> captureFrame,
        TimeProvider? timeProvider = null,
        TimeSpan? frameInterval = null,
        int maximumQueuedFrames = DefaultMaximumQueuedFrames)
        : this(
            WrapMetadataCapture(captureFrame),
            timeProvider,
            frameInterval,
            maximumQueuedFrames,
            captureOwner: null)
    {
    }

    private PassiveCaptureSession(
        Func<Rectangle, CancellationToken, CapturedDesktopBitmap> captureFrame,
        TimeProvider? timeProvider,
        TimeSpan? frameInterval,
        int maximumQueuedFrames,
        IDisposable? captureOwner)
    {
        _captureFrame = captureFrame ?? throw new ArgumentNullException(nameof(captureFrame));
        _captureOwner = captureOwner;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _frameInterval = frameInterval ?? LiveFrameInterval;
        _maximumQueuedFrames = maximumQueuedFrames;
        if (_frameInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frameInterval));
        }
        if (_maximumQueuedFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumQueuedFrames));
        }
    }

    public event EventHandler<CaptureSessionStoppedEventArgs>? Stopped;

    public TimeSpan FrameInterval => _frameInterval;
    public int MaximumQueuedFrames => _maximumQueuedFrames;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _runTask is { IsCompleted: false };
            }
        }
    }

    /// <summary>
    /// Captures at the configured cadence while one ordered consumer analyzes
    /// frames. A full bounded queue pauses capture instead of discarding frames.
    /// Normal stop ends capture, then finishes every acquired frame before the
    /// session's reconciler can be flushed or reset.
    /// </summary>
    internal void StartCompanion(
        Rectangle desktopRegion,
        Func<Bitmap, CapturedFrameMetadata, CancellationToken, Task> onFrame,
        Func<bool>? canObserveHud = null)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        ValidateArguments(desktopRegion);

        lock (_sync)
        {
            if (_runTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("Die Aufnahme läuft bereits.");
            }

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _runTask = Task.Run(
                () => RunAsync(desktopRegion, onFrame, canObserveHud, token),
                CancellationToken.None);
        }
    }

    public async Task StopAsync()
    {
        Task? task;

        lock (_sync)
        {
            _cancellation?.Cancel();
            task = _runTask;
        }

        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A user-requested stop is the normal completion path.
        }
    }

    private async Task RunAsync(
        Rectangle desktopRegion,
        Func<Bitmap, CapturedFrameMetadata, CancellationToken, Task> onFrame,
        Func<bool>? canObserveHud,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var frames = Channel.CreateBounded<QueuedCapture>(new BoundedChannelOptions(_maximumQueuedFrames)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        var producer = Task.Run(
            () => CaptureFramesAsync(desktopRegion, frames.Writer, canObserveHud, producerCancellation.Token),
            CancellationToken.None);

        try
        {
            await foreach (var frame in frames.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                using (frame.Bitmap)
                {
                    var metadata = frame.Metadata with
                    {
                        QueueDelay = _timeProvider.GetElapsedTime(frame.EnqueuedAtTimestamp)
                    };
                    // A normal pause must not cancel delayed OCR and lose loot.
                    // Individual recognition passes enforce their own limits.
                    await onFrame(frame.Bitmap, metadata, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            producerCancellation.Cancel();
        }
        finally
        {
            var producerFailure = await producer.ConfigureAwait(false);
            failure ??= producerFailure;
            // A failing consumer cannot safely advance the reconciler. Dispose
            // the remaining frames only after the producer has relinquished it.
            while (frames.Reader.TryRead(out var remaining)) remaining.Bitmap.Dispose();
            Stopped?.Invoke(this, new CaptureSessionStoppedEventArgs(failure));
        }
    }

    private async Task<Exception?> CaptureFramesAsync(
        Rectangle desktopRegion,
        ChannelWriter<QueuedCapture> frames,
        Func<bool>? canObserveHud,
        CancellationToken cancellationToken)
    {
        long? lastCaptureTimestamp = null;
        long sequence = 0;

        try
        {
            while (true)
            {
                var queueWaitStarted = _timeProvider.GetTimestamp();
                if (!await frames.WaitToWriteAsync(cancellationToken).ConfigureAwait(false)) break;
                var backpressureDuration = _timeProvider.GetElapsedTime(queueWaitStarted);
                cancellationToken.ThrowIfCancellationRequested();
                // Include capture work in the deadline: a 60 ms acquisition
                // leaves 140 ms at the nominal 200 ms cadence, not another 200.
                var frameDeadlineStart = _timeProvider.GetTimestamp();
                var hudVisible = TryObserveHud(canObserveHud);
                var capturedFrame = _captureFrame(desktopRegion, cancellationToken);
                var captureTimestamp = _timeProvider.GetTimestamp();
                // Capture succeeded: even a stop arriving now must drain this
                // bitmap. The single producer already reserved a queue slot.
                using var pending = new PendingCapture(capturedFrame.Bitmap);
                hudVisible = hudVisible && TryObserveHud(canObserveHud);
                if (_captureEpochUtc is null)
                {
                    _captureEpochUtc = _timeProvider.GetUtcNow();
                    _captureEpochTimestamp = captureTimestamp;
                }
                // The temporal counter needs real elapsed capture time, even
                // when Windows corrects its wall clock while paused. Keep one
                // epoch for this capture-session instance; the monotonic clock
                // continues through pauses so their actual gaps are preserved.
                var capturedAt = EnsureMonotonicTimestamp(
                    _captureEpochUtc.Value +
                        _timeProvider.GetElapsedTime(_captureEpochTimestamp, captureTimestamp),
                    _lastCapturedAtUtc);
                _lastCapturedAtUtc = capturedAt;
                sequence++;

                var metadata = new CapturedFrameMetadata(
                    sequence,
                    capturedAt,
                    capturedFrame.IsHdr, capturedFrame.IsToneMapped)
                {
                    CanObserveHud = hudVisible,
                    TargetFrameInterval = _frameInterval,
                    CaptureDuration = _timeProvider.GetElapsedTime(frameDeadlineStart, captureTimestamp),
                    CaptureInterval = lastCaptureTimestamp is { } previousCapture
                        ? _timeProvider.GetElapsedTime(previousCapture, captureTimestamp)
                        : null,
                    BackpressureDuration = backpressureDuration
                };
                lastCaptureTimestamp = captureTimestamp;
                var enqueuedAtTimestamp = _timeProvider.GetTimestamp();
                if (!frames.TryWrite(new QueuedCapture(capturedFrame.Bitmap, metadata, enqueuedAtTimestamp)))
                {
                    throw new InvalidOperationException("Ein aufgenommenes Bild konnte nicht zur OCR-Verarbeitung übergeben werden.");
                }
                pending.TransferOwnership();

                var elapsed = _timeProvider.GetElapsedTime(frameDeadlineStart);
                var remaining = _frameInterval - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, _timeProvider, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A user-requested stop is the normal completion path.
        }
        catch (Exception exception)
        {
            return exception;
        }
        finally
        {
            frames.TryComplete();
        }
        return null;
    }

    private readonly record struct QueuedCapture(
        Bitmap Bitmap,
        CapturedFrameMetadata Metadata,
        long EnqueuedAtTimestamp);

    private static bool TryObserveHud(Func<bool>? canObserveHud)
    {
        try { return canObserveHud?.Invoke() ?? false; }
        catch (Exception) { return false; } // Optional HUD checks cannot fail loot capture.
    }

    private sealed class PendingCapture(Bitmap bitmap) : IDisposable
    {
        private Bitmap? _bitmap = bitmap;
        public void TransferOwnership() => _bitmap = null;
        public void Dispose() => _bitmap?.Dispose();
    }

    private static void ValidateArguments(Rectangle desktopRegion)
    {
        if (desktopRegion.Width <= 0 || desktopRegion.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(desktopRegion),
                desktopRegion,
                "Der Aufnahmebereich muss eine positive Breite und Höhe haben.");
        }
    }

    private static DateTimeOffset EnsureMonotonicTimestamp(
        DateTimeOffset timestamp,
        DateTimeOffset previousTimestamp) =>
        timestamp > previousTimestamp
            ? timestamp
            : previousTimestamp.AddTicks(1);

    private static Func<Rectangle, CancellationToken, CapturedDesktopBitmap>
        CreateCaptureDelegate(PassiveScreenCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return capture.Capture;
    }

    private static Func<Rectangle, CancellationToken, CapturedDesktopBitmap>
        WrapLegacyCapture(Func<Rectangle, Bitmap> captureFrame)
    {
        ArgumentNullException.ThrowIfNull(captureFrame);
        return (region, _) => new CapturedDesktopBitmap(captureFrame(region), IsHdr: false);
    }

    private static Func<Rectangle, CancellationToken, CapturedDesktopBitmap>
        WrapMetadataCapture(Func<Rectangle, CapturedDesktopBitmap> captureFrame)
    {
        ArgumentNullException.ThrowIfNull(captureFrame);
        return (region, _) => captureFrame(region);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        IDisposable? captureOwner;
        lock (_sync)
        {
            _cancellation?.Dispose();
            _cancellation = null;
            _runTask = null;
            captureOwner = _captureOwner;
            _captureOwner = null;
        }

        captureOwner?.Dispose();
    }
}

internal readonly record struct CapturedFrameMetadata(
    long Sequence,
    DateTimeOffset CapturedAtUtc,
    bool IsHdr = false,
    bool IsToneMapped = false)
{
    // The legacy HDR threshold expects clipped BGRA highlights near 255.
    // Tone-mapped scRGB instead uses the existing SDR recognition thresholds.
    public bool UseHdrOcr => IsHdr && !IsToneMapped;
    // Bound to acquisition, before OCR/queue delay can change the foreground window.
    public bool CanObserveHud { get; init; }

    // Durations use the monotonic capture clock, so a wall-clock correction
    // cannot make queue latency or observed cadence negative.
    public TimeSpan TargetFrameInterval { get; init; }
    public TimeSpan CaptureDuration { get; init; }
    public TimeSpan? CaptureInterval { get; init; }
    public TimeSpan BackpressureDuration { get; init; }
    public TimeSpan QueueDelay { get; init; }
}

internal sealed class CaptureSessionStoppedEventArgs(Exception? error) : EventArgs
{
    public Exception? Error { get; } = error;
}
