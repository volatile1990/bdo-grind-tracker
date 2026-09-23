using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.UI;

public sealed record SessionDropSample(TimeSpan Elapsed, string ItemName, long Quantity);

/// <summary>Retains observed loot increases, independently of overlay visibility.</summary>
internal sealed class SessionDropHistory
{
    private readonly List<SessionDropSample> _drops = [];
    private Dictionary<string, long> _totals = new(StringComparer.OrdinalIgnoreCase);
    private Guid? _sessionId;
    private bool _isDemo;
    private int _confirmed;
    private TimeSpan _lastElapsed;
    private IReadOnlyList<SessionDropSample>? _snapshot;

    internal IReadOnlyList<SessionDropSample> Update(TrackerState state, TimeSpan? observedElapsed = null,
        bool manualCorrection = false)
    {
        var elapsed = new LiveSessionPresentation(state).Elapsed;
        var reset = _sessionId != state.SessionId || _isDemo != state.IsDemo;
        if (reset) { _drops.Clear(); _snapshot = null; }
        var dropElapsed = observedElapsed is { } observed
            ? TimeSpan.FromTicks(Math.Clamp(observed.Ticks, 0, elapsed.Ticks)) : elapsed;
        // Automatic estimates can retract a previously confirmed drop. Reconcile
        // the recorded quantities too, or a later recovery adds another marker
        // while the inventory correctly contains only one item. Aggregate totals
        // cannot identify which drop was revised, so undo the newest samples first.
        // Explicit manual edits only change the baseline; their observed times stay.
        if (!reset && !manualCorrection)
            foreach (var (name, quantity) in _totals)
            {
                var removed = quantity - state.Loot.Totals.GetValueOrDefault(name);
                for (var index = _drops.Count - 1; removed > 0 && index >= 0; index--)
                {
                    var drop = _drops[index];
                    if (!string.Equals(drop.ItemName, name, StringComparison.OrdinalIgnoreCase)) continue;
                    var reduction = Math.Min(removed, drop.Quantity);
                    if (reduction == drop.Quantity) _drops.RemoveAt(index);
                    else _drops[index] = drop with { Quantity = drop.Quantity - reduction };
                    removed -= reduction;
                    _snapshot = null;
                }
            }
        if (!reset && elapsed < _lastElapsed && _drops.RemoveAll(drop => drop.Elapsed > elapsed) > 0)
            _snapshot = null;
        // Aggregate totals alone cannot prove drop times. New confirmed events
        // get a sample; correction-only increases amend the latest known sample
        // without inventing another drop time. This also keeps a later downward
        // revision from removing a drop whose upward revision was never recorded.
        if (!reset && !manualCorrection)
            foreach (var (name, quantity) in state.Loot.Totals)
            {
                var delta = quantity - _totals.GetValueOrDefault(name);
                if (delta <= 0) continue;
                if (state.Loot.ConfirmedEventCount > _confirmed)
                {
                    _drops.Add(new(dropElapsed, name, delta));
                    _snapshot = null;
                }
                else
                {
                    var index = _drops.FindLastIndex(drop => string.Equals(drop.ItemName, name, StringComparison.OrdinalIgnoreCase));
                    if (index < 0) continue;
                    _drops[index] = _drops[index] with { Quantity = checked(_drops[index].Quantity + delta) };
                    _snapshot = null;
                }
            }
        _sessionId = state.SessionId;
        _isDemo = state.IsDemo;
        _confirmed = state.Loot.ConfirmedEventCount;
        _lastElapsed = elapsed;
        if (_totals.Count != state.Loot.Totals.Count ||
            state.Loot.Totals.Any(pair => !_totals.TryGetValue(pair.Key, out var quantity) || quantity != pair.Value))
            _totals = new(state.Loot.Totals, StringComparer.OrdinalIgnoreCase);
        return _snapshot ??= Array.AsReadOnly(_drops.ToArray());
    }

    internal void Restore(Guid sessionId, LootSessionSnapshot loot, TimeSpan duration,
        IReadOnlyList<SessionDropSample>? drops)
    {
        _drops.Clear();
        _drops.AddRange(Normalize(drops, duration, loot.Totals) ?? []);
        _sessionId = sessionId;
        _isDemo = false;
        _confirmed = loot.ConfirmedEventCount;
        _totals = new(loot.Totals, StringComparer.OrdinalIgnoreCase);
        _lastElapsed = duration;
        _snapshot = null;
    }

    internal static IReadOnlyList<SessionDropSample>? Normalize(IReadOnlyList<SessionDropSample>? drops,
        TimeSpan duration, IReadOnlyDictionary<string, long> totals)
    {
        // null distinguishes older files without timing information from a known
        // empty timeline. Optional invalid markers must not discard valid loot.
        if (drops is null) return null;
        var names = totals.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Array.AsReadOnly(drops.Where(drop => drop is not null &&
            drop.Elapsed >= TimeSpan.Zero && drop.Elapsed <= duration && drop.Quantity > 0 &&
            !string.IsNullOrWhiteSpace(drop.ItemName) && drop.ItemName.Length <= 4096 &&
            names.Contains(drop.ItemName)).OrderBy(drop => drop.Elapsed).ToArray());
    }
}
