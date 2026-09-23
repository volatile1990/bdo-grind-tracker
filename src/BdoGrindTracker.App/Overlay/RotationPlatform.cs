using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Overlay;

/// <summary>Append-only session evidence. At is capture time; RecordedAt is processing time.
/// Corrections reference the superseded entry instead of deleting evidence.</summary>
public sealed record RotationTimelineEntry(Guid Id, DateTimeOffset At, string? SpotId, string Type, string Kind,
    string Detail, Guid? RunId = null, Guid? Corrects = null, string? ItemName = null, long? Quantity = null,
    Guid? LootEventId = null, int Revision = 0)
{
    public int Version { get; init; } = 1;
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RotationSection(string Id, double Start, double End);

/// <summary>One recovery engine for every spot. Late OCR is replayed at capture time, including loot
/// collected before its delayed AFK-end confirmation. Snapshots never advance the measurement clock.</summary>
internal class RotationPlatform : IRotationEventTracker
{
    private sealed record Input(Guid Id, DateTimeOffset At, string Type, string Kind, string Label);
    private readonly RotationDefinition _definition;
    private readonly string? _path;
    private readonly List<RotationRun> _history = [];
    private readonly List<RotationRun> _legacyUnrated = [];
    private readonly List<Input> _inputs = [];
    private readonly List<RotationTimelineEntry> _journal = [];
    private readonly Dictionary<Guid, (DateTimeOffset StartedAt, RotationRun Run)> _published = [];
    private readonly Dictionary<Guid, (DateTimeOffset StartedAt, RotationRun Run)> _pending = [];
    private Dictionary<string, RotationTimelineEntry> _decisions = [];
    private readonly List<(DateTimeOffset StartedAt, RotationRun Run)> _finished = [];
    private readonly List<RotationEvent> _events = [];
    private readonly List<RotationSection> _sections = [];
    private readonly HashSet<string> _visited = [];
    private readonly Dictionary<string, Queue<double>> _sectionSamples = [];
    private DateTimeOffset? _start, _lootAllowedAt, _sectionStart;
    private DateTimeOffset _clock;
    private Guid _runId;
    private int _position = -1, _setupCount;
    private bool _completeStart, _missing, _boundaryAvailable;
    private string _status = "Warte auf Erkennung";
    private string? _error;
    private string _sectionId = "startup";
    private double _finishedElapsed;
    private bool _dirty;
    private Dictionary<string, RotationTimelineEntry> _building = [];
    private string? _lastSaved;
    private bool _cannotOverwrite;
    private DateTimeOffset? _restoredBoundary;
    private bool _restoredCleanStart;

    internal RotationPlatform(RotationDefinition definition, string? path = null)
    {
        _definition = definition;
        _path = path;
        if (path is null) return;
        var source = File.Exists(path) ? path : Path.Combine(Path.GetDirectoryName(path)!, definition.SpotId + "-rotations.json");
        if (!File.Exists(source)) return;
        try
        {
            if (new FileInfo(source).Length > 16_000_000) throw new InvalidDataException("Rotationsdatei zu groß.");
            var records = JsonSerializer.Deserialize<List<RotationRun>>(File.ReadAllText(source)) ?? [];
            foreach (var record in records.OrderBy(r => r?.RecordedAt))
            {
                if (record is null) continue;
                var r = record.TimingVersion < 3 ? ImportLegacy(record) : record;
                if (r is not null && UsableReference(r))
                    _history.Add(r);
                else _legacyUnrated.Add(record with { Outcome = "incomplete", Reason = "Altreferenz nicht vollständig prüfbar" });
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        { _cannotOverwrite = true; _error = "Rotationsreferenzen konnten nicht geladen werden: " + e.Message; }
        foreach (var run in _history) RecordSectionSamples(run);
    }

    private static bool UsableReference(RotationRun run) => run.EligibleForStatistics && run.TimingVersion >= 3 &&
        run.Events is { Count: > 2 and < 1000 } && run.Sections is { Count: > 0 and < 1000 } &&
        run.Duration > 0 && double.IsFinite(run.Duration) &&
        run.Events.All(e => e is not null && !string.IsNullOrWhiteSpace(e.Kind) && !string.IsNullOrWhiteSpace(e.Label) &&
            double.IsFinite(e.Seconds) && e.Seconds >= 0 && e.Seconds <= run.Duration) &&
        run.Events[0].Kind == "start" && run.Events[0].Seconds == 0 && run.Events[^1].Kind == "end" && run.Events[^1].Seconds == run.Duration &&
        run.Events.Select(e => e.Key).Distinct().Count() == run.Events.Count &&
        run.Events.Zip(run.Events.Skip(1)).All(p => p.First.Seconds <= p.Second.Seconds) &&
        run.Sections.All(s => s is not null && !string.IsNullOrWhiteSpace(s.Id) && double.IsFinite(s.Start) && double.IsFinite(s.End) &&
            s.Start >= 0 && s.End > s.Start && s.End <= run.Duration);

    private RotationRun? ImportLegacy(RotationRun run)
    {
        if (run.Events is not { Count: > 2 and < 1000 } || !double.IsFinite(run.Duration) || run.Duration is <= 0 or >= 7200 ||
            run.Events.Any(e => e is null || !double.IsFinite(e.Seconds) || e.Seconds < 0 || e.Seconds > run.Duration)) return null;
        if (_definition == RotationDefinition.Hermesia) run = HermesiaRotationTracker.FromFirstEvent(run);
        var replay = new RotationPlatform(_definition);
        var epoch = DateTimeOffset.UnixEpoch;
        if (_definition == RotationDefinition.EventHorizon)
        { replay.Observe("end", "Altreferenz-Grenze", epoch.AddSeconds(-5)); replay.ObserveLoot(epoch); }
        else if (_definition == RotationDefinition.Aphrodon) replay.Observe("restart", "Altreferenz-Start", epoch);
        foreach (var e in run.Events.Where(e => e.Kind != "start"))
            replay.Observe(e.Kind == "end" ? _definition.AfkEndMessages[0] : e.Kind, e.Label, epoch.AddSeconds(e.Seconds));
        var completed = replay.DrainCompleted();
        return completed.Length == 1 && completed[0].Run.EligibleForStatistics ? completed[0].Run with { LegacyImported = true } : null;
    }

    internal static string DefaultPath(string spotId) => Path.Combine(AppDataPaths.Current.BaseDirectory, spotId + "-rotation-platform-v1.json");

    public RotationTimelineEntry[] DrainTimeline()
    { var result = _journal.ToArray(); _journal.Clear(); return result; }

    public (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted()
    { EnsureRebuilt(); var result = _pending.Values.ToArray(); _pending.Clear(); return result; }

    public (DateTimeOffset StartedAt, RotationRun Run)? ActiveRun()
    {
        if (_start is not { } start) return null;
        var duration = Math.Max(0, (_clock - start).TotalSeconds);
        RotationSection[] sections = [.. _sections];
        if (_sectionStart is { } section && _clock > section)
            sections = [.. sections, new(_sectionId, (section - start).TotalSeconds, duration)];
        return (start, new(duration, _events.ToArray()) { Id = _runId, TimingVersion = 3, Outcome = "active",
            Reason = _status, Sections = sections, RecordedAt = _clock });
    }

    public void Observe(string kind, string label, DateTimeOffset at) => AddInput("message", kind, label, at);
    public void RestoreBoundary(DateTimeOffset at, bool cleanStart)
    {
        _restoredBoundary = at; _restoredCleanStart = cleanStart;
        _dirty = true; EnsureRebuilt();
    }
    public void Advance(DateTimeOffset at)
    {
        _clock = at > _clock ? at : _clock;
        ExpireIfNecessary(at);
    }

    public bool ObserveLoot(DateTimeOffset at)
    {
        var previous = _runId;
        // Keep loot times for replay, but no need to rebuild while an established run is active.
        AddInput("loot", "trash", "Trashloot", at, defer: _start is not null && at >= _clock);
        return _start is not null && previous != _runId;
    }

    public void Interrupt(string status = "Tracking pausiert") => InterruptAt(status, _clock);
    public void InterruptAt(string status, DateTimeOffset at)
    {
        if (_inputs.Count == 0) return; // An empty, never-started session has no interrupted observation.
        if (_inputs.LastOrDefault() is { Type: "interrupt" } last && last.Label == status) return;
        AddInput("interrupt", "interrupted", status, at);
    }

    private void AddInput(string type, string kind, string label, DateTimeOffset at, bool defer = false)
    {
        var input = new Input(Guid.NewGuid(), at, type, kind, label);
        if (type == "message" && _inputs.Any(i => i.Type == type && i.Kind == kind && i.At == at))
        {
            _journal.Add(new(input.Id, at, _definition.SpotId, "duplicate", kind, "Identische Erkennung bereits verarbeitet"));
            return;
        }
        _inputs.Add(input);
        if (type != "loot") _journal.Add(new(input.Id, at, _definition.SpotId, type, kind, label));
        if (type == "message" && at < _clock) _journal.Add(new(Guid.NewGuid(), at, _definition.SpotId, "late-observation", kind,
            "Verspätete Erkennung · Ablauf anhand der Aufnahmezeit neu ausgewertet"));
        _clock = at > _clock ? at : _clock;
        if (!defer) _dirty = true;
        if (!defer) EnsureRebuilt();
    }

    private void EnsureRebuilt()
    {
        if (!_dirty) return;
        _dirty = false;
        _finished.Clear(); _events.Clear(); _sections.Clear(); _visited.Clear();
        _sectionSamples.Clear();
        foreach (var run in _history) RecordSectionSamples(run);
        _start = _lootAllowedAt = _sectionStart = null; _runId = Guid.Empty; _position = -1;
        _setupCount = 0; _completeStart = _missing = _boundaryAvailable = false; _finishedElapsed = 0;
        if (_restoredBoundary is { } restored)
        { _lootAllowedAt = restored.AddSeconds(5); _boundaryAvailable = _restoredCleanStart; }
        _status = "Warte auf Erkennung"; _building = [];
        foreach (var input in _inputs.OrderBy(i => i.At).ThenBy(i => i.Type == "loot" ? 1 : 0))
        {
            CheckTimeout(input.At, input);
            Apply(input);
        }
        foreach (var (key, previous) in _decisions)
            if (!_building.ContainsKey(key)) _journal.Add(new(Guid.NewGuid(), previous.At, _definition.SpotId,
                "correction", previous.Kind, "Entscheidung durch verspätete Erkennung aufgehoben", previous.RunId, previous.Id));
        foreach (var (key, next) in _building.ToArray())
        {
            if (_decisions.TryGetValue(key, out var old) && old.Detail == next.Detail && old.Kind == next.Kind && old.At == next.At && old.RunId == next.RunId)
                _building[key] = old;
            else
                _journal.Add(_building[key] = next with { Corrects = old?.Id });
        }
        _decisions = _building;
        var current = _finished.ToDictionary(r => r.Run.Id);
        foreach (var (id, old) in _published)
            if (!current.ContainsKey(id)) _pending[id] = (old.StartedAt, old.Run with {
                Outcome = "superseded", Reason = "Verspätete Erkennung hat den Durchlauf korrigiert" });
        foreach (var (id, next) in current)
            if (!_published.TryGetValue(id, out var old) || JsonSerializer.Serialize(old.Run) != JsonSerializer.Serialize(next.Run))
                _pending[id] = next;
        _published.Clear();
        foreach (var (id, run) in current) _published[id] = run;
        Save();
    }

    private void Apply(Input input)
    {
        if (input.Type == "tick") return;
        if (input.Type == "interrupt")
        {
            Finish(input, "aborted", input.Label);
            _status = input.Label + " · Warte auf Erkennung";
            return;
        }
        if (input.Type == "loot")
        {
            if (_start is null && _definition.LootStart)
            {
                if (_lootAllowedAt is { } allowed && input.At < allowed)
                    Decision(input, "loot-blocked", "Trashloot innerhalb der 5-Sekunden-Sperrzeit ignoriert");
                else Begin(input, _boundaryAvailable);
            }
            return;
        }
        if (_definition.FailureMessages.Contains(input.Kind))
        {
            _setupCount = Math.Clamp(_setupCount + _definition.FailureSetupDelta, 0, _definition.SetupTarget);
            AddEvent(input.Kind, input.Label, input.At);
            Finish(input, "aborted", "Eindeutige Fehlschlagmeldung: " + input.Label);
            _status = "Rotation fehlgeschlagen · Warte auf Erkennung";
            return;
        }
        if (_definition.SetupMessages?.Contains(input.Kind) == true)
        { Finish(input, "aborted", "Spot-Aufbau neu gestartet"); _setupCount = 0; return; }
        if (_definition.SetupCountMessages?.Contains(input.Kind) == true)
        { _setupCount = Math.Min(_definition.SetupTarget, _setupCount + 1); _status = $"Aufbau · {_setupCount} / {_definition.SetupTarget} · Warte auf Erkennung"; return; }
        if (_start is not null && _definition.AmbientMessages?.Contains(input.Kind) == true &&
            (_definition.AmbientAfter is not { } after || _visited.Contains(after)) && _position != _definition.Steps.Length - 1)
        {
            AddEvent(input.Kind, input.Label, input.At);
            return;
        }

        // Some spots reuse a combat banner at AFK end; only the final AFK disambiguates it.
        var end = _definition.AfkEndMessages.Contains(input.Kind) &&
            (!_definition.Steps.Any(s => s.Matches(input.Kind)) || _position == _definition.Steps.Length - 1);
        if (end && _definition.AfkEndStartsRun && _start is { } began && _position < 0 && input.At - began < TimeSpan.FromMinutes(1))
        { Decision(input, "duplicate", "Wiederholte Einblendung des Rotationsbeginns ignoriert"); return; }
        if (end)
        {
            if (_start is not null)
            {
                var allRequired = _definition.Steps.Where(Required).All(s => _visited.Contains(s.Id));
                AddEvent("end", input.Label, input.At);
                Finish(input, _completeStart && !_missing && allRequired ? "complete" : "incomplete",
                    _completeStart && !_missing && allRequired ? "Rotation vollständig erkannt" : "Rotation unvollständig erfasst");
            }
            _lootAllowedAt = input.At.AddSeconds(5);
            _boundaryAvailable = true;
            _status = "AFK beendet · Warte auf Erkennung";
            Decision(input, "afk-end", "AFK-Ende erkannt · Lootstart für 5 Sekunden gesperrt");
            // The spot restarts on its own while the player stays: the same banner opens the next rotation.
            if (_definition.AfkEndStartsRun) Begin(input, true);
            return;
        }

        var indices = _definition.Steps.Select((s, i) => (Step: s, Index: i)).Where(s => s.Step.Matches(input.Kind)).ToArray();
        var explicitStart = _definition.StartMessages.Contains(input.Kind);
        if (explicitStart && indices.Length == 0 && _start is { } opened && input.At - opened < TimeSpan.FromSeconds(8) &&
            _inputs.Any(i => i.Id == _runId && i.Type == "message" && i.Kind == input.Kind))
        { Decision(input, "duplicate", "Wiederholte Starteinblendung ignoriert"); return; }
        if (indices.Length == 0 && !explicitStart)
        { Decision(input, "unknown", "Unbekannte Mitteilung · Warte auf Erkennung"); return; }
        if (_start is null) Begin(input, explicitStart);
        // Start-only messages always open a clean run, even inside the loot lockout.
        else if (explicitStart && indices.Length == 0)
        { Finish(input, "aborted", "Neue Startmeldung vor Rotationsabschluss"); Begin(input, true); }
        if (indices.Length == 0) { _setupCount = _definition.SetupTarget; return; }

        // A late sighting of the AFK-end banner that just opened the current step must not open the step after it.
        if (_position >= 0 && _definition.Steps[_position].Matches(input.Kind) && _definition.AfkEndMessages.Contains(input.Kind) &&
            _sectionStart is { } entered && input.At - entered < TimeSpan.FromMinutes(1))
        { Decision(input, "duplicate", "Wiederholte Einblendung ignoriert"); return; }
        var next = Next(indices);
        if (next.Step is null && InferOpening(input, indices)) next = Next(indices);
        // A message that belongs to exactly one step (several orbs of Elion's Tears) may repeat inside its own phase.
        // Where the rotation models the repetition itself (Hermesia's five offerings, Aphrodon's nine waves), one more
        // than modelled means the sequence is off: that must still abort and resynchronise.
        if (next.Step is null && _position >= 0 && indices.Length == 1 && indices[0].Index == _position)
        { Decision(input, "duplicate", "Wiederholung innerhalb der laufenden Phase"); return; }
        if (next.Step is null && _completeStart && !_missing && indices.All(s => s.Step.Requires is not null && !_visited.Contains(s.Step.Requires)))
        { Decision(input, "unconfirmed", "Mitteilung ohne passende optionale Mechanik · letzte bestätigte Phase bleibt erhalten"); return; }
        if (next.Step is null)
        {
            Finish(input, "aborted", "Mitteilung außerhalb der erlaubten Reihenfolge: " + input.Label);
            Begin(input, explicitStart);
            next = indices[0];
            _status = "Synchronisiere … · " + input.Label;
        }
        InferClosing(input, next.Index, input.At);
        Enter(input, next.Index, input.Kind, input.Label, input.At);
        _status = input.Label + (_completeStart && !_missing ? " · erkannt" : " · unvollständig erfasst");
        if (_definition.StartupCounterMessage is { } counter && !_visited.Contains(_definition.AmbientAfter ?? ""))
            _status = $"Startup · {_events.Count(e => e.Kind == counter)} / {_definition.StartupCounterTarget} {_definition.StartupCounterLabel}";
        Decision(input, "phase", _status);
    }

    private bool Required(RotationStep step) => !step.Optional ||
        step.RequiredWhenBranchObserved && step.Requires is { } required && _visited.Contains(required);

    private (RotationStep Step, int Index) Next((RotationStep Step, int Index)[] indices) => indices.FirstOrDefault(s =>
        s.Index > _position && (s.Step.Requires is null || _visited.Contains(s.Step.Requires)));

    private void Enter(Input input, int index, string kind, string label, DateTimeOffset at, bool inferred = false)
    {
        if (_definition.Steps.Skip(_position + 1).Take(index - _position - 1).Any(Required))
        {
            _missing = true;
            Decision(input, "missing", "Erwartete Mitteilungen fehlen · Durchlauf wird nicht gewertet");
        }
        CloseSection(at);
        var step = _definition.Steps[index];
        _position = index; _sectionId = _definition.SectionId(step, kind); _sectionStart = at;
        _visited.Add(step.Id);
        AddEvent(kind, label, at, inferred);
    }

    /// <summary>
    /// The reliable middle of a branch whose opening banner was lost (Event Horizon's distortion without the falling
    /// debris): the opening is filled in half a branch earlier, so the special event still counts. Only where the
    /// middle fits the order, so a late repetition can never invent the next wormhole's mini AFK.
    /// </summary>
    private bool InferOpening(Input input, (RotationStep Step, int Index)[] indices)
    {
        if (_start is not { } start) return false;
        foreach (var (step, index) in indices)
        {
            if (index <= _position || step.Midpoint <= 0 || step.Requires is not { } branch || _visited.Contains(branch)) continue;
            var opening = Array.FindIndex(_definition.Steps, s => s.Id == branch);
            if (opening <= _position || opening > index ||
                _position >= 0 && _definition.Steps.Skip(_position + 1).Take(opening - _position - 1).Any(Required)) continue;
            var openingStep = _definition.Steps[opening];
            var half = SectionAverage(_definition.SectionId(openingStep, openingStep.Messages[0])) ?? step.Midpoint;
            // Never before what was already recorded: the events stay in order.
            var earliest = start.AddSeconds(_events.Count == 0 ? 0 : _events[^1].Seconds);
            var at = input.At.AddSeconds(-half);
            Infer(input, opening, at < earliest ? earliest : at > input.At ? input.At : at);
            return true;
        }
        return false;
    }

    /// <summary>
    /// A branch whose reliable middle was seen is over, but its closing banner was lost: the closing is filled in half
    /// a branch after the middle (or halfway to the message that followed), and the run stays complete.
    /// </summary>
    private bool InferClosing(Input input, int beforeIndex, DateTimeOffset before)
    {
        if (_position < 0 || _sectionStart is not { } entered ||
            _definition.Steps[_position] is not { Midpoint: > 0, Requires: { } branch } middle) return false;
        var closing = _position + 1;
        if (closing >= beforeIndex || closing >= _definition.Steps.Length ||
            _definition.Steps[closing] is not { RequiredWhenBranchObserved: true } step || step.Requires != branch) return false;
        var at = entered.AddSeconds(SectionAverage(_sectionId) ?? middle.Midpoint);
        if (at >= before) at = entered + (before - entered) / 2;
        Infer(input, closing, at);
        return true;
    }

    private void Infer(Input input, int index, DateTimeOffset at)
    {
        var step = _definition.Steps[index];
        Enter(input, index, step.Messages[0], "Nicht gelesen · nachgetragen", at, inferred: true);
        Decision(input, "inferred-" + step.Id, "Nicht gelesene Mitteilung nachgetragen: " + step.Id);
    }

    private double? SectionAverage(string sectionId) =>
        _sectionSamples.TryGetValue(sectionId, out var samples) && samples.Count > 0 ? samples.Average() : null;

    private void Begin(Input input, bool complete)
    {
        _start = _sectionStart = input.At; _runId = input.Id; _position = -1; _sectionId = "startup";
        _events.Clear(); _sections.Clear(); _visited.Clear(); _completeStart = complete; _missing = false;
        _boundaryAvailable = false;
        AddEvent("start", "Rotationsstart", input.At);
        _status = "Warte auf Erkennung";
        Decision(input, "start", complete ? "Rotationsstart erkannt" : "Einstieg ohne bestätigten Rotationsbeginn · unvollständig");
    }

    private void AddEvent(string kind, string label, DateTimeOffset at, bool inferred = false)
    {
        if (_start is not { } start) return;
        _events.Add(new(kind, label, Math.Max(0, (at - start).TotalSeconds), _events.Count(e => e.Kind == kind) + 1)
            { Inferred = inferred });
    }

    private void CloseSection(DateTimeOffset at)
    {
        if (_start is { } start && _sectionStart is { } section && at > section)
            _sections.Add(new(_sectionId, (section - start).TotalSeconds, (at - start).TotalSeconds));
    }

    private void Finish(Input input, string outcome, string reason)
    {
        if (_start is not { } start) return;
        CloseSection(input.At);
        _finishedElapsed = Math.Max(0, (input.At - start).TotalSeconds);
        var run = new RotationRun(_finishedElapsed, _events.ToArray()) { Id = _runId, TimingVersion = 3,
            Outcome = outcome, Reason = reason, Sections = _sections.ToArray(), RecordedAt = input.At };
        _finished.Add((start, run));
        if (run.EligibleForStatistics) RecordSectionSamples(run);
        Decision(input, "finish", outcome + ": " + reason);
        _start = _sectionStart = null; _position = -1;
        _status = reason + " · Warte auf Erkennung";
    }

    private IEnumerable<RotationRun> References() => _history.Concat(_finished.Select(r => r.Run)).Where(r => r.EligibleForStatistics);

    private void RecordSectionSamples(RotationRun run)
    {
        if (run.LegacyImported) return; // Old files lack chronological recording dates.
        foreach (var section in run.Sections)
        {
            if (!_sectionSamples.TryGetValue(section.Id, out var samples)) _sectionSamples[section.Id] = samples = new();
            samples.Enqueue(section.End - section.Start);
            while (samples.Count > 20) samples.Dequeue();
        }
    }

    private double? TimeoutSeconds() => _sectionSamples.TryGetValue(_sectionId, out var samples) && samples.Count >= 3
        ? samples.Average() * 2 : null;

    private void CheckTimeout(DateTimeOffset now, Input input)
    {
        if (_sectionStart is not { } start || TimeoutSeconds() is not { } limit || (now - start).TotalSeconds <= limit) return;
        var at = start.AddSeconds(limit);
        // A special event whose middle was seen is over, not stalled, when its closing banner never came.
        if (InferClosing(input with { At = at }, _definition.Steps.Length, at)) { CheckTimeout(now, input); return; }
        Finish(input with { At = at }, "aborted", $"Abschnitt {_sectionId}: doppelte eigene Durchschnittszeit überschritten");
    }

    private void Decision(Input input, string kind, string detail) =>
        _building[input.Id + ":" + kind] = new(Guid.NewGuid(), input.At, _definition.SpotId, "decision", kind, detail,
            _runId == Guid.Empty ? null : _runId);

    private void ExpireIfNecessary(DateTimeOffset now)
    {
        // Leave the buffered OCR window time to confirm an already visible boundary before expiring it.
        // The recorded timeout itself remains at the exact twice-average boundary.
        if (_sectionStart is { } start && TimeoutSeconds() is { } limit && (now - start).TotalSeconds > limit + 10)
            AddInput("tick", "timeout", "Zeitgrenze geprüft", now.AddSeconds(-10));
    }

    public RotationMonitorSnapshot Snapshot(DateTimeOffset now)
    {
        ExpireIfNecessary(now);
        var references = References().ToArray();
        int? matchedSpecial = null;
        var pool = references;
        if (_definition.SpecialComparison == SpecialEventComparison.ByCount && references.Length > 0)
        {
            // The current rotation's count so far; between rotations the latest one's.
            var count = _definition.SpecialEventCount(_start is not null ? _events : _finished.LastOrDefault().Run?.Events ?? []);
            var counts = references.Select(run => _definition.SpecialEventCount(run.Events)).Distinct().ToArray();
            matchedSpecial = counts.Contains(count) ? count
                : counts.Where(c => c > count).Order().Cast<int?>().FirstOrDefault() ?? counts.Where(c => c < count).Max();
            pool = references.Where(run => _definition.SpecialEventCount(run.Events) == matchedSpecial).ToArray();
        }
        var (best, ideal, sectors) = RotationComparison.Compare(pool);
        // Runs with a special event are also compared separately: the regular pool leaves them out entirely.
        var regular = _definition.MarksSpecialRotations
            ? references.Where(run => _definition.SpecialEventCount(run.Events) == 0).ToArray() : references;
        var withoutSpecial = _definition.MarksSpecialRotations ? RotationComparison.Compare(regular) : (Best: best, Ideal: ideal, Sectors: sectors);
        return new() { Elapsed = _start is { } at ? Math.Max(0, (now - at).TotalSeconds) : _finishedElapsed,
            Synchronized = _start is not null, IsAfk = _position >= 0 && _definition.Steps[_position].Afk,
            TrackingState = _start is null || _position < 0 ? "waiting" : _completeStart && !_missing ? "confirmed" : "partial",
            CurrentPhaseId = _position >= 0 ? _definition.Steps[_position].Id : null,
            LastInterruptionReason = _finished.Where(r => r.Run.Outcome == "aborted").Select(r => r.Run.Reason).LastOrDefault(),
            LootStartAllowedAt = _lootAllowedAt,
            Status = _status, Events = _events.ToArray(), Best = best, Ideal = ideal, SectorBests = sectors,
            Completed = references.Length, Error = _error,
            SmallScarecrows = _definition.SetupCountMessages is null ? null : _setupCount,
            SupportsSpecialEvents = _definition.HasSpecialEvents,
            SpecialEvents = _definition.SpecialEventCount(_events),
            SpecialEventActive = _position >= 0 && (_sectionId.EndsWith("-special", StringComparison.Ordinal) ||
                _definition.Steps[_position].Messages.All(_definition.IsSpecial)),
            WithoutSpecialEvents = new(withoutSpecial.Best, withoutSpecial.Ideal, withoutSpecial.Sectors, regular.Length),
            ComparedSpecialEvents = matchedSpecial };
    }

    private void Save()
    {
        if (_path is null || _cannotOverwrite) return;
        // Retain recent samples as well as record donors. Never substitute the fastest 20 for the latest 20.
        var runs = References().ToArray();
        var keep = runs.TakeLast(100).Concat(runs.OrderBy(r => r.Duration).Take(200))
            .Concat(runs.SelectMany(r => r.Sections).Select(s => s.Id).Distinct()
                .SelectMany(id => runs.Where(r => r.Sections.Any(s => s.Id == id)).TakeLast(20)))
            .Distinct().OrderBy(r => r.RecordedAt).ToArray();
        var json = JsonSerializer.Serialize(keep.Concat(_legacyUnrated));
        if (json == _lastSaved) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            AtomicFile.WriteAllText(_path, json); _lastSaved = json; _error = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { _error = "Rotationsreferenzen nicht gespeichert: " + e.Message; }
    }
}
