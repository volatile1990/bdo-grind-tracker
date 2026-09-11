using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

/// <summary>
/// Normal and rare maintain separate accounts. A positive normal total takes
/// priority over its rare counterpart, as in the verified Garmoth model.
/// Summation differences are audit records, not newly arrived physical drops.
/// </summary>
internal sealed class LifetimeLootProjectionComposer
{
    private Dictionary<string, long> _rare = new(StringComparer.Ordinal);
    private Dictionary<string, List<int>> _rareLots = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, long> _previous = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>());
    private int _dropCount;
    private DateTimeOffset? _latestArrival;
    private long _revision;

    public (LootTotalsProjection Projection, IReadOnlyList<TrackedLootEvent> Events) Combine(
        LifetimeSnapshot normal, IReadOnlyList<CompanionRareCountDelta> rareChanges, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(normal);
        ArgumentNullException.ThrowIfNull(rareChanges);
        var rare = new Dictionary<string, long>(_rare, StringComparer.Ordinal);
        var rareLots = new Dictionary<string, List<int>>(_rareLots, StringComparer.Ordinal);
        var latest = Max(_latestArrival, normal.LatestArrivalAt);
        foreach (var change in rareChanges)
        {
            ArgumentNullException.ThrowIfNull(change);
            ArgumentException.ThrowIfNullOrWhiteSpace(change.Name);
            var amount = checked(rare.GetValueOrDefault(change.Name) + change.Count);
            if (amount < 0) throw new InvalidOperationException("Rare correction exceeds its independent balance.");
            if (change.Count == 0) continue;
            var lots = rareLots.TryGetValue(change.Name, out var previousLots) ? new List<int>(previousLots) : [];
            if (change.Count > 0) lots.Add(change.Count);
            else
            {
                long remaining = -(long)change.Count;
                while (remaining > 0)
                {
                    var taken = (int)Math.Min(remaining, lots[^1]);
                    lots[^1] -= taken;
                    remaining -= taken;
                    if (lots[^1] == 0) lots.RemoveAt(lots.Count - 1);
                }
            }
            if (lots.Count == 0) rareLots.Remove(change.Name); else rareLots[change.Name] = lots;
            if (amount == 0) rare.Remove(change.Name); else rare[change.Name] = amount;
            if (change.Count > 0 && normal.Totals.GetValueOrDefault(change.Name) <= 0)
                latest = Max(latest, timestamp);
        }

        var totals = new Dictionary<string, long>(rare, StringComparer.Ordinal);
        foreach (var (name, amount) in normal.Totals)
        {
            if (amount < 0) throw new InvalidOperationException("Normal totals cannot be negative.");
            if (amount > 0) totals[name] = amount;
        }
        var count = checked(normal.SupportedDropCount + rareLots
            .Where(pair => normal.Totals.GetValueOrDefault(pair.Key) <= 0).Sum(pair => pair.Value.Count));
        var differences = totals.Keys.Concat(_previous.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(name => (Name: name, Amount: checked(totals.GetValueOrDefault(name) - _previous.GetValueOrDefault(name))))
            .Where(pair => pair.Amount != 0).ToArray();
        var revision = differences.Length > 0 || count != _dropCount || latest != _latestArrival
            ? checked(_revision + 1) : _revision;
        var projection = new LootTotalsProjection(revision, totals, count, latest);
        projection.Validate();
        var events = new List<TrackedLootEvent>();
        foreach (var (name, amount) in differences)
        {
            var remaining = amount;
            var chunk = 0;
            while (remaining != 0)
            {
                var delta = (int)Math.Clamp(remaining, -int.MaxValue, int.MaxValue);
                var identity = Encoding.UTF8.GetBytes($"lifetime-v1|{timestamp.UtcTicks}|{revision}|{name}|{chunk++}");
                events.Add(new(new Guid(SHA256.HashData(identity).AsSpan(0, 16)), timestamp, name, delta));
                remaining -= delta;
            }
        }
        // Commit only after the complete replacement and all audit deltas validate.
        _rare = rare;
        _rareLots = rareLots;
        _previous = projection.Totals;
        _dropCount = count;
        _latestArrival = latest;
        _revision = revision;
        return (projection, events.AsReadOnly());
    }

    public void Reset()
    {
        _rare.Clear();
        _rareLots.Clear();
        _previous = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>());
        _dropCount = 0;
        _latestArrival = null;
        _revision = 0;
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null || left >= right ? left : right;
}
