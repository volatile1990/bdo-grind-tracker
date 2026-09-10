using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace PaddlePrimaryReplay;

internal sealed class ReplayStatistics
{
    public Dictionary<string, long> Totals { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> AcceptedObservationsBySource { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> EventCountBySource { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, long>> QuantityBySource { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> RowReviewOutcomes { get; } = new(StringComparer.Ordinal);
    public long RowReviewReadings { get; private set; }
    public long ObservedOcrErrors { get; private set; }
    public long OcrCallsReportedByAnalyzer { get; private set; }
    public int ChangedObservationFrames { get; private set; }
    public int ChangedEventFrames { get; private set; }
    public double PreparationMilliseconds { get; private set; }
    public double AnalysisMilliseconds { get; private set; }

    public void AddFrameTiming(double preparation, double analysis)
    {
        PreparationMilliseconds += preparation;
        AnalysisMilliseconds += analysis;
    }

    public void Observe(LootDiagnosticEntry source, LootDiagnosticEntry fresh, FrameAnalysisResult result)
    {
        Add(Totals, fresh.Events);
        foreach (var row in fresh.Observations.Where(row => row.ItemName is not null && row.RejectionReason is null))
            Increment(AcceptedObservationsBySource, row.Source.ToString(), 1);
        foreach (var entry in fresh.Events)
        {
            var decision = fresh.Decisions.FirstOrDefault(candidate => candidate.EventId == entry.EventId &&
                candidate.Status == LootTrackingDecisionStatus.Counted);
            var sourceName = decision?.Source.ToString() ?? "Unknown";
            Increment(EventCountBySource, sourceName, 1);
            if (!QuantityBySource.TryGetValue(sourceName, out var quantities))
                QuantityBySource[sourceName] = quantities = new(StringComparer.Ordinal);
            Increment(quantities, entry.ItemName, entry.Quantity);
        }
        foreach (var review in result.RowReviews)
        {
            Increment(RowReviewOutcomes, review.Reason + ":" + review.Outcome, 1);
            RowReviewReadings += review.Readings.Count;
            ObservedOcrErrors += review.Errors;
        }
        ObservedOcrErrors += result.Recovery.Errors;
        OcrCallsReportedByAnalyzer += result.OcrRowCount;
        if (source.Kind == "frame" && ObservationSignature(source.Observations) != ObservationSignature(fresh.Observations))
            ChangedObservationFrames++;
        if (EventSignature(source.Events) != EventSignature(fresh.Events)) ChangedEventFrames++;
    }

    public static void Add(Dictionary<string, long> totals, IReadOnlyList<TrackedLootEvent> events)
    {
        foreach (var entry in events) Increment(totals, entry.ItemName, entry.Quantity);
    }

    private static void Increment(Dictionary<string, long> values, string key, long delta) =>
        values[key] = values.GetValueOrDefault(key) + delta;

    private static string ObservationSignature(IReadOnlyList<LootObservation> observations) =>
        JsonSerializer.Serialize(observations.OrderBy(row => row.Source).ThenBy(row => row.Slot)
            .Select(row => new { row.Source, row.Slot, row.NativeY, row.ItemName, row.Quantity, row.RejectionReason,
                row.QuantityBounds, row.UsesImplicitUnitQuantity, row.UsesFixedUnitQuantity, row.IsAlignmentAnchor }), LootDiagnosticFormat.JsonOptions);

    private static string EventSignature(IReadOnlyList<TrackedLootEvent> events) =>
        JsonSerializer.Serialize(events.OrderBy(entry => entry.ItemName, StringComparer.Ordinal).ThenBy(entry => entry.Quantity)
            .Select(entry => new { entry.ItemName, entry.Quantity, entry.Revision, entry.TotalDropQuantity }), LootDiagnosticFormat.JsonOptions);
}
