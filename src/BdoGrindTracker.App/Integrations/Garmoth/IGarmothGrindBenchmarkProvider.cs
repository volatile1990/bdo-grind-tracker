using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Integrations.Garmoth;

internal interface IGarmothGrindBenchmarkProvider : IDisposable
{
    GarmothBenchmarkSnapshot GetCachedSnapshot();
    Task<GarmothBenchmarkSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}

internal sealed record GarmothBenchmarkSnapshot(IReadOnlyList<GrindBenchmark> Benchmarks, string Status)
{
    internal GrindBenchmark? Find(string? spotId) => Benchmarks.FirstOrDefault(value => value.SpotId == spotId);
    internal static GarmothBenchmarkSnapshot Bundled { get; } = new(GarmothGrindBenchmarks.All,
        "Mitgelieferter Garmoth-Referenzstand. Beim Start wird nach aktuellen Werten gesucht.");
}
