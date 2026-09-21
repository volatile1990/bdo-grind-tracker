namespace BdoGrindTracker.Core;

/// <summary>
/// A preexisting loot log and quantity corrections cannot start a grind. Require
/// two later arrivals that increase both the physical drop count and monster trash.
/// </summary>
public sealed class GrindStartConfirmation(DateTimeOffset startedAt)
{
    private static readonly HashSet<string> TrashNames = TrashLootMinimumCatalog.Entries
        .Select(entry => entry.ItemName).ToHashSet(StringComparer.Ordinal);
    private LootTotalsProjection? _previous;
    private DateTimeOffset _lastArrival = startedAt;
    private int _arrivals;

    public bool Observe(LootTotalsProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var previous = _previous;
        _previous = projection;
        if (previous is null)
        {
            if (projection.LatestArrivalAt > _lastArrival) _lastArrival = projection.LatestArrivalAt.Value;
            return false;
        }
        var newArrival = projection.LatestArrivalAt is { } arrival &&
            arrival > _lastArrival &&
            projection.ConfirmedDropCount > previous.ConfirmedDropCount;
        if (projection.LatestArrivalAt is { } latest && latest > _lastArrival)
            _lastArrival = latest;
        if (newArrival && projection.Totals.Any(pair => TrashNames.Contains(pair.Key) &&
                pair.Value > previous.Totals.GetValueOrDefault(pair.Key)))
            _arrivals++;
        return _arrivals >= 2;
    }
}
