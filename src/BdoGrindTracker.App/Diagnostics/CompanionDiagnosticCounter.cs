using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Diagnostics;

/// <summary>
/// Adapts recorded, already matched and spot-filtered rows to the restored counters.
/// This deliberately does not perform confidence gating, OCR or spot inference.
/// </summary>
internal sealed class CompanionDiagnosticCounter(IReadOnlyList<CompanionRareCatalogEntry> catalog,
    IReadOnlyDictionary<string, uint>? minimumQuantities = null)
{
    private readonly CompanionFrameReconciler normal = new(minimumQuantities);
    private readonly CompanionLootLedger ledger = new();
    private CompanionRareFrameReconciler? rare;
    private bool? rareEnabled;

    public TrackerFrameResult ProcessFrame(
        DateTimeOffset timestamp,
        IReadOnlyList<LootObservation> observations,
        bool enableRare)
    {
        if (rareEnabled is { } configured && configured != enableRare)
        {
            throw new InvalidDataException("Rare-Loot-Konfiguration wechselt innerhalb der Diagnose-Aufnahme.");
        }

        if (rareEnabled is null)
        {
            rareEnabled = enableRare;
            rare = enableRare ? new CompanionRareFrameReconciler(catalog, ledger) : null;
        }

        var accepted = observations
            .Where(static observation => !string.IsNullOrWhiteSpace(observation.ItemName) &&
                observation.RejectionReason is null)
            .ToArray();
        if (!enableRare && accepted.Any(static observation => observation.Source == LootSource.Rare))
        {
            throw new InvalidDataException("Rare-Loot-Eingabe trotz deaktiviertem Rare-Loot-Kanal.");
        }

        var normalRows = accepted.Where(static observation => observation.Source == LootSource.Normal)
            .OrderByDescending(static observation => observation.NativeY)
            .Select(static observation => new CompanionRecognizedEntry(
                observation.ItemName!, unchecked((uint)(observation.Quantity ?? -1)), observation.NativeY!.Value))
            .ToArray();
        var rareRows = accepted.Where(static observation => observation.Source == LootSource.Rare)
            .OrderBy(static observation => observation.NativeY)
            .Select(static observation => new CompanionRareRecognizedEntry(
                observation.ItemName!, observation.Quantity ?? -1, observation.NativeY!.Value))
            .ToArray();

        var (events, decisions) = AddNormal(timestamp, normal.ProcessFrame(normalRows));
        if (rare is not null)
        {
            // Native rare reconciliation sees the same ledger as the normal path.
            AddRare(timestamp, rare.ProcessFrame(rareRows), events);
        }

        return new TrackerFrameResult(events, decisions);
    }

    public TrackerFrameResult CompleteSession(DateTimeOffset timestamp)
    {
        var (events, decisions) = AddNormal(timestamp, normal.Complete());
        if (rare is not null)
        {
            AddRare(timestamp, rare.Complete(), events);
        }

        return new TrackerFrameResult(events, decisions);
    }

    private (List<TrackedLootEvent> Events, List<LootTrackingDecision> Decisions) AddNormal(
        DateTimeOffset timestamp,
        IReadOnlyList<CompanionRecognizedEntry> entries)
    {
        var events = new List<TrackedLootEvent>(entries.Count);
        var decisions = new List<LootTrackingDecision>();
        foreach (var entry in entries)
        {
            ledger.Add(entry.Name, entry.Count);
            var lootEvent = new TrackedLootEvent(Guid.NewGuid(), timestamp, entry.Name, checked((int)entry.Count));
            events.Add(lootEvent);
            if (entry.IsMinimumQuantityEstimate)
                decisions.Add(new(new LootObservation(LootSource.Normal, 0, "", entry.Name,
                    lootEvent.Quantity, 0, 0, null, null) { NativeY = entry.Y }, lootEvent.EventId,
                    LootTrackingDecisionStatus.Counted, LootDiagnosticFormat.MinimumQuantityEstimateReason));
        }

        return (events, decisions);
    }

    private static void AddRare(
        DateTimeOffset timestamp,
        IReadOnlyList<CompanionRareCountDelta> changes,
        List<TrackedLootEvent> events)
    {
        foreach (var change in changes)
        {
            events.Add(new TrackedLootEvent(Guid.NewGuid(), timestamp, change.Name, change.Count));
        }
    }
}
