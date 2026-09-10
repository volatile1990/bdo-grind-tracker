namespace BdoGrindTracker.App.UI;

/// <summary>The character level and its three-decimal HUD percentage from one observation.</summary>
public sealed record ExperienceState(int? Level = null, decimal? Percent = null, DateTimeOffset? ObservedAt = null)
{
    public static ExperienceState Unknown { get; } = new();
    public bool IsKnown => Level is >= 1 and <= 100 && Percent is >= 0 and < 100;
}
