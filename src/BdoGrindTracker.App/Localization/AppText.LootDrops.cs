namespace BdoGrindTracker.App.Localization;

internal static partial class AppText
{
    private static void AddLootDrops(Dictionary<string, string> text)
    {
        text["Gerade eben"] = "Just now";
        text["vor {0} s"] = "{0}s ago";
        text["vor {0} Min."] = "{0}m ago";
        text["vor {0} Std. {1} Min."] = "{0}h {1}m ago";
        text["Letzter Drop: {0} · aktive Grindzeit, ohne Pausen."] = "Last drop: {0} · active grind time, excluding pauses.";
        text["Für dieses Item wurde noch kein Zeitpunkt des letzten Drops erfasst."] = "No last drop time has been recorded for this item yet.";
    }
}
