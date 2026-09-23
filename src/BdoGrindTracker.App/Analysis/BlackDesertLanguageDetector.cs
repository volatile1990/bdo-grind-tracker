using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Analysis;

internal static class BlackDesertLanguageDetector
{
    public static GameLanguageDetection Detect() => BlackDesertLanguageReader.Read(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Black Desert", "GameOption.txt"),
        BlackDesertInstallationLocator.FindDirectories());
}
