using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

internal interface IRotationProfileMonitor : IDisposable
{
    void Observe(Bitmap frame, DateTimeOffset at);
    void Interrupt(string status);
    /// <summary>A message recognized outside this monitor's own capture, replayed at the time it appeared.</summary>
    void ObserveMessage(string kind, string label, DateTimeOffset at) { }
    RotationMonitorSnapshot Snapshot(DateTimeOffset now);
    (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted() => [];
    Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    void AttachDiagnostics(RotationDiagnosticRecording? recording, string spotId) { }
    void ObserveLoot(DateTimeOffset at) { }
    RotationTimelineEntry[] DrainTimeline() => [];
    (DateTimeOffset StartedAt, RotationRun Run)? ActiveRun() => null;
    void RestoreBoundary(DateTimeOffset at, bool cleanStart) { }
}

/// <summary>Each spot owns its message recognition, rotation rules and records.</summary>
internal static partial class RotationProfiles
{
    private static readonly IReadOnlyDictionary<string, Func<IRotationProfileMonitor>> Factories =
        new Dictionary<string, Func<IRotationProfileMonitor>>(StringComparer.Ordinal)
        {
            [LootSpotCatalog.HermesiaId] = () => CreateShared(LootSpotCatalog.HermesiaId, RotationMessageProfile.Hermesia),
            [LootSpotCatalog.AphrodonId] = () => new BufferedRotationProfileMonitor(
                new RotationPlatform(RotationDefinition.Aphrodon, RotationPlatform.DefaultPath(LootSpotCatalog.AphrodonId)), RotationMessageProfile.Aphrodon),
            [LootSpotCatalog.MagaiaId] = () => new BufferedRotationProfileMonitor(
                new RotationPlatform(RotationDefinition.Magaia, RotationPlatform.DefaultPath(LootSpotCatalog.MagaiaId)), RotationMessageProfile.Magaia),
            [LootSpotCatalog.EventHorizonId] = () => new BufferedRotationProfileMonitor(
                new RotationPlatform(RotationDefinition.EventHorizon, RotationPlatform.DefaultPath(LootSpotCatalog.EventHorizonId)), RotationMessageProfile.EventHorizon),
        };

    private static IRotationProfileMonitor CreateShared(string spot, RotationMessageProfile messages) =>
        new BufferedRotationProfileMonitor(new RotationPlatform(RotationDefinition.For(spot), RotationPlatform.DefaultPath(spot)), messages);

    internal static IRotationProfileMonitor? Create(string? spotId) =>
        spotId is not null && Factories.TryGetValue(spotId, out var create) ? create() : null;

    private static readonly IReadOnlyDictionary<string, RotationMessageProfile> Recognition =
        new Dictionary<string, RotationMessageProfile>(StringComparer.Ordinal)
        {
            [LootSpotCatalog.HermesiaId] = RotationMessageProfile.Hermesia,
            [LootSpotCatalog.AphrodonId] = RotationMessageProfile.Aphrodon,
            [LootSpotCatalog.EventHorizonId] = RotationMessageProfile.EventHorizon,
            [LootSpotCatalog.MagaiaId] = RotationMessageProfile.Magaia,
        };

