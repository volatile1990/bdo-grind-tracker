using System.Collections.ObjectModel;

namespace BdoGrindTracker.App.Integrations.Garmoth;

internal sealed record GarmothUploadInterval(
    Guid Id,
    TimeSpan ActiveDuration,
    IReadOnlyDictionary<string, long> Totals,
    DateTimeOffset StartedAt);

/// <summary>
/// Keeps immutable hourly cutoffs independently of the upload switch. Quantities
/// are cumulative local counters; only positive differences from already sent
/// quantities can leave the ledger. All methods may run concurrently with capture.
/// </summary>
internal sealed class GarmothUploadIntervals
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly IReadOnlyDictionary<string, long> EmptyTotals =
        new ReadOnlyDictionary<string, long>(new Dictionary<string, long>());

    private readonly object _gate = new();
    private readonly Queue<HourCutoff> _hours = new();
    private readonly Dictionary<string, long> _transmitted = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, long> _observedTotals = EmptyTotals;
    private TimeSpan _observedDuration;
    private TimeSpan _nextHour = Hour;
    private TimeSpan _consumedDuration;
    private DateTimeOffset? _windowStartedAt;
    private PreparedUpload? _prepared;
    private bool _inFlight;
    private bool _blocked;
    private bool _automaticSuspended;

    public bool IsBlocked { get { lock (_gate) return _blocked; } }
    public bool AutomaticSuspended { get { lock (_gate) return _automaticSuspended; } }
    public Guid? PreparedIntervalId { get { lock (_gate) return _prepared?.Interval.Id; } }

    public void Observe(TimeSpan confirmedActiveDuration,
        IReadOnlyDictionary<string, long> totals, DateTimeOffset observedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(confirmedActiveDuration, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(totals);
        lock (_gate)
        {
            // Separate monotonic clock/idle samples can jitter by a few ticks.
            // Keep time monotonic without discarding a newly reconciled quantity.
            if (confirmedActiveDuration < _observedDuration)
                confirmedActiveDuration = _observedDuration;
            var snapshot = CopyTotals(totals);
            _windowStartedAt ??= observedAt - confirmedActiveDuration;
            while (_nextHour <= confirmedActiveDuration)
            {
                // A frame after the boundary belongs to the next hour. At the
                // boundary itself its counters are part of the completed hour.
                var cutoffTotals = _nextHour == confirmedActiveDuration ? snapshot : _observedTotals;
                _hours.Enqueue(new(Guid.NewGuid(), _nextHour, cutoffTotals, _windowStartedAt.Value));
                _windowStartedAt = observedAt - (confirmedActiveDuration - _nextHour);
                _nextHour += Hour;
            }
            _observedDuration = confirmedActiveDuration;
            _observedTotals = snapshot;
        }
    }

    public GarmothUploadInterval? PrepareAutomatic()
    {
        lock (_gate)
        {
            if (_blocked || _automaticSuspended || _inFlight) return null;
            if (_prepared is { IsAutomatic: true }) return Reserve(_prepared);
            // A rejected manual remainder may have any duration. It must not
            // become an automatic request before a full hour is available.
            _prepared = null;

            while (_hours.TryPeek(out var hour))
            {
                if (hour.EndDuration <= _consumedDuration)
                {
                    _hours.Dequeue();
                    continue;
                }

                var delta = Delta(hour.Totals);
                if (delta.Count == 0)
                {
                    // Garmoth rejects empty sessions. Consume only this empty
                    // time window; never lower quantity watermarks after a correction.
                    _consumedDuration = hour.EndDuration;
                    _hours.Dequeue();
                    continue;
                }

                var duration = hour.EndDuration - _consumedDuration;
                return Reserve(new(new(hour.Id, duration, delta, hour.StartedAt), hour.EndDuration, IsAutomatic: true));
            }
            return null;
        }
    }

    public GarmothUploadInterval? PrepareManual(TimeSpan activeDuration,
        IReadOnlyDictionary<string, long> totals, DateTimeOffset startedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(activeDuration, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(totals);
        lock (_gate)
        {
            if (_blocked || _inFlight) return null;
            var remaining = activeDuration - _consumedDuration;
            if (remaining < TimeSpan.FromMinutes(1)) return null;
            var delta = Delta(CopyTotals(totals));
            if (delta.Count == 0) return null;

            // A definite rejection cannot have committed. An explicit manual
            // upload can therefore replace it with the entire unsent remainder.
            return Reserve(new(new(Guid.NewGuid(), remaining, delta,
                startedAt + _consumedDuration), activeDuration, IsAutomatic: false));
        }
    }

    public GarmothUploadInterval? PreviewManual(TimeSpan activeDuration,
        IReadOnlyDictionary<string, long> totals, DateTimeOffset startedAt)
    {
        lock (_gate)
        {
            if (_blocked || _inFlight) return null;
            return new(Guid.NewGuid(), activeDuration > _consumedDuration ? activeDuration - _consumedDuration : TimeSpan.Zero,
                Delta(CopyTotals(totals)), startedAt + _consumedDuration);
        }
    }

    public void BlockFurtherUploads()
    {
        lock (_gate) { _blocked = true; _automaticSuspended = true; }
    }

    public void Complete(GarmothUploadInterval interval, GarmothUploadResult result)
    {
        ArgumentNullException.ThrowIfNull(interval);
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            // Reset/new-session callbacks and duplicate completion notifications
            // cannot consume or block the current session.
            if (!_inFlight || _prepared?.Interval.Id != interval.Id) return;
            _inFlight = false;
            if (result.Status == GarmothUploadStatus.Succeeded)
            {
                // Always use our frozen request, never a caller's replacement
                // record or the live counters collected while HTTP was pending.
                foreach (var (name, quantity) in _prepared.Interval.Totals)
                    _transmitted[name] = checked(_transmitted.GetValueOrDefault(name) + quantity);
                _consumedDuration = _prepared.EndDuration;
                _prepared = null;
                while (_hours.TryPeek(out var hour) && hour.EndDuration <= _consumedDuration)
                    _hours.Dequeue();
                return;
            }

            _automaticSuspended = true;
            if (result.Status is GarmothUploadStatus.OutcomeUnknown or GarmothUploadStatus.AlreadySubmitted)
                _blocked = true;
        }
    }

    public void SuspendAutomatic()
    {
        lock (_gate) _automaticSuspended = true;
    }

    public void ResumeAutomatic()
    {
        lock (_gate)
        {
            if (!_blocked) _automaticSuspended = false;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _hours.Clear();
            _transmitted.Clear();
            _observedTotals = EmptyTotals;
            _observedDuration = TimeSpan.Zero;
            _nextHour = Hour;
            _consumedDuration = TimeSpan.Zero;
            _windowStartedAt = null;
            _prepared = null;
            _inFlight = false;
            _blocked = false;
            _automaticSuspended = false;
        }
    }

    private GarmothUploadInterval Reserve(PreparedUpload prepared)
    {
        _prepared = prepared;
        _inFlight = true;
        return prepared.Interval;
    }

    private IReadOnlyDictionary<string, long> Delta(IReadOnlyDictionary<string, long> totals)
    {
        var delta = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, total) in totals)
        {
            var sent = _transmitted.GetValueOrDefault(name);
            if (total > sent) delta[name] = total - sent;
        }
        return new ReadOnlyDictionary<string, long>(delta);
    }

    private static IReadOnlyDictionary<string, long> CopyTotals(IReadOnlyDictionary<string, long> totals)
    {
        var copy = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, quantity) in totals)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (quantity > 0) copy[name] = quantity;
        }
        return new ReadOnlyDictionary<string, long>(copy);
    }

    private sealed record HourCutoff(Guid Id, TimeSpan EndDuration,
        IReadOnlyDictionary<string, long> Totals, DateTimeOffset StartedAt);
    private sealed record PreparedUpload(GarmothUploadInterval Interval, TimeSpan EndDuration, bool IsAutomatic);
}
