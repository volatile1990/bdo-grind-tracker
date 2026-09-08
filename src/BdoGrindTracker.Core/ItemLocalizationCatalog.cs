using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BdoGrindTracker.Core;

/// <summary>Localized game names resolve to the existing language-independent catalog keys.</summary>
public static class ItemLocalizationCatalog
{
    public static IReadOnlyDictionary<string, string> GermanNames { get; } = LoadGermanNames();

    public static string DisplayName(string canonicalName, string gameLanguage) =>
        gameLanguage == "de" && GermanNames.TryGetValue(canonicalName, out var name) ? name : canonicalName;

    // Both Windows OCR and the original text pipeline can drop accents, punctuation
    // and spaces. Normalize the source names identically, including German sharp S.
    internal static string NormalizeGermanName(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var rune in text.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (rune.Value is 0x00DF or 0x1E9E) result.Append("ss");
            else if (Rune.IsLetterOrDigit(rune)) result.Append(Rune.ToLowerInvariant(rune));
        }
        return result.ToString();
    }

    private static FrozenDictionary<string, string> LoadGermanNames()
    {
        using var stream = typeof(ItemLocalizationCatalog).Assembly.GetManifestResourceStream(
            "BdoGrindTracker.Core.GermanItems.json") ?? throw new InvalidDataException("Der deutsche Itemkatalog fehlt.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("items").EnumerateArray().ToFrozenDictionary(
            item => item.GetProperty("canonicalName").GetString()!,
            item => item.GetProperty("germanName").GetString()!, StringComparer.Ordinal);
    }
}
