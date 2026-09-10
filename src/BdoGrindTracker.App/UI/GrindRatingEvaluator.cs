using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.UI;

internal static class GrindRatingEvaluator
{
    private static readonly TimeSpan ProvisionalDuration = TimeSpan.FromMinutes(5);
    private static readonly GrindRatingResult Unavailable = new(GrindRatingTier.Unavailable, null, null);

    internal static GrindRatingResult Evaluate(string? spotId, long trashQuantity, TimeSpan elapsed, GrindBenchmark? benchmark)
    {
        if (string.IsNullOrWhiteSpace(spotId) || !LootSpotCatalog.Spots.Any(spot => spot.Id == spotId) ||
            benchmark is null || benchmark.SpotId != spotId || trashQuantity < 0 || elapsed <= TimeSpan.Zero ||
            !HasSource(benchmark.SourceUrl) ||
            benchmark.AverageTrashPerHour <= 0 ||
            benchmark.HighTrashPerHour is { } high && high <= benchmark.AverageTrashPerHour ||
            benchmark.TopTrashPerHour is { } top && top <= (benchmark.HighTrashPerHour ?? benchmark.AverageTrashPerHour))
            return Unavailable;

        decimal rate;
        try
        {
            // Match the live session's Presentation.Hourly decimal arithmetic;
            // formatting, early-session smoothing and overlay age play no role.
            rate = trashQuantity / ((decimal)elapsed.Ticks / TimeSpan.TicksPerHour);
        }
        catch (OverflowException)
        {
            return Unavailable;
        }

        var tier = benchmark.TopTrashPerHour is { } topRate && rate >= topRate ? GrindRatingTier.Top :
            benchmark.HighTrashPerHour is { } highRate && rate >= highRate ? GrindRatingTier.High :
            rate >= benchmark.AverageTrashPerHour ? GrindRatingTier.Average : GrindRatingTier.BelowAverage;
        return new(tier, rate, benchmark, elapsed < ProvisionalDuration);
    }

    private static bool HasSource(string sourceUrl) => Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && !string.IsNullOrWhiteSpace(uri.Host);
}
