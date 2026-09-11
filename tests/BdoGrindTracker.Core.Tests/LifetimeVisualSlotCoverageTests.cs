using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BdoGrindTracker.Core.Tests;

public sealed class LifetimeVisualSlotCoverageTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private static readonly DateTimeOffset Start = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("recording1", 576, 576)]
    [InlineData("recording2", 264, 264)]
    [InlineData("recording3", 2038, 2034)]
    [InlineData("recording4", 604, 600)]
    public void RecordedGlyphOccupancyPreservesTheVerifiedTotals(string name, long helmets, long historicalHelmets)
    {
        // These are replay results, not assertions that every recording is exact.
        // Independent in-game totals for recordings 2/3 are 324/2050; there is no
        // fabricated input for their remaining missing drops.
        var tracker = VisualTracker();
        var duplicate = VisualTracker();
        var historical = new LifetimeLootReconciler(_ => null);
        LifetimeSnapshot? last = null;
        foreach (var frame in Fixture(name))
        {
            last = tracker.ProcessObservations(frame.Rows, frame.At);
            var replay = duplicate.ProcessObservations(frame.Rows, frame.At);
            historical.ProcessObservations(frame.Rows.Select(row => row with { OccupancyEvidence = null }).ToArray(), frame.At);
            Assert.Equal(last.Totals.OrderBy(pair => pair.Key), replay.Totals.OrderBy(pair => pair.Key));
            Assert.Equal(last.ObservedDrops, replay.ObservedDrops);
        }
        var completion = tracker.Complete(last!.At.AddMilliseconds(1));
        Assert.Equal(helmets, completion.Totals[Helmet]);
        Assert.Equal(0, completion.VisualCoverageFallbackCount);
        Assert.Empty(completion.Deltas);
        Assert.Equal(historicalHelmets, historical.Complete(last.At.AddMilliseconds(1)).Totals[Helmet]);
    }

    [Theory]
    [InlineData("single-reading", 0)]
    [InlineData("one-frame-extra-slot", 4)]
    [InlineData("name-glitch", 4)]
    [InlineData("fading-reappearance", 4)]
    public void UnconfirmedOcrDoesNotBecomeAPermanentAdditionalDrop(string scenario, long expected)
    {
        var tracker = VisualTracker();
        LifetimeSnapshot? last = null;
        for (var frame = 0; frame < 26; frame++)
        {
            LootObservation[] rows = scenario switch
            {
                "single-reading" => frame == 0 ? [Row()] : [],
                "one-frame-extra-slot" => frame == 3 ? [Row(), Row(1)] : frame < 7 ? [Row()] : [],
                "name-glitch" => frame < 7 ? [frame == 3 ? Row(name: "Black Stone", quantity: 1) : Row()] : [],
                "fading-reappearance" => frame < 6 || frame == 7 ? [Row()] : [],
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };
            // Real lower-row image evidence must not confirm an unrelated upper
            // OCR alarm. The extra slot has no matching glyph in this guard.
            if (frame > 0 && rows.Length > 0) rows[0] = rows[0] with { OccupancyEvidence = Evidence(0, .99) };
            last = tracker.ProcessObservations(rows, Start.AddMilliseconds(frame * 200));
        }
        Assert.Equal(expected, last!.Totals.GetValueOrDefault(Helmet));
        Assert.Equal(0, last.VisualCoverageFallbackCount);
    }

    [Fact]
    public void CoverageUsesTheValidatedAcceptedFallbackAndSafelyReportsAnImpossibleConstraint()
    {
        var tracker = VisualTracker();
        EstablishOneRow(tracker);
        var rows = Enumerable.Range(0, 5).Select(slot => Row(slot)).ToArray();
        // Same accepted/raw-parser discrepancy as recording 4, frame 291 slot 3:
        // raw parsing cannot identify this truncated text, but validated OCR did.
        rows[4] = rows[4] with { RawText = "io Follower's Helmet x4", NameConfidence = .8695652173913043,
            OccupancyEvidence = Evidence(1, .9382691203892453) };
        // Four new places in 1 ms cannot fit the unchanged birth search space.
        // The confirmed coverage attempt must fall back safely, not empty a lane.
        var snapshot = tracker.ProcessObservations(rows, Start.AddMilliseconds(201));
        Assert.True(snapshot.VisualCoverageFallbackCount > 0);
        Assert.Equal(tracker.VisualCoverageFallbackCount, snapshot.VisualCoverageFallbackCount);
        Assert.Equal(4, snapshot.Lanes.Count);
        Assert.All(snapshot.Lanes, lane => Assert.True(double.IsFinite(lane.LogEvidence)));
        Assert.NotEmpty(snapshot.Totals);
        tracker.Reset();
        Assert.Equal(0, tracker.VisualCoverageFallbackCount);
        var reset = tracker.ProcessObservations(rows, Start.AddMilliseconds(201));
        var fresh = VisualTracker().ProcessObservations(rows, Start.AddMilliseconds(201));
        Assert.Equal(fresh.Lanes, reset.Lanes);
        Assert.Equal(0, reset.VisualCoverageFallbackCount);
    }

    [Theory]
    [InlineData("complete", true)]
    [InlineData("quantityless", false)]
    [InlineData("excluded", false)]
    [InlineData("weak-correlation", false)]
    [InlineData("unstable-name", false)]
    [InlineData("unstable-quantity", false)]
    [InlineData("gap", false)]
    public void OnlyCompleteStableHistoryCanForceSlotCoverage(string condition, bool attempted)
    {
        var tracker = new LifetimeLootReconciler(row => condition == "excluded" && row.Slot == 4
            ? LifetimeParsedReading.Excluded : null, null, true);
        EstablishOneRow(tracker);
        tracker.ProcessObservations([condition switch
        {
            "unstable-name" => Row(name: "Black Stone"),
            "unstable-quantity" => Row(quantity: 5),
            _ => Row(),
        }], Start.AddMilliseconds(250));
        var rows = Enumerable.Range(0, 5).Select(slot => Row(slot)).ToArray();
        rows[4] = rows[4] with { OccupancyEvidence = Evidence(0, condition == "weak-correlation" ? .899 : .99) };
        if (condition == "quantityless") rows[4] = rows[4] with { Quantity = null };
        var result = tracker.ProcessObservations(rows, Start.AddMilliseconds(condition == "gap" ? 851 : 251));
        Assert.Equal(attempted, result.VisualCoverageFallbackCount > 0);
    }

    [Fact]
    public void StaleFramesCannotReplaceTheTwoPreviousVisualRows()
    {
        var tracker = VisualTracker();
        EstablishOneRow(tracker);
        var stale = tracker.ProcessObservations([Row(name: "Injected Item")], Start.AddMilliseconds(100));
        Assert.Empty(stale.Deltas);
        var rows = Enumerable.Range(0, 5).Select(slot => Row(slot)).ToArray();
        rows[4] = rows[4] with { OccupancyEvidence = Evidence(0, .99) };
        var actual = tracker.ProcessObservations(rows, Start.AddMilliseconds(201));
        var baseline = VisualTracker();
        EstablishOneRow(baseline);
        var expected = baseline.ProcessObservations(rows, Start.AddMilliseconds(201));
        Assert.Equal(expected.Lanes, actual.Lanes);
        Assert.Equal(expected.VisualCoverageFallbackCount, actual.VisualCoverageFallbackCount);
        Assert.True(actual.VisualCoverageFallbackCount > 0);
    }

    [Fact]
    public void OccupancyNeverSuppliesAnItemOrQuantity()
    {
        var tracker = VisualTracker();
        for (var frame = 0; frame < 20; frame++)
        {
            var unknown = Row() with { ItemName = null, Quantity = null, RawText = "unreadable",
                RejectionReason = "unrecognized", OccupancyEvidence = Evidence(0, 1) };
            var snapshot = tracker.ProcessObservations([unknown], Start.AddMilliseconds(frame * 200));
            Assert.Empty(snapshot.Totals);
            Assert.Equal(0, snapshot.SupportedDropCount);
        }
    }

    [Fact]
    public void OccupancyIsValidatedSnapshottedAndVersioned()
    {
        var source = new List<NormalLootOccupancyMatch> { new(0, .95) };
        var evidence = new NormalLootOccupancyEvidence(source);
        source[0] = new(4, .1);
        Assert.Equal(new NormalLootOccupancyMatch(0, .95), Assert.Single(evidence.Matches));
        Assert.Throws<NotSupportedException>(() => ((IList<NormalLootOccupancyMatch>)evidence.Matches).Add(new(1, 1)));
        var restored = JsonSerializer.Deserialize<NormalLootOccupancyEvidence>(JsonSerializer.Serialize(evidence))!;
        Assert.Equal(evidence.Matches, restored.Matches);
        restored.Validate();
        foreach (var matches in new NormalLootOccupancyMatch[][]
        {
            [new(-1, .95)], [new(6, .95)], [new(0, double.NaN)], [new(0, double.PositiveInfinity)],
            [new(0, 1.01)], [new(0, -1.01)], [new(0, .95), new(0, .96)], [null!],
        }) Assert.Throws<ArgumentException>(() => new NormalLootOccupancyEvidence(matches).Validate());
        Assert.Throws<ArgumentNullException>(() => new NormalLootOccupancyEvidence(null!));
        Assert.False(new LifetimeLootReconciler().UsesVisualSlotCoverage);
        Assert.False(new LifetimeLootReconciler(_ => null).UsesVisualSlotCoverage);
        Assert.True(VisualTracker().UsesVisualSlotCoverage);
        Assert.Throws<ArgumentException>(() => new LifetimeLootReconciler(null, null, true));
        Assert.Throws<ArgumentException>(() => new LifetimeLootReconciler(_ => null)
            .ProcessObservations([Row() with { OccupancyEvidence = evidence }], Start));
    }

    private static LifetimeLootReconciler VisualTracker() => new(_ => null, null, true);
    private static void EstablishOneRow(LifetimeLootReconciler tracker)
    {
        for (var frame = 0; frame <= 4; frame++)
            tracker.ProcessObservations([Row()], Start.AddMilliseconds(frame * 50));
    }
    private static LootObservation Row(int slot = 0, string name = Helmet, int? quantity = 4) =>
        new(LootSource.Normal, slot, name + " x" + quantity, name, quantity, 1, 1, null, null);
    private static NormalLootOccupancyEvidence Evidence(int slot, double correlation) => new([new(slot, correlation)]);

    private static IEnumerable<(DateTimeOffset At, LootObservation[] Rows)> Fixture(string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "lifetime");
        var evidence = File.ReadLines(Path.Combine(directory, name + ".occupancy.txt"))
            .Where(line => !line.StartsWith('#')).Select(line => line.Split('|')).ToDictionary(
                parts => int.Parse(parts[0], CultureInfo.InvariantCulture),
                parts => parts[1].Split(';').Select(value => value.Split(','))
                    .GroupBy(fields => int.Parse(fields[0], CultureInfo.InvariantCulture))
                    .ToDictionary(group => group.Key, group => new NormalLootOccupancyEvidence(group.Select(fields =>
                        new NormalLootOccupancyMatch(int.Parse(fields[1], CultureInfo.InvariantCulture),
                            double.Parse(fields[2], CultureInfo.InvariantCulture))).ToArray())));
        var sequence = 0;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() } };
        foreach (var line in File.ReadLines(Path.Combine(directory, name + ".raw.txt")).Where(line => !line.StartsWith('#')))
        {
            sequence++;
            using var frame = JsonDocument.Parse(line);
            yield return (frame.RootElement.GetProperty("timestamp").GetDateTimeOffset(),
                frame.RootElement.GetProperty("observations").Deserialize<LootObservation[]>(options)!.Select(row =>
                {
                    return evidence.TryGetValue(sequence, out var matches) && matches.TryGetValue(row.Slot, out var occupancy)
                        ? row with { OccupancyEvidence = occupancy } : row;
                }).ToArray());
        }
    }
}
