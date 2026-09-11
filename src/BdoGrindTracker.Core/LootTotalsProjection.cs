using System.Collections.Frozen;

namespace BdoGrindTracker.Core;

/// <summary>A complete, immutable estimate whose revision can replace a prior projection.</summary>
public sealed record LootTotalsProjection
{
    public long Revision { get; }
    public IReadOnlyDictionary<string, long> Totals { get; }
    public int ConfirmedDropCount { get; }
    public DateTimeOffset? LatestArrivalAt { get; }

    public LootTotalsProjection(long revision, IReadOnlyDictionary<string, long> totals,
        int confirmedDropCount, DateTimeOffset? latestArrivalAt)
    {
        ArgumentNullException.ThrowIfNull(totals);
        Revision = revision;
        Totals = totals.ToFrozenDictionary(StringComparer.Ordinal);
        ConfirmedDropCount = confirmedDropCount;
        LatestArrivalAt = latestArrivalAt;
        Validate();
    }

    public void Validate()
    {
        if (Revision < 0 || ConfirmedDropCount < 0 || Totals.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 4096 || pair.Value < 0))
            throw new ArgumentException("Invalid loot totals projection.");
        long total = 0;
        foreach (var quantity in Totals.Values) total = checked(total + quantity);
    }
}
