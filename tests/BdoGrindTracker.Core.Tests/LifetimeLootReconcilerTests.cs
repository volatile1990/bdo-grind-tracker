using System.Globalization;

namespace BdoGrindTracker.Core.Tests;

public sealed class LifetimeLootReconcilerTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("recording1", 576, 148, 1350)]
    [InlineData("recording2", 264, 68, 1350)]
    [InlineData("recording3", 2034, 385, 1350)]
    [InlineData("recording4", 600, 153, 1450)]
    public void RecordedAcceptedInputsReproduceHistoricalLifetimeV1Results(string name, long helmets, int drops,
        int lifetime)
    {
        // Historical replay baselines, not accuracy targets. The independently
        // confirmed 2050/604 helmet references for recordings 3/4 are recorded
        // separately in fixtures/lifetime/reference-sessions.json.
        var tracker = new LifetimeLootReconciler();
        var duplicate = new LifetimeLootReconciler();
        LifetimeSnapshot last = tracker.Complete(Start);
        var negativeFrames = 0;
        foreach (var frame in Fixture(name))
        {
            last = tracker.ProcessFrame(frame.Rows, frame.At);
            var repeated = duplicate.ProcessFrame(frame.Rows, frame.At);
            Assert.Equal(last.Totals.OrderBy(pair => pair.Key), repeated.Totals.OrderBy(pair => pair.Key));
            Assert.Equal(last.ObservedDrops, repeated.ObservedDrops);
            if (last.Deltas.Values.Any(delta => delta < 0)) negativeFrames++;
        }
        var completion = tracker.Complete(last.At.AddMilliseconds(1));
        Assert.Empty(completion.Deltas);
        Assert.Equal(helmets, completion.Totals[Helmet]);
        Assert.Equal(drops, completion.SupportedDropCount);
        Assert.Equal(lifetime, completion.SelectedLifetimeMs);
        Assert.NotNull(completion.LatestArrivalAt);
        Assert.True(negativeFrames > 0);
        if (name == "recording1")
        {
            Assert.Equal(3, completion.Totals["Black Stone"]);
            Assert.Equal(1, completion.Totals["Caphras Stone"]);
            Assert.Equal(3, completion.Totals.Count);
        }
        else if (name == "recording2")
        {
            // The source has no accepted evidence for the two Dust groups hidden
            // under the boss popup. Do not turn the user's 7 into invented input.
            Assert.Equal(4, completion.Totals["Ancient Spirit Dust"]);
            Assert.Equal(2, completion.Totals.Count);
        }
        else if (name == "recording3")
        {
            Assert.Equal(38, completion.Totals["Ancient Spirit Dust"]);
            Assert.Equal(8, completion.Totals["Black Stone"]);
            Assert.Equal(10, completion.Totals["Caphras Stone"]);
            Assert.Equal(1, completion.Totals["Fusion Shard"]);
            Assert.Equal(1, completion.Totals["JIN Origin Shard"]);
            Assert.Equal(1, completion.Totals["Nev's Fragment"]);
            Assert.Equal(7, completion.Totals.Count);
        }
        else if (name == "recording4")
        {
            Assert.Equal(2, completion.Totals["Black Stone"]);
            Assert.Equal(1, completion.Totals["Caphras Stone"]);
            Assert.Equal(1, completion.Totals["Ancient Spirit Dust"]);
            Assert.Equal(4, completion.Totals.Count);
        }
    }

    [Fact]
    public void SingleReadingCanAppearImmediatelyAndBeRetractedByLaterEvidence()
    {
        var tracker = new LifetimeLootReconciler();
        var first = tracker.ProcessFrame([Row(4)], Start);
        Assert.Equal(4, first.Totals[Helmet]);
        Assert.Equal(1, first.SupportedDropCount);
        var drop = Assert.Single(first.ObservedDrops);
        Assert.Equal(Helmet, drop.Name);
        Assert.Equal(4, drop.Quantity);
        Assert.NotEqual(Guid.Empty, drop.EventId);
        Assert.True(drop.DetectedAt <= Start);
        Assert.Equal(drop.DetectedAt, first.LatestArrivalAt);
        var negative = false;
        var last = first;
        for (var frame = 1; frame <= 20; frame++)
        {
            last = tracker.ProcessFrame([], Start.AddMilliseconds(frame * 200));
            negative |= last.Deltas.Values.Any(delta => delta < 0);
        }
        Assert.True(negative);
        Assert.Empty(last.Totals);
        Assert.Equal(0, last.SupportedDropCount);
        Assert.Empty(last.PolicyDrops);
        Assert.Equal(4, Assert.Single(first.PolicyDrops).Quantity);
        Assert.Equal(4, first.Totals[Helmet]); // Earlier snapshots remain immutable.
    }

    [Fact]
    public void RepeatedQuantityEvidenceRepairsAnOutlierWithoutPermanentBooking()
    {
        var tracker = new LifetimeLootReconciler();
        var negative = false;
        var snapshots = new List<LifetimeSnapshot>();
        for (var frame = 0; frame <= 20; frame++)
        {
            var snapshot = tracker.ProcessFrame(frame < 7 ? [Row(frame == 2 ? 400u : 4u)] : [],
                Start.AddMilliseconds(frame * 200));
            snapshots.Add(snapshot);
            negative |= snapshot.Deltas.Values.Any(delta => delta < 0);
        }
        Assert.True(negative);
        Assert.Equal(4, snapshots[^1].Totals[Helmet]);
        Assert.Equal(1, snapshots[^1].SupportedDropCount);
        Assert.Equal(4, Assert.Single(snapshots[^1].PolicyDrops).Quantity);
        var revision = snapshots[^1].Revision;
        var complete = tracker.Complete(Start.AddSeconds(5));
        Assert.Equal(revision, complete.Revision);
        Assert.Empty(complete.Deltas);
    }

    [Fact]
    public void PolicyHistoryReplacesTheFirstProvisionalQuantityForTheSameBirthIdentity()
    {
        var tracker = new LifetimeLootReconciler();
        var initial = tracker.ProcessFrame([Row(400)], Start);
        var first = Assert.Single(initial.PolicyDrops);
        Assert.Equal(400, first.Quantity);
        tracker.ProcessFrame([Row(4)], Start.AddMilliseconds(200));
        var corrected = tracker.ProcessFrame([Row(4)], Start.AddMilliseconds(400));
        var sample = Assert.Single(corrected.PolicyDrops);
        Assert.Equal(first.EventId, sample.EventId);
        Assert.Equal(4, sample.Quantity);
        Assert.Equal(400, Assert.Single(initial.PolicyDrops).Quantity);
    }

    [Fact]
    public void SettledTotalsKeepDropCountAndLastArrivalWithoutRetainingOldRows()
    {
        var tracker = new LifetimeLootReconciler();
        var first = tracker.ProcessFrame([Row(4)], Start);
        var last = first;
        for (var frame = 1; frame <= 100; frame++)
            last = tracker.ProcessFrame(frame < 7 ? [Row(4)] : [], Start.AddMilliseconds(frame * 200));
        Assert.Equal(4, last.Totals[Helmet]);
        Assert.Equal(1, last.SupportedDropCount);
        Assert.Equal(first.LatestArrivalAt, last.LatestArrivalAt);
        Assert.Empty(last.ObservedDrops);
        Assert.Equal(Assert.Single(first.ObservedDrops), Assert.Single(last.PolicyDrops));
    }

    [Fact]
    public void SnapshotCollectionsCannotBeMutated()
    {
        var snapshot = new LifetimeLootReconciler().ProcessFrame([Row(4)], Start);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, long>)snapshot.Totals).Add("Injected", 1));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, long>)snapshot.Deltas).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<LaneEstimate>)snapshot.Lanes).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<LifetimeObservedDrop>)snapshot.ObservedDrops).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<LifetimeObservedDrop>)snapshot.PolicyDrops).Clear());
    }

    [Fact]
    public void DuplicateAndOlderFramesDoNotLearnOrEmitAgain()
    {
        var tracker = new LifetimeLootReconciler();
        var reference = new LifetimeLootReconciler();
        var first = tracker.ProcessFrame([Row(4)], Start);
        reference.ProcessFrame([Row(4)], Start);
        foreach (var timestamp in new[] { Start, Start.AddMilliseconds(-200), Start.AddTicks(100) })
        {
            var ignored = tracker.ProcessFrame([Row(500)], timestamp);
            Assert.Equal(first.Frame, ignored.Frame);
            Assert.Equal(first.Revision, ignored.Revision);
            Assert.Equal(first.At, ignored.At);
            Assert.Empty(ignored.Deltas);
        }
        var actual = tracker.ProcessFrame([Row(4)], Start.AddMilliseconds(200));
        var expected = reference.ProcessFrame([Row(4)], Start.AddMilliseconds(200));
        Assert.Equal(expected.Lanes, actual.Lanes);
        Assert.Equal(expected.ObservedDrops, actual.ObservedDrops);
    }

    [Fact]
    public void StaleFrameBeforeTheFirstReadableQuantityDoesNotStartTheEstimator()
    {
        var tracker = new LifetimeLootReconciler();
        tracker.ProcessFrame([], Start);
        var ignored = tracker.ProcessFrame([Row(4)], Start.AddMilliseconds(-1));
        Assert.Equal(0, ignored.Frame);
        Assert.Empty(ignored.Totals);
        Assert.Equal(1, tracker.ProcessFrame([Row(4)], Start.AddMilliseconds(200)).Frame);
    }

    [Fact]
    public void InvalidRowsAreRejectedBeforeChangingState()
    {
        var tracker = new LifetimeLootReconciler();
        var reference = new LifetimeLootReconciler();
        tracker.ProcessFrame([Row(4)], Start);
        reference.ProcessFrame([Row(4)], Start);
        foreach (var input in new CompanionRecognizedEntry[][]
        {
            [null!], [Row(4) with { Slot = -1 }], [Row(4) with { Slot = 6 }],
            [Row(4), Row(4)], [Row(4) with { NameConfidence = double.NaN }],
            [Row(4) with { NameConfidence = 1.1 }],
        })
            Assert.Throws<ArgumentException>(() => tracker.ProcessFrame(input, Start.AddMilliseconds(100)));
        var actual = tracker.ProcessFrame([Row(4)], Start.AddMilliseconds(200));
        var expected = reference.ProcessFrame([Row(4)], Start.AddMilliseconds(200));
        Assert.Equal(expected.Lanes, actual.Lanes);
        Assert.Equal(expected.ObservedDrops, actual.ObservedDrops);
    }

    [Fact]
    public void ResetRestoresInitialLearningProjectionAndDeterministicBirthIds()
    {
        var tracker = new LifetimeLootReconciler();
        var first = tracker.ProcessFrame([Row(4)], Start);
        tracker.ProcessFrame([Row(400)], Start.AddMilliseconds(200));
        tracker.Reset();
        var empty = tracker.Complete(Start);
        Assert.Equal(0, empty.Frame);
        Assert.Equal(0, empty.Revision);
        Assert.Empty(empty.Totals);
        Assert.Equal(0, empty.SupportedDropCount);
        Assert.Null(empty.LatestArrivalAt);
        Assert.Empty(empty.PolicyDrops);
        var repeated = tracker.ProcessFrame([Row(4)], Start);
        Assert.Equal(first.ObservedDrops, repeated.ObservedDrops);
        Assert.Equal(first.Lanes, repeated.Lanes);
        Assert.Equal(first.Deltas, repeated.Deltas);
    }

    [Fact]
    public void EmptySixthCropAndUnknownQuantityDoNotBecomeCountedItems()
    {
        var tracker = new LifetimeLootReconciler();
        var result = tracker.ProcessFrame([Row(4) with { Slot = 5 }, Row(uint.MaxValue)], Start);
        Assert.Empty(result.Totals);
        Assert.Equal(0, result.Frame);
        Assert.Empty(result.ObservedDrops);
    }

    [Fact]
    public void PolicyHistoryRetainsOnlyTheLatestSixtyFourSupportedDropsAfterSettlement()
    {
        var tracker = new LifetimeLootReconciler();
        LifetimeSnapshot last = tracker.Complete(Start);
        for (var frame = 0; frame <= 1000; frame++)
        {
            // Seventy isolated messages, each visible for 1.4 s then a 0.6 s gap.
            var ms = frame * 200;
            last = tracker.ProcessFrame(ms < 140000 && ms % 2000 < 1400 ? [Row(4)] : [],
                Start.AddMilliseconds(ms));
            Assert.InRange(last.PolicyDrops.Count, 0, 64);
            Assert.Equal(last.PolicyDrops.Count, last.PolicyDrops.Select(drop => drop.EventId).Distinct().Count());
        }
        Assert.Equal(70 * 4, last.Totals[Helmet]);
        Assert.Equal(70, last.SupportedDropCount);
        Assert.Empty(last.ObservedDrops);
        Assert.Equal(64, last.PolicyDrops.Count);
        Assert.True(last.PolicyDrops[0].DetectedAt > Start.AddSeconds(10));
        Assert.Equal(last.LatestArrivalAt, last.PolicyDrops[^1].DetectedAt);
    }

    [Fact]
    public void LongCaptureGapsAndLargeValidQuantitiesAvoidIntegerOverflow()
    {
        var tracker = new LifetimeLootReconciler();
        var rows = Enumerable.Range(0, 5).Select(slot => Row(int.MaxValue) with { Slot = slot, }).ToArray();
        var first = tracker.ProcessFrame(rows, DateTimeOffset.MinValue);
        Assert.Equal(5L * int.MaxValue, first.Totals[Helmet]);
        Assert.All(first.ObservedDrops, drop => Assert.Equal(DateTimeOffset.MinValue, drop.DetectedAt));
        var later = tracker.ProcessFrame([Row(4)], DateTimeOffset.MaxValue);
        Assert.True(later.Totals[Helmet] >= first.Totals[Helmet]);
    }

    private static CompanionRecognizedEntry Row(uint quantity) => new(Helmet, quantity, 250)
        { Slot = 0, NameConfidence = 1 };

    private static IEnumerable<(DateTimeOffset At, CompanionRecognizedEntry[] Rows)> Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "lifetime", name + ".txt");
        foreach (var line in File.ReadLines(path).Where(line => !line.StartsWith('#')))
        {
            var parts = line.Split('|');
            yield return (DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture),
                parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value =>
                {
                    var fields = value.Split(',');
                    var slot = int.Parse(fields[0], CultureInfo.InvariantCulture);
                    return new CompanionRecognizedEntry(fields[1], fields[2] == "?" ? uint.MaxValue :
                        uint.Parse(fields[2], CultureInfo.InvariantCulture), 250 - slot * 50)
                    { Slot = slot, NameConfidence = double.Parse(fields[3], CultureInfo.InvariantCulture) };
                }).ToArray());
        }
    }
}
