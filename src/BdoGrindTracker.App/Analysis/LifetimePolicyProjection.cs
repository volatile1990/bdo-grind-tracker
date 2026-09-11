using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Policies see the current event history, never accumulated discarded alternatives.</summary>
internal static class LifetimePolicyProjection
{
    public static void Apply(LifetimeSnapshot projection, AutomaticLootSpotLock spotLock,
        TrashQuantityAnomalyDetector? anomalies, Func<string?, string, DropQuantityBounds?> bounds)
    {
        var evidence = projection.PolicyDrops.Select(drop => new CompanionRecognizedEntry(drop.Name, checked((uint)drop.Quantity))
            { EventId = drop.EventId, DetectedAt = drop.DetectedAt, QuantityDelta = drop.Quantity,
                TotalDropQuantity = drop.Quantity, QuantityBounds = bounds(spotLock.Spot?.Id, drop.Name) }).ToArray();
        // Until a spot is established, competing histories must not contribute
        // separate votes for alternative identities of the very same pickup.
        if (spotLock.Spot is null)
        {
            spotLock.Reset();
            spotLock.ObserveConfirmed(evidence);
        }
        // The bounded best-lane history includes finalized recent drops. Rebuild
        // the amount prior so removed rows and corrected quantities cannot linger.
        anomalies?.Reset();
        anomalies?.ObserveCountedDrops(spotLock.Spot?.Id, evidence);
    }
}
