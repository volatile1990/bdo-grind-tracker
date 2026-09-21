namespace BdoGrindTracker.App.Theming;

/// <summary>Stable saved identifiers shared by the UI and native overlay.</summary>
public static class AppThemes
{
    public const string Grindcrest = "grindcrest";
    public const string BlackDesert = "black-desert";
    public const string Light = "light";
    public const string Cats = "cats";
    public const string Obsidian = "obsidian";
    public const string Kamasylvia = "kamasylvia";
    public const string Valencia = "valencia";

    public static bool IsKnown(string? themeId) => themeId is Grindcrest or BlackDesert or Light or Cats or
        Obsidian or Kamasylvia or Valencia;

    public static string Normalize(string? themeId) => IsKnown(themeId) ? themeId! : Grindcrest;

    /// <summary>Missing or retired overlay themes follow the main window's selection.</summary>
    public static string? NormalizeOverlay(string? themeId) => IsKnown(themeId) ? themeId : null;
}
