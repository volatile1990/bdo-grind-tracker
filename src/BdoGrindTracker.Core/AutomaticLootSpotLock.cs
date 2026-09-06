namespace BdoGrindTracker.Core;

/// <summary>A session-scoped allowlist selected by native-matched trashloot.</summary>
public sealed class AutomaticLootSpotLock
{
    public const string OutsideSpotPoolReason = "outside-spot-pool";

    public LootSpot? Spot { get; private set; }

    public void Observe(IEnumerable<string> newestFirstCanonicalNames)
    {
        ArgumentNullException.ThrowIfNull(newestFirstCanonicalNames);
        if (Spot is not null) return;
        foreach (var name in newestFirstCanonicalNames)
        {
            var id = name switch
            {
                "Branch of Abundance" => LootSpotCatalog.AphrodonId,
                "Black Crystal Fragment" => LootSpotCatalog.HermesiaId,
                "Elion Follower's Helmet" => LootSpotCatalog.MagaiaId,
                "Scorched Belt Ornament" => LootSpotCatalog.AresionId,
                "Elion Follower's Mark" => LootSpotCatalog.ScalesOfJudgmentId,
                "Broken Gloves of the Void" => LootSpotCatalog.EventHorizonId,
                _ => null,
            };
            if (id is null) continue;
            Spot = LootSpotCatalog.GetRequired(id);
            return;
        }
    }

    public bool Allows(string canonicalName, bool includeEventLoot = false) =>
        Spot is null || Spot.Allows(canonicalName) ||
        (includeEventLoot && LootSpotCatalog.IsEventItem(canonicalName));

    public void Reset() => Spot = null;
}