    /// <summary>The spot's message recognition, also used while watching for a rotation start before a session.</summary>
    internal static RotationMessageProfile? Messages(string? spotId) =>
        spotId is not null && Recognition.TryGetValue(spotId, out var profile) ? profile : null;
}

/// <summary>Follows the tracker's selected/detected spot without leaking a previous spot's run.</summary>
internal sealed class RotationMonitor : IDisposable
{
    private readonly object _sync = new();
    private readonly Func<string?, IRotationProfileMonitor?> _create;
    private IRotationProfileMonitor? _profile;
    private string? _spotId;
    private bool _disposed;
    private readonly List<SessionRotation> _sessionRotations = [];
    private readonly List<RotationTimelineEntry> _timeline = [];
    private readonly Dictionary<Guid, (int Revision, int Quantity)> _lootSeen = [];
    private readonly Dictionary<Guid, RotationTimelineEntry> _lastLootEntry = [];
    private RotationDiagnosticRecording? _diagnostics;
    private void CollectCompleted()
    {
        if (_profile is null || _spotId is null) return;
        foreach (var (startedAt, run) in _profile.DrainCompleted())
        {
            if (run.Id != Guid.Empty) _sessionRotations.RemoveAll(r => r.Run.Id == run.Id);
            _sessionRotations.Add(new SessionRotation(_spotId, startedAt, run));
            _diagnostics?.Completed(_spotId, startedAt, run);
        }
        foreach (var entry in _profile.DrainTimeline()) AppendTimeline(entry);
    }
    private void AppendTimeline(RotationTimelineEntry entry)
    {
        _timeline.Add(entry);
        if (entry.LootEventId is { } id) _lastLootEntry[id] = entry;
    }
    /// <summary>Records the current and every later profile, including the provisional ones before spot detection.</summary>
    internal void AttachDiagnostics(RotationDiagnosticRecording? recording)
    {
        lock (_sync)
        {
            _diagnostics = recording;
            if (_spotId is not null) _profile?.AttachDiagnostics(recording, _spotId);
            foreach (var (spot, candidate) in _candidates) candidate?.AttachDiagnostics(recording, spot);
        }
    }
    internal SessionRotation[] ExportSession()
    {
        lock (_sync)
        {
            CollectCompleted();
            var active = _profile?.ActiveRun();
            return active is { } run && _spotId is { } spot
                ? [.. _sessionRotations.Where(r => r.Run.Id != run.Run.Id), new(spot, run.StartedAt, run.Run)]
                : _sessionRotations.ToArray();
        }
    }
    internal RotationTimelineEntry[] ExportTimeline()
    {
        lock (_sync) { CollectCompleted(); return _timeline.ToArray(); }
    }
    internal void RestoreSession(IEnumerable<SessionRotation> rotations, IEnumerable<RotationTimelineEntry>? timeline = null)
    {
        var restored = rotations.ToArray();
        lock (_sync)
        {
            _profile?.Interrupt("Neue Session · warte auf erstes Ereignis");
            _profile?.DrainCompleted();
            _profile?.Dispose(); _profile = null; _spotId = null;
            DisposeCandidates();
            _sessionRotations.Clear();
            _sessionRotations.AddRange(restored.Select(r => r.Run.Outcome == "active"
                ? r with { Run = r.Run with { Outcome = "aborted", Reason = "Session nach Neustart wiederhergestellt · Warte auf Erkennung" } } : r));
            _timeline.Clear(); _lootSeen.Clear(); _lastLootEntry.Clear();
            foreach (var entry in timeline ?? []) AppendTimeline(entry);
            foreach (var run in restored.Where(r => r.Run.Outcome == "active"))
                AppendTimeline(new(Guid.NewGuid(), run.StartedAt.AddSeconds(run.Run.Duration), run.SpotId, "decision", "finish",
                    "aborted: Session nach Neustart wiederhergestellt · Warte auf Erkennung", run.Run.Id));
            foreach (var entry in _timeline.Where(e => e.LootEventId is not null && e.Quantity is not null))
                _lootSeen[entry.LootEventId!.Value] = (entry.Revision, (int)entry.Quantity!.Value);
        }
    }

    // The spot is only known after its first loot, but the first rotation's opening messages
    // appear before that drop. Until then every profile watches, and the detected spot keeps its own.
    private readonly Dictionary<string, IRotationProfileMonitor?> _candidates = new(StringComparer.Ordinal);

    internal RotationMonitor(Func<string?, IRotationProfileMonitor?>? create = null) => _create = create ?? RotationProfiles.Create;
    private void Select(string? spotId)
    {
        // A caller that does not know the spot yet says nothing about the one already detected: only a different
        // spot is a spot change. Falling back to unknown would throw away the running rotation.
        if (_disposed || _spotId == spotId || spotId is null && _spotId is not null) return;
        _profile?.Interrupt("Spotwechsel");
        CollectCompleted();
        _profile?.Dispose();
        _spotId = spotId;
        IRotationProfileMonitor? candidate = null;
        var adopted = spotId is not null && _candidates.Remove(spotId, out candidate) && candidate is not null;
        _profile = adopted ? candidate : _create(spotId);
        DisposeCandidates();
        if (spotId is null) return;
        _profile?.AttachDiagnostics(_diagnostics, spotId);
        if (!adopted && _profile is not null)
        {
            var corrected = _timeline.Where(e => e.Corrects is not null).Select(e => e.Corrects!.Value).ToHashSet();
            var boundary = _timeline.Where(e => e.SpotId == spotId && e.Type == "decision" && e.Kind == "afk-end" && !corrected.Contains(e.Id))
                .MaxBy(e => e.At);
            if (boundary is not null) _profile.RestoreBoundary(boundary.At,
                !_sessionRotations.Any(r => r.SpotId == spotId && r.StartedAt >= boundary.At));
        }
        _diagnostics?.Note(spotId, DateTimeOffset.UtcNow, "spot", _profile is null ? "Spot ohne Rotationsprofil"
            : adopted ? "Spot erkannt · vorläufig erfasste Meldungen übernommen" : "Spot erkannt · Erkennung gestartet");
    }
    internal void Observe(Bitmap frame, DateTimeOffset at, string? spotId)
    {
        lock (_sync)
        {
            if (_disposed) return;
            Select(spotId);
            if (_spotId is not null) { _profile?.Observe(frame, at); return; }
            foreach (var candidate in Candidates(at)) candidate.Observe(frame, at);
        }
    }

