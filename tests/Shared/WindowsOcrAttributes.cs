global using BdoGrindTracker.TestSupport;

using Windows.Globalization;
using Windows.Media.Ocr;
using Xunit;

namespace BdoGrindTracker.TestSupport;

// Discovery reports missing machine prerequisites as skipped. Strict release runs set
// GRINDCREST_REQUIRE_WINDOWS_OCR=1, so the test body fails its engine assertion
// instead of silently accepting a runner that cannot exercise native OCR.
public sealed class WindowsOcrFactAttribute : FactAttribute
{
    public WindowsOcrFactAttribute(params string[] languages) => Skip = WindowsOcrPrerequisites.SkipReason(languages);
}

public sealed class WindowsOcrTheoryAttribute : TheoryAttribute
{
    public WindowsOcrTheoryAttribute(params string[] languages) => Skip = WindowsOcrPrerequisites.SkipReason(languages);
}

internal static class WindowsOcrPrerequisites
{
    internal static string? SkipReason(string[] languages)
    {
        if (Environment.GetEnvironmentVariable("GRINDCREST_REQUIRE_WINDOWS_OCR") == "1") return null;
        var available = languages.Length == 0
            ? OcrEngine.AvailableRecognizerLanguages.Count > 0
            : languages.All(language => OcrEngine.IsLanguageSupported(new Language(language)));
        return available ? null : "Windows OCR prerequisite missing: " +
            (languages.Length == 0 ? "an installed OCR language" : string.Join(", ", languages)) +
            ". Native recognition was not executed.";
    }
}
