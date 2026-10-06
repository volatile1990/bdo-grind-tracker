using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Analysis;

internal interface IAutomaticGrindMonitor : IDisposable
{
    bool IsGameForeground { get; }
    bool IsConfirming { get; }
    Task PendingAnalysis => Task.CompletedTask;
    Task<AutoStartDetection?> CheckAsync(string language, CancellationToken cancellationToken);
}

/// <summary>One slow visual sample, followed only on a candidate by a bounded OCR burst.</summary>
internal sealed class AutomaticGrindMonitor(
    IGrindStandbyCapture capture,
    IGrindStartVisualDetector visual,
    CompanionCalibration calibration,
    Func<string, ILootFrameAnalyzer> createAnalyzer,
    TimeProvider? timeProvider = null,
    RotationStartWatcher? rotationStart = null,
    TimeSpan? initialBannerTimeout = null) : IAutomaticGrindMonitor
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly RotationStartWatcher _rotationStart = rotationStart ?? new RotationStartWatcher();
    private RotationStartSighting? _seenRotationStart;
    private long? _seenRotationStartTimestamp;
    private int _disposed;
    private readonly TimeSpan _initialBannerTimeout = initialBannerTimeout ?? AnalysisTimeout;

    /// <summary>The rotation start banner that is still recent enough to confirm the next trash drop.</summary>
    public RotationStartSighting? RecentRotationStart => _seenRotationStart is { } seen &&
        _seenRotationStartTimestamp is { } observed &&
        _time.GetElapsedTime(observed) <= RotationStartWatcher.Validity ? seen : null;

    /// <summary>The sighting a starting session takes over: one banner confirms one start, never a later drop too.</summary>
    private RotationStartSighting? TakeRecentRotationStart()
    {
        var recent = RecentRotationStart;
        _seenRotationStart = null;
        _seenRotationStartTimestamp = null;
        return recent;
    }
    private long? _lastSample;
    private long? _lastBurstEnd;
    private volatile bool _confirming;
    private Task _pendingAnalysis = Task.CompletedTask;
    public Task PendingAnalysis => _pendingAnalysis;
    internal static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan BurstDuration = TimeSpan.FromSeconds(6);
    internal static readonly TimeSpan BurstCooldown = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan AnalysisTimeout = TimeSpan.FromSeconds(5);
    public bool IsGameForeground => capture.IsGameForeground;
    public bool IsConfirming => _confirming;
    internal Func<IDisposable> PreserveCapturePriority { get; init; } = CpuScheduling.PreserveCapturePriority;

    public async Task<AutoStartDetection?> CheckAsync(string language, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_pendingAnalysis.IsCompleted) return null;
        if (!capture.IsGameForeground)
        {
            visual.Reset();
            capture.Suspend();
            return null;
        }
        if (_lastSample is { } sampled && _time.GetElapsedTime(sampled) < SampleInterval) return null;
        // Standby samples and their confirmation burst acquire the first loot
        // evidence too. Protect them before capture, including any OCR that
        // continues after a canceled or timed-out probe.
        var priorityLease = PreserveCapturePriority();
        try
        {
            return await CheckCurrentSampleAsync(language, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            var pending = PendingAnalysis;
            if (pending.IsCompleted) priorityLease.Dispose();
            else _ = ReleaseCapturePriorityAfterAnalysisAsync(pending, priorityLease);
        }
    }

    private async Task<AutoStartDetection?> CheckCurrentSampleAsync(string language,
        CancellationToken cancellationToken)
    {
        _lastSample = _time.GetTimestamp();
        using var replay = new AutoStartDetection();
        var first = capture.Capture(cancellationToken);
        replay.Add(first, _time.GetUtcNow());
        if (first.Bitmap.Width != calibration.ScreenWidth || first.Bitmap.Height != calibration.ScreenHeight)
            throw new InvalidOperationException("Die Spielfenstergröße passt nicht zur BDO-Konfiguration. " +
                "Bitte UI-Konfiguration speichern.");
        // A rotation start banner alone never starts a session: walking past a spot can show it. It only marks the
        // spot as grinding, so the next trash drop starts the session without waiting for five of them.
        if (await ObserveRotationStartAsync(first.Bitmap, replay.Frames[0].Metadata.CapturedAtUtc,
            replay, cancellationToken).ConfigureAwait(false) is { } sighting)
        {
            _seenRotationStart = sighting;
            _seenRotationStartTimestamp = _lastSample;
        }
        else if (RecentRotationStart is null)
        {
            _seenRotationStart = null;
            _seenRotationStartTimestamp = null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!visual.Observe(first.Bitmap, calibration) ||
            (_lastBurstEnd is { } ended && _time.GetElapsedTime(ended) < BurstCooldown)) return null;
        ILootFrameAnalyzer? analyzer = null;
        var failed = false;
        try
        {
            _confirming = true;
            capture.SetBurst(true);
            analyzer = createAnalyzer(language);
            if (!analyzer.IsAvailable) throw new InvalidOperationException(analyzer.Status);
            analyzer.ConfigureGameLanguage(language);
            analyzer.ValidateCaptureSetup(first.Bitmap.Size);
            // Observe already compared this row against an earlier visual
            // baseline. Recognize that first arrival without requiring another.
            var confirmation = new GrindStartConfirmation(replay.Frames[0].Metadata.CapturedAtUtc,
                acceptInitialArrival: true);
            var started = _time.GetTimestamp();
            var frame = first;
            var at = replay.Frames[^1].Metadata.CapturedAtUtc;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!capture.IsGameForeground) return null;
                var remaining = BurstDuration - _time.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero) return null;
                var currentAnalyzer = analyzer;
                var currentFrame = frame;
                var currentAt = at;
                var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var worker = Task.Run(async () =>
                {
                    return await currentAnalyzer.AnalyzeAsync(currentFrame.Bitmap, currentAt,
                        currentFrame.IsHdr && !currentFrame.IsToneMapped, currentFrame.IsToneMapped,
                        workerCancellation.Token).ConfigureAwait(false);
                }, CancellationToken.None);
                FrameAnalysisResult analysis;
                try
                {
                    analysis = await worker.WaitAsync(remaining < AnalysisTimeout ? remaining : AnalysisTimeout,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    // A native OCR call may ignore cancellation. It owns its analyzer
                    // and image until completion; never dispose underneath it.
                    Task cancellation;
                    try { cancellation = ObserveCancellationAsync(workerCancellation.CancelAsync()); }
                    catch (ObjectDisposedException) { cancellation = Task.CompletedTask; }
                    if (!worker.IsCompleted || !cancellation.IsCompleted)
                    {
                        var retained = replay.Detach();
                        var abandoned = analyzer;
                        analyzer = null;
                        _pendingAnalysis = DisposeAfterAnalysisAsync(Task.WhenAll(worker, cancellation),
                            abandoned, retained, workerCancellation);
                    }
                    else workerCancellation.Dispose();
                    // Reaching the burst deadline is an ordinary rejected probe.
                    if (error is TimeoutException && remaining < AnalysisTimeout) return null;
                    throw;
                }
                workerCancellation.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                if (!capture.IsGameForeground) return null;
                if (analysis.LootProjection is { } projection && confirmation.Observe(projection))
                {
                    replay.DetectedDropAt = projection.LatestArrivalAt ?? at;
                    replay.SpotId = confirmation.SpotId;
                    replay.RotationStart = TakeRecentRotationStart();
                    return replay.Detach();
                }
                if (_time.GetElapsedTime(started) >= BurstDuration) return null;
                await Task.Delay(PassiveCaptureSession.LiveFrameInterval, _time, cancellationToken).ConfigureAwait(false);
                if (!capture.IsGameForeground) return null;
                frame = capture.Capture(cancellationToken);
                at = _time.GetUtcNow();
                replay.Add(frame, at);
            }
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            try
            {
                // A completed probe returns to slow sampling on the same native
                // session. Only interrupted/failed work must release it here.
                if (failed || cancellationToken.IsCancellationRequested ||
                    !capture.IsGameForeground || !_pendingAnalysis.IsCompleted)
                    capture.Suspend();
                else
                    capture.SetBurst(false);
            }
            finally
            {
                if (_confirming) _lastBurstEnd = _time.GetTimestamp();
                _confirming = false;
                analyzer?.Dispose();
            }
        }
    }

    private async Task<RotationStartSighting?> ObserveRotationStartAsync(Bitmap frame, DateTimeOffset at,
        AutoStartDetection replay, CancellationToken cancellationToken)
    {
        var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = Task.Run(() => _rotationStart.Observe(frame, at, workerCancellation.Token), CancellationToken.None);
        try
        {
            var sighting = await worker.WaitAsync(_initialBannerTimeout, cancellationToken).ConfigureAwait(false);
            workerCancellation.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            return sighting;
        }
        catch
        {
            Task cancellation;
            try { cancellation = ObserveCancellationAsync(workerCancellation.CancelAsync()); }
            catch (ObjectDisposedException) { cancellation = Task.CompletedTask; }
            // Even a reader that ignores cancellation still owns its image and watcher.
            // Leave the caller promptly, but prevent another probe and defer disposal.
            if (!worker.IsCompleted || !cancellation.IsCompleted)
                _pendingAnalysis = DisposeAfterBannerAsync(Task.WhenAll(worker, cancellation),
                    replay.Detach(), workerCancellation);
            else workerCancellation.Dispose();
            capture.Suspend();
            throw;
        }
    }

    private static async Task DisposeAfterBannerAsync(Task pending, AutoStartDetection replay,
        CancellationTokenSource cancellation)
    {
        try { await pending.ConfigureAwait(false); } catch (Exception) { }
        finally { cancellation.Dispose(); replay.Dispose(); }
    }

    private static async Task ObserveCancellationAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); } catch (Exception) { }
    }

    private static async Task ReleaseCapturePriorityAfterAnalysisAsync(Task analysis, IDisposable priorityLease)
    {
        try { await analysis.ConfigureAwait(false); }
        catch (Exception) { /* The original probe already owns failure reporting. */ }
        finally { priorityLease.Dispose(); }
    }

    private static async Task DisposeAfterAnalysisAsync(Task pending, ILootFrameAnalyzer analyzer,
        AutoStartDetection replay, CancellationTokenSource cancellation)
    {
        try { await pending.ConfigureAwait(false); } catch (Exception) { }
        finally
        {
            cancellation.Dispose();
            try { analyzer.Dispose(); } finally { replay.Dispose(); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        capture.Dispose();
        visual.Reset();
        if (_pendingAnalysis.IsCompleted) _rotationStart.Dispose();
        else _ = DisposeWatcherAfterAnalysisAsync(_pendingAnalysis, _rotationStart);
    }

    private static async Task DisposeWatcherAfterAnalysisAsync(Task pending, RotationStartWatcher watcher)
    {
        try { await pending.ConfigureAwait(false); } catch (Exception) { }
        finally { watcher.Dispose(); }
    }
}

/// <summary>At most three recent full frames and 128 MiB; replay uses the live reconciler.</summary>
internal sealed class AutoStartDetection : IDisposable
{
    internal const long MaximumBytes = 128L * 1024 * 1024;
    private readonly List<(Bitmap Bitmap, CapturedFrameMetadata Metadata)> _frames = [];
    private long _bytes;
    public IReadOnlyList<(Bitmap Bitmap, CapturedFrameMetadata Metadata)> Frames => _frames;
    public DateTimeOffset? DetectedDropAt { get; set; }
    /// <summary>The spot identified by the confirmed trash arrival, independent of older replay rows.</summary>
    public string? SpotId { get; set; }
    /// <summary>Set when a rotation start banner preceded this drop: the session starts confirmed and from that banner.</summary>
    public RotationStartSighting? RotationStart { get; set; }

    public void Add(CapturedDesktopBitmap frame, DateTimeOffset at)
    {
        var bytes = (long)frame.Bitmap.Width * frame.Bitmap.Height * 4;
        if (bytes > MaximumBytes)
        {
            frame.Bitmap.Dispose();
            throw new InvalidOperationException("Das Spielfenster ist für den Autostart-Bildpuffer zu groß.");
        }
        while (_frames.Count >= 3 || _bytes + bytes > MaximumBytes)
        {
            var oldest = _frames[0].Bitmap;
            _bytes -= (long)oldest.Width * oldest.Height * 4;
            oldest.Dispose();
            _frames.RemoveAt(0);
        }
        _frames.Add((frame.Bitmap, new CapturedFrameMetadata(_frames.Count + 1, at, frame.IsHdr, frame.IsToneMapped)
        { CanObserveHud = true }));
        _bytes += bytes;
    }

    public IReadOnlyList<(Bitmap Bitmap, CapturedFrameMetadata Metadata)> TakeFrames()
    {
        var frames = _frames.ToArray();
        _frames.Clear();
        _bytes = 0;
        return frames;
    }

    public AutoStartDetection Detach()
    {
        var owned = new AutoStartDetection { DetectedDropAt = DetectedDropAt, SpotId = SpotId, RotationStart = RotationStart };
        owned._frames.AddRange(_frames);
        owned._bytes = _bytes;
        _frames.Clear();
        _bytes = 0;
        DetectedDropAt = null;
        SpotId = null;
        return owned;
    }

    public void Dispose()
    {
        foreach (var frame in _frames) frame.Bitmap.Dispose();
        _frames.Clear();
        _bytes = 0;
    }
}
