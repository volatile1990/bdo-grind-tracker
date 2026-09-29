namespace BdoGrindTracker.App.Localization;

internal static partial class AppText
{
    private static void AddPersistenceErrors(Dictionary<string, string> text)
    {
        text["Verlauf noch nicht gespeichert. Die Session bleibt in Grindcrest erhalten."] =
            "History has not been saved yet. The session remains in Grindcrest.";
        text["Die aktuelle Session ist noch nicht gespeichert."] = "The current session has not been saved yet.";
        text["Die neue Session konnte noch nicht angelegt werden."] = "The new session could not be created yet.";
        text["Upload abgeschlossen, sein lokaler Status konnte nicht gespeichert werden."] =
            "Upload completed, but its local status could not be saved.";
        text["Das Garmoth-Uploadjournal konnte nicht gelesen werden. Uploads sind zum Schutz vor Dubletten gesperrt."] =
            "The Garmoth upload journal could not be read. Uploads are blocked to prevent duplicates.";
        text["Die Uploadabsicht konnte nicht sicher gespeichert werden. Es wurde nichts gesendet."] =
            "The upload intent could not be saved safely. Nothing was sent.";
        text["Das Upload-Ergebnis konnte lokal nicht gespeichert werden. Die Uploadabsicht bleibt erhalten; weitere Uploads sind zum Schutz vor Dubletten gesperrt."] =
            "The upload result could not be saved locally. The upload intent remains recorded; further uploads are blocked to prevent duplicates.";
    }

    private static string? TranslatePersistenceError(string source)
    {
        const string currentPrefix = "Die aktuelle Session konnte nicht gelesen werden und wird nicht überschrieben. Bitte prüfe die Datei ";
        const string currentEnglish = "The current session could not be read and will not be overwritten. Check the file ";
        const string settingsPrefix = "Die Einstellungen konnten nicht gelesen werden und werden nicht überschrieben. Bitte prüfe die Datei ";
        const string settingsSuffix = " und versuche das erneute Laden und Sichern.";
        const string settingsEnglish = "Settings could not be read and will not be overwritten. Check the file ";
        const string historyPrefix = "Der Verlauf konnte nicht gelesen werden und wird nicht überschrieben. Bitte prüfe die Datei ";
        const string historySuffix = " und versuche das Speichern erneut.";
        const string historyEnglish = "History could not be read and will not be overwritten. Check the file ";

        if (source.StartsWith(currentPrefix, StringComparison.Ordinal) && source.EndsWith(".", StringComparison.Ordinal))
            return currentEnglish + source[currentPrefix.Length..];
        if (source.StartsWith(settingsPrefix, StringComparison.Ordinal) &&
            source.EndsWith(settingsSuffix, StringComparison.Ordinal))
            return settingsEnglish + source[settingsPrefix.Length..^settingsSuffix.Length] +
                " and try loading and saving again.";
        if (source.StartsWith(historyPrefix, StringComparison.Ordinal) &&
            source.EndsWith(historySuffix, StringComparison.Ordinal))
            return historyEnglish + source[historyPrefix.Length..^historySuffix.Length] +
                " and try saving again.";
        return null;
    }
}
