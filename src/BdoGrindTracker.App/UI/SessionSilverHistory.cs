using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.UI;

public sealed record SessionSilverSample(TimeSpan Elapsed, decimal SilverPerHour);

/// <summary>Retains observed session rates independently of any dashboard or overlay.</summary>
internal sealed class SessionSilverHistory
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(10);
    private readonly List<SessionSilverSample> _samples = [];
    private Guid? _sessionId;
    private bool _isDemo;
    private TimeSpan _lastElapsed;

    internal IReadOnlyList<SessionSilverSample> Update(TrackerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var live = new LiveSessionPresentation(state);
        var elapsed = live.Elapsed;
        if (_sessionId != state.SessionId || _isDemo != state.IsDemo)
            _samples.Clear();
        else if (elapsed < _lastElapsed)
            _samples.RemoveAll(sample => sample.Elapsed > elapsed);

        _sessionId = state.SessionId;
        _isDemo = state.IsDemo;
        _lastElapsed = elapsed;

        if (!state.HasSession && !state.IsRunning && !state.IsDemo)
        {
            _samples.Clear();
            return [];
        }

        if (live.SilverHourly is not { } hourly)
            return [];

        var current = new SessionSilverSample(elapsed, hourly);
        var bucket = elapsed.Ticks / SampleInterval.Ticks;
        if (_samples.Count > 0 && _samples[^1].Elapsed == elapsed)
            _samples[^1] = current;
        else if (bucket > 0 && (_samples.Count == 0 ||
                 bucket > _samples[^1].Elapsed.Ticks / SampleInterval.Ticks))
            _samples.Add(current);

        // Keep an up-to-date endpoint between samples. Only observed timestamps are
        // retained; reopening a view never backfills a fictional earlier curve.
        return Array.AsReadOnly(_samples.Count > 0 && _samples[^1].Elapsed == elapsed
            ? _samples.ToArray()
            : [.. _samples, current]);
    }
}
