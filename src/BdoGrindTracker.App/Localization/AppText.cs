using System.Collections.Frozen;
using System.Globalization;

namespace BdoGrindTracker.App.Localization;

/// <summary>
/// UI resources use their original German text as stable lookup keys. The selected
/// language is passed explicitly so browser circuits and native overlays stay isolated.
/// Game/OCR language, persisted IDs and interchange formats are independent of this.
/// </summary>
internal static partial class AppText
{
    internal const string DefaultLanguage = "en";
    internal static bool IsKnownLanguage(string? language) => language is "de" or "en";
    internal static string NormalizeLanguage(string? language) => IsKnownLanguage(language) ? language! : DefaultLanguage;
    internal static CultureInfo Culture(string? language) => CultureInfo.GetCultureInfo(
        NormalizeLanguage(language) == "en" ? "en-US" : "de-DE");

    internal static IReadOnlyDictionary<string, string> English { get; } = CreateEnglish();

    internal static string Translate(string? source, string? language)
    {
        if (string.IsNullOrEmpty(source)) return source ?? "";
        if (NormalizeLanguage(language) != "en") return source;
        return English.TryGetValue(source, out var translated) ? translated : TranslateMessage(source);
    }

    internal static string Format(string source, string? language, params object?[] arguments) =>
        string.Format(Culture(language), Translate(source, language), arguments);

    private static FrozenDictionary<string, string> CreateEnglish()
    {
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        AddCommon(text);
        AddMain(text);
        AddSecondary(text);
        AddOverlay(text);
        AddMessages(text);
        AddSetup(text);
        AddThemes(text);
        AddUploadCorrections(text);
        AddRotationMetrics(text);
        AddLootDrops(text);
        AddBuffs(text);
        AddNavigation(text);
        return text.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static void AddCommon(Dictionary<string, string> text)
    {
        text["Mrd."] = "B";
        text["Mio."] = "M";
        text["Tsd."] = "K";
        text["{0} Std. {1:00} Min."] = "{0} hr {1:00} min";
        text["{0} Min."] = "{0} min";
        text["Grindspot wird erkannt"] = "Detecting grind spot";
        text["AP-Limit {0}"] = "AP limit {0}";
        text["NPC / Festwert"] = "NPC / fixed value";
        text["Verarbeitung"] = "Processing";
        text["Zentralmarkt"] = "Central Market";
        text["Ohne Marktwert"] = "No market value";
        text["Kampf-EP"] = "Combat EXP";
        text["Marnis Reich"] = "Marni's Realm";
        text["Niederschlag / Umwerfen"] = "Knockdown / Bound";
        text["Rückstoß / Hochschleudern"] = "Knockback / Floating";
        text["Betäuben / Erstarren / Einfrieren"] = "Stun / Stiffness / Freezing";
        text["Allan Serbins Landschaft"] = "Allan Serbin's Landscape";
        text["Höchste Stufe"] = "Highest tier";
        text["Göttliche Autorität"] = "Divine Authority";
        text["Gruppe · 3 Spieler"] = "Party · 3 players";
        text["Verstärkte Monster"] = "Empowered monsters";
        text["Dehkias Laterne"] = "Dehkia's Lantern";
        text["Dehkias Laterne · Stufe II"] = "Dehkia's Lantern · Tier II";
        text["Bitte wähle Deutsch oder Englisch als App-Sprache."] = "Please select German or English as the app language.";
    }
}
