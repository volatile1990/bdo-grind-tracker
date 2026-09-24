namespace BdoGrindTracker.App.Localization;

internal static partial class AppText
{
    private static void AddRotationMetrics(Dictionary<string, string> text)
    {
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
        text["Special Events sind zufällige Mechaniken, die eine volle Rotation nicht braucht und die zusätzlich oder ersetzend auftreten: das Agris-Event in Aphrodon. Ausgeschaltet zählen Rotationen mit Special Event nicht für Bestzeit, Idealrotation, Bestabschnitte und Rotations / h. In Event Horizon vergleicht der Rotation Monitor immer Rotationen mit gleich vielen Mini-AFKs. Die Fragmente of Divinity in Magaia fallen in fest getimte Phasen und ändern die Rotationslänge nicht; dort wird jede Rotation verglichen."] =
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
