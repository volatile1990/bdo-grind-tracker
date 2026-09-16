using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

internal interface IRotationProfileMonitor : IDisposable
{
    void Observe(Bitmap frame, DateTimeOffset at);
    void Interrupt(string status);
    RotationMonitorSnapshot Snapshot(DateTimeOffset now);
    (DateTimeOffset StartedAt, RotationRun Run)[] DrainCompleted() => [];
    Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Each spot owns its message recognition, rotation rules and records.</summary>
internal static partial class RotationProfiles
{
    private static readonly IReadOnlyDictionary<string, Func<IRotationProfileMonitor>> Factories =
        new Dictionary<string, Func<IRotationProfileMonitor>>(StringComparer.Ordinal)
        {
            [LootSpotCatalog.HermesiaId] = () => new HermesiaRotationMonitor(HermesiaRotationTracker.DefaultPath),
            [LootSpotCatalog.AphrodonId] = () => new BufferedRotationProfileMonitor(
                new AphrodonRotationTracker(AphrodonRotationTracker.DefaultPath), RotationMessageProfile.Aphrodon),
        };

    internal static IRotationProfileMonitor? Create(string? spotId) =>
        spotId is not null && Factories.TryGetValue(spotId, out var create) ? create() : null;
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
    private void CollectCompleted()
    {
        if (_profile is null || _spotId is null) return;
        _sessionRotations.AddRange(_profile.DrainCompleted().Select(r => new SessionRotation(_spotId, r.StartedAt, r.Run)));
    }
    internal SessionRotation[] ExportSession()
    {
        lock (_sync) { CollectCompleted(); return _sessionRotations.ToArray(); }
    }
    internal void RestoreSession(IEnumerable<SessionRotation> rotations)
    {
        lock (_sync)
        {
            _profile?.Interrupt("Neue Session · warte auf erstes Ereignis");
            _profile?.DrainCompleted();
            DisposeCandidates();
            _sessionRotations.Clear();
            _sessionRotations.AddRange(rotations);
        }
    }

    // The spot is only known after its first loot, but the first rotation's opening messages
    // appear before that drop. Until then every profile watches, and the detected spot keeps its own.
    private readonly Dictionary<string, IRotationProfileMonitor?> _candidates = new(StringComparer.Ordinal);

    internal RotationMonitor(Func<string?, IRotationProfileMonitor?>? create = null) => _create = create ?? RotationProfiles.Create;
    private void Select(string? spotId)
    {
        if (_disposed || _spotId == spotId) return;
        _profile?.Interrupt("Spotwechsel");
        CollectCompleted();
        _profile?.Dispose();
        _spotId = spotId;
        _profile = spotId is not null && _candidates.Remove(spotId, out var candidate) && candidate is not null
            ? candidate : _create(spotId);
        DisposeCandidates();
    }
    internal void Observe(Bitmap frame, DateTimeOffset at, string? spotId)
    {
        lock (_sync)
        {
            if (_disposed) return;
            Select(spotId);
            if (spotId is not null) { _profile?.Observe(frame, at); return; }
            foreach (var candidateSpot in RotationProfiles.SupportedSpotIds)
            {
                if (!_candidates.TryGetValue(candidateSpot, out var candidate))
                {
                    candidate = _create(candidateSpot);
                    // A factory may hand out one shared instance; it must watch each frame only once.
                    _candidates[candidateSpot] = candidate is not null && _candidates.ContainsValue(candidate) ? null : candidate;
                }
                _candidates[candidateSpot]?.Observe(frame, at);
            }
        }
    }
    private void DisposeCandidates()
    {
        foreach (var candidate in _candidates.Values) candidate?.Dispose();
        _candidates.Clear();
    }
    internal RotationMonitorSnapshot Snapshot(DateTimeOffset now, string? spotId)
    {
        lock (_sync)
        {
            Select(spotId);
            CollectCompleted();
            var snapshot = _profile?.Snapshot(now);
            var presented = RotationProfiles.Present(spotId, snapshot is null ? null : snapshot with { SpotId = spotId });
            var rotations = _sessionRotations.Where(rotation => rotation.SpotId == spotId).OrderBy(rotation => rotation.StartedAt).ToArray();
            // The running rotation's start also ends the walk back after the latest completed one.
            DateTimeOffset? running = presented.Synchronized ? now.AddSeconds(-presented.Elapsed) : null;
            return presented with
            {
                SessionRotations = rotations.Select((rotation, index) => new SessionRotationTiming(rotation.Run.Duration,
                    WalkBack(rotation, index + 1 < rotations.Length ? rotations[index + 1].StartedAt : running))).ToArray(),
            };
        }
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
