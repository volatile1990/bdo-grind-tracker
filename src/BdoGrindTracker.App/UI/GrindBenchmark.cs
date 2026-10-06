using System.Collections.ObjectModel;

namespace BdoGrindTracker.App.UI;

public sealed record GrindBenchmark(string SpotId, decimal AverageTrashPerHour, decimal? HighTrashPerHour,
    decimal? TopTrashPerHour, DateTimeOffset UpdatedAt, string SourceUrl, string Conditions)
{
    private readonly IReadOnlyDictionary<string, decimal>? _rareDropHourlyRates;

    /// <summary>Hourly rare-drop averages at Garmoth's public, anonymous 100% setting.
    /// Keys are canonical local item names; values retain the source precision.</summary>
    public IReadOnlyDictionary<string, decimal>? RareDropHourlyRates
    {
        get => _rareDropHourlyRates;
        init => _rareDropHourlyRates = value is null ? null : new RareDropRates(value);
    }

    /// <summary>False for Garmoth's no-scroll spots, where drop-rate bonuses do not apply.</summary>
    public bool RareDropRateScalingApplies { get; init; } = true;

    /// <summary>The displayed Garmoth average trash/hour from the same reporting window
    /// as the rare-drop rates, at that spot's Lv.2/no-Agris reference conditions.
    /// This may differ from AverageTrashPerHour when the rating reference has another age.</summary>
    public decimal? RareDropReferenceTrashPerHour { get; init; }

    /// <summary>Separate provenance when rare-drop and trash references have different ages.</summary>
    public DateTimeOffset? RareDropUpdatedAt { get; init; }
    public string? RareDropSourceUrl { get; init; }

    // Preserve record value equality after a JSON cache round trip. Copying also prevents
    // callers from changing a benchmark after it has been published to the UI.
    private sealed class RareDropRates(IReadOnlyDictionary<string, decimal> values)
        : ReadOnlyDictionary<string, decimal>(new Dictionary<string, decimal>(values, StringComparer.Ordinal))
    {
        public override bool Equals(object? obj) => obj is RareDropRates other && Count == other.Count &&
            this.All(pair => other.TryGetValue(pair.Key, out var value) && value == pair.Value);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var pair in this.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                hash.Add(pair.Key, StringComparer.Ordinal);
                hash.Add(pair.Value);
            }
            return hash.ToHashCode();
        }
    }
}

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
