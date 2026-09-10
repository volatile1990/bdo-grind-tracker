namespace BdoGrindTracker.App.Overlay;

public sealed record OverlayTemplate
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public OverlaySettings Layout { get; init; } = new();

    /// <summary>Applies only layout and appearance; window behavior belongs to the current overlay.</summary>
    public OverlaySettings ApplyTo(OverlaySettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var layout = CopyLayout(Layout);
        return current with
        {
            Width = layout.Width, Height = layout.Height, Widgets = layout.Widgets,
            Scale = layout.Scale, BackgroundOpacity = layout.BackgroundOpacity,
            ShowBorder = layout.ShowBorder, SnapToGrid = layout.SnapToGrid,
        };
    }

    internal static OverlayTemplate Create(string name, OverlaySettings layout, string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        Name = NormalizeName(name),
        Layout = CopyLayout(layout),
    };

    internal static string NormalizeName(string name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("Bitte einen Namen für die Vorlage eingeben.");
        if (normalized.Length > 60) throw new ArgumentException("Der Vorlagenname darf höchstens 60 Zeichen enthalten.");
        if (normalized.Any(char.IsControl)) throw new ArgumentException("Der Vorlagenname enthält ungültige Zeichen.");
        return normalized;
    }

    internal static OverlaySettings CopyLayout(OverlaySettings layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var normalized = OverlayLayout.Normalize(new()
        {
            Width = layout.Width, Height = layout.Height, Widgets = layout.Widgets,
            Scale = layout.Scale, BackgroundOpacity = layout.BackgroundOpacity,
            ShowBorder = layout.ShowBorder, SnapToGrid = layout.SnapToGrid,
        });
        return normalized with
        {
            Widgets = Array.AsReadOnly(normalized.Widgets.Select(widget => widget with
            {
                ItemNames = Array.AsReadOnly(widget.ItemNames.ToArray()),
            }).ToArray()),
        };
    }
}
