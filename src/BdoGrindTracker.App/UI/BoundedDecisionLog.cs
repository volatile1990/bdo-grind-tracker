using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Bounded, incremental view of recent sampled decisions. This is deliberately
/// not the full audit trail; the optional recorder owns that separate workflow.
/// </summary>
internal sealed class BoundedDecisionLog
{
    internal const int MaximumLines = 80;
    internal const int MaximumCharacters = 16_384;
    internal const int MaximumLineCharacters = 512;

    private readonly Queue<LogLine> _lines = new();
    private readonly HashSet<DecisionKey> _visibleKeys = [];

    public int LineCount => _lines.Count;
    public int CharacterCount { get; private set; }

    public DecisionLogUpdate Append(IReadOnlyList<LootTrackingDecision> decisions)
    {
        var previousLineCount = _lines.Count;
        var removedCharacters = 0;
        List<string>? appended = null;
        var skippedAppendedLines = 0;
        foreach (var decision in decisions)
        {
            if (decision.Status is not (LootTrackingDecisionStatus.Counted or
                LootTrackingDecisionStatus.Pending or LootTrackingDecisionStatus.Rejected or
                LootTrackingDecisionStatus.Unresolved))
                continue;

            var observation = decision.Observation;
            var key = new DecisionKey(
                decision.EventId,
                decision.Status,
                observation.Source,
                observation.ItemName ?? observation.RawText,
                observation.Quantity,
                decision.Reason);
            if (!_visibleKeys.Add(key))
                continue;

            var state = decision.Status switch
            {
                LootTrackingDecisionStatus.Counted => observation.Quantity < 0 ? "Korrigiert" : "Gebucht",
                LootTrackingDecisionStatus.Pending => "Im Frame-Abgleich",
                _ => "Ungeklärt",
            };
            var text = $"{state} · {observation.Source}/{observation.Slot} · " +
                $"{observation.ItemName ?? observation.RawText} ×{observation.Quantity?.ToString() ?? "?"} · " +
                $"{key.Id?.ToString("N")[..8] ?? "–"} · {decision.Reason}";
            text = text.Replace('\r', ' ').Replace('\n', ' ');
            if (text.Length > MaximumLineCharacters)
                text = text[..(MaximumLineCharacters - 1)] + "…";
            text += Environment.NewLine;
            _lines.Enqueue(new LogLine(key, text.Length));
            CharacterCount += text.Length;
            (appended ??= []).Add(text);

            while (_lines.Count > MaximumLines || CharacterCount > MaximumCharacters)
            {
                var removed = _lines.Dequeue();
                _visibleKeys.Remove(removed.Key);
                CharacterCount -= removed.CharacterCount;
                if (previousLineCount > 0)
                {
                    removedCharacters += removed.CharacterCount;
                    previousLineCount--;
                }
                else
                {
                    skippedAppendedLines++;
                }
            }
        }

        return appended is null
            ? default
            : new DecisionLogUpdate(
                removedCharacters,
                string.Concat(appended.Skip(skippedAppendedLines)));
    }

    public void Clear()
    {
        _lines.Clear();
        _visibleKeys.Clear();
        CharacterCount = 0;
    }

    private readonly record struct DecisionKey(
        Guid? Id,
        LootTrackingDecisionStatus Status,
        LootSource Source,
        string? ItemName,
        int? Quantity,
        string Reason);

    private readonly record struct LogLine(DecisionKey Key, int CharacterCount);
}

internal readonly record struct DecisionLogUpdate(int RemovePrefixCharacters, string? AppendedText)
{
    public bool HasChanges => RemovePrefixCharacters > 0 || !string.IsNullOrEmpty(AppendedText);
}
