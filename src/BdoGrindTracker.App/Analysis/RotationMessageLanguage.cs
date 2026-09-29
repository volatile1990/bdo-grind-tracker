namespace BdoGrindTracker.App.Analysis;

internal static class RotationMessageLanguage
{
    internal static bool IsKnown(string? language) => language is "auto" or "en" or "de" or "fr" or "sp";
    internal static string Resolve(string preference, string? gameLanguage) => preference == "auto"
        ? gameLanguage is "de" or "fr" or "sp" ? gameLanguage : "en"
        : IsKnown(preference) ? preference : throw new ArgumentException("Unknown rotation message language.");
    internal static string OcrTag(string language) => language switch
    {
        "en" => "en-US", "de" => "de-DE", "fr" => "fr-FR", "sp" => "es-ES",
        _ => throw new ArgumentException("Unknown rotation message language.")
    };
}
