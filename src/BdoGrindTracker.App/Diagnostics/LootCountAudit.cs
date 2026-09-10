using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Diagnostics;

/// <summary>Bounded counters and examples only. Full decisions remain in observations.jsonl.</summary>
internal sealed class LootCountAudit
{
    private readonly Dictionary<string, long> _recordedTotals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _outcomes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _candidateReasons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _candidateItems = new(StringComparer.Ordinal);
    private readonly List<LootCountCandidate> _examples = [];
    private long _lastFinalizedCapture;
    private long _lastCapture;
    private long _traceFrames;
    private long _newDrops;
    private long _quantityRevisions;
    private long _candidateCount;

    public void Observe(TrackerFrameResult result, IReadOnlyList<RecordedNormalReconciliation>? traces)
    {
        _lastCapture = Math.Max(_lastCapture, result.NormalCaptureIndex ?? 0);
        foreach (var change in result.NewEvents)
            _recordedTotals[change.ItemName] = checked(_recordedTotals.GetValueOrDefault(change.ItemName) + change.Quantity);
        foreach (var recorded in traces ?? [])
        {
            var trace = recorded.Trace;
            _lastFinalizedCapture = Math.Max(_lastFinalizedCapture, trace.CaptureIndex);
            _traceFrames++;
            foreach (var row in trace.Rows)
            {
                _outcomes[row.Outcome] = _outcomes.GetValueOrDefault(row.Outcome) + 1;
                if (row.Outcome == "quantity-revised") _quantityRevisions++;
                if (row.Outcome != "counted-new") continue;
                _newDrops++;
                if (row.AlignmentReason is not ("overlap-rejected-by-frame-tags" or "item-conflict-after-placeholder")) continue;
                _candidateCount++;
                _candidateReasons[row.AlignmentReason] = _candidateReasons.GetValueOrDefault(row.AlignmentReason) + 1;
                var name = row.ItemName ?? "unknown";
                _candidateItems[name] = _candidateItems.GetValueOrDefault(name) + 1;
                if (_examples.Count < 100)
                    _examples.Add(new(name, row.AlignmentReason, row.TrackId, row.CandidatePreviousTrackId,
                        trace.CaptureIndex, trace.CapturedAt, recorded.RecordingSequence,
                        recorded.NormalCropFileName, recorded.PreviousRecordingSequence,
                        recorded.PreviousNormalCropFileName, row.Slot, row.QuantityDelta));
            }
        }
    }

    public object Snapshot(Guid sessionId, DateTimeOffset savedAt, TimeSpan activeTime,
        IReadOnlyDictionary<string, long> savedTotals) => new
    {
        FormatVersion = 1, SessionId = sessionId, SavedAt = savedAt, ActiveSeconds = activeTime.TotalSeconds,
        Description = "Verdachtsstellen zur Prüfung, keine nachgewiesenen Doppelzählungen. Die vollständigen Zeilenabgleiche stehen in observations.jsonl. Mengen und Zählregeln wurden durch die Diagnose nicht verändert.",
        Scope = "Zeilenabgleich: normaler Droplog. RecordedTotals: alle aufgezeichneten Normal-/Rare-Deltas einschließlich Mengenrevisionen. SavedTotals: gespeicherte Session einschließlich manueller Änderungen und ggf. früherer Aufzeichnungsabschnitte.",
        NormalTraceFrames = _traceFrames, PendingNormalFrames = Math.Max(0, _lastCapture - _lastFinalizedCapture),
        NewNormalDrops = _newDrops, NormalQuantityRevisions = _quantityRevisions,
        RowOutcomes = _outcomes, CandidateCount = _candidateCount, CandidateReasons = _candidateReasons,
        CandidatesByItem = _candidateItems, ExampleLimit = 100, ExamplesTruncated = _candidateCount > _examples.Count,
        Examples = _examples,
        Totals = _recordedTotals.Keys.Concat(savedTotals.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(name => new { ItemName = name, Recorded = _recordedTotals.GetValueOrDefault(name),
                Saved = savedTotals.GetValueOrDefault(name),
                SavedMinusRecorded = savedTotals.GetValueOrDefault(name) - _recordedTotals.GetValueOrDefault(name) }).ToArray(),
    };
}

internal sealed record LootCountCandidate(string ItemName, string Reason, Guid? EventId, Guid? PreviousCandidateEventId,
    long CaptureIndex, DateTimeOffset? CapturedAt, int? RecordingSequence, string? NormalCropFileName,
    int? PreviousRecordingSequence, string? PreviousNormalCropFileName, int? Slot, int Quantity);
