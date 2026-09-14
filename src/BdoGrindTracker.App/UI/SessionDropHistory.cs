using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.UI;

public sealed record SessionDropSample(TimeSpan Elapsed, string ItemName, long Quantity);

/// <summary>Retains observed loot increases, independently of overlay visibility.</summary>
internal sealed class SessionDropHistory
{
    private readonly List<SessionDropSample> _drops = [];
    private Dictionary<string, long> _totals = new(StringComparer.Ordinal);
    private Guid? _sessionId;
    private bool _isDemo;
    private int _confirmed;

    internal IReadOnlyList<SessionDropSample> Update(TrackerState state)
    {
        var elapsed = new LiveSessionPresentation(state).Elapsed;
        var reset = _sessionId != state.SessionId || _isDemo != state.IsDemo;
        if (reset) _drops.Clear();
        _drops.RemoveAll(drop => drop.Elapsed > elapsed);
        // Restored aggregate totals have no known drop times. Establish a baseline
        // instead of inventing events; manual edits do not increase the event count.
        if (!reset && state.Loot.ConfirmedEventCount > _confirmed)
            foreach (var (name, quantity) in state.Loot.Totals)
            {
                var delta = quantity - _totals.GetValueOrDefault(name);
                if (delta > 0) _drops.Add(new(elapsed, name, delta));
            }
        _sessionId = state.SessionId;
        _isDemo = state.IsDemo;
        _confirmed = state.Loot.ConfirmedEventCount;
        _totals = new(state.Loot.Totals, StringComparer.Ordinal);
        return Array.AsReadOnly(_drops.ToArray());
    }
}
