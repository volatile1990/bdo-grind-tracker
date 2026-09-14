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
            _sessionRotations.Clear();
            _sessionRotations.AddRange(rotations);
        }
    }

    internal RotationMonitor(Func<string?, IRotationProfileMonitor?>? create = null) => _create = create ?? RotationProfiles.Create;
    private void Select(string? spotId)
    {
        if (_disposed || _spotId == spotId) return;
        _profile?.Interrupt("Spotwechsel");
        CollectCompleted();
        _profile?.Dispose();
        _spotId = spotId;
        _profile = _create(spotId);
    }
    internal void Observe(Bitmap frame, DateTimeOffset at, string? spotId)
    {
        lock (_sync) { if (_disposed) return; Select(spotId); _profile?.Observe(frame, at); }
    }
    internal RotationMonitorSnapshot Snapshot(DateTimeOffset now, string? spotId)
    {
        lock (_sync)
        {
            Select(spotId);
            var snapshot = _profile?.Snapshot(now);
            return RotationProfiles.Present(spotId, snapshot is null ? null : snapshot with { SpotId = spotId });
        }
    }
    internal void Interrupt(string status = "Tracking pausiert · warte auf Rotationsstart")
    { lock (_sync) _profile?.Interrupt(status); }
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
    { lock (_sync) { _disposed = true; _profile?.Dispose(); _profile = null; } }
}
