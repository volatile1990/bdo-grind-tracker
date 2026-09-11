using BdoGrindTracker.Core;
using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.Diagnostics;

/// <summary>
/// Adapts recorded inputs to their original counter. Raw-text mode also receives
/// rejected rows and the recorded parsing context; replay performs no OCR or spot inference.
/// </summary>
internal sealed class CompanionDiagnosticCounter(IReadOnlyList<CompanionRareCatalogEntry> catalog,
    IReadOnlyDictionary<string, uint>? minimumQuantities = null, bool trackRows = false, bool temporal = false,
    bool legacyTemporal = false, bool lifetime = false, bool rawLifetime = false,
    LifetimeParsingContext? parsingContext = null, bool visualLifetime = false)
{
    private readonly bool usesRawLifetime = rawLifetime || visualLifetime;
    private readonly ICompanionReconciliation normal = lifetime || rawLifetime || visualLifetime
        ? new LifetimeNormalReconciliationAdapter(rawLifetime || visualLifetime
            ? parsingContext ?? throw new InvalidDataException("Dem Rohtext-Normalzähler fehlt der Parsing-Kontext.") : null,
            useVisualSlotCoverage: visualLifetime)
        : temporal
        ? new TemporalNormalReconciliationAdapter(minimumQuantities, legacyTemporal)
        : new CompanionReconciliationAdapter(minimumQuantities, trackRows);
    private readonly CompanionLootLedger ledger = new();
    private readonly LifetimeLootProjectionComposer projectionComposer = new();
    private CompanionRareFrameReconciler? rare;
    private bool? rareEnabled;

    public TrackerFrameResult ProcessFrame(
        DateTimeOffset timestamp,
        IReadOnlyList<LootObservation> observations,
        bool enableRare,
        LifetimeParsingContext? parsingContext = null)
    {
        if ((lifetime || usesRawLifetime || !temporal || legacyTemporal) && observations.Any(observation => observation.AppearanceEvidence is not null))
            throw new InvalidDataException("Visuelle Zeilenevidenz gehört nicht zu diesem historischen Normalzähler.");
        if (!visualLifetime && observations.Any(observation => observation.OccupancyEvidence is not null))
            throw new InvalidDataException("Visuelle Belegung gehört ausschließlich zum Lebensdauer-Normalzähler v3.");
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
            .Select(observation => new CompanionRecognizedEntry(
                observation.ItemName!, unchecked((uint)(observation.Quantity ?? -1)), observation.NativeY!.Value)
                { QuantityBounds = observation.QuantityBounds, Slot = trackRows || temporal || lifetime ? observation.Slot : null,
                    IsAlignmentAnchor = observation.IsAlignmentAnchor, AlignmentPreviousSlot = observation.AlignmentPreviousSlot,
                    NameConfidence = temporal || lifetime ? observation.NameConfidence : 1,
                    RawText = temporal || lifetime ? observation.RawText : null,
                    AppearanceEvidence = temporal && !legacyTemporal ? observation.AppearanceEvidence : null })
            .ToArray();
        var rareRows = accepted.Where(static observation => observation.Source == LootSource.Rare)
            .OrderBy(static observation => observation.NativeY)
            .Select(static observation => new CompanionRareRecognizedEntry(
                observation.ItemName!, observation.UsesImplicitUnitQuantity && observation.QuantityBounds is not null
                    ? -1 : observation.Quantity ?? -1,
                observation.NativeY!.Value) { QuantityBounds = observation.QuantityBounds })
            .ToArray();

        ApplyParsingContext(parsingContext);
        var normalChanges = usesRawLifetime
            ? ((LifetimeNormalReconciliationAdapter)normal).ProcessObservations(
                observations.Where(static row => row.Source == LootSource.Normal).ToArray(), timestamp)
            : normal.ProcessFrame(normalRows, timestamp);
        if (lifetime || usesRawLifetime)
            return ComposeProjection(timestamp, rare?.ProcessFrame(rareRows) ?? []);
        var (events, decisions) = AddNormal(timestamp, normalChanges);
        if (rare is not null)
        {
            // Native rare reconciliation sees the same ledger as the normal path.
            AddRare(timestamp, rare.ProcessFrame(rareRows), events);
        }

        return new TrackerFrameResult(events, decisions)
            { NormalCaptureIndex = normal.CaptureIndex, NormalReconciliation = normal.LastTrace };
    }

    public TrackerFrameResult CompleteSession(DateTimeOffset timestamp, LifetimeParsingContext? parsingContext = null)
    {
        ApplyParsingContext(parsingContext);
        var normalChanges = normal.Complete();
        if (lifetime || usesRawLifetime)
            return ComposeProjection(timestamp, rare?.Complete() ?? []);
        var (events, decisions) = AddNormal(timestamp, normalChanges);
        if (rare is not null)
        {
            AddRare(timestamp, rare.Complete(), events);
        }

        return new TrackerFrameResult(events, decisions)
            { NormalCaptureIndex = normal.CaptureIndex, NormalReconciliation = normal.LastTrace };
    }

    private void ApplyParsingContext(LifetimeParsingContext? context)
    {
        if (context is null) return;
        if (!usesRawLifetime) throw new InvalidDataException("Parsing-Kontext ohne Rohtext-Normalzähler.");
        ((LifetimeNormalReconciliationAdapter)normal).UpdateParsingContext(context);
    }

    private TrackerFrameResult ComposeProjection(DateTimeOffset timestamp,
        IReadOnlyList<CompanionRareCountDelta> rareChanges)
    {
        if (normal.Projection is not { } snapshot)
            return new([], []) { NormalCaptureIndex = normal.CaptureIndex, NormalReconciliation = normal.LastTrace };
        var combined = projectionComposer.Combine(snapshot, rareChanges, timestamp);
        return new(combined.Events, [])
        {
            NormalCaptureIndex = normal.CaptureIndex, NormalReconciliation = normal.LastTrace,
            LootProjection = combined.Projection,
            LifetimeParsingContext = usesRawLifetime ? ((LifetimeNormalReconciliationAdapter)normal).ParsingContext : null,
        };
    }

    private (List<TrackedLootEvent> Events, List<LootTrackingDecision> Decisions) AddNormal(
        DateTimeOffset timestamp,
        IReadOnlyList<CompanionRecognizedEntry> entries)
    {
        var events = new List<TrackedLootEvent>(entries.Count);
        var decisions = new List<LootTrackingDecision>();
        foreach (var entry in entries)
        {
            var delta = entry.QuantityDelta ?? checked((int)entry.Count);
            ledger.ApplyDelta(entry.Name, delta);
            var lootEvent = new TrackedLootEvent(entry.EventId ?? Guid.NewGuid(), entry.DetectedAt ?? timestamp, entry.Name, delta)
                { Revision = entry.Revision, TotalDropQuantity = entry.TotalDropQuantity };
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
