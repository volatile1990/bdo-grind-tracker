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
    IReadOnlyList<OverlayTemplate> Templates => Array.Empty<OverlayTemplate>();
    string? TemplateError => null;
    Task<OverlaySaveResult> SaveTemplateAsync(string name, OverlaySettings layout, string? replaceId = null) =>
        Task.FromResult(new OverlaySaveResult("Eigene Vorlagen sind hier nicht verfügbar."));
    Task<OverlaySaveResult> DeleteTemplateAsync(string id) =>
        Task.FromResult(new OverlaySaveResult("Eigene Vorlagen sind hier nicht verfügbar."));
    Task<OverlaySaveResult> SaveAsync(OverlaySettings settings);
    Task<OverlaySaveResult> SavePositionAsync(double x, double y, double? width = null, double? height = null);
    Task SetPreviewAsync(bool enabled);
    Task ResetPositionAsync();
    Task ToggleTrackingAsync();
    void UpdateRuntime(OverlayRuntimeState state);
}
