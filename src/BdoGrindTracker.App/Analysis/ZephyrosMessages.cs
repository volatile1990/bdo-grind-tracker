namespace BdoGrindTracker.App.Analysis;

/// <summary>Banner boundaries of the Zephyros hatchery cycle.</summary>
internal static class ZephyrosMessages
{
    // Two banners can announce the same transition. Parse returns one event.
    internal static readonly (string Kind, string Label, string Phrase)[] Definitions = [
        ("start", "Brutstätte aktiviert", "within the lava"),
        ("knights", "Schattenritter und Wellen", "in the hatchery"),
        ("knights", "Schattenritter und Wellen", "absorb caphras s"),
        ("boss", "Beelzebub", "energy beelzebub"),
        ("afk", "AFK-Phase", "hatchery s energy"),
        ("afk", "AFK-Phase", "surge of its"),
        ("end", "Brutstätte bereit", "stirs once more"),
    ];

    internal static IReadOnlyList<(string Kind, string Label)> Parse(string text) => Parse(text, null);
    internal static IReadOnlyList<(string Kind, string Label)> Parse(string text, string? language) =>
        LocalizedRotationMessages.Parse("zephyros", text, Definitions, language);
}