    /// <summary>
    /// A rotation start the automatic grind detection recognized before this session existed. The spot is usually
    /// still unknown at that moment, so the sighting goes to its provisional profile and the first rotation is
    /// measured from its banner instead of from the first drop that started the session.
    /// </summary>
    internal void ObserveRotationStart(RotationStartSighting sighting)
    {
        lock (_sync)
        {
            if (_disposed || _spotId is not null && _spotId != sighting.SpotId) return;
            var profile = _profile;
            if (_spotId != sighting.SpotId)
            {
                foreach (var _ in Candidates(sighting.At)) { } // Every provisional profile exists after this.
                profile = _candidates.GetValueOrDefault(sighting.SpotId);
            }
            profile?.ObserveMessage(sighting.Kind, sighting.Label, sighting.At);
            _diagnostics?.Note(sighting.SpotId, sighting.At, "auto-start",
                "Rotationsstart vor dem Sessionstart erkannt · " + sighting.Label);
        }
    }

    /// <summary>A new loot arrival, which starts the rotation of loot-started spots such as Event Horizon.</summary>
    internal void ObserveLoot(DateTimeOffset at, string? spotId)
    {
        lock (_sync)
        {
            if (_disposed) return;
            Select(spotId);
            // The first loot usually arrives before the spot is known; every provisional profile sees it.
            if (_spotId is not null) _profile?.ObserveLoot(at);
            else foreach (var candidate in Candidates(at)) candidate.ObserveLoot(at);
        }
    }

    internal void ObserveLootEvents(IEnumerable<LootEventView> events, string? spotId)
    {
        lock (_sync)
        {
            foreach (var loot in events)
            {
                var value = (loot.Revision, loot.TotalDropQuantity ?? loot.Quantity);
                if (_lootSeen.TryGetValue(loot.EventId, out var old) && (value.Revision < old.Revision || value == old)) continue;
                var previous = _lastLootEntry.GetValueOrDefault(loot.EventId);
                _lootSeen[loot.EventId] = value;
                AppendTimeline(new(Guid.NewGuid(), loot.DetectedAt, spotId, "loot", previous is null ? "drop" : "correction",
                    previous is null ? "Loot erkannt" : "Lootmenge korrigiert", Corrects: previous?.Id,
                    ItemName: loot.ItemName, Quantity: value.Item2, LootEventId: loot.EventId, Revision: loot.Revision));
            }
        }
    }

