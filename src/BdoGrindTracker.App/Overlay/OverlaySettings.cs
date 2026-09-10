namespace BdoGrindTracker.App.Overlay;

public sealed record OverlayWidget
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Kind { get; init; } = "duration";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 160;
    public double Height { get; init; } = 72;
    public bool ShowLabel { get; init; } = true;
    public bool ShowIcon { get; init; } = true;
    public double FontScale { get; init; } = 1;
    public int ItemLimit { get; init; } = 8;
    public string ItemView { get; init; } = "grid";
    public string ItemFilter { get; init; } = "all";
    public IReadOnlyList<string> ItemNames { get; init; } = [];
    public double ItemSize { get; init; } = 56;
    public string ItemSort { get; init; } = "default";
}

public sealed record OverlaySettings
{
    public const int CurrentHotkeySettingsVersion = 1;
    public bool Enabled { get; init; }
    public string Interaction { get; init; } = "move";
    public string Visibility { get; init; } = "game";
    public double Width { get; init; } = 360;
    public double Height { get; init; } = 260;
    // Fractions of the available travel within the current monitor, independent
    // of its resolution, DPI, or position on the virtual desktop.
    public double PositionX { get; init; } = .02;
    public double PositionY { get; init; } = .15;
    public double Scale { get; init; } = 1;
    public double BackgroundOpacity { get; init; } = .85;
    public bool ShowBorder { get; init; } = true;
    public bool SnapToGrid { get; init; } = true;
    public bool CaptureExcluded { get; init; } = true;
    public bool HotkeysEnabled { get; init; } = true;
    public int HotkeySettingsVersion { get; init; } = CurrentHotkeySettingsVersion;
    public OverlayHotkey ToggleOverlayHotkey { get; init; } = OverlayHotkey.DefaultToggleOverlay;
    public OverlayHotkey ToggleInteractionHotkey { get; init; } = OverlayHotkey.DefaultToggleInteraction;
    public IReadOnlyList<OverlayWidget> Widgets { get; init; } = OverlayCatalog.CompactWidgets();
}

public sealed record OverlayWidgetDefinition(string Kind, string Label, string Description, string Icon,
    double Width, double Height);

public static class OverlayCatalog
{
    public const int MaximumWidgets = 24;
    public static IReadOnlyList<OverlayWidgetDefinition> Widgets { get; } = Array.AsReadOnly(new[]
    {
        new OverlayWidgetDefinition("drop-grid", "Drop-Raster", "Itemicons und Mengen der Live-Session", "loot", 344, 168),
        new OverlayWidgetDefinition("drop-strip", "Drop-Leiste", "Kompakte Iconreihe mit Mengen", "loot", 344, 112),
        new OverlayWidgetDefinition("drop-list", "Drop-Liste", "Itemnamen und Mengen untereinander", "loot", 344, 224),
        new OverlayWidgetDefinition("drop-item", "Einzelnes Item", "Ein gewähltes Item als eigene Kachel", "loot", 168, 104),
        new OverlayWidgetDefinition("duration", "Aktive Zeit", "Grindzeit ohne Pausen", "clock", 168, 72),
        new OverlayWidgetDefinition("spot", "Grindspot", "Automatisch erkannter Spot", "pin", 344, 64),
        new OverlayWidgetDefinition("silver", "Silber netto", "Wert nach Marktsteuern", "silver", 168, 72),
        new OverlayWidgetDefinition("silver-hour", "Silber / Stunde", "Durchschnitt der Session", "trend", 168, 72),
        new OverlayWidgetDefinition("trash", "Trashloot", "Gesammelte Trashmenge", "loot", 168, 72),
        new OverlayWidgetDefinition("trash-hour", "Trash / Stunde", "Trashmenge pro aktiver Stunde", "trend", 168, 72),
        new OverlayWidgetDefinition("drops", "Drop-Inventar", "Alle Drops als Liste oder Icons", "loot", 344, 128),
        new OverlayWidgetDefinition("rare-drops", "Seltene Drops", "Seltene Gegenstände im Blick", "spark", 344, 112),
        new OverlayWidgetDefinition("chart", "Silberverlauf", "Silber pro Stunde im Sessionverlauf", "trend", 344, 144),
        new OverlayWidgetDefinition("controls", "Tracking-Steuerung", "Grind starten, pausieren und fortsetzen", "play", 168, 56),
        new OverlayWidgetDefinition("status", "Tracking-Status", "Aktiv, pausiert oder Fehler", "live", 168, 56),
        new OverlayWidgetDefinition("loot-scroll", "Loot-Scroll", "Aktivstatus und erkannte Stufe", "loot", 168, 72),
    });

