using System.Diagnostics;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Monotonic gate for expensive transient UI work such as cloning and painting
/// preview frames. Aggregate data is intentionally handled outside this gate.
/// </summary>
internal sealed class TransientUiRefreshGate
{
    private readonly Func<long> _timestampProvider;
    private readonly long _minimumIntervalTicks;
    private readonly object _sync = new();
    private long? _lastRefreshTimestamp;

    public TransientUiRefreshGate(TimeSpan minimumInterval)
        : this(minimumInterval, Stopwatch.GetTimestamp, Stopwatch.Frequency)
    {
    }

    internal TransientUiRefreshGate(
        TimeSpan minimumInterval,
        Func<long> timestampProvider,
        long timestampFrequency)
    {
        if (minimumInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumInterval),
                "Das Aktualisierungsintervall muss positiv sein.");
        }

        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        _timestampProvider = timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
        _minimumIntervalTicks = Math.Max(
            1,
            (long)Math.Ceiling(minimumInterval.TotalSeconds * timestampFrequency));
    }

    public bool TryAcquire()
    {
        var now = _timestampProvider();
        lock (_sync)
        {
            if (_lastRefreshTimestamp is { } previous &&
                now >= previous &&
                now - previous < _minimumIntervalTicks)
            {
                return false;
            }

            _lastRefreshTimestamp = now;
            return true;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _lastRefreshTimestamp = null;
        }
    }
}
