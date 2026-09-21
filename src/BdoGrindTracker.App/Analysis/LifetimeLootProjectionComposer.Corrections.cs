using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

internal sealed partial class LifetimeLootProjectionComposer
{
    private SourceEvidence? _normalEvidence, _specialEvidence;
    private bool _correctionEvidenceTrusted = true;
    private long _correctionVersion;
    private long? _lastCorrectionVersion;
    private bool _hasProjection;

    private sealed record SourceEvidence(Dictionary<string, long> Totals, int Count,
        Dictionary<Guid, LifetimeObservedDrop> Drops, LifetimeObservedDrop? PolicyStart, bool IsComplete);

    private static SourceEvidence? ReadSourceEvidence(LifetimeSnapshot source)
    {
        // PolicyDrops includes settled identities; ObservedDrops additionally
        // covers every still-mutable row even when the policy history is capped.
        if (source.PolicyDrops is null || source.ObservedDrops is null) return null;
        var drops = new Dictionary<Guid, LifetimeObservedDrop>();
        foreach (var drop in source.PolicyDrops.Concat(source.ObservedDrops))
        {
            if (drop is null || drop.EventId == Guid.Empty || string.IsNullOrWhiteSpace(drop.Name) || drop.Quantity <= 0)
                return null;
            if (drops.TryGetValue(drop.EventId, out var duplicate) && duplicate != drop) return null;
            drops[drop.EventId] = drop;
        }
        var totals = source.Totals.Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (source.SupportedDropCount < drops.Count || source.SupportedDropCount > 0 && drops.Count == 0)
            return null;
        var knownTotals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var drop in drops.Values)
            knownTotals[drop.Name] = checked(knownTotals.GetValueOrDefault(drop.Name) + drop.Quantity);
        if (knownTotals.Any(pair => pair.Value > totals.GetValueOrDefault(pair.Key))) return null;
        var complete = source.SupportedDropCount == drops.Count && SameAmounts(totals, knownTotals);
        var policyStart = source.PolicyDrops.OrderBy(drop => drop.DetectedAt).ThenBy(drop => drop.EventId).FirstOrDefault();
        return new(totals, source.SupportedDropCount, drops, policyStart, complete);
    }

    private static bool? CompareSourceEvidence(SourceEvidence? previous, SourceEvidence? current)
    {
        if (current is null) return null;
        if (previous is null) return current.IsComplete ? false : null;

        foreach (var (id, oldDrop) in previous.Drops)
        {
            if (current.Drops.TryGetValue(id, out var newDrop))
            {
                if (oldDrop.Name != newDrop.Name || oldDrop.Quantity != newDrop.Quantity) return true;
            }
            else if (current.PolicyStart is { } start &&
                (oldDrop.DetectedAt > start.DetectedAt ||
                 oldDrop.DetectedAt == start.DetectedAt && oldDrop.EventId.CompareTo(start.EventId) >= 0))
            {
                // An identity inside the retained policy range disappeared.
                // Older missing identities may simply have aged out of that range.
                return true;
            }
        }
        if (current.Count < previous.Count || previous.Totals.Any(pair =>
                current.Totals.GetValueOrDefault(pair.Key) < pair.Value)) return true;

        var added = current.Drops.Values.Where(drop => !previous.Drops.ContainsKey(drop.EventId)).ToArray();
        var expected = new Dictionary<string, long>(previous.Totals, StringComparer.Ordinal);
        foreach (var drop in added)
            expected[drop.Name] = checked(expected.GetValueOrDefault(drop.Name) + drop.Quantity);
        // New identities must explain both the quantities and physical count.
        // A policy-window eviction changes neither, so normal long sessions
        // retain their proven additions without storing every historical ID.
        return current.Count == (long)previous.Count + added.Length && SameAmounts(current.Totals, expected)
            ? false : null;
    }

    private static bool SameAmounts(IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right) =>
        left.Count == right.Count && left.All(pair => right.GetValueOrDefault(pair.Key) == pair.Value);
}