    public static OverlayWidgetDefinition? Find(string kind) => Widgets.FirstOrDefault(value => value.Kind == kind);

    public static bool IsLootWidget(string kind) => kind is "drops" or "rare-drops" or
        "drop-grid" or "drop-strip" or "drop-list" or "drop-item";

    public static OverlayWidget CreateWidget(string kind, double x = 8, double y = 8)
    {
        var definition = Find(kind) ?? throw new ArgumentException("Unbekanntes Overlay-Modul.", nameof(kind));
        return new()
        {
            Kind = kind, X = x, Y = y, Width = definition.Width, Height = definition.Height,
            ItemView = kind switch { "drop-strip" => "strip", "drop-list" => "list", "drop-item" => "card", _ => "grid" },
            ItemFilter = kind == "drop-item" ? "selected" : kind == "rare-drops" ? "rare" : "all",
            ItemLimit = kind == "drop-item" ? 1 : 8,
        };
    }

    public static IReadOnlyList<OverlayWidget> CompactWidgets() => Array.AsReadOnly(new[]
    {
        CreateWidget("duration", 8, 8), CreateWidget("trash", 184, 8),
        CreateWidget("silver", 8, 88), CreateWidget("silver-hour", 184, 88),
        CreateWidget("controls", 8, 184), CreateWidget("status", 184, 184),
    });

    public static OverlaySettings Preset(string name) => name switch
    {
        "compact" => new(),
        "dashboard" => new()
        {
            Width = 536, Height = 384,
            Widgets = Array.AsReadOnly(new[]
            {
                CreateWidget("spot", 8, 8) with { Width = 520, Height = 56 },
                CreateWidget("duration", 8, 72), CreateWidget("trash", 184, 72), CreateWidget("silver-hour", 360, 72),
                CreateWidget("chart", 8, 152), CreateWidget("silver", 360, 152),
                CreateWidget("trash-hour", 360, 232),
                CreateWidget("controls", 8, 320), CreateWidget("status", 184, 320) with { Width = 344 },
            })
        },
        "loot" => new()
        {
            Width = 504, Height = 960,
            Widgets = Array.AsReadOnly(new[]
            {
                CreateWidget("spot", 12, 12) with { Width = 480, Height = 108, FontScale = 1.5 },
                CreateWidget("duration", 12, 132) with { Width = 252, Height = 108, ShowLabel = false, FontScale = 1.5 },
                CreateWidget("silver", 276, 132) with { Width = 216, Height = 108, FontScale = 1.5 },
                CreateWidget("chart", 12, 252) with { Width = 480, Height = 216, ShowLabel = false, FontScale = 1.5 },
                CreateWidget("controls", 12, 480) with { Width = 252, Height = 108, ShowLabel = false, FontScale = 1.5 },
                CreateWidget("trash-hour", 276, 480) with { Width = 216, Height = 108, FontScale = 1.5 },
                CreateWidget("drop-grid", 12, 600) with { Width = 480, Height = 348, ItemLimit = 24, ItemSize = 84, ShowLabel = false, FontScale = 1.5 },
            })
        },
        "loot-strip" => new()
        {
            Width = 536, Height = 184,
            Widgets = Array.AsReadOnly(new[]
            {
                CreateWidget("drop-strip", 8, 8) with { Width = 520, ItemLimit = 24 },
                CreateWidget("duration", 8, 128) with { Height = 48, ShowLabel = false },
                CreateWidget("silver-hour", 184, 128) with { Height = 48, ShowLabel = false },
                CreateWidget("controls", 360, 128) with { Height = 48, ShowLabel = false },
            })
        },
        _ => throw new ArgumentException("Unbekannte Overlay-Vorlage.", nameof(name)),
    };
}

