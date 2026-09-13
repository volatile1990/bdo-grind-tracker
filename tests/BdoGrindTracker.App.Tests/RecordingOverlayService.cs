using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

/// <summary>Real in-memory overlay behavior, with a controllable boundary for persistence interaction tests.</summary>
internal sealed class RecordingOverlayService : IOverlayService
{
    private readonly PreviewTrackerSession _tracker = new(empty: true);
    private readonly OverlayService _inner;
    public RecordingOverlayService(OverlaySettings? settings = null)
    {
        _inner = new(_tracker);
        _inner.SaveAsync(OverlayLayout.Normalize(settings)).GetAwaiter().GetResult();
    }
    public event Action? Changed { add => _inner.Changed += value; remove => _inner.Changed -= value; }
    public List<OverlaySettings> Saves { get; } = [];
    public Func<Task<OverlaySaveResult>> SaveResponse { get; init; } = () => Task.FromResult(new OverlaySaveResult());
    public OverlaySettings Settings => _inner.Settings;
    public string? LoadError => _inner.LoadError;
    public OverlayHotkeySettings Hotkeys => _inner.Hotkeys;
    public string? HotkeyStatus => _inner.HotkeyStatus;
    public OverlayRuntimeState State => _inner.State;
    public OverlaySnapshot Snapshot => _inner.Snapshot;
    public IReadOnlyList<OverlayInstance> Overlays => _inner.Overlays;
    public string SelectedOverlayId => _inner.SelectedOverlayId;
    public IReadOnlyList<OverlayTemplate> Templates => _inner.Templates;
    public string? TemplateError => _inner.TemplateError;
    public Task<OverlaySaveResult> SaveAsync(OverlaySettings settings) => SaveAsync(SelectedOverlayId, settings);
    public async Task<OverlaySaveResult> SaveAsync(string id, OverlaySettings settings)
    {
        Saves.Add(settings);
        var result = await SaveResponse();
        return result.Succeeded ? await _inner.SaveAsync(id, settings) : result;
    }
    public Task<OverlaySaveResult> ReloadAsync() => _inner.ReloadAsync();
    public Task<OverlaySaveResult> SaveHotkeysAsync(OverlayHotkeySettings hotkeys) => _inner.SaveHotkeysAsync(hotkeys);
    public Task<OverlaySaveResult> ToggleAllOverlaysAsync() => _inner.ToggleAllOverlaysAsync();
    public Task<OverlaySaveResult> ToggleAllInteractionAsync() => _inner.ToggleAllInteractionAsync();
    public void UpdateHotkeyRuntime(string? status) => _inner.UpdateHotkeyRuntime(status);
    public void RefreshClock() => _inner.RefreshClock();
    public OverlayRuntimeState GetState(string id) => _inner.GetState(id);
    public Task<OverlaySaveResult> SelectOverlayAsync(string id) => _inner.SelectOverlayAsync(id);
    public Task<OverlaySaveResult> CreateOverlayAsync(string name, string? duplicateId = null) => _inner.CreateOverlayAsync(name, duplicateId);
    public Task<OverlaySaveResult> RenameOverlayAsync(string id, string name) => _inner.RenameOverlayAsync(id, name);
    public Task<OverlaySaveResult> DeleteOverlayAsync(string id) => _inner.DeleteOverlayAsync(id);
    public Task<OverlaySaveResult> SaveTemplateAsync(string name, OverlaySettings layout, string? replaceId = null) => _inner.SaveTemplateAsync(name, layout, replaceId);
    public Task<OverlaySaveResult> DeleteTemplateAsync(string id) => _inner.DeleteTemplateAsync(id);
    public Task<OverlaySaveResult> SavePositionAsync(double x, double y, double? width = null, double? height = null) => _inner.SavePositionAsync(x, y, width, height);
    public Task<OverlaySaveResult> SavePositionAsync(string id, double x, double y, double? width = null, double? height = null) => _inner.SavePositionAsync(id, x, y, width, height);
    public Task SetPreviewAsync(bool enabled) => _inner.SetPreviewAsync(enabled);
    public Task SetPreviewAsync(string id, bool enabled) => _inner.SetPreviewAsync(id, enabled);
    public Task ResetPositionAsync() => _inner.ResetPositionAsync();
    public Task ToggleTrackingAsync() => _inner.ToggleTrackingAsync();
    public void UpdateRuntime(OverlayRuntimeState state) => _inner.UpdateRuntime(state);
    public void UpdateRuntime(string id, OverlayRuntimeState state) => _inner.UpdateRuntime(id, state);
    public void Dispose() { _inner.Dispose(); _tracker.DisposeAsync().GetAwaiter().GetResult(); }
}
