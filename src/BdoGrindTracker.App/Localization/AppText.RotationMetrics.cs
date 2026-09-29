namespace BdoGrindTracker.App.Localization;

internal static partial class AppText
{
    private static void AddRotationMetrics(Dictionary<string, string> text)
    {
        text["Neue Session · warte auf erstes Ereignis"] = "New session · waiting for the first event";
        text["Tracking pausiert · warte auf Rotationsstart"] = "Tracking paused · waiting for rotation start";
        text["Für diesen Spot sind noch keine Rotationsdaten hinterlegt"] = "No rotation data is available for this spot yet";
        text["Warte auf Spot-Erkennung"] = "Waiting for spot detection";
        text["Warte auf Erkennung"] = "Waiting for detection";
        text["Rotation fehlgeschlagen · Warte auf Erkennung"] = "Rotation failed · waiting for detection";
        text["AFK beendet · Warte auf Erkennung"] = "AFK ended · waiting for detection";
        text["Spot-Aufbau neu gestartet"] = "Spot setup restarted";
        text["Session nach Neustart wiederhergestellt · Warte auf Erkennung"] =
            "Session restored after restart · waiting for detection";
        text["Bildsignal unterbrochen · warte auf erstes Ereignis"] =
            "Video signal interrupted · waiting for the first event";
        text["Tracking pausiert · warte auf erstes Ereignis"] =
            "Tracking paused · waiting for the first event";
        text["Erkennung unterbrochen · warte auf erstes Ereignis"] =
            "Detection interrupted · waiting for the first event";
        text["Rotation Monitor beendet"] = "Rotation Monitor stopped";
        text["Tracking pausiert"] = "Tracking paused";
        text["Rotationsreferenzen konnten nicht geladen werden."] = "Rotation references could not be loaded.";
        text["Rotationsreferenzen nicht gespeichert."] = "Rotation references were not saved.";
        text["Rotationserkennung vorübergehend nicht verfügbar."] =
            "Rotation detection is temporarily unavailable.";
        text["Sprache der Spot-Nachrichten"] = "Spot message language";
        text["Wie eingestellte Spielsprache"] = "Use selected game language";
        text["Für Hermesia, Aphrodon, Magaia, Event Horizon und Zephyros. Das passende Windows-OCR-Sprachpaket muss installiert sein. Diese Auswahl gilt nur für Spot-Nachrichten; die Loot-Erkennung unterstützt Deutsch und Englisch."] = "For Hermesia, Aphrodon, Magaia, Event Horizon and Zephyros. The matching Windows OCR language pack must be installed. This selection applies to spot messages only; loot recognition supports German and English.";
        text["Wähle Englisch, Deutsch, Französisch oder Spanisch für die Spot-Nachrichten."] = "Choose English, German, French or Spanish for spot messages.";

        text["Brutstätte aktiviert"] = "Hatchery activated";
        text["Brutstätte bereit"] = "Hatchery ready";
        text["Brutstätte · Wellen"] = "Hatchery · Waves";
        text["Schattenritter und Wellen"] = "Shadow Knights and waves";
        text["Kein Rotationsprofil für diesen Spot"] = "No rotation profile for this spot";
        text["Nach der ersten vollständigen Rotation"] = "After the first complete rotation";
        text["ohne Rückweg"] = "without walk back";
        text["1 Rotation"] = "1 rotation";
        text["letzte {0}"] = "last {0}";
        text["In dieser Session"] = "In this session";
        text["Nach der ersten Rotation ohne Special Event"] = "After the first rotation without a special event";
        text["Special Event läuft"] = "Special event running";
        text["Keine Special Events an diesem Spot"] = "No special events at this spot";
        text["{0} in {1}"] = "{0} in {1}";
        text["Zufällige Zusatz- und Ersatzmechaniken in dieser Session"] = "Random extra and replacing mechanics in this session";
        text["Special Events pro aktiver Stunde"] = "Special events per active hour";
        text["Rotationen mit Special Events werten"] = "Count rotations with special events";
        text["Special Events sind zufällige Mechaniken, die eine volle Rotation nicht braucht und die zusätzlich oder ersetzend auftreten: das Agris-Event in Aphrodon. Ausgeschaltet zählen Rotationen mit Special Event nicht für Bestzeit, Idealrotation, Bestabschnitte und Rotationen / h. In Event Horizon vergleicht der Rotation Monitor immer Rotationen mit gleich vielen Mini-AFKs. Die Fragmente of Divinity in Magaia fallen in fest getimte Phasen und ändern die Rotationslänge nicht; dort wird jede Rotation verglichen."] =
            "Special events are random mechanics a full rotation does not need, appearing in addition or as a replacement: the Agris event at Aphrodon. When off, rotations with a special event do not count for best time, ideal rotation, best sections and rotations / h. At Event Horizon the Rotation Monitor always compares rotations with the same number of mini AFKs. Magaia's fragments of divinity fall into phases with a fixed timing and do not change a rotation's length; there every rotation is compared.";
        text["Special Events sind zufällige Mechaniken, die eine volle Rotation nicht braucht " +
            "und die zusätzlich oder ersetzend auftreten: das Agris-Event in Aphrodon, das Mini-AFK in Event Horizon und die " +
            "Fragmente of Divinity in Magaia. Gezählt wird jedes erkannte Special Event dieser Session, auch in abgebrochenen " +
            "oder laufenden Rotationen."] =
            "Special events are random mechanics a full rotation does not need, appearing in addition or as a replacement: " +
            "the Agris event at Aphrodon, the mini AFK at Event Horizon and the fragments of divinity at Magaia. Every detected " +
            "special event of this session counts, including those in abandoned or running rotations.";
        text["Special Event"] = "Special event";
        text["Tracking-Fehler"] = "Tracking error";
        text["Tracking-Fehler · erwartete Phase nicht erkannt"] = "Tracking error · expected phase not recognised";
        text["Tracking-Fehler · der Beginn dieser Rotation wurde nicht sicher erfasst"] =
            "Tracking error · the beginning of this rotation was not reliably captured";
        text["Referenz mit {0} Special Events"] = "Reference with {0} special events";
        text["Zuletzt {0}"] = "Latest {0}";
        text["Beispieldaten · Hermesia-Session vom {0}"] = "Sample data · Hermesia session from {0}";
        text["Volle Rotationen pro Stunde beim aktuellen Tempo"] = "Complete rotations per hour at the current pace";
        text["Vollendete Rotationen in dieser Session"] = "Completed rotations in this session";
        text["Volle Rotationen pro Stunde beim aktuellen Tempo: 60 Minuten geteilt durch die " +
            "durchschnittliche Zeit der letzten bis zu drei in dieser Session vollständig abgeschlossenen Rotationen, " +
            "jeweils einschließlich Rückweg bis zum Start der nächsten Rotation. Bis die nächste Rotation beginnt, gilt " +
            "der durchschnittliche Rückweg dieser Session. Pausen über zwei Minuten, Aufbau und abgebrochene Versuche zählen nicht."] =
            "Complete rotations per hour at the current pace: 60 minutes divided by the average time of the last up to three " +
            "fully completed rotations in this session, including the walk back to the start of the next rotation. Until " +
            "the next rotation starts, the session's average walk back is used. Breaks over two minutes, setup and abandoned attempts are excluded.";
        text["Vollständig abgeschlossene Rotationen am aktuellen Spot in dieser Session. " +
            "Abgebrochene oder unvollständig erkannte Rotationen zählen nicht."] =
            "Fully completed rotations at the current spot in this session. Abandoned or incompletely detected rotations are excluded.";
    }
}
