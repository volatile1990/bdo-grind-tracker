using System.Security;
using System.Xml;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Turns configuration failures into fixed, actionable UI text without exposing file contents.</summary>
internal static class CaptureConfigurationErrorPresentation
{
    private const string MissingNormalPosition =
        "Die Position des normalen Droplogs fehlt oder das Droplog ist ausgeblendet. " +
        "Blende es in BDO unter „UI bearbeiten“ ein und speichere die Oberfläche.";
    private const string AmbiguousNormalPosition =
        "Die Position des normalen Droplogs fehlt oder ist nicht eindeutig gespeichert. " +
        "Blende es in BDO unter „UI bearbeiten“ ein und speichere die Oberfläche erneut.";
    private const string InvalidNormalPosition =
        "Die gespeicherte Position des normalen Droplogs ist ungültig oder liegt außerhalb des Spielbilds. " +
        "Platziere es in BDO unter „UI bearbeiten“ vollständig im Spielbild und speichere die Oberfläche.";
    private const string InvalidResolution =
        "Die gespeicherte Bildschirmauflösung fehlt oder ist ungültig. " +
        "Prüfe die Auflösung in den BDO-Einstellungen und speichere sie erneut.";
    private const string InvalidScale =
        "Die gespeicherte Größe der Benutzeroberfläche fehlt oder ist ungültig. " +
        "Prüfe die UI-Skalierung in den BDO-Einstellungen und speichere sie erneut.";

    internal static string Describe(Exception error)
    {
        while (error is LootPanelUnavailableException { InnerException: { } inner }) error = inner;

        // XML and operating-system messages can contain paths or arbitrary file
        // contents. Never pass them through, including for unknown failures.
        if (error is XmlException)
            return "Die Konfigurationsdatei ist beschädigt oder unvollständig. " +
                "Speichere die Oberfläche in BDO erneut oder wähle eine andere Konfigurationsdatei.";
        if (error is UnauthorizedAccessException or SecurityException)
            return "Grindcrest darf die Konfigurationsdatei nicht lesen. " +
                "Prüfe die Zugriffsrechte des BDO-Ordners oder wähle eine lesbare Kopie.";
        if (error is FileNotFoundException missing)
            return string.Equals(Path.GetFileName(missing.FileName), "GameOption.txt", StringComparison.OrdinalIgnoreCase) ||
                error.Message == "GameOption.txt was not found."
                ? "Die Datei GameOption.txt mit den BDO-Anzeigeeinstellungen fehlt. " +
                    "Starte BDO einmal und speichere die Einstellungen. Bei einer Sicherung wird auch diese Datei benötigt."
                : "Die Konfigurationsdatei wurde nicht gefunden. " +
                    "Lade die Liste erneut oder wähle eine vorhandene Datei.";
        if (error is DirectoryNotFoundException)
            return "Der BDO-Konfigurationsordner wurde nicht gefunden. " +
                "Starte BDO einmal und speichere die Oberfläche oder wähle eine vorhandene Konfigurationsdatei.";
        if (error is PathTooLongException)
            return "Der Pfad zur Konfigurationsdatei ist zu lang. " +
                "Wähle eine Kopie in einem Ordner mit kürzerem Pfad.";

        if (error is InvalidDataException or ArgumentOutOfRangeException)
        {
            var known = error.Message switch
            {
                "UIData Index 159 with visible position was not found." => MissingNormalPosition,
                "The active UI configuration is missing or ambiguous." or
                    "The active main loot panel is missing or ambiguous." => AmbiguousNormalPosition,
                "The required loot-panel position is outside the screen." or
                    "The normal loot panel contains no usable rows." or
                    "RelativePosX is not a valid single-precision number." or
                    "RelativePosY is not a valid single-precision number." => InvalidNormalPosition,
                "No screen width was found." or "No screen height was found." or
                    "The calibrated screen resolution must be positive." or
                    "Width is not a positive 32-bit screen dimension." or
                    "Height is not a positive 32-bit screen dimension." or
                    "GameOption width is not a positive 32-bit screen dimension." or
                    "GameOption height is not a positive 32-bit screen dimension." => InvalidResolution,
                "No UI scale was found." or
                    "The calibrated UI scale must be finite and positive." or
                    "UiScale Value is not a valid single-precision number." or
                    "GameOption uiScale is not a valid single-precision number." => InvalidScale,
                "UserCache contains no non-zero, unsigned 32-bit numeric profile directory with gamevariable.xml." =>
                    "Es wurde keine automatisch nutzbare BDO-Konfiguration gefunden. " +
                    "Speichere die Oberfläche in BDO oder wähle eine Konfigurationsdatei aus der Liste.",
                _ => null,
            };
            if (known is not null) return known;
        }

        if (error is ArgumentOutOfRangeException range)
        {
            if (range.ParamName == "uiScale") return InvalidScale;
            if (range.Message.StartsWith("The calibrated screen dimensions must be positive.", StringComparison.Ordinal))
                return InvalidResolution;
            if (range.Message.StartsWith("The loot anchor lies outside the calibrated screen.", StringComparison.Ordinal) ||
                range.Message.StartsWith("The panel is narrower than Companion's normal-loot left inset.", StringComparison.Ordinal))
                return InvalidNormalPosition;
        }

        return error switch
        {
            IOException when (error.HResult & 0xffff) is 32 or 33 =>
                "Die Konfigurationsdatei wird gerade von einem anderen Programm verwendet. " +
                "Warte kurz und lade die Liste erneut.",
            IOException => "Die Konfigurationsdatei konnte nicht gelesen werden. " +
                "Prüfe, ob die Datei erreichbar ist, und lade die Liste erneut.",
            ArgumentException => "Der Pfad zur BDO-Konfiguration ist ungültig. Wähle eine vorhandene Konfigurationsdatei.",
            _ => "Die BDO-Konfiguration konnte nicht geprüft werden. " +
                "Speichere die Oberfläche im Spiel erneut oder wähle eine andere Konfigurationsdatei.",
        };
    }
}
