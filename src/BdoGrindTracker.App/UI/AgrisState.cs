namespace BdoGrindTracker.App.UI;

public enum AgrisStatus
{
    Unknown,
    Inactive,
    Active,
}

/// <summary>A visual Agris observation used by the live session and its duration record.</summary>
public sealed record AgrisState(AgrisStatus Status, DateTimeOffset? ObservedAt = null)
{
    public static AgrisState Unknown { get; } = new(AgrisStatus.Unknown);
}