public static class OverlayLayout
{
    public static OverlaySettings Normalize(OverlaySettings? settings)
    {
        settings ??= new();
        var width = Finite(settings.Width, 360, 160, 1600);
        var height = Finite(settings.Height, 260, 64, 1200);
        var toggleOverlay = OverlayHotkey.Normalize(settings.ToggleOverlayHotkey, OverlayHotkey.DefaultToggleOverlay);
        var toggleInteraction = OverlayHotkey.Normalize(settings.ToggleInteractionHotkey, OverlayHotkey.DefaultToggleInteraction);
        if (toggleOverlay == toggleInteraction)
        {
            toggleOverlay = OverlayHotkey.DefaultToggleOverlay;
            toggleInteraction = OverlayHotkey.DefaultToggleInteraction;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var widgets = new List<OverlayWidget>();
        foreach (var widget in (settings.Widgets ?? []).Take(OverlayCatalog.MaximumWidgets))
        {
            if (widget is null || OverlayCatalog.Find(widget.Kind) is not { } definition) continue;
            var id = Guid.TryParse(widget.Id, out var parsed) ? parsed.ToString("N") : Guid.NewGuid().ToString("N");
            if (!ids.Add(id)) continue;
            var w = Finite(widget.Width, definition.Width, 80, width);
            var h = Finite(widget.Height, definition.Height, 40, height);
            widgets.Add(widget with
            {
                Id = id, Width = w, Height = h,
                X = Finite(widget.X, 0, 0, width - w), Y = Finite(widget.Y, 0, 0, height - h),
                FontScale = Finite(widget.FontScale, 1, .7, 2),
                ItemLimit = Math.Clamp(widget.ItemLimit, 1, 24),
                ItemView = widget.Kind == "drop-item" ? "card" :
                    widget.ItemView is "list" or "strip" or "card" ? widget.ItemView : "grid",
                ItemSize = Finite(widget.ItemSize, 56, 32, 112),
                ItemFilter = widget.Kind == "drop-item" ? "selected" : widget.Kind == "rare-drops" ? "rare" :
                    widget.ItemFilter is "rare" or "trash" or "selected" ? widget.ItemFilter : "all",
                ItemSort = widget.ItemSort is "quantity" or "name" ? widget.ItemSort : "default",
                ItemNames = NormalizeNames(widget),
            });
        }
        return settings with
        {
            Width = width, Height = height,
            PositionX = Finite(settings.PositionX, .02, 0, 1), PositionY = Finite(settings.PositionY, .15, 0, 1),
            Scale = Finite(settings.Scale, 1, .5, 2),
            BackgroundOpacity = Finite(settings.BackgroundOpacity, .85, 0, 1),
            Interaction = settings.Interaction is "move" or "locked" or "passthrough" ? settings.Interaction : "move",
            Visibility = settings.Visibility is "game" or "session" or "always" ? settings.Visibility : "game",
            HotkeySettingsVersion = Math.Max(OverlaySettings.CurrentHotkeySettingsVersion, settings.HotkeySettingsVersion),
            ToggleOverlayHotkey = toggleOverlay,
            ToggleInteractionHotkey = toggleInteraction,
            Widgets = Array.AsReadOnly(widgets.ToArray()),
        };
    }

    private static double Finite(double value, double fallback, double minimum, double maximum) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, minimum, maximum);

    private static IReadOnlyList<string> NormalizeNames(OverlayWidget widget)
    {
        var names = (widget.ItemNames ?? []).Where(name => !string.IsNullOrWhiteSpace(name) && name.Length <= 256)
            .Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(widget.Kind == "drop-item" ? 1 : 24).ToArray();
        if (names.Length == 0) return Array.Empty<string>();
        // Preserve the value's identity when it is already immutable and normalized.
        return widget.ItemNames is System.Collections.ObjectModel.ReadOnlyCollection<string> existing &&
               existing.SequenceEqual(names) ? existing : Array.AsReadOnly(names);
    }
}
