namespace BdoGrindTracker.App.Theming;

/// <summary>Stable saved identifiers shared by the UI and native overlay.</summary>
public static class AppThemes
{
    public const string Grindcrest = "grindcrest";
    public const string BlackDesert = "black-desert";
    public const string Light = "light";
    public const string Cats = "cats";

    public static bool IsKnown(string? themeId) => themeId is Grindcrest or BlackDesert or Light or Cats;

    public static string Normalize(string? themeId) => IsKnown(themeId) ? themeId! : Grindcrest;
}
