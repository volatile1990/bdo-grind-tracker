using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Publishes the lowest estimate seen for each item during the last two seconds.
/// Short-lived overestimates disappear inside this window instead of reaching the
/// session. This does not constrain inference or clamp a late correction to a
/// wrong high-water mark. Arrival times remain immediate for the inactivity clock.
/// </summary>
internal sealed class LootProjectionBuffer
{
    internal static readonly TimeSpan ConfirmationDelay = TimeSpan.FromSeconds(2);
    private sealed record Sample(DateTimeOffset At, IReadOnlyDictionary<string, long> Totals, int Count);
    private readonly List<Sample> _samples = [];
    private LootTotalsProjection _published = Empty();
    private IReadOnlyDictionary<string, long> _publishedAmounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    private long? _rawRevision;
    private DateTimeOffset? _lastAt;

    public LootTotalsProjection Observe(LootTotalsProjection projection, DateTimeOffset capturedAt, bool flush = false)
    {
        ArgumentNullException.ThrowIfNull(projection);
        projection.Validate();
        // Match the session's item comparer before consuming time or revisions.
        if (projection.Totals.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != projection.Totals.Count)
            throw new ArgumentException("A projection contains duplicate item names.", nameof(projection));
        if (_rawRevision is { } revision && projection.Revision < revision ||
            _lastAt is { } lastAt && capturedAt < lastAt)
            return _published;

        var incoming = projection.Totals.Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var sample = new Sample(capturedAt, incoming, projection.ConfirmedDropCount);

        if (flush)
        {
            // A pause has no future image evidence. Preserve its exact current
            // estimate, and use it as the baseline if capture subsequently resumes.
            _samples.Clear();
            _samples.Add(sample);
        }
        else
        {
            if (_samples.Count == 0) _samples.Add(new(capturedAt,
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase), 0));
            if (_samples[^1].Count != sample.Count || !SameTotals(_samples[^1].Totals, incoming))
            {
                if (_samples.Count > 1 && _samples[^1].At == capturedAt) _samples[^1] = sample;
                else _samples.Add(sample);
            }
            // Keep the state active at the left edge, including across capture
            // gaps. An unchanged raw revision still advances this time window.
            while (_samples.Count > 1 && capturedAt - _samples[1].At >= ConfirmationDelay)
                _samples.RemoveAt(0);
        }

        var totals = new Dictionary<string, long>(incoming, StringComparer.OrdinalIgnoreCase);
        var count = projection.ConfirmedDropCount;
        foreach (var historical in _samples)
        {
            foreach (var name in totals.Keys.ToArray())
            {
                var amount = Math.Min(totals[name], historical.Totals.GetValueOrDefault(name));
                if (amount == 0) totals.Remove(name); else totals[name] = amount;
            }
            count = Math.Min(count, historical.Count);
        }
        var latest = _published.LatestArrivalAt;
        if (projection.LatestArrivalAt is { } arrival && (latest is null || arrival > latest)) latest = arrival;
        var changed = count != _published.ConfirmedDropCount || latest != _published.LatestArrivalAt ||
            !SameTotals(totals, _publishedAmounts);
        if (changed)
        {
            _published = new(checked(_published.Revision + 1), totals, count, latest);
            _publishedAmounts = totals;
        }
        _rawRevision = projection.Revision;
        _lastAt = capturedAt;
        return _published;
    }

    public void Reset()
    {
        _samples.Clear();
        _published = Empty();
        _publishedAmounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        _rawRevision = null;
        _lastAt = null;
    }

    private static LootTotalsProjection Empty() => new(0, new Dictionary<string, long>(), 0, null);

    private static bool SameTotals(IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
