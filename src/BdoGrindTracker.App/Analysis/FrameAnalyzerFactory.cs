using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using System.Text.Json;

namespace BdoGrindTracker.App.Analysis;

internal static class FrameAnalyzerFactory
{
    public static ILootFrameAnalyzer Create()
    {
        var vocabularyPath = Path.Combine(AppContext.BaseDirectory, "data", "items.en.txt");
        var iconCatalogPath = Path.Combine(AppContext.BaseDirectory, "data", "icons", "catalog.json");
        CompanionNormalRowPipeline? rowPipeline = null;
        try
        {
            var catalog = LoadCatalog(vocabularyPath, iconCatalogPath);
            if (catalog.Length == 0)
            {
                throw new InvalidDataException("Die Item-Liste des Grindspots fehlt oder ist leer.");
            }

            var blackDesertPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Black Desert");
            var calibration = new CompanionCalibrationReader().Read(blackDesertPath);
            var fontType = (CompanionUiFontType)(byte)calibration.FontType;
            var digitSources = CompanionDigitCatalog.CreateSources();
            using var templateSet = new CompanionDigitTemplateLoader().LoadNormalQuantity(
                digitSources,
                fontType,
                calibration.UiScale);
            if (templateSet.Templates.Count != 10)
            {
                throw new InvalidDataException(
                    "Companions vollständiger Ziffernsatz (0–9) konnte nicht geladen werden.");
            }

            var quantityRecognizer = new CompanionQuantityRecognizer(templateSet.Templates);
            rowPipeline = new CompanionNormalRowPipeline(quantityRecognizer);
            var windowsOcr = CompanionWindowsOcrRecognizer.TryCreate(
                    preferredLanguageTag: "en-US",
                    throwIfUnavailable: true) ??
                throw new InvalidOperationException("Windows OCR konnte nicht erstellt werden.");
            var matcher = new CompanionItemMatcher(catalog);
            var nameRecognizer = new CompanionNameRecognizer(windowsOcr);
            var analyzer = new CompanionLootFrameAnalyzer(
                calibration,
                matcher,
                rowPipeline,
                nameRecognizer,
                normalRecovery: new NormalLootRecovery(matcher, nameRecognizer));
            rowPipeline = null;
            return analyzer;
        }
        catch (Exception ex)
        {
            rowPipeline?.Dispose();
            return new UnavailableFrameAnalyzer(
                $"BDO-Companion-Pipeline konnte nicht initialisiert werden: " +
                DescribeException(ex));
        }
    }

    private static string DescribeException(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var detail = $"{current.GetType().Name}: {current.Message}";
            if (!messages.Contains(detail, StringComparer.Ordinal))
            {
                messages.Add(detail);
            }
        }

        return string.Join(" -> ", messages);
    }

    internal static CompanionRareCatalogEntry[] LoadCatalog(string vocabularyPath, string iconCatalogPath)
    {
        if (!File.Exists(vocabularyPath)) return [];
        var iconPaths = LoadIconPaths(iconCatalogPath);
        return File.ReadLines(vocabularyPath).Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Distinct(StringComparer.Ordinal)
            .Select(name => new CompanionRareCatalogEntry(name, iconPaths.GetValueOrDefault(name)))
            .ToArray();
    }

    private static Dictionary<string, string> LoadIconPaths(string path)
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return paths;
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array) return paths;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameValue) ||
                !item.TryGetProperty("sourceIcon", out var iconValue)) continue;
            var name = nameValue.GetString();
            var icon = iconValue.GetString();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(icon)) continue;
            var offset = icon.IndexOf("new_icon/", StringComparison.Ordinal);
            if (offset >= 0) paths[name] = icon[offset..];
        }
        return paths;
    }
}