    private IEnumerable<IRotationProfileMonitor> Candidates(DateTimeOffset at)
    {
        if (_candidates.Count == 0) _diagnostics?.Note(null, at, "candidates", "Spot noch unbekannt · alle Rotationsprofile lesen vorläufig mit");
        foreach (var candidateSpot in RotationProfiles.SupportedSpotIds)
        {
            if (!_candidates.TryGetValue(candidateSpot, out var candidate))
            {
                candidate = _create(candidateSpot);
                // A factory may hand out one shared instance; it must watch each frame only once.
                _candidates[candidateSpot] = candidate is not null && _candidates.ContainsValue(candidate) ? null : candidate;
                _candidates[candidateSpot]?.AttachDiagnostics(_diagnostics, candidateSpot);
            }
            if (_candidates[candidateSpot] is { } watching) yield return watching;
        }
    }
    private void DisposeCandidates()
    {
        foreach (var candidate in _candidates.Values) candidate?.Dispose();
        _candidates.Clear();
    }
    /// <param name="includeSpecialEvents">False compares only with rotations that had no special event.</param>
    internal RotationMonitorSnapshot Snapshot(DateTimeOffset now, string? spotId, bool includeSpecialEvents = true)
    {
        lock (_sync)
        {
            Select(spotId);
            CollectCompleted();
            var snapshot = _profile?.Snapshot(now);
            var presented = RotationProfiles.Present(spotId, snapshot is null ? null : snapshot with { SpotId = spotId });
            CollectCompleted();
            var definition = RotationDefinition.Find(spotId);
            // Failed attempts and the running rotation belong on the session timeline as well; only complete ones
            // become statistics.
            var active = _spotId == spotId && _profile?.ActiveRun() is { } run && run.Run.Duration > 0
                ? new SessionRotation(spotId!, run.StartedAt, run.Run) : null;
            var rotations = _sessionRotations.Where(rotation => rotation.SpotId == spotId &&
                rotation.Run.Outcome != "superseded" && rotation.Run.Duration > 0 && rotation.Run.Id != active?.Run.Id)
                .Append(active).OfType<SessionRotation>()
                .OrderBy(rotation => rotation.StartedAt).ToArray();
            // The running rotation's start also ends the walk back after the latest completed one.
            DateTimeOffset? running = presented.Synchronized ? now.AddSeconds(-presented.Elapsed) : null;
            if (!includeSpecialEvents && presented.WithoutSpecialEvents is { } regular)
                presented = presented with { Best = regular.Best, Ideal = regular.Ideal, SectorBests = regular.SectorBests,
                    Completed = regular.Completed, ExcludesSpecialEvents = true };
            return presented with
            {
                SessionRotations = rotations.Select((rotation, index) => new SessionRotationTiming(rotation.Run.Duration,
                    rotation.Run.EligibleForStatistics ? WalkBack(rotation, rotations.Skip(index + 1)
                        .FirstOrDefault(next => next.Run.EligibleForStatistics)?.StartedAt ?? running) : null,
                    definition is { MarksSpecialRotations: true } && definition.SpecialEventCount(rotation.Run.Events) > 0,
                    rotation.StartedAt, definition?.SpecialEventSeconds(rotation.Run.Events) ?? [],
                    rotation.Run.Id, Outcome: rotation.Run.Outcome, Events: rotation.Run.Events)).ToArray(),
                SessionSpecialEvents = SessionSpecialEvents(spotId),
            };
        }
    }

    // Every special event observed at this spot counts, whether its rotation completed, aborted or is still running.
    // A session that moved between spots keeps each spot's count with that spot.
    private int SessionSpecialEvents(string? spotId)
    {
        if (RotationDefinition.Find(spotId) is not { } definition) return 0;
        var active = _spotId == spotId && _profile?.ActiveRun() is { } run ? new SessionRotation(spotId!, run.StartedAt, run.Run) : null;
        return _sessionRotations.Where(rotation => rotation.SpotId == spotId &&
                rotation.Run.Outcome != "superseded" && rotation.Run.Id != active?.Run.Id)
            .Append(active).OfType<SessionRotation>()
            .Sum(rotation => definition.SpecialEventCount(rotation.Run.Events));
    }

    // Longer gaps are breaks, failed attempts or interruptions rather than the way back to the start.
    internal static readonly TimeSpan MaximumWalkBack = TimeSpan.FromMinutes(2);

    private static double? WalkBack(SessionRotation rotation, DateTimeOffset? nextStart)
    {
        if (nextStart is not { } next) return null;
        var seconds = (next - rotation.StartedAt).TotalSeconds - rotation.Run.Duration;
        // Buffered confirmations can place the next start a fraction of a second before the end.
        return seconds >= -1 && seconds <= MaximumWalkBack.TotalSeconds ? Math.Max(0, seconds) : null;
    }
    internal void Interrupt(string status = "Tracking pausiert · warte auf Rotationsstart")
    {
        lock (_sync)
        {
            _profile?.Interrupt(status);
            foreach (var candidate in _candidates.Values) candidate?.Interrupt(status);
        }
    }
    internal async Task FlushAsync(TimeSpan? timeout = null)
    {
        IRotationProfileMonitor? profile;
        lock (_sync) profile = _disposed ? null : _profile;
        if (profile is null) return;
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        try { await profile.FlushAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        { /* A stuck optional OCR worker must never prevent pause or shutdown. */ }
    }
    public void Dispose()
    { lock (_sync) { _disposed = true; _profile?.Dispose(); _profile = null; DisposeCandidates(); } }
}
