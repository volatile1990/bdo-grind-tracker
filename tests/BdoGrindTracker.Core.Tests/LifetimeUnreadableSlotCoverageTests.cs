namespace BdoGrindTracker.Core.Tests;

public sealed class LifetimeUnreadableSlotCoverageTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableOldestRowKeepsFiveDistinctSupportedDrops(bool emptyText)
    {
        var tracker = Tracker();
        LifetimeSnapshot? last = null;
        for (var frame = 0; frame < 40; frame++)
        {
            var count = frame switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 or 5 or 6 => 4, 7 => 3, 8 or 9 => 2, 10 => 1, _ => 0 };
            var rows = Enumerable.Range(0, count).Select(Row).ToList();
            if (frame == 6) rows.Add(Unknown(4, emptyText ? 0 : 3, emptyText ? "" : "Elioti"));
            last = tracker.ProcessObservations(rows, Start.AddMilliseconds(frame * 200));
        }
        Assert.Equal(20, last!.Totals[Helmet]);
        Assert.Equal(5, last.SupportedDropCount);
        Assert.All(last.PolicyDrops, drop => Assert.Equal(4, drop.Quantity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableOldestRowRetainsItsOwnPreviouslyReadNameAndQuantity(bool differentNames)
    {
        var tracker = Tracker();
        var drops = Enumerable.Range(0, 5).Select(index =>
            (Name: differentNames ? $"Item {index}" : Helmet, Quantity: (index + 1) * 4)).ToArray();
        LifetimeSnapshot? last = null;
        for (var frame = 0; frame < 40; frame++)
        {
            int[] visible = frame switch
            {
                1 => [0], 2 => [1, 0], 3 => [2, 1, 0], 4 or 5 => [3, 2, 1, 0],
                6 => [4, 3, 2, 1], 7 => [4, 3, 2], 8 or 9 => [4, 3], 10 => [4], _ => []
            };
            var rows = visible.Select((index, slot) => Row(slot) with
            {
                RawText = $"{drops[index].Name} x{drops[index].Quantity}",
                ItemName = drops[index].Name,
                Quantity = drops[index].Quantity
            }).ToList();
            if (frame == 6) rows.Add(Unknown(4, 3));
            last = tracker.ProcessObservations(rows, Start.AddMilliseconds(frame * 200));
        }

        Assert.Equal(5, last!.SupportedDropCount);
        Assert.Equal(drops.GroupBy(drop => drop.Name).OrderBy(group => group.Key)
            .Select(group => (group.Key, Quantity: group.Sum(drop => (long)drop.Quantity))),
            last.Totals.OrderBy(pair => pair.Key).Select(pair => (pair.Key, Quantity: pair.Value)));
    }

    [Theory]
    [InlineData("no-evidence")]
    [InlineData("wrong-previous-slot")]
    [InlineData("weak-correlation")]
    [InlineData("conflicting-item")]
    [InlineData("excluded")]
    [InlineData("outside-pool")]
    [InlineData("anchor")]
    [InlineData("gap")]
    public void UnconfirmedOrConflictingOccupancyDoesNotChangeTheHistoricalCounter(string scenario)
    {
        LifetimeParsedReading? Parse(LootObservation row) => scenario == "excluded" && row.Slot == 4
            ? LifetimeParsedReading.Excluded : null;
        var tracker = new LifetimeLootReconciler(Parse, null, true, useUnreadableSlotCoverage: true);
        var legacy = new LifetimeLootReconciler(Parse, null, true);
        for (var frame = 0; frame < 35; frame++)
        {
            var count = frame switch { 0 => 1, 1 => 2, 2 => 3, 3 or 4 or 5 => 4, 6 => 3, 7 => 2, 8 => 1, _ => 0 };
            var rows = Enumerable.Range(0, count).Select(Row).ToList();
            if (frame == 5)
            {
                var unknown = Unknown(4, scenario == "wrong-previous-slot" ? 5 : 3);
                unknown = scenario switch
                {
                    "no-evidence" => unknown with { OccupancyEvidence = null },
                    "weak-correlation" => unknown with { OccupancyEvidence = new([new(3, .899)]) },
                    "conflicting-item" => unknown with { ItemName = "Black Stone", RejectionReason = null },
                    "anchor" => unknown with { IsAlignmentAnchor = true },
                    "outside-pool" => unknown with { RejectionReason = AutomaticLootSpotLock.OutsideSpotPoolReason },
                    _ => unknown,
                };
                rows.Add(unknown);
            }
            var at = Start.AddMilliseconds(frame * 200 + (scenario == "gap" && frame >= 5 ? 601 : 0));
            var actual = tracker.ProcessObservations(rows, at);
            var expected = legacy.ProcessObservations(rows, at);
            Assert.Equal(expected.Totals.OrderBy(pair => pair.Key), actual.Totals.OrderBy(pair => pair.Key));
            Assert.Equal(expected.Lanes, actual.Lanes);
        }
    }

    [Theory]
    [InlineData("suffix-name")]
    [InlineData("suffix-quantity")]
    [InlineData("interior-hole")]
    [InlineData("two-unreadable")]
    [InlineData("same-size")]
    [InlineData("one-previous")]
    public void UnsupportedStackShapeOrSuffixDoesNotChangeTheHistoricalCounter(string scenario)
    {
        var tracker = Tracker();
        var legacy = new LifetimeLootReconciler(_ => null, null, true);
        for (var frame = 0; frame < 35; frame++)
        {
            var count = frame switch { 0 => 1, 1 => 2, 2 => 3, 3 or 4 or 5 => 4, 6 => 3, 7 => 2, 8 => 1, _ => 0 };
            if (scenario == "one-previous" && frame == 4) count = 1;
            var rows = Enumerable.Range(0, count).Select(Row).ToList();
            if (frame == 5)
            {
                if (scenario == "suffix-name") rows[2] = rows[2] with { ItemName = "Black Stone", RawText = "Black Stone x4" };
                if (scenario == "suffix-quantity") rows[2] = rows[2] with { Quantity = 8, RawText = Helmet + " x8" };
                if (scenario == "interior-hole") rows[1] = Unknown(1, 3);
                if (scenario is "two-unreadable" or "same-size") rows[3] = Unknown(3, 3);
                if (scenario != "same-size") rows.Add(Unknown(4, 3));
            }

            var at = Start.AddMilliseconds(frame * 200);
            var actual = tracker.ProcessObservations(rows, at);
            var expected = legacy.ProcessObservations(rows, at);
            Assert.Equal(expected.Totals.OrderBy(pair => pair.Key), actual.Totals.OrderBy(pair => pair.Key));
            Assert.Equal(expected.Lanes, actual.Lanes);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void UnreadableTailBelowTheFiveSlotBoundaryKeepsEveryHistoricalEstimate(int occupiedSlots)
    {
        var tracker = Tracker();
        var legacy = new LifetimeLootReconciler(_ => null, null, true);
        var readableSlots = occupiedSlots - 1;
        for (var frame = 0; frame < 35; frame++)
        {
            var count = frame < 6 ? Math.Min(frame, readableSlots) :
                frame == 6 ? readableSlots : Math.Max(0, readableSlots - (frame - 6));
            var rows = Enumerable.Range(0, count).Select(Row).ToList();
            if (frame == 6) rows.Add(Unknown(readableSlots, 0));

            var at = Start.AddMilliseconds(frame * 200);
            var actual = tracker.ProcessObservations(rows, at);
            var expected = legacy.ProcessObservations(rows, at);
            Assert.Equal(expected.Totals.OrderBy(pair => pair.Key), actual.Totals.OrderBy(pair => pair.Key));
            Assert.Equal(expected.SupportedDropCount, actual.SupportedDropCount);
            Assert.Equal(expected.Lanes, actual.Lanes);
        }
    }

    [Fact]
    public void AlreadyOccupiedUnreadablePreviousTailIsNotTreatedAsAnEmptyPosition()
    {
        var tracker = Tracker();
        var legacy = new LifetimeLootReconciler(_ => null, null, true);
        for (var frame = 0; frame < 35; frame++)
        {
            var count = frame switch
            {
                0 => 0, 1 => 1, 2 => 2, 3 or 4 or 5 => 3, 6 => 4,
                7 => 3, 8 => 2, 9 => 1, _ => 0
            };
            var rows = Enumerable.Range(0, count).Select(Row).ToList();
            // The fourth position was already occupied before its text became
            // readable. A three-row OCR prefix must not imply a two-row scroll.
            if (frame is 4 or 5)
                rows.Add(Unknown(3, 0, "Elioti") with { RejectionReason = "native-catalog-miss" });
            if (frame == 6) rows.Add(Unknown(4, 0));

            var at = Start.AddMilliseconds(frame * 200);
            var actual = tracker.ProcessObservations(rows, at);
            var expected = legacy.ProcessObservations(rows, at);
            Assert.Equal(expected.Totals.OrderBy(pair => pair.Key), actual.Totals.OrderBy(pair => pair.Key));
            Assert.Equal(expected.SupportedDropCount, actual.SupportedDropCount);
            Assert.Equal(expected.Lanes, actual.Lanes);
        }
    }

    [Fact]
    public void UnreadableGlyphsNeverSupplyNamesOrQuantities()
    {
        var tracker = Tracker();
        for (var frame = 0; frame < 30; frame++)
        {
            var rows = Enumerable.Range(0, 5).Select(slot => Unknown(slot, slot)).ToArray();
            var result = tracker.ProcessObservations(rows, Start.AddMilliseconds(frame * 200));
            Assert.Empty(result.Totals);
            Assert.Equal(0, result.SupportedDropCount);
        }
    }

    [Fact]
    public void OptInRequiresTheNormalFiveSlotVisualCounter()
    {
        Assert.False(new LifetimeLootReconciler(_ => null, null, true).UsesUnreadableSlotCoverage);
        Assert.True(Tracker().UsesUnreadableSlotCoverage);
        Assert.Throws<ArgumentException>(() => new LifetimeLootReconciler(_ => null, null, false, useUnreadableSlotCoverage: true));
        Assert.Throws<ArgumentException>(() => new LifetimeLootReconciler(_ => null, null, true,
            LootSource.Rare, 1, useUnreadableSlotCoverage: true));
    }

    private static LifetimeLootReconciler Tracker() => new(_ => null, null, true, useUnreadableSlotCoverage: true);
    private static LootObservation Row(int slot) => new(LootSource.Normal, slot, Helmet + " x4", Helmet, 4, 1, 1, null, null);
    private static LootObservation Unknown(int slot, int previousSlot, string text = "") =>
        new(LootSource.Normal, slot, text, null, null, 0, 0, null, "visual-occupancy-only")
        { OccupancyEvidence = new([new(previousSlot, .99)]) };
}
