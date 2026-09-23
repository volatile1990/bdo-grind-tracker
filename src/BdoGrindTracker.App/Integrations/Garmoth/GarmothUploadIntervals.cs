using System.Collections.ObjectModel;

namespace BdoGrindTracker.App.Integrations.Garmoth;

internal sealed record GarmothUploadInterval(
    Guid Id,
    TimeSpan ActiveDuration,
    IReadOnlyDictionary<string, long> Totals,
    DateTimeOffset StartedAt);

/// <summary>
/// Tracks cumulative local counters and quantities already sent to Garmoth.
/// Legacy hourly cutoffs remain readable for persisted sessions; live sessions
/// use their complete totals. All methods may run concurrently with capture.
/// </summary>
internal sealed partial class GarmothUploadIntervals
{
    public const string CorrectionReviewMessage = "Lootmengen haben sich nach Stundenabschluss geändert. Bitte die Vorschau für den verbleibenden Grind prüfen und manuell senden.";
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
    private bool _correctionReviewRequired;
    private bool _preparedCorrected;

    public bool IsBlocked { get { lock (_gate) return _blocked; } }
    public bool HasTransmittedLoot { get { lock (_gate) return _transmitted.Count > 0; } }
    public bool AutomaticSuspended { get { lock (_gate) return _automaticSuspended; } }
    public bool CorrectionReviewRequired { get { lock (_gate) return _correctionReviewRequired; } }
    public Guid? PreparedIntervalId { get { lock (_gate) return _prepared?.Interval.Id; } }

