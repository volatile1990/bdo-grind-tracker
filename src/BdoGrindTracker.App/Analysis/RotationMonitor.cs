using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

internal interface IRotationProfileMonitor : IDisposable
{
    void Observe(Bitmap frame, DateTimeOffset at);
    void Interrupt(string status);
    RotationMonitorSnapshot Snapshot(DateTimeOffset now);
}

/// <summary>Each spot owns its message recognition, rotation rules and records.</summary>
internal static class RotationProfiles
{
    private static readonly IReadOnlyDictionary<string, Func<IRotationProfileMonitor>> Factories =
        new Dictionary<string, Func<IRotationProfileMonitor>>(StringComparer.Ordinal)
        {
            [LootSpotCatalog.HermesiaId] = () => new HermesiaRotationMonitor(HermesiaRotationTracker.DefaultPath),
        };

    internal static bool Supports(string? spotId) => spotId is not null && Factories.ContainsKey(spotId);
    internal static IRotationProfileMonitor? Create(string? spotId) =>
        spotId is not null && Factories.TryGetValue(spotId, out var create) ? create() : null;
    internal static RotationMonitorSnapshot Present(string? spotId, RotationMonitorSnapshot? snapshot = null)
    {
        var supported = Supports(spotId);
        var current = supported && snapshot is not null && snapshot.SpotId == spotId ? snapshot : new RotationMonitorSnapshot();
        return current with
        {
            SpotId = spotId, SpotName = spotId is null ? "Noch kein Spot erkannt" : Presentation.SpotName(spotId),
            HasProfile = supported,
            Status = spotId is null ? "Warte auf Spot-Erkennung" : !supported ?
                "Für diesen Spot sind noch keine Rotationsdaten hinterlegt" : current.Status,
        };
    }
}

/// <summary>Follows the tracker's selected/detected spot without leaking a previous spot's run.</summary>
internal sealed class RotationMonitor : IDisposable
{
    private readonly object _sync = new();
    private readonly Func<string?, IRotationProfileMonitor?> _create;
    private IRotationProfileMonitor? _profile;
    private string? _spotId;
    private bool _disposed;

    internal RotationMonitor(Func<string?, IRotationProfileMonitor?>? create = null) => _create = create ?? RotationProfiles.Create;
    private void Select(string? spotId)
    {
        if (_disposed || _spotId == spotId) return;
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
    public void Dispose()
    { lock (_sync) { _disposed = true; _profile?.Dispose(); _profile = null; } }
}
