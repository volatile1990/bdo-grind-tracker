using System.Text.RegularExpressions;

namespace BdoGrindTracker.App.Localization;

internal static partial class AppText
{
    // Existing diagnostic/status records persist rendered messages. Translate only
    // known complete message shapes at the display boundary, keeping filenames,
    // item names and external error details intact. New UI text should use Format.
    private static readonly MessageTemplate[] MessageTemplates =
    [
        new("Overlay nicht gespeichert: {0}", "Overlay was not saved: {0}", true),
        new("Vorlage nicht gespeichert: {0}", "Template was not saved: {0}", true),
        new("Vorlage nicht gelöscht: {0}", "Template was not deleted: {0}", true),
        new("Es sind höchstens {0} eigene Vorlagen möglich.", "You can create up to {0} custom templates."),
        new("Tastenkürzel nicht gespeichert: {0}", "Shortcuts were not saved: {0}", true),
        new("Das Overlay-Fenster konnte nicht geändert werden: {0}", "Could not change the overlay window: {0}", true),
        new("Das Overlay-Fenster konnte nicht gelöscht werden: {0}", "Could not delete the overlay window: {0}", true),
        new("Das Overlay konnte nicht gespeichert werden: {0}", "Could not save the overlay: {0}", true),
        new("Die Bestätigung konnte nicht geöffnet werden: {0}", "Could not open the confirmation dialog: {0}", true),
        new("Die Bestätigung konnte nicht geöffnet werden. {0}", "Could not open the confirmation dialog. {0}", true),
        new("Der Dialog konnte nicht geöffnet werden. {0}", "Could not open the dialog. {0}", true),
        new("Die Bildschirmvorschau konnte nicht geändert werden: {0}", "Could not change the screen preview: {0}", true),
        new("Die Position konnte nicht zurückgesetzt werden: {0}", "Could not reset the position: {0}", true),
        new("Die Mausbedienung konnte nicht geladen werden. Position und Größe können weiterhin über die Felder geändert werden. {0}", "Mouse interaction could not be loaded. You can still change position and size using the fields. {0}", true),
        new("Bitte einen Wert zwischen {0} und {1} eingeben. Die Änderung wurde nicht gespeichert.", "Enter a value between {0} and {1}. The change was not saved."),
        new("Automatisch pausiert: seit {0} Minuten kein neuer Drop. Die Zeit ohne Drops wurde abgezogen.", "Automatically paused: no new drops for {0} minutes. Idle time has been deducted."),
        new("Session für {0} gespeichert.", "Session for {0} saved."),
        new("Spot-Auswahl gespeichert: {0}", "Spot selection saved: {0}"),
        new("Preisstand {0}", "Prices as of {0}"),
        new("{0} · NPC- und Festwerte", "{0} · NPC and fixed values"),
        new("{0} · Preise offline, letzte bekannte Werte", "{0} · Prices offline, last known values"),
        new("{0} · Letzter gespeicherter Preisstand", "{0} · Last saved prices"),
        new("{0} · Preisstand {1}", "{0} · Prices as of {1}"),
        new("Bildschirm {0} · {1} · Hauptbildschirm", "Display {0} · {1} · Primary display"),
        new("Bildschirm {0} · {1}", "Display {0} · {1}"),
        new("Windows installiert die Texterkennung ({0}) … Das kann einige Minuten dauern.", "Windows is installing text recognition ({0}) … This may take a few minutes."),
        new("Windows hat die Installation abgeschlossen, die Texterkennung ist aber noch nicht verfügbar. Bitte prüfe erneut. Falls das Paket weiterhin fehlt, prüfe das Windows-Installationsprotokoll: {0}", "Windows finished installation, but text recognition is not available yet. Please check again. If the pack is still missing, check the Windows installation log: {0}"),
        new("Einstellungen nicht gespeichert: {0}", "Settings not saved: {0}", true),
        new("Verlauf nicht gespeichert: {0}", "History not saved: {0}", true),
        new("Upload abgeschlossen, sein lokaler Status konnte nicht gespeichert werden: {0}", "Upload completed, but its local status could not be saved: {0}", true),
        new("Garmoth nicht gesendet: {0} Auto-Upload angehalten.", "Not sent to Garmoth: {0} Automatic uploads suspended.", true),
        new("Garmoth nicht gesendet: {0}", "Not sent to Garmoth: {0}", true),
        new("Tracking gestoppt: {0}", "Tracking stopped: {0}", true),
        new("Demo: Magaia-Session vom {0}, nicht gespeicherte Beispieldaten. Tracking starten beendet die Demo.",
            "Demo: Magaia session from {0}, unsaved sample data. Starting tracking ends the demo."),
        new("Aufzeichnung beendet: {0}", "Recording stopped: {0}", true),
        new("Rotation-Diagnose beendet: {0}", "Rotation diagnostics stopped: {0}", true),
        new("Startup · {0} / {1} Opfergaben", "Startup · {0} / {1} offerings"),
        new("AFK beendet · Startup unvollständig ({0} / {1} Opfergaben), Rotation nicht gezählt · warte auf erstes Ereignis", "AFK ended · incomplete startup ({0} / {1} offerings), rotation not counted · waiting for first event"),
        new("{0} · Startup verworfen ({1} / {2} Opfergaben), zählt nicht als vollständige Rotation", "{0} · startup discarded ({1} / {2} offerings), does not count as a complete rotation", true),
        new("Grindcrest bleibt geöffnet: {0}", "Grindcrest remains open: {0}", true),
        new("BDO-Konfiguration übernommen. {0}", "BDO configuration applied. {0}", true),
        new("Grind Goals konnten nicht geladen werden: {0}", "Grind Goals could not be loaded: {0}", true),
        new("Garmoth-Referenzwerte für {0} von {1} Spots aktualisiert; übrige Werte behalten ihren bisherigen Stand.", "Garmoth benchmarks updated for {0} of {1} spots; remaining values retain their previous data."),
        new("{0} Der letzte verfügbare Referenzstand wird weiterverwendet.", "{0} The last available benchmarks will continue to be used.", true),
        new("{0} Die aktualisierten Werte konnten nicht lokal gespeichert werden.", "{0} The updated values could not be saved locally.", true),
        new("Aktuelle Konfiguration: {0}", "Current configuration: {0}", true),
        new("{0} · Rare-Droplog nicht verfügbar; normales Droplog bleibt aktiv.", "{0} · Rare drop log unavailable; normal drop log remains active.", true),
        new("Das Windows-OCR-Sprachpaket konnte nicht installiert werden (Fehler {0}). {1}", "The Windows OCR language pack could not be installed (error {0}). {1}", true),
        new("{0} Windows-Installationsprotokoll: {1}", "{0} Windows installation log: {1}", true),
    ];

    private static string TranslateMessage(string source)
    {
        foreach (var template in MessageTemplates)
        {
            if (template.TryTranslate(source) is { } translated) return translated;
        }
        return source;
    }

    private sealed class MessageTemplate
    {
        private readonly Regex _pattern;
        private readonly string _prefix;
        private readonly string _english;
        private readonly bool _translateArgument;

        internal MessageTemplate(string source, string english, bool translateArgument = false)
        {
            _english = english;
            _translateArgument = translateArgument;
            _prefix = source[..source.IndexOf('{')];
            var pattern = Regex.Escape(source);
            pattern = Regex.Replace(pattern, @"\\\{\d+}", "(.*?)");
            _pattern = new Regex("\\A" + pattern + "\\z", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        }

        internal string? TryTranslate(string source)
        {
            if (!source.StartsWith(_prefix, StringComparison.Ordinal)) return null;
            var match = _pattern.Match(source);
            if (!match.Success) return null;
            var values = match.Groups.Cast<Group>().Skip(1)
                .Select(group => _translateArgument && English.TryGetValue(group.Value, out var value) ? value : group.Value)
                .Cast<object>().ToArray();
            return string.Format(Culture("en"), _english, values);
        }
    }
}
