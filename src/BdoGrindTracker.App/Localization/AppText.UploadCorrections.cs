namespace BdoGrindTracker.App.Localization;

internal static partial class AppText
{
    private static void AddUploadCorrections(Dictionary<string, string> text)
    {
        const string explanation = "Loot quantities changed after an hour was completed. Please review the remaining grind in the upload preview and send it manually.";
        const string source = "Lootmengen haben sich nach Stundenabschluss geändert. Bitte die Vorschau für den verbleibenden Grind prüfen und manuell senden.";
        text[source] = explanation;
        text["Lootmenge gespeichert. " + source] = "Loot quantity saved. " + explanation;
    }
}
