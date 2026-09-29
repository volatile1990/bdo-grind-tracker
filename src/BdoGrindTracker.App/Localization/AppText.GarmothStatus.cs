using System.Text;

namespace BdoGrindTracker.App.Localization;

internal static partial class AppText
{
    private static IEnumerable<(string Source, string English)> GarmothResultMessages()
    {
        yield return ("Bitte einen gültigen Garmoth-API-Key eingeben (ohne Leerzeichen).",
            "Enter a valid Garmoth API key without spaces.");
        yield return ("Upload wurde vor dem Senden abgebrochen.", "Upload cancelled before sending.");
        yield return ("Diese Sitzung wurde bereits gesendet oder ihr Upload-Ergebnis ist unklar. Bitte zuerst in Garmoth prüfen.",
            "This session was already sent or its upload result is uncertain. Check Garmoth first.");
        yield return ("Garmoth hat den Upload abgelehnt. API-Key und Sitzungsdaten prüfen.",
            "Garmoth rejected the upload. Check the API key and session data.");
        yield return ("Sitzung erfolgreich an Garmoth übertragen.", "Session uploaded to Garmoth successfully.");
        yield return ("Garmoth hat die Sitzungsdaten abgelehnt. Klasse, Loot und Dauer prüfen.",
            "Garmoth rejected the session data. Check the class, loot and duration.");
        yield return ("Garmoth hat den Zugriff abgelehnt. API-Key und dessen Berechtigung prüfen.",
            "Garmoth denied access. Check the API key and its permissions.");
        yield return ("Die Garmoth-Uploadschnittstelle ist nicht verfügbar oder hat sich geändert.",
            "The Garmoth upload API is unavailable or has changed.");
        yield return ("Garmoth begrenzt derzeit Anfragen. Später manuell erneut versuchen.",
            "Garmoth is limiting requests. Try again manually later.");
        yield return ("Upload-Ergebnis unklar. Die Sitzung könnte bereits gespeichert sein. Bitte in Garmoth prüfen; kein automatischer Wiederholungsversuch.",
            "Upload result uncertain. The session may already be saved. Check Garmoth; no automatic retry will be made.");
        yield return ("Upload-Ergebnis unklar. Bitte direkt in Garmoth prüfen; kein erneuter Upload.",
            "Upload result uncertain. Check directly in Garmoth; do not upload again.");
        yield return ("Lokale Upload-Vorbereitung fehlgeschlagen.", "Local upload preparation failed.");
    }

    private static IEnumerable<(string Source, string English)> GarmothResultNotes()
    {
        yield return ("Zum Weitergrinden eine neue Sitzung starten.", "Start a new session to continue grinding.");
        yield return ("Silber ist eine Teilsumme; fehlende Preise wurden nicht geschätzt.",
            "Silver is a subtotal; missing prices were not estimated.");
        yield return ("Silber verwendet den letzten verfügbaren Preisstand.",
            "Silver uses the last available prices.");
        yield return ("Der lokale Verlaufstatus konnte nicht gespeichert werden. Der Dublettenschutz bleibt im Uploadjournal erhalten.",
            "The local history status could not be saved. Duplicate protection remains in the upload journal.");
        yield return ("Das Upload-Ergebnis konnte lokal nicht gespeichert werden. Die Uploadabsicht bleibt erhalten; weitere Uploads sind zum Schutz vor Dubletten gesperrt.",
            "The upload result could not be saved locally. The upload intent remains recorded; further uploads are blocked to prevent duplicates.");
    }

    private static void AddGarmothStatus(Dictionary<string, string> text)
    {
        foreach (var (source, english) in GarmothResultMessages()) text[source] = english;
        foreach (var (source, english) in GarmothResultNotes()) text[source] = english;
        text["Die lokale Sitzungs-ID fehlt."] = "The local session ID is missing.";
        text["Garmoth benötigt mindestens eine volle Minute Sitzungsdauer."] = "Garmoth requires at least one full minute of session time.";
        text["Der bestätigte Gesamtwert in Silber darf nicht negativ sein."] = "The confirmed silver total cannot be negative.";
        text["Der Grindspot ist nicht für den Garmoth-Upload zugeordnet."] = "The grind spot is not mapped for Garmoth upload.";
        text["Die Sitzung enthält noch keinen für diesen Garmoth-Spot unterstützten Loot."] = "The session does not yet contain loot supported for this Garmoth spot.";
        text["Der Silberwert pro Stunde ist zu groß für den Garmoth-Upload."] = "The silver per hour value is too large for Garmoth upload.";
        text["Alle hochgeladenen Lootmengen müssen positiv sein."] = "All uploaded loot quantities must be positive.";
        text["Mehrere Items würden demselben Garmoth-Eintrag zugeordnet."] = "Multiple items would map to the same Garmoth entry.";
        text["Die gemeinsame Artefaktmenge ist zu groß für den Garmoth-Upload."] = "The combined artifact quantity is too large for Garmoth upload.";
        text["Starte zuerst eine Live-Session."] = "Start a live session first.";
        text["Garmoth-Upload konnte nicht abgeschlossen werden. Prüfe den Status in Garmoth, bevor du es erneut versuchst."] =
            "The Garmoth upload could not be completed. Check its status in Garmoth before trying again.";
    }

    // Upload results may carry several independent, optional notes. Translate the
    // known app-owned sentences without treating item names as translatable text.
    private static string? TranslateGarmothStatus(string source)
    {
        foreach (var (baseSource, baseEnglish) in GarmothResultMessages())
        {
            if (!source.StartsWith(baseSource, StringComparison.Ordinal)) continue;
            var remaining = source[baseSource.Length..];
            if (remaining.Length == 0) return baseEnglish;
            var translated = new StringBuilder(baseEnglish);
            while (remaining.Length > 0)
            {
                if (remaining.StartsWith(" Ohne Garmoth-Zuordnung ausgelassen: ", StringComparison.Ordinal))
                {
                    const string prefix = " Ohne Garmoth-Zuordnung ausgelassen: ";
                    var end = remaining.Length - 1;
                    foreach (var (noteSource, _) in GarmothResultNotes())
                    {
                        var boundary = remaining.IndexOf(". " + noteSource, prefix.Length,
                            StringComparison.Ordinal);
                        if (boundary >= 0 && boundary < end) end = boundary;
                    }
                    if (end < prefix.Length || remaining[end] != '.') return null;
                    translated.Append(" Skipped without a Garmoth mapping: ")
                        .Append(remaining.AsSpan(prefix.Length, end - prefix.Length)).Append('.');
                    remaining = remaining[(end + 1)..];
                    continue;
                }

                var matched = false;
                foreach (var (noteSource, noteEnglish) in GarmothResultNotes())
                {
                    var note = " " + noteSource;
                    if (!remaining.StartsWith(note, StringComparison.Ordinal)) continue;
                    translated.Append(' ').Append(noteEnglish);
                    remaining = remaining[note.Length..];
                    matched = true;
                    break;
                }
                if (!matched) return null;
            }
            return translated.ToString();
        }
        return null;
    }
}
