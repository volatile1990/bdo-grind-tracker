namespace BdoGrindTracker.Core;

/// <summary>A session-scoped allowlist selected by native-matched trashloot.</summary>
public sealed class AutomaticLootSpotLock
{
    public const string OutsideSpotPoolReason = "outside-spot-pool";
    private const int RequiredConfirmedDrops = 3;
    private const int RequiredLead = 2;
    private const int EvidenceCapacity = 12;
    private const int RecentIdCapacity = 128;
    private readonly Dictionary<string, int> _confirmedScores = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _confirmedIds = [];
    private readonly Queue<Guid> _recentIds = new();
    private readonly Queue<string> _evidence = new();

    public LootSpot? Spot { get; private set; }

    public void Observe(IEnumerable<string> newestFirstCanonicalNames)
    {
        ArgumentNullException.ThrowIfNull(newestFirstCanonicalNames);
        if (Spot is not null) return;
        foreach (var name in newestFirstCanonicalNames)
        {
            var id = SpotIdForTrash(name);
            if (id is null) continue;
            Spot = LootSpotCatalog.GetRequired(id);
            return;
        }
    }

    /// <summary>
    /// Selects a spot from distinct first bookings, not repeated sightings or
    /// quantity corrections. The last twelve trash events supply the evidence;
    /// the last 128 IDs suppress redelivery from the ordered reconciliation stream.
    /// </summary>
    public void ObserveConfirmed(IEnumerable<CompanionRecognizedEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (Spot is not null) return;
        foreach (var entry in entries)
        {
            if (entry.IsPlaceholder || entry.IsAlignmentAnchor || entry.IsMinimumQuantityEstimate ||
                entry.Revision != 0 || entry.Count == 0 || entry.QuantityDelta is <= 0 ||
                entry.EventId is not { } eventId || eventId == Guid.Empty) continue;
            var spotId = SpotIdForTrash(entry.Name);
            if (spotId is null || !_confirmedIds.Add(eventId)) continue;
            _recentIds.Enqueue(eventId);
            if (_recentIds.Count > RecentIdCapacity) _confirmedIds.Remove(_recentIds.Dequeue());
            _evidence.Enqueue(spotId);
            _confirmedScores[spotId] = _confirmedScores.GetValueOrDefault(spotId) + 1;
            if (_evidence.Count > EvidenceCapacity)
            {
                var expiredSpot = _evidence.Dequeue();
                var remaining = _confirmedScores[expiredSpot] - 1;
                if (remaining == 0) _confirmedScores.Remove(expiredSpot);
                else _confirmedScores[expiredSpot] = remaining;
            }
        }

        // Consider the entire batch before locking so competing trash in the
        // same frame cannot lose solely because of its enumeration order.
        var ranked = _confirmedScores.OrderByDescending(pair => pair.Value).Take(2).ToArray();
        if (ranked.Length == 0 || ranked[0].Value < RequiredConfirmedDrops) return;
        var runnerUp = ranked.Length > 1 ? ranked[1].Value : 0;
        if (ranked[0].Value - runnerUp >= RequiredLead)
            Spot = LootSpotCatalog.GetRequired(ranked[0].Key);
    }

    public bool Allows(string canonicalName) =>
        Spot is null || Spot.Allows(canonicalName) ||
        LootSpotCatalog.IsEventItem(canonicalName);

    public void Reset()
    {
        Spot = null;
        _confirmedScores.Clear();
        _confirmedIds.Clear();
        _recentIds.Clear();
        _evidence.Clear();
    }

    private static string? SpotIdForTrash(string name) => name switch
    {
        "Branch of Abundance" => LootSpotCatalog.AphrodonId,
        "Black Crystal Fragment" => LootSpotCatalog.HermesiaId,
        "Elion Follower's Helmet" => LootSpotCatalog.MagaiaId,
        "Scorched Belt Ornament" => LootSpotCatalog.AresionId,
        "Elion Follower's Mark" => LootSpotCatalog.ScalesOfJudgmentId,
        "Broken Gloves of the Void" => LootSpotCatalog.EventHorizonId,
        _ => null,
    };
}
