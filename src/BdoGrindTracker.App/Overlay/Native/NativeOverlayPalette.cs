using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>Native counterparts of the Obsidian, Kamasylvia and Valencia CSS tokens.</summary>
internal sealed record NativeOverlayPalette(Color Surface, Color Panel, Color Slot, Color Edge,
    Color Text, Color Muted, Color Accent, Color Positive, Color Warning, Color Rare,
    Color GoalTrack, Color GoalFill, int Corner)
{
    internal static NativeOverlayPalette? For(string theme) => theme switch
    {
        AppThemes.Obsidian => Obsidian,
        AppThemes.Kamasylvia => Kamasylvia,
        AppThemes.Valencia => Valencia,
        _ => null,
    };

    private static Color Hex(int value) => Color.FromArgb(255, (value >> 16) & 255, (value >> 8) & 255, value & 255);
    private static readonly NativeOverlayPalette Obsidian = new(
        Hex(0x101216), Hex(0x191c22), Hex(0x12151a), Hex(0x505964),
        Hex(0xf1f4f8), Hex(0xafbac8), Hex(0x9fc5ff), Hex(0x85d9b0), Hex(0xf2cf89), Hex(0xf2cf89),
        Hex(0x29323f), Hex(0x3f608b), 6);
    private static readonly NativeOverlayPalette Kamasylvia = new(
        Hex(0x14231e), Hex(0x21372d), Hex(0x15271f), Hex(0x65806b),
        Hex(0xf1efda), Hex(0xb6c6ad), Hex(0xd8ca8a), Hex(0xa7d69c), Hex(0xf0cc88), Hex(0xe7c77d),
        Hex(0x304635), Hex(0x506e3f), 10);
    private static readonly NativeOverlayPalette Valencia = new(
        Hex(0xf3eadb), Hex(0xfff9ee), Hex(0xfff9ee), Hex(0xbda488),
        Hex(0x433226), Hex(0x715d4b), Hex(0x9c482d), Hex(0x386443), Hex(0x80561b), Hex(0x896016),
        Hex(0xe4d5bf), Hex(0xcfaa77), 10);
}
