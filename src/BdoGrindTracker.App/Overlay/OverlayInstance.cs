namespace BdoGrindTracker.App.Overlay;

/// <summary>A desktop overlay owns its layout and window preferences independently of editor selection.</summary>
public sealed record OverlayInstance
{
    public const string DefaultId = "default";
    public string Id { get; init; } = DefaultId;
    public string Name { get; init; } = "Overlay 1";
    public OverlaySettings Settings { get; init; } = new();

    public static string NormalizeName(string? name)
    {
        var normalized = (name ?? "").Trim();
        if (normalized.Length is < 1 or > 60 || normalized.Any(char.IsControl))
            throw new ArgumentException("Bitte einen Overlay-Namen mit 1 bis 60 Zeichen eingeben.");
        return normalized;
    }
}

internal sealed record OverlayCollectionSettings
{
    public int Version { get; init; } = 2;
    public string SelectedOverlayId { get; init; } = OverlayInstance.DefaultId;
    public OverlayHotkeySettings? Hotkeys { get; init; }
    public IReadOnlyList<OverlayInstance> Overlays { get; init; } = Array.AsReadOnly(new[] { new OverlayInstance() });
}
