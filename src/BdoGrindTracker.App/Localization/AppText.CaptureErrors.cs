namespace BdoGrindTracker.App.Localization;

internal static partial class AppText
{
    private static void AddCaptureErrors(Dictionary<string, string> text)
    {
        // These messages originate in the capture/service layer and may be shown
        // directly or inside a translated status such as "Tracking gestoppt: {0}".
        text["Das Black-Desert-Spielfenster ist minimiert oder nicht sichtbar. Fenster wiederherstellen und Tracking erneut starten."] =
            "The Black Desert game window is minimized or not visible. Restore the window and restart tracking.";
        text["Das Black-Desert-Spielfenster besitzt keinen sichtbaren Spielbereich."] =
            "The Black Desert game window has no visible game area.";
        text["Die Spielfensteraufnahme wurde nicht vorbereitet."] = "Game window capture was not prepared.";
        text["Der Aufnahmebereich stimmt nicht mit dem gebundenen Spielfenster überein."] =
            "The capture area does not match the selected game window.";
        text["Die Größe des Black-Desert-Spielfensters hat sich geändert. Tracking bitte erneut starten und die Kalibrierung prüfen."] =
            "The Black Desert game window size changed. Restart tracking and check calibration.";
        text["Die Fensteraufnahme und die Spielbereichsgröße stimmen nicht überein. Tracking bitte erneut starten."] =
            "The captured window and game area sizes do not match. Restart tracking.";
        text["Der Spielbereich liegt außerhalb des aufgenommenen Fensters."] =
            "The game area is outside the captured window.";
        text["Kein sichtbares Black-Desert-Spielfenster gefunden. Spiel öffnen und minimierte Fenster wiederherstellen."] =
            "No visible Black Desert game window was found. Open the game and restore minimized windows.";
        text["Das aufgenommene Black-Desert-Spielfenster wurde geschlossen. Tracking bitte erneut starten."] =
            "The captured Black Desert game window was closed. Restart tracking.";
        text["Das aufgenommene Black-Desert-Spielfenster wurde geschlossen."] =
            "The captured Black Desert game window was closed.";
        text["Windows unterstützt die Spielfensteraufnahme auf diesem System nicht."] =
            "Windows does not support game window capture on this system.";
        text["Der HDR-Modus des Spielfensters hat sich geändert. Tracking bitte erneut starten."] =
            "The game window's HDR mode changed. Restart tracking.";
        text["Die Geometrie des Spielfensters hat sich während der Aufnahme geändert. Tracking bitte erneut starten."] =
            "The game window geometry changed during capture. Restart tracking.";
        text["Black Desert liefert keine neuen Fensterbilder. Spiel sichtbar öffnen und Tracking erneut starten."] =
            "Black Desert is not providing new window frames. Make the game visible and restart tracking.";
        text["Die aktuelle Grafikoberfläche enthält den Spielbereich nicht vollständig."] =
            "The current graphics surface does not fully contain the game area.";
        text["Automatisches Tracking wartet, bis Black Desert im Vordergrund ist."] =
            "Automatic tracking is waiting for Black Desert to be in the foreground.";
        text["Die Vordergrunderkennung für automatisches Tracking konnte nicht gestartet werden."] =
            "Foreground detection for automatic tracking could not be started.";
        text["Das Spielfenster hat sich während des Autostarts geändert."] =
            "The game window changed during automatic start.";
        text["Die Spielfenstergröße passt nicht zur BDO-Konfiguration. Bitte UI-Konfiguration speichern."] =
            "The game window size does not match the BDO configuration. Save the UI configuration.";
        text["Das Spielfenster ist für den Autostart-Bildpuffer zu groß."] =
            "The game window is too large for the automatic start frame buffer.";
        text["Erfassung derzeit nicht verfügbar."] = "Capture is currently unavailable.";
        text["Die Aufnahme läuft bereits."] = "Capture is already running.";
        text["Bitte die laufende Aufnahme zuerst pausieren."] = "Please pause the current capture first.";
        text["Die abgebrochene Texterkennung wird noch beendet."] =
            "The cancelled text recognition is still stopping.";
        text["Die Texterkennung hat nicht rechtzeitig geantwortet. Tracking wurde gestoppt; der zuletzt erkannte Stand bleibt erhalten."] =
            "Text recognition did not respond in time. Tracking was stopped; the last detected state has been preserved.";
        text["Ein Autostart-Bild konnte nicht übernommen werden."] =
            "An automatic start frame could not be transferred.";
        text["Ein aufgenommenes Bild konnte nicht zur OCR-Verarbeitung übergeben werden."] =
            "A captured frame could not be passed to OCR processing.";
        text["Die Grafikkarte hat das aufgenommene Bild nicht rechtzeitig bereitgestellt. Tracking wurde gestoppt; bitte erneut starten."] =
            "The graphics card did not provide the captured frame in time. Tracking was stopped; please restart.";
        text["Die Fensteraufnahme wurde wegen eines Grafikfehlers gestoppt. Tracking bitte erneut starten."] =
            "Game window capture stopped because of a graphics error. Restart tracking.";
        text["Direct3D hat einen Grafikgerätefehler gemeldet."] =
            "Direct3D reported a graphics device error.";
        text["Die Position des Haupt-Droplogs konnte nicht erkannt werden. Aktiviere das Droplog in der BDO-Oberfläche, speichere die UI-Einstellungen und starte Grindcrest neu. Ohne diesen Bereich ist kein Tracking möglich."] =
            "The main drop log position could not be detected. Enable the drop log in the BDO UI, save the UI settings, and restart Grindcrest. Tracking requires this area.";
        text["Die gespeicherte Droplog-Position passt nicht zur Größe des aufgenommenen Spielbilds. Prüfe die BDO-Auflösung und speichere die UI-Einstellungen im Spiel. Starte Grindcrest danach neu."] =
            "The saved drop log position does not match the captured game image size. Check the BDO resolution and save the UI settings in the game. Then restart Grindcrest.";
        text["Das BDO-Profil oder die Anzeigeeinstellungen haben sich geändert. Das Tracking wurde angehalten. Starte Grindcrest neu, damit der richtige Bereich erfasst wird."] =
            "The BDO profile or display settings changed. Tracking was stopped. Restart Grindcrest to capture the correct area.";
        text["Die neue Droplog-Position verändert die sichtbaren Zeilen am Bildschirmrand. Platziere das Droplog vollständig im Spielbild, speichere die BDO-UI-Einstellungen und starte Grindcrest neu."] =
            "The new drop log position changes the visible rows at the screen edge. Place the drop log fully inside the game image, save the BDO UI settings, and restart Grindcrest.";
        text["Die Item-Liste des Grindspots fehlt oder ist leer."] =
            "The grind spot item list is missing or empty.";
        text["BDO-Companion-Erkennung wurde beendet."] = "BDO Companion detection was stopped.";
        text["Englische Windows-Texterkennung fehlt."] = "English Windows text recognition is unavailable.";
        text["Der Aufnahmebereich muss eine positive Breite und Höhe haben."] =
            "The capture area must have a positive width and height.";
        text["Ein erforderliches COM-Objekt fehlt."] = "A required COM object is missing.";
        text["Die DXGI-Zeilenbreite ist zu klein."] = "The DXGI row pitch is too small.";
        text["Die letzte Session wurde pausiert wiederhergestellt. Du kannst sie fortsetzen."] =
            "The last session was restored in a paused state. You can resume it.";
        text["Kein neuer Loot mit mindestens einer vollen Minute seit dem letzten Upload vorhanden."] =
            "There is no new loot with at least one full minute since the last upload.";
    }
}
