namespace BdoGrindTracker.App.Integrations.Garmoth;

internal sealed record GarmothHourCutoffState(Guid Id, TimeSpan EndDuration,
    Dictionary<string, long> Totals, DateTimeOffset StartedAt);

internal sealed record GarmothUploadState
{
    public TimeSpan ObservedDuration { get; init; }
    public TimeSpan ConsumedDuration { get; init; }
    public TimeSpan NextHour { get; init; } = TimeSpan.FromHours(1);
    public DateTimeOffset? WindowStartedAt { get; init; }
    public Dictionary<string, long> ObservedTotals { get; init; } = new();
    public Dictionary<string, long> TransmittedTotals { get; init; } = new();
    public GarmothHourCutoffState[] Hours { get; init; } = [];
    public bool IsBlocked { get; init; }
    public bool AutomaticSuspended { get; init; }
}

internal sealed partial class GarmothUploadIntervals
{
    public GarmothUploadState ExportState()
    {
        lock (_gate)
        {
            return new()
            {
                ObservedDuration = _observedDuration,
                ConsumedDuration = _consumedDuration,
                NextHour = _nextHour,
                WindowStartedAt = _windowStartedAt,
                ObservedTotals = new(_observedTotals, StringComparer.OrdinalIgnoreCase),
                TransmittedTotals = new(_transmitted, StringComparer.OrdinalIgnoreCase),
                Hours = _hours.Select(hour => new GarmothHourCutoffState(hour.Id, hour.EndDuration,
                    new(hour.Totals, StringComparer.OrdinalIgnoreCase), hour.StartedAt)).ToArray(),
                // A checkpoint may race an HTTP request. The journal supplies an
                // independent guard too; never resume an uncertain request here.
                IsBlocked = _blocked || _inFlight,
                AutomaticSuspended = _automaticSuspended || _inFlight,
            };
        }
    }

    public void RestoreState(GarmothUploadState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ObservedDuration < TimeSpan.Zero || state.ConsumedDuration < TimeSpan.Zero ||
            state.NextHour <= state.ObservedDuration || state.NextHour.Ticks % Hour.Ticks != 0 ||
            state.NextHour <= TimeSpan.Zero || state.Hours is null || state.Hours.Length > 10000)
            throw new ArgumentException("Invalid saved upload durations.", nameof(state));
        var observed = ValidatedTotals(state.ObservedTotals);
        var transmitted = ValidatedTotals(state.TransmittedTotals);
        var hours = new List<HourCutoff>();
        var ids = new HashSet<Guid>();
        var previousEnd = TimeSpan.Zero;
        foreach (var hour in state.Hours)
        {
            if (hour is null || hour.Id == Guid.Empty || !ids.Add(hour.Id) ||
                hour.EndDuration <= previousEnd || hour.EndDuration > state.ObservedDuration ||
                hour.EndDuration >= state.NextHour || hour.EndDuration.Ticks % Hour.Ticks != 0)
                throw new ArgumentException("Invalid saved hourly cutoff.", nameof(state));
            hours.Add(new(hour.Id, hour.EndDuration, ValidatedTotals(hour.Totals), hour.StartedAt));
            previousEnd = hour.EndDuration;
        }
        if (state.WindowStartedAt is null && (state.ObservedDuration > TimeSpan.Zero || observed.Count > 0))
            throw new ArgumentException("Saved upload window has no start.", nameof(state));
        lock (_gate)
        {
            if (_inFlight) throw new InvalidOperationException("Cannot restore while an upload is in flight.");
            _hours.Clear();
            foreach (var hour in hours) _hours.Enqueue(hour);
            _transmitted.Clear();
            foreach (var (name, quantity) in transmitted) _transmitted.Add(name, quantity);
            _observedTotals = observed;
            _observedDuration = state.ObservedDuration;
            _consumedDuration = state.ConsumedDuration;
            _nextHour = state.NextHour;
            _windowStartedAt = state.WindowStartedAt;
            _prepared = null;
            _inFlight = false;
            _blocked = state.IsBlocked;
            _automaticSuspended = state.AutomaticSuspended || state.IsBlocked;
        }
    }

    private static IReadOnlyDictionary<string, long> ValidatedTotals(Dictionary<string, long>? source)
    {
        if (source is null || source.Count > 10000) throw new ArgumentException("Invalid saved upload totals.");
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, quantity) in source)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 1024 || quantity < 0 || !result.TryAdd(name, quantity))
                throw new ArgumentException("Invalid saved upload item.");
        }
        return CopyTotals(result);
    }
}