    public void ObserveSessionTotals(TimeSpan confirmedActiveDuration,
        IReadOnlyDictionary<string, long> totals, DateTimeOffset observedAt,
        bool isCorrection = false, bool requiresReview = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(confirmedActiveDuration, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(totals);
        lock (_gate)
        {
            if (confirmedActiveDuration < _observedDuration)
                confirmedActiveDuration = _observedDuration;
            var snapshot = CopyTotals(totals);
            var corrected = _observedTotals.Keys.Concat(snapshot.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .Any(name => snapshot.GetValueOrDefault(name) != _observedTotals.GetValueOrDefault(name) &&
                    (isCorrection || snapshot.GetValueOrDefault(name) < _observedTotals.GetValueOrDefault(name)));
            _preparedCorrected |= _inFlight && (corrected || requiresReview);

            // An unsent session no longer needs corrections allocated among
            // hours. Keep guards for requests that may already have committed.
            _hours.Clear();
            if (_correctionReviewRequired && !_blocked && !_inFlight &&
                _prepared is null && _transmitted.Count == 0)
            {
                _correctionReviewRequired = false;
                _automaticSuspended = false;
            }
            _windowStartedAt ??= observedAt - confirmedActiveDuration;
            _observedDuration = confirmedActiveDuration;
            _observedTotals = snapshot;
            // Retain the persisted schema so older session checkpoints remain
            // readable without manufacturing any new hourly upload windows.
            _nextHour = TimeSpan.FromTicks(checked((confirmedActiveDuration.Ticks / Hour.Ticks + 1) * Hour.Ticks));
        }
    }

    public void Observe(TimeSpan confirmedActiveDuration,
        IReadOnlyDictionary<string, long> totals, DateTimeOffset observedAt,
        bool isCorrection = false, bool includesNewDrops = false, bool requiresReview = false)
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
            var corrections = ReconcileCorrections(snapshot, isCorrection, includesNewDrops);
            if (corrections.Changed)
            {
                _hours.Clear();
                foreach (var hour in corrections.Hours) _hours.Enqueue(hour);
            }
            _correctionReviewRequired |= corrections.ReviewRequired;
            if (requiresReview && (_nextHour <= confirmedActiveDuration ||
                _hours.Any(hour => hour.EndDuration > _consumedDuration &&
                    (!_inFlight || hour.Id != _prepared?.Interval.Id))))
                _correctionReviewRequired = true;
            if (corrections.HasCorrection && includesNewDrops && _nextHour < confirmedActiveDuration)
                _correctionReviewRequired = true;
            _automaticSuspended |= _correctionReviewRequired;
            _preparedCorrected |= _inFlight && (corrections.HasCorrection || requiresReview);
            var priorTotals = _observedTotals;
            if (corrections.HasCorrection)
            {
                var revisedPrior = new Dictionary<string, long>(_observedTotals, StringComparer.OrdinalIgnoreCase);
                foreach (var name in _observedTotals.Keys.Concat(snapshot.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (isCorrection || snapshot.GetValueOrDefault(name) < _observedTotals.GetValueOrDefault(name))
                        revisedPrior[name] = snapshot.GetValueOrDefault(name);
                priorTotals = CopyTotals(revisedPrior);
            }
            _windowStartedAt ??= observedAt - confirmedActiveDuration;
            while (_nextHour <= confirmedActiveDuration)
            {
                // A frame after the boundary belongs to the next hour. At the
                // boundary itself its counters are part of the completed hour.
                var cutoffTotals = _nextHour == confirmedActiveDuration ? snapshot : priorTotals;
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
                if (!_prepared.IsAutomatic && !_preparedCorrected) _correctionReviewRequired = false;
                _prepared = null;
                _preparedCorrected = false;
                while (_hours.TryPeek(out var hour) && hour.EndDuration <= _consumedDuration)
                    _hours.Dequeue();
                return;
            }

            _automaticSuspended = true;
            // A rejected request is safe to replace, but retrying its frozen
            // quantities after a correction would send a known outdated value.
            _correctionReviewRequired |= _preparedCorrected;
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
            if (!_blocked && !_correctionReviewRequired) _automaticSuspended = false;
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
            _correctionReviewRequired = false;
            _preparedCorrected = false;
        }
    }

    private GarmothUploadInterval Reserve(PreparedUpload prepared)
    {
        if (_prepared?.Interval.Id != prepared.Interval.Id) _preparedCorrected = false;
        _prepared = prepared;
        _inFlight = true;
        return prepared.Interval;
    }

    private (HourCutoff[] Hours, bool Changed, bool ReviewRequired, bool HasCorrection) ReconcileCorrections(
        IReadOnlyDictionary<string, long> snapshot, bool isCorrection, bool includesNewDrops = false)
    {
        var correctedItems = _observedTotals.Keys.Concat(snapshot.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => snapshot.GetValueOrDefault(name) != _observedTotals.GetValueOrDefault(name) &&
                (isCorrection || snapshot.GetValueOrDefault(name) < _observedTotals.GetValueOrDefault(name)))
            .ToArray();
        var hours = _hours.ToArray();
        if (correctedItems.Length == 0) return (hours, false, false, false);
        var pending = hours.Where(hour => hour.EndDuration > _consumedDuration &&
            (!_inFlight || hour.Id != _prepared?.Interval.Id)).ToArray();
        if (pending.Length == 0) return (hours, false, false, true);

        // With one unsent hour and no loot for these items after its cutoff,
        // all affected quantities belong to that hour. Across several windows
        // (or an already dispatched request) there is no reliable allocation.
        if (includesNewDrops || pending.Length != 1 || _prepared is not null || _consumedDuration != TimeSpan.Zero ||
            _transmitted.Count != 0 || correctedItems.Any(name =>
                pending[0].Totals.GetValueOrDefault(name) != _observedTotals.GetValueOrDefault(name)))
            return (hours, false, true, true);

        var corrected = new Dictionary<string, long>(pending[0].Totals, StringComparer.OrdinalIgnoreCase);
        foreach (var name in correctedItems)
        {
            var quantity = snapshot.GetValueOrDefault(name);
            if (quantity > 0) corrected[name] = quantity;
            else corrected.Remove(name);
        }
        return (hours.Select(hour => hour.Id == pending[0].Id
            ? hour with { Totals = CopyTotals(corrected) } : hour).ToArray(), true, false, true);
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
