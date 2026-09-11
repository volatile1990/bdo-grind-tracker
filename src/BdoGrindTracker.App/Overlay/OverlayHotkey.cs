using System.Text.Json.Serialization;

namespace BdoGrindTracker.App.Overlay;

[Flags]
public enum OverlayHotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
}

/// <summary>A global shortcut registered with Windows; no input is sent to the game.</summary>
public sealed record OverlayHotkey
{
    private const OverlayHotkeyModifiers AllModifiers = OverlayHotkeyModifiers.Control | OverlayHotkeyModifiers.Alt;
    private static readonly IReadOnlyList<KeyDefinition> Definitions = CreateDefinitions();
    private static readonly IReadOnlyDictionary<string, KeyDefinition> ByName =
        Definitions.ToDictionary(key => key.Name, StringComparer.OrdinalIgnoreCase);

    public OverlayHotkeyModifiers Modifiers { get; init; } = OverlayHotkeyModifiers.Control | OverlayHotkeyModifiers.Alt;
    public string Key { get; init; } = "O";

    public static OverlayHotkey DefaultToggleOverlay { get; } = new();
    public static OverlayHotkey DefaultToggleInteraction { get; } = new() { Key = "L" };
    public static IReadOnlyList<string> SupportedKeys { get; } = Array.AsReadOnly(Definitions.Select(key => key.Name).ToArray());

    [JsonIgnore]
    public bool IsValid => Modifiers != OverlayHotkeyModifiers.None &&
        (Modifiers & ~AllModifiers) == 0 && Find(Key) is not null;

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(OverlayHotkeyModifiers.Control)) parts.Add("Strg");
            if (Modifiers.HasFlag(OverlayHotkeyModifiers.Alt)) parts.Add("Alt");
            parts.Add(KeyDisplayText(Key));
            return string.Join("+", parts);
        }
    }

    internal uint VirtualKey => Find(Key)?.VirtualKey ?? 0;

    public static string KeyDisplayText(string key) => Find(key)?.Label ?? "–";

    public static OverlayHotkey Normalize(OverlayHotkey? value, OverlayHotkey fallback)
    {
        if (value is { IsValid: true }) return value with { Key = Find(value.Key)!.Name };
        return fallback is { IsValid: true }
            ? fallback with { Key = Find(fallback.Key)!.Name } : DefaultToggleOverlay;
    }

    private static KeyDefinition? Find(string? key) => key is null
        ? null : ByName.GetValueOrDefault(key.Trim());

    private static IReadOnlyList<KeyDefinition> CreateDefinitions()
    {
        var keys = new List<KeyDefinition>();
        for (var value = 'A'; value <= 'Z'; value++) keys.Add(new(value.ToString(), value.ToString(), value));
        for (var value = '0'; value <= '9'; value++) keys.Add(new(value.ToString(), value.ToString(), value));
        // Windows reserves F12 for debuggers, even when no debugger is attached.
        for (var index = 1; index <= 11; index++) keys.Add(new($"F{index}", $"F{index}", (uint)(0x6f + index)));
        keys.AddRange([
            new("Space", "Leertaste", 0x20), new("Tab", "Tab", 0x09), new("Enter", "Eingabe", 0x0d),
            new("Escape", "Esc", 0x1b), new("Backspace", "Rücktaste", 0x08),
            new("Insert", "Einfg", 0x2d), new("Delete", "Entf", 0x2e),
            new("Home", "Pos1", 0x24), new("End", "Ende", 0x23),
            new("PageUp", "Bild ↑", 0x21), new("PageDown", "Bild ↓", 0x22),
            new("Left", "←", 0x25), new("Up", "↑", 0x26), new("Right", "→", 0x27), new("Down", "↓", 0x28),
            new("Pause", "Pause", 0x13), new("PrintScreen", "Druck", 0x2c),
            new("ScrollLock", "Rollen", 0x91), new("NumLock", "Num", 0x90),
        ]);
        for (var index = 0; index <= 9; index++) keys.Add(new($"Numpad{index}", $"Num {index}", (uint)(0x60 + index)));
        keys.AddRange([
            new("Add", "Num +", 0x6b), new("Subtract", "Num −", 0x6d),
            new("Multiply", "Num *", 0x6a), new("Divide", "Num /", 0x6f), new("Decimal", "Num ,", 0x6e),
        ]);
        return Array.AsReadOnly(keys.ToArray());
    }

    private sealed record KeyDefinition(string Name, string Label, uint VirtualKey);
}
