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

    public PassiveCaptureSession(PassiveScreenCapture capture)
        : this(
            CreateCaptureDelegate(capture),
            TimeProvider.System,
            frameInterval: null,
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
        _frameInterval = frameInterval ?? CompanionFrameInterval;
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
    /// Captures at the Companion cadence while one ordered consumer analyzes
    /// frames. A full bounded queue pauses capture instead of discarding frames.
    /// Normal stop ends capture, then finishes every acquired frame before the
    /// session's reconciler can be flushed or reset.
    /// </summary>
    internal void StartCompanion(
        Rectangle desktopRegion,
        Func<Bitmap, CapturedFrameMetadata, CancellationToken, Task> onFrame)
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
                () => RunAsync(desktopRegion, onFrame, token),
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
            () => CaptureFramesAsync(desktopRegion, frames.Writer, producerCancellation.Token),
            CancellationToken.None);

        try
        {
            await foreach (var frame in frames.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                using (frame.Bitmap)
                {
                    // A normal pause must not cancel delayed OCR and lose loot.
                    // Individual recognition passes enforce their own limits.
                    await onFrame(frame.Bitmap, frame.Metadata, CancellationToken.None)
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
        CancellationToken cancellationToken)
    {
        var lastCapturedAt = DateTimeOffset.MinValue;
        long sequence = 0;

        try
        {
            while (await frames.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var capturedFrame = _captureFrame(desktopRegion, cancellationToken);
                // Capture succeeded: even a stop arriving now must drain this
                // bitmap. The single producer already reserved a queue slot.
                using var pending = new PendingCapture(capturedFrame.Bitmap);
                var frameDeadlineStart = _timeProvider.GetTimestamp();
                var capturedAt = EnsureMonotonicTimestamp(
                    _timeProvider.GetUtcNow(),
                    lastCapturedAt);
                lastCapturedAt = capturedAt;
                sequence++;

                var metadata = new CapturedFrameMetadata(
                    sequence,
                    capturedAt,
                    capturedFrame.IsHdr, capturedFrame.IsToneMapped);
                if (!frames.TryWrite(new QueuedCapture(capturedFrame.Bitmap, metadata)))
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

    private readonly record struct QueuedCapture(Bitmap Bitmap, CapturedFrameMetadata Metadata);

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
}

internal sealed class CaptureSessionStoppedEventArgs(Exception? error) : EventArgs
{
    public Exception? Error { get; } = error;
}
