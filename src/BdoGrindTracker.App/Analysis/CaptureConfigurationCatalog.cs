using BdoGrindTracker.App.Services;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Uses the same validated geometry for configuration inspection and live capture.</summary>
internal sealed class CaptureConfigurationCatalog(string? blackDesertDirectory = null)
{
    private readonly string _root = blackDesertDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Black Desert");
    private readonly CompanionCalibrationReader _reader = new();

    internal CompanionCalibration Read(string? path)
    {
        try { return LootPanelCaptureGuard.ReadCalibration(() => _reader.Read(FindRoot(path), path)); }
        catch (Exception error) when (IsConfigurationError(error))
        {
            throw new LootPanelUnavailableException(CaptureConfigurationErrorPresentation.Describe(error), error);
        }
    }

    internal CaptureConfigurationOption Inspect(string path)
    {
        try { return Describe(Read(path)); }
        catch (Exception error) when (IsConfigurationError(error))
        {
            return Invalid(path, error);
        }
    }

    internal CaptureConfigurationScan Scan(string? selectedPath)
    {
        var options = new Dictionary<string, CaptureConfigurationOption>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var root in new[] { _root, FindRoot(selectedPath) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var candidate in _reader.ScanCandidates(root))
                    // Validate through the app boundary to retain the exception
                    // type; a raw candidate error can include XML contents.
                    options[candidate.GameVariablePath] = Inspect(candidate.GameVariablePath);
            }
            catch (Exception error) when (IsConfigurationError(error))
            {
                errors.Add(CaptureConfigurationErrorPresentation.Describe(error));
            }
        }

        string? activePath = null;
        try
        {
            var active = Describe(Read(selectedPath));
            activePath = active.Path;
            options[active.Path] = active;
        }
        catch (Exception error) when (IsConfigurationError(error))
        {
            errors.Add("Aktuelle Konfiguration: " + CaptureConfigurationErrorPresentation.Describe(error));
            if (selectedPath is not null) options[selectedPath] = Invalid(selectedPath, error);
        }
        return new(options.Values.OrderByDescending(option => option.LastWriteUtc)
            .ThenBy(option => option.Path, StringComparer.OrdinalIgnoreCase).ToArray(), activePath,
            errors.Count == 0 ? null : string.Join(" ", errors));
    }

    internal CaptureConfigurationOption Describe(CompanionCalibration calibration)
    {
        var normal = CompanionNormalLootGeometry.CalculatePanelBounds(calibration);
        Rectangle? rare = calibration.HasRareLootAnchor
            ? CompanionNormalLootGeometry.CalculateRareBandCrop(calibration) : null;
        var status = calibration.RareLootResolution?.Reason == "rare-band-shape-changed-restart-required"
            ? "Position verändert · für eine neue Session erneut übernehmen"
            : calibration.RareLootResolution?.Status switch
        {
            RareLootAnchorStatus.Active => "Position gespeichert · aktive Oberfläche",
            RareLootAnchorStatus.PresetFallback => "Position gespeichert · aus einem UI-Preset",
            RareLootAnchorStatus.Hidden => "Im Spiel ausgeblendet",
            RareLootAnchorStatus.Missing => "Keine Position gespeichert",
            RareLootAnchorStatus.Ambiguous => "Mehrdeutige Position · keine Erfassung",
            RareLootAnchorStatus.Invalid => "Ungültige Position · keine Erfassung",
            _ => rare is null ? "Keine Position gespeichert" : "Position gespeichert",
        };
        return new(calibration.GameVariablePath, Label(calibration.GameVariablePath),
            LastWrite(calibration.GameVariablePath), calibration.ScreenWidth, calibration.ScreenHeight,
            calibration.UiScale, normal, rare, status);
    }

    private CaptureConfigurationOption Invalid(string path, Exception error) =>
        new(path, Label(path), LastWrite(path), 0, 0, 0, null, null, "Nicht geprüft",
            CaptureConfigurationErrorPresentation.Describe(error));

    private string Label(string path)
    {
        try { return Path.GetRelativePath(FindRoot(path), path); }
        catch (ArgumentException) { return path; }
    }

    private static DateTime? LastWrite(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch (Exception error) when (IsConfigurationError(error)) { return null; }
    }

    private string FindRoot(string? path)
    {
        // A manually selected file may belong to another Documents/backup folder.
        // Its own GameOption.txt takes precedence over the default Documents folder.
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                for (var directory = new FileInfo(path).Directory; directory is not null; directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "GameOption.txt"))) return directory.FullName;
            }
            catch (Exception error) when (IsConfigurationError(error)) { }
        }
        return _root;
    }

    internal static bool IsConfigurationError(Exception error) => error is IOException or InvalidDataException or
        UnauthorizedAccessException or System.Xml.XmlException or ArgumentException or
        FormatException or OverflowException or System.Security.SecurityException or LootPanelUnavailableException;
}
