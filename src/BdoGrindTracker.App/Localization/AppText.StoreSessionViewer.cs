namespace BdoGrindTracker.App.Localization;

internal static partial class AppText
{
    private static void AddStoreSessionViewer(Dictionary<string, string> text)
    {
        text["Store-Session · Lesemodus"] = "Store session · Read-only view";
        text["Store-Session übernehmen"] = "Take over Store session";
        text["Zum Steuern die Store-App im Tray-Menü beenden und „Store-Session übernehmen“ wählen."] =
            "To use the controls, exit the Store app from its tray menu and choose ‘Take over Store session’.";
        text["Die Store-App läuft noch. Bitte dort im Tray-Menü „Beenden“ wählen und dann die Session übernehmen."] =
            "The Store app is still running. Please choose ‘Exit’ in its tray menu, then take over the session.";
        text["Die Session-Übernahme ist hier nicht verfügbar."] = "Session takeover is not available here.";
        text["Die lokale EXE konnte nicht gestartet werden. Bitte die lokale Ausgabe erneut öffnen."] =
            "The local EXE could not be started. Please reopen the local build.";
        text["Die lokale Ausgabe konnte noch nicht geöffnet werden. Die Store-Ansicht bleibt verfügbar."] =
            "The local build could not be opened yet. The Store view remains available.";
        text["Gespeicherte aktive Zeit · ohne Pausen"] = "Saved active time · excluding pauses";
        text["Gespeicherte Zwischenstände werden automatisch aktualisiert. Die Store-App kann geöffnet bleiben. Änderungen an der Droprate gelten nur für diese Ansicht."] =
            "Saved checkpoints are refreshed automatically. The Store app can stay open. Drop rate changes apply only to this view.";
        text["Diese Ansicht liest die Store-Daten. Änderungen an Sessions sind nur in der Store-App möglich."] =
            "This view reads the Store data. Sessions can only be changed in the Store app.";
        text["Die Store-Daten konnten gerade nicht gelesen werden. Der letzte gültige Stand bleibt sichtbar; ein erneuter Leseversuch folgt."] =
            "The Store data could not be read right now. The last valid snapshot remains visible; another read will be attempted.";
        text["Store-Ansicht · keine aktuelle Session gespeichert."] = "Store view · no current session saved.";
        text["Store-Ansicht · gespeicherter Stand vom {0}."] = "Store view · saved snapshot from {0}.";
        text["Buffs aus dem gespeicherten Store-Stand."] = "Buffs from the saved Store snapshot.";
        text["Sprache aus dem gespeicherten Store-Stand."] = "Language from the saved Store snapshot.";
        text["Gespeicherte Marktpreise sowie NPC- und Festwerte."] = "Saved market prices plus NPC and fixed values.";
    }
}
