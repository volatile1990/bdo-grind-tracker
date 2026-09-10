namespace BdoGrindTracker.App.UI;

public sealed record GrindBenchmark(string SpotId, decimal AverageTrashPerHour, decimal? HighTrashPerHour,
    decimal? TopTrashPerHour, DateTimeOffset UpdatedAt, string SourceUrl, string Conditions);

public enum GrindRatingTier
{
    Unavailable,
    BelowAverage,
    Average,
    High,
    Top,
}

public sealed record GrindRatingResult(GrindRatingTier Tier, decimal? TrashPerHour,
    GrindBenchmark? Benchmark, bool IsProvisional = false);
