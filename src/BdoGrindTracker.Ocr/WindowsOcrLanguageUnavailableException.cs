namespace BdoGrindTracker.Ocr;

/// <summary>The Windows OCR API confirmed that this recognition language is missing.</summary>
public sealed class WindowsOcrLanguageUnavailableException(string languageTag)
    : InvalidOperationException(
        $"Die Windows-Texterkennung für {LanguageName(languageTag)} ({languageTag}) ist nicht installiert.")
{
    public string LanguageTag { get; } = languageTag;

    private static string LanguageName(string tag) => tag switch
    {
        "de-DE" => "Deutsch",
        "en-US" => "Englisch",
        _ => tag,
    };
}
