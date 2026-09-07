using System.Globalization;
using System.Text;
using System.Text.Json;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Diagnostics;

internal sealed record LootDiagnosticReplayResult(
    string RecordingPath,
    string? SpotId,
    int FrameCount,
    int CompletionCount,
    IReadOnlyDictionary<string, long> Totals,
    IReadOnlyDictionary<string, long> RecordedTotals,
    bool TotalsMatch,
    bool EventTimelineMatches,
    int? FirstDifferentSequence,
    bool HasFinalCompletion)
{
    public string RecordingEngineVersion { get; init; } = LootDiagnosticFormat.EngineVersion;

    public bool UsesCurrentEngine => RecordingEngineVersion == LootDiagnosticFormat.EngineVersion;

    public string ToDisplayText()
    {
        var text = new StringBuilder();
        text.AppendLine(AppBranding.Name + " – Diagnose-Replay");
        text.AppendLine($"Aufnahme: {RecordingPath}");
        text.AppendLine($"Spot: {SpotId ?? "nicht angegeben"}");
        text.AppendLine($"Frames: {FrameCount}; Sitzungsabschlüsse: {CompletionCount}");
        text.AppendLine($"Aufnahme-Engine: {RecordingEngineVersion}; Replay-Engine: {LootDiagnosticFormat.EngineVersion}");
        text.AppendLine("Umfang: aktuelle Companion-basierte Zähllogik; gespeicherte OCR-/Matching-Ergebnisse werden wiederverwendet.");
        if (!UsesCurrentEngine)
            text.AppendLine("Versionsvergleich: Die Aufnahme stammt aus einer anderen Engine-Version und wird mit dem aktuellen Zähler verglichen. Unterschiede können versionsbedingt sein; gespeicherte OCR-Mengen bleiben unverändert.");
        text.AppendLine("Bildausschnitte werden nicht geöffnet. OCR-Erkennung wird nicht erneut ausgeführt.");
        text.AppendLine($"Summen identisch: {(TotalsMatch ? "ja" : "nein")}");
        text.AppendLine($"Ereignisse je Frame identisch: {(EventTimelineMatches ? "ja" : "nein")}");
        if (FirstDifferentSequence is { } sequence)
        {
            text.AppendLine($"Erste abweichende Sequenz: {sequence}");
        }

        if (!HasFinalCompletion)
        {
            text.AppendLine("Teilaufnahme: kein abschließender Sitzungsabschluss; nur aufgezeichnete Aktionen abgespielt.");
        }

        text.AppendLine();
        text.AppendLine("Item | Replay | Aufnahme");
        foreach (var name in Totals.Keys.Concat(RecordedTotals.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            text.AppendLine($"{name} | {GetTotal(Totals, name).ToString(CultureInfo.InvariantCulture)} | " +
                GetTotal(RecordedTotals, name).ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    private static long GetTotal(IReadOnlyDictionary<string, long> totals, string itemName) =>
        totals.TryGetValue(itemName, out var total) ? total : 0;
}

/// <summary>
/// Replays serialized accepted observations through the current Companion-based counters. It does not execute
/// OCR, capture a screen, interact with a game, or follow any image path from the recording.
/// </summary>
internal static class LootDiagnosticReplay
{
    public static LootDiagnosticReplayResult Run(string recordingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingPath);
        var path = Path.GetFullPath(recordingPath);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0)
        {
            throw new InvalidDataException("Diagnose-Datei fehlt oder ist leer.");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var header = Deserialize<LootDiagnosticHeader>(ReadBoundedLine(reader), 1);
        if (header.Kind != "header" || header.FormatVersion != LootDiagnosticFormat.Version ||
            (header.EngineVersion != LootDiagnosticFormat.EngineVersion &&
             header.EngineVersion != LootDiagnosticFormat.PreviousEngineVersion &&
             header.EngineVersion != LootDiagnosticFormat.ExperimentalEngineVersion) ||
            header.SpotId?.Length > LootDiagnosticFormat.MaximumTextLength ||
            header.Catalog is null || header.Catalog.Count is 0 or > 1024 ||
            header.Catalog.Any(static item => item is null || string.IsNullOrWhiteSpace(item.Name) ||
                item.Name.Length > LootDiagnosticFormat.MaximumTextLength ||
                item.IconPath?.Length > LootDiagnosticFormat.MaximumTextLength))
        {
            throw new InvalidDataException("Nicht unterstütztes Diagnose-Format oder Ereignislogik-Version.");
        }

        var tracker = new CompanionDiagnosticCounter(header.Catalog);
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        var recordedTotals = new Dictionary<string, long>(StringComparer.Ordinal);
        var frameCount = 0;
        var completionCount = 0;
        var sequence = 0;
        int? firstDifferentSequence = null;
        DateTimeOffset? lastTimestamp = null;
        var finalCompletion = false;
        while (ReadBoundedLine(reader) is { } line)
        {
            sequence++;
            var entry = Deserialize<LootDiagnosticEntry>(line, sequence + 1);
            if (entry.Sequence != sequence || entry.Timestamp < lastTimestamp ||
                entry.Events is null || entry.Events.Count > 256 ||
                entry.Decisions is null || entry.Decisions.Count > 256 ||
                entry.Crops is null || entry.Crops.Count > 2)
            {
                throw new InvalidDataException($"Ungültige Diagnose-Metadaten in Sequenz {sequence}.");
            }

            DiagnosticRecordingSession.ValidateObservations(entry.Observations);
            ValidateEvents(entry.Events, sequence);
            TrackerFrameResult actual;
            if (entry.Kind == "frame")
            {
                frameCount++;
                actual = tracker.ProcessFrame(entry.Timestamp, entry.Observations, entry.RareEnabled);
                finalCompletion = false;
            }
            else if (entry.Kind == "complete" && entry.Observations.Count == 0 && entry.Crops.Count == 0)
            {
                actual = tracker.CompleteSession(entry.Timestamp);
                completionCount++;
                finalCompletion = true;
            }
            else
            {
                throw new InvalidDataException($"Unbekannte Diagnose-Aktion in Sequenz {sequence}.");
            }

            lastTimestamp = entry.Timestamp;
            AddEvents(totals, actual.NewEvents);
            AddEvents(recordedTotals, entry.Events);
            if (firstDifferentSequence is null && !EventsEqual(actual.NewEvents, entry.Events))
            {
                firstDifferentSequence = sequence;
            }
        }

        return new LootDiagnosticReplayResult(
            path,
            header.SpotId,
            frameCount,
            completionCount,
            totals,
            recordedTotals,
            TotalsEqual(totals, recordedTotals),
            firstDifferentSequence is null,
            firstDifferentSequence,
            finalCompletion)
        {
            RecordingEngineVersion = header.EngineVersion,
        };
    }

    private static T Deserialize<T>(string? line, int lineNumber)
    {
        try
        {
            if (line is null || JsonSerializer.Deserialize<T>(line, LootDiagnosticFormat.JsonOptions) is not { } value)
            {
                throw new InvalidDataException($"Fehlender Diagnose-Eintrag in Zeile {lineNumber}.");
            }

            return value;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Beschädigter Diagnose-Eintrag in Zeile {lineNumber}.", exception);
        }
    }

    private static string? ReadBoundedLine(StreamReader reader)
    {
        var line = new StringBuilder();
        while (true)
        {
            var next = reader.Read();
            if (next < 0)
            {
                return line.Length == 0 ? null : line.ToString();
            }

            if (next == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            if (line.Length >= LootDiagnosticFormat.MaximumJsonLineBytes)
            {
                throw new InvalidDataException("Diagnose-Zeile überschreitet die zulässige Größe.");
            }

            line.Append((char)next);
        }
    }

    private static void ValidateEvents(IReadOnlyList<TrackedLootEvent> events, int sequence)
    {
        if (events.Any(static entry => entry is null || string.IsNullOrWhiteSpace(entry.ItemName) ||
            entry.ItemName.Length > LootDiagnosticFormat.MaximumTextLength || entry.Quantity == 0))
        {
            throw new InvalidDataException($"Ungültige Ereignisse in Sequenz {sequence}.");
        }
    }

    private static void AddEvents(Dictionary<string, long> totals, IReadOnlyList<TrackedLootEvent> events)
    {
        foreach (var lootEvent in events)
        {
            totals.TryGetValue(lootEvent.ItemName, out var current);
            var updated = checked(current + lootEvent.Quantity);
            if (updated == 0)
            {
                totals.Remove(lootEvent.ItemName);
            }
            else
            {
                totals[lootEvent.ItemName] = updated;
            }
        }
    }

    private static bool EventsEqual(IReadOnlyList<TrackedLootEvent> actual, IReadOnlyList<TrackedLootEvent> expected) =>
        actual.Select(static entry => (entry.ItemName, entry.Quantity))
            .OrderBy(static entry => entry.ItemName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Quantity)
            .SequenceEqual(expected.Select(static entry => (entry.ItemName, entry.Quantity))
                .OrderBy(static entry => entry.ItemName, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Quantity));

    private static bool TotalsEqual(
        IReadOnlyDictionary<string, long> actual,
        IReadOnlyDictionary<string, long> expected) =>
        actual.Count == expected.Count && actual.All(pair =>
            expected.TryGetValue(pair.Key, out var expectedValue) && expectedValue == pair.Value);
}
