namespace BdoGrindTracker.App.Overlay;

public sealed record OverlayRuntimeState
{
    public bool Previewing { get; init; }
    public bool IsVisible { get; init; }
    public string Status { get; init; } = "Overlay ausgeschaltet.";
    public bool IsError { get; init; }
    public string? TargetMonitorLabel { get; init; }
    public string? HotkeyStatus { get; init; }
}

public sealed record OverlaySaveResult(string? Error = null)
{
    public bool Succeeded => Error is null;
}

internal interface IOverlayService : IDisposable
{
    event Action? Changed;
    OverlaySettings Settings { get; }
    string? LoadError { get; }
    Task<OverlaySaveResult> ReloadAsync();
    OverlayHotkeySettings Hotkeys { get; }
    string? HotkeyStatus { get; }
    Task<OverlaySaveResult> SaveHotkeysAsync(OverlayHotkeySettings hotkeys);
    Task<OverlaySaveResult> ToggleAllOverlaysAsync();
    Task<OverlaySaveResult> ToggleAllInteractionAsync();
    void UpdateHotkeyRuntime(string? status);
    OverlayRuntimeState State { get; }
    OverlaySnapshot Snapshot { get; }
    void RefreshClock();
    IReadOnlyList<OverlayInstance> Overlays { get; }
    string SelectedOverlayId { get; }
    OverlayRuntimeState GetState(string id);
    Task<OverlaySaveResult> SelectOverlayAsync(string id);
    Task<OverlaySaveResult> CreateOverlayAsync(string name, string? duplicateId = null);
    Task<OverlaySaveResult> RenameOverlayAsync(string id, string name);
    Task<OverlaySaveResult> DeleteOverlayAsync(string id);
    IReadOnlyList<OverlayTemplate> Templates { get; }
    string? TemplateError { get; }
    Task<OverlaySaveResult> SaveTemplateAsync(string name, OverlaySettings layout, string? replaceId = null);
    Task<OverlaySaveResult> DeleteTemplateAsync(string id);
    Task<OverlaySaveResult> SaveAsync(OverlaySettings settings);
    Task<OverlaySaveResult> SaveAsync(string id, OverlaySettings settings);
    Task<OverlaySaveResult> SavePositionAsync(double x, double y, double? width = null, double? height = null);
    Task<OverlaySaveResult> SavePositionAsync(string id, double x, double y, double? width = null, double? height = null);
    Task SetPreviewAsync(bool enabled);
    Task SetPreviewAsync(string id, bool enabled);
    Task ResetPositionAsync();
    Task ToggleTrackingAsync();
    Task NewSessionAsync() => Task.CompletedTask;
    void UpdateRuntime(OverlayRuntimeState state);
    void UpdateRuntime(string id, OverlayRuntimeState state);
}
