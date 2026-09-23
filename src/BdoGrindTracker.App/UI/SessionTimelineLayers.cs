namespace BdoGrindTracker.App.UI;

/// <param name="Id">Stable key, also used by the saved selection.</param>
public sealed record SessionTimelineLayer(string Id, string Label, string Color);

/// <summary>The layers of the session timeline. Rotations, rare drops and the silver curve are on by default.</summary>
public static class SessionTimelineLayers
{
    public static readonly IReadOnlyList<SessionTimelineLayer> All =
    [
        new("rotations", "Rotationen", "#9275BE"),
        new("rare", "Seltene Drops & Favoriten", "#F2C14E"),
        new("silver", "Silber je Abschnitt", "#D8BD75"),
        new("special", "Special Events", "#66D8C7"),
        new("trash", "Trashloot", "#78A88B"),
    ];

    public static readonly IReadOnlyList<string> Default = ["rotations", "rare", "silver"];

    public static string ColorOf(string id) => All.FirstOrDefault(layer => layer.Id == id)?.Color ?? "#9AA7B4";

    /// <summary>Colors for the items chosen in the loot selector, apart from the layers' own ones.</summary>
    public static readonly IReadOnlyList<string> ItemColors =
        ["#6E8AA8", "#C77C8E", "#7FC98B", "#C9A26E", "#8E7FD0", "#5FB3C4", "#C4746E", "#9FB86A"];

    public static string ItemColorOf(int index) => ItemColors[Math.Abs(index) % ItemColors.Count];
}
