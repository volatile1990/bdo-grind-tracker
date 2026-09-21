using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Analysis;

internal sealed record BuffDetectionSnapshot(DateTimeOffset? ObservedAt, IReadOnlyList<BuffObservation> Observations)
{
    internal bool IsKnown => ObservedAt is not null;
    internal static BuffDetectionSnapshot Unknown { get; } = new(null, []);
}

/// <summary>One bounded worker reads passive frames; optional recognition never blocks capture.</summary>
internal sealed class BuffMonitor : IDisposable
{
    internal static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan AnalysisTimeout = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private readonly IBuffFrameReader _reader;
    private readonly TimeSpan _samplingInterval;
    private Task _analysis = Task.CompletedTask;
    private CancellationTokenSource? _cancellation;
    private DateTimeOffset? _lastScheduledAt;
    private BuffDetectionSnapshot _state = BuffDetectionSnapshot.Unknown;
    private string _lastDiagnostic = "Buff-Erkennung wartet auf ein kalibriertes Bild.";
    private long _epoch, _generation;
    private bool _running, _disposed, _readerDisposed;

    internal BuffMonitor(IBuffFrameReader reader, TimeSpan? samplingInterval = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _samplingInterval = samplingInterval ?? SamplingInterval;
        if (_samplingInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(samplingInterval));
    }

    internal Task CurrentAnalysis { get { lock (_sync) return _analysis; } }
    internal string LastDiagnostic { get { lock (_sync) return _lastDiagnostic; } }

    internal void Observe(Bitmap frame, DateTimeOffset capturedAt)
    {
        lock (_sync)
        {
            if (_disposed || _running || capturedAt <= DateTimeOffset.UnixEpoch ||
                _lastScheduledAt is { } previous && capturedAt - previous < _samplingInterval) return;
            _lastScheduledAt = capturedAt;
            Bitmap? copy = null;
            CancellationTokenSource? cancellation = null;
            try
            {
                copy = (Bitmap)frame.Clone();
                cancellation = new CancellationTokenSource(AnalysisTimeout);
                _cancellation = cancellation;
                _running = true;
                var epoch = _epoch;
                var ownedCopy = copy;
                var ownedCancellation = cancellation;
                _analysis = Task.Run(() => Read(ownedCopy, capturedAt, epoch, ownedCancellation));
            }
            catch (Exception)
            {
                copy?.Dispose(); cancellation?.Dispose();
                _cancellation = null; _running = false;
                Clear("Bild für die Buff-Erkennung konnte nicht vorbereitet werden. Nächste Aufnahme abwarten.");
            }
        }
    }

    internal BuffDetectionSnapshot Snapshot(DateTimeOffset now) => Snapshot(now, out _);

    internal BuffDetectionSnapshot Snapshot(DateTimeOffset now, out long generation)
    {
        lock (_sync)
        {
            if (_state.ObservedAt is { } at && (now < at || now - at >= MaximumObservationAge))
                Clear("Buff-Erkennung ist veraltet. Auf eine neue Aufnahme der sichtbaren Buffleiste warten.");
            generation = _generation;
            return _disposed ? BuffDetectionSnapshot.Unknown : _state;
        }
    }

    internal void Reset()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (_disposed) return;
            _epoch++; Clear("Buff-Erkennung zurückgesetzt. Auf die nächste Aufnahme warten."); _lastScheduledAt = null;
            cancellation = _cancellation;
        }
        Cancel(cancellation);
    }

    private void Read(Bitmap frame, DateTimeOffset capturedAt, long epoch, CancellationTokenSource cancellation)
    {
        try
        {
            var reading = _reader.Read(frame, cancellation.Token);
            lock (_sync)
            {
                if (_disposed || epoch != _epoch) return;
                if (cancellation.IsCancellationRequested)
                {
                    Clear("Zeitlimit der Buff-Erkennung erreicht. Vorlagen und Buffleistenbereich verkleinern.");
                    return;
                }
                if (reading is null || reading.Observations.Count == 0)
                {
                    Clear(_reader.LastDiagnostic ?? "Buff-Erkennung unbekannt. Buffleiste, Vorlagen und Restzeiten prüfen.");
                    return;
                }
                _state = new(capturedAt, reading.Observations.ToArray());
                _lastDiagnostic = _reader.LastDiagnostic ?? $"{reading.Observations.Count} Buff(s) mit Restzeit erkannt.";
            }
        }
        catch (Exception)
        {
            lock (_sync) if (!_disposed && epoch == _epoch)
                Clear(cancellation.IsCancellationRequested
                    ? "Zeitlimit der Buff-Erkennung erreicht. Vorlagen und Buffleistenbereich verkleinern."
                    : _reader.LastDiagnostic ?? "Buff-Erkennung fehlgeschlagen. Kalibrierung im Screenshot-Test prüfen.");
        }
        finally
        {
            frame.Dispose();
            var disposeReader = false;
            lock (_sync)
            {
                if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
                _running = false;
                if (_disposed && !_readerDisposed) { _readerDisposed = true; disposeReader = true; }
            }
            cancellation.Dispose();
            if (disposeReader) DisposeReader();
        }
    }

    private void Clear(string reason)
    {
        _state = BuffDetectionSnapshot.Unknown;
        _lastDiagnostic = reason;
        _generation++;
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        var disposeReader = false;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _epoch++; Clear("Buff-Erkennung beendet.");
            cancellation = _cancellation;
            if (!_running && !_readerDisposed) { _readerDisposed = true; disposeReader = true; }
        }
        Cancel(cancellation);
        if (disposeReader) DisposeReader();
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch (Exception) { /* Optional analysis must not interrupt tracking. */ }
    }

    private void DisposeReader()
    {
        try { _reader.Dispose(); }
        catch (Exception) { /* Optional analysis must not interrupt shutdown. */ }
    }
}
