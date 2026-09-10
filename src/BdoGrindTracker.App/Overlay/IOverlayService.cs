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
    OverlayRuntimeState State { get; }
    OverlaySnapshot Snapshot { get; }
    Task<OverlaySaveResult> SaveAsync(OverlaySettings settings);
    Task<OverlaySaveResult> SavePositionAsync(double x, double y, double? width = null, double? height = null);
    Task SetPreviewAsync(bool enabled);
    Task ResetPositionAsync();
    Task ToggleTrackingAsync();
    void UpdateRuntime(OverlayRuntimeState state);
}
