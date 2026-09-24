using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>
/// Native counterparts of the session timeline's colour tokens (session-timeline.css). Grindcrest keeps the
/// timeline's own values; every other theme maps them onto its gold, gold-bright, teal and red as themes.css does.
/// Surfaces, lines and text come from the overlay's own palette, like the timeline's --line, --text and --muted.
/// </summary>
internal sealed record NativeTimelinePalette(Color Silver, Color Rare, Color Special, Color Complete,
    Color Failed, Color Active, Color Fastest)
{
    internal static NativeTimelinePalette For(string? theme) => AppThemes.Normalize(theme) switch
    {
        AppThemes.BlackDesert => Themed(0xd3b675, 0xe4ce91, 0xa4bf83, 0xe19b92),
        AppThemes.Light => Themed(0x365f91, 0x244c7d, 0x237557, 0xa43d43),
        AppThemes.Cats => Themed(0xe0b97f, 0xf4d5a7, 0xaacbaa, 0xeaa999),
        AppThemes.Obsidian => Themed(0x9fc5ff, 0xc3daff, 0x85d9b0, 0xffabab),
        AppThemes.Kamasylvia => Themed(0xd8ca8a, 0xeee0a6, 0xa7d69c, 0xf1aa9b),
        AppThemes.Valencia => Themed(0x9c482d, 0x80371f, 0x386443, 0xa03235),
        _ => Grindcrest,
    };

    private static readonly NativeTimelinePalette Grindcrest = new(Hex(0xd8bd75), Hex(0xf2c14e), Hex(0x66d8c7),
        Hex(0x5f8f6d), Hex(0xc9737f), Hex(0x9275be), Hex(0x9fe6a8));

    // --timeline-silver: gold, --timeline-rare and -active: gold-bright, -special, -complete and -fastest: teal.
    private static NativeTimelinePalette Themed(int gold, int goldBright, int teal, int red) =>
        new(Hex(gold), Hex(goldBright), Hex(teal), Hex(teal), Hex(red), Hex(goldBright), Hex(teal));

    private static Color Hex(int value) => Color.FromArgb(255, (value >> 16) & 255, (value >> 8) & 255, value & 255);
}
