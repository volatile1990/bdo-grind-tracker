using System.Globalization;
using System.Text.Json;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class FadeAwareHistoricalReplayTests
{
    [Theory]
    [InlineData("recording1", 576)]
    [InlineData("recording2", 264)]
    [InlineData("recording3", 2038)]
    [InlineData("recording4", 604)]
    public void ArchivedOccupancyWithoutFadeMeasurementsPreservesEveryLegacyProjection(string recording, long helmets)
    {
        // These are frozen replay baselines. The independently reported totals
        // for recordings 2 and 3 were 324 and 2050; missing pixel measurements
        // cannot justify stronger coverage assumptions or repair those inputs.
        var legacy = new LifetimeLootReconciler(_ => null, null, true, useUnreadableSlotCoverage: true);
        var current = new LifetimeLootReconciler(_ => null, null, true,
            useUnreadableSlotCoverage: true, useFadeEvidence: true);
        LifetimeSnapshot? previous = null;
        foreach (var frame in Load(recording))
        {
            Assert.All(frame.Rows, row => Assert.Null(row.FadeEvidence));
            previous = legacy.ProcessObservations(frame.Rows, frame.At);
            AssertEquivalent(previous, current.ProcessObservations(frame.Rows, frame.At));
        }

        var complete = legacy.Complete(previous!.At);
        AssertEquivalent(complete, current.Complete(previous.At));
        Assert.Equal(helmets, complete.Totals["Elion Follower's Helmet"]);
        Assert.Equal(0, complete.VisualCoverageFallbackCount);
    }

    private static void AssertEquivalent(LifetimeSnapshot expected, LifetimeSnapshot actual)
    {
        Assert.Equal(expected.SelectedLifetimeMs, actual.SelectedLifetimeMs);
        Assert.Equal(expected.Totals.OrderBy(pair => pair.Key), actual.Totals.OrderBy(pair => pair.Key));
        Assert.Equal(expected.Deltas.OrderBy(pair => pair.Key), actual.Deltas.OrderBy(pair => pair.Key));
        Assert.Equal(expected.ObservedDrops, actual.ObservedDrops);
        Assert.Equal(expected.PolicyDrops, actual.PolicyDrops);
        Assert.Equal(expected.Lanes, actual.Lanes);
        Assert.Equal(expected.SupportedDropCount, actual.SupportedDropCount);
        Assert.Equal(expected.VisualCoverageFallbackCount, actual.VisualCoverageFallbackCount);
    }

    private static IEnumerable<(DateTimeOffset At, LootObservation[] Rows)> Load(string recording)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "lifetime");
        var evidence = File.ReadLines(Path.Combine(directory, recording + ".occupancy.txt"))
            .Where(line => !line.StartsWith('#')).Select(line => line.Split('|')).ToDictionary(
                parts => int.Parse(parts[0], CultureInfo.InvariantCulture),
                parts => parts[1].Split(';').Select(value => value.Split(','))
                    .GroupBy(fields => int.Parse(fields[0], CultureInfo.InvariantCulture))
                    .ToDictionary(group => group.Key, group => new NormalLootOccupancyEvidence(group.Select(fields =>
                        new NormalLootOccupancyMatch(int.Parse(fields[1], CultureInfo.InvariantCulture),
                            double.Parse(fields[2], CultureInfo.InvariantCulture))).ToArray())));
        var sequence = 0;
        foreach (var line in File.ReadLines(Path.Combine(directory, recording + ".raw.txt")).Where(line => !line.StartsWith('#')))
        {
            sequence++;
            using var document = JsonDocument.Parse(line);
            var frame = document.RootElement;
            // Preserve accepted-parser semantics: the archived ItemName and
            // Quantity are the votes; raw text is not reparsed with a new pool.
            var rows = frame.GetProperty("observations").Deserialize<LootObservation[]>(LootDiagnosticFormat.JsonOptions)!
                .Select(row => evidence.TryGetValue(sequence, out var slots) && slots.TryGetValue(row.Slot, out var occupancy)
                    ? row with { OccupancyEvidence = occupancy } : row).ToArray();
            yield return (frame.GetProperty("timestamp").GetDateTimeOffset(), rows);
        }
    }
}
