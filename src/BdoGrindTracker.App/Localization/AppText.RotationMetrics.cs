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
