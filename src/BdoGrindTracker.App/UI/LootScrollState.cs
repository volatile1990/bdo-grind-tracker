namespace BdoGrindTracker.App.UI;

public enum LootScrollStatus
{
    Unknown,
    Inactive,
    Active,
}

/// <summary>A confirmed observation shared by the live session and its overlay.</summary>
public sealed record LootScrollState(
    LootScrollStatus Status,
    int? Level = null,
    DateTimeOffset? ObservedAt = null)
{
    public static LootScrollState Unknown { get; } = new(LootScrollStatus.Unknown);
    public bool ShouldWarn => Status == LootScrollStatus.Inactive;
}
