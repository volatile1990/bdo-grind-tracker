namespace BdoGrindTracker.Core;

/// <summary>
/// By default the first projection is an old-log baseline. A caller with an
/// independent visual arrival may accept its first recognized monster drop,
/// including delayed OCR, to start a provisional session. Retaining that session
/// requires further live evidence.
/// </summary>
public sealed class GrindStartConfirmation(DateTimeOffset startedAt, bool acceptInitialArrival = false)
{
    private static readonly HashSet<string> TrashNames = TrashLootMinimumCatalog.Entries
        .Select(entry => entry.ItemName).ToHashSet(StringComparer.Ordinal);
    private LootTotalsProjection? _previous;
    private DateTimeOffset _lastArrival = startedAt;
    private bool _confirmed;
    private bool _acceptInitialArrival = acceptInitialArrival;

    public bool Observe(LootTotalsProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var previous = _previous;
        _previous = projection;
        var unchangedCorrections = previous is null ||
            projection.QuantityCorrectionRevision == previous.QuantityCorrectionRevision;
        if (!unchangedCorrections) _acceptInitialArrival = false;
        // The reconciler can date the visually new row just before the triggering
        // capture. Independent visual evidence permits that initial estimate;
        // neither another arrival nor another OCR-positive frame is required.
        if (_acceptInitialArrival && projection.ConfirmedDropCount > 0 &&
            projection.Totals.Any(pair => TrashNames.Contains(pair.Key) && pair.Value > 0))
            _confirmed = true;
        if (previous is null)
        {
            if (projection.LatestArrivalAt > _lastArrival) _lastArrival = projection.LatestArrivalAt.Value;
            return _confirmed;
        }
        var newArrival = projection.LatestArrivalAt is { } arrival &&
            arrival > _lastArrival &&
            projection.ConfirmedDropCount > previous.ConfirmedDropCount;
        if (projection.LatestArrivalAt is { } latest && latest > _lastArrival)
            _lastArrival = latest;
        // A corrected trash quantity and a simultaneous non-monster arrival are
        // not proof of a new monster drop. Legacy projections without correction
        // metadata still use the independent arrival/count/quantity checks.
        if (newArrival && unchangedCorrections && projection.Totals.Any(pair => TrashNames.Contains(pair.Key) &&
                pair.Value > previous.Totals.GetValueOrDefault(pair.Key)))
            _confirmed = true;
        return _confirmed;
    }
}
