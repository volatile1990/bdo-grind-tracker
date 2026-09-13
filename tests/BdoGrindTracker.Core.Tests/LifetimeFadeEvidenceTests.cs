namespace BdoGrindTracker.Core.Tests;

public sealed class LifetimeFadeEvidenceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static LifetimeLootReconciler Counter(bool fade = true) =>
        new(_ => null, null, true, useUnreadableSlotCoverage: true, useFadeEvidence: fade);

    [Fact]
    public void FadeModeRequiresTheNormalUnreadableVisualCounter()
    {
        Assert.True(Counter().UsesFadeEvidence);
        Assert.False(Counter(false).UsesFadeEvidence);
        Assert.Throws<ArgumentException>(() => new LifetimeLootReconciler(_ => null, null, true, useFadeEvidence: true));
        Assert.Throws<ArgumentException>(() => new LifetimeLootReconciler(_ => null, null, true,
            LootSource.Rare, 1, useFadeEvidence: true));
        Assert.Throws<ArgumentException>(() => Counter(false).ProcessObservations([Row(0) with
            { FadeEvidence = new(.5, .99) }], Start));
    }

    [Theory]
    [InlineData(double.NaN, .99)]
    [InlineData(double.PositiveInfinity, .99)]
    [InlineData(-.01, .99)]
    [InlineData(4.01, .99)]
    [InlineData(.5, double.NaN)]
    [InlineData(.5, 1.01)]
    public void MalformedMeasurementsAreRejected(double ratio, double correlation)
    {
        Assert.Throws<ArgumentException>(() => Counter().ProcessObservations([Row(0) with
            { FadeEvidence = new(ratio, correlation) }], Start));
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("rejected")]
    [InlineData("outside-pool")]
    [InlineData("anchor")]
    public void IneligibleOcrCannotCarryAgeEvidence(string reason)
    {
        var row = Row(0) with { FadeEvidence = new(.5, .99) };
        row = reason switch
        {
            "anonymous" => row with { ItemName = null, Quantity = null },
            "rejected" => row with { RejectionReason = "native-catalog-miss" },
            "outside-pool" => row with { RejectionReason = AutomaticLootSpotLock.OutsideSpotPoolReason },
            "anchor" => row with { IsAlignmentAnchor = true },
            _ => throw new InvalidOperationException(),
        };
        Assert.Throws<ArgumentException>(() => Counter().ProcessObservations([row], Start));
    }

    [Fact]
    public void MeasuredFadingNeverSuppliesAnUnreadAmount()
    {
        var counter = Counter();
        for (var frame = 0; frame < 20; frame++)
        {
            var row = Row(0) with { Quantity = null, RawText = "Any Item", FadeEvidence = new(.5, .99) };
            var snapshot = counter.ProcessObservations([row], Start.AddMilliseconds(frame * 200));
            Assert.Empty(snapshot.Totals);
            Assert.Equal(0, snapshot.SupportedDropCount);
        }
    }

    [Fact]
    public void RealNewDropAfterEmptyFrameRetainsItsReadQuantity()
    {
        var counter = Counter();
        for (var frame = 0; frame < 30; frame++)
        {
            var local = frame < 10 ? frame : frame - 10;
            var rows = local is >= 1 and <= 7
                ? new[] { Row(0) with { Quantity = frame < 10 ? 52 : 84,
                    FadeEvidence = local == 7 ? new(.3, .99) : null } }
                : [];
            counter.ProcessObservations(rows, Start.AddMilliseconds(frame * 200));
        }
        var result = counter.Complete(Start.AddSeconds(6));
        Assert.Equal(136, result.Totals["Any Item"]);
        Assert.Equal(2, result.SupportedDropCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoFadingMeasurementsPreserveThePreviousVisualCounterIncludingAfterReset(bool reset)
    {
        var candidate = Counter();
        var historical = Counter(false);
        if (reset)
        {
            candidate.ProcessObservations([Row(0) with { FadeEvidence = new(.5, .99) }], Start.AddSeconds(-1));
            candidate.Reset();
        }
        for (var frame = 0; frame < 50; frame++)
        {
            var count = (frame % 20) switch { 1 => 1, 2 => 2, 3 or 4 or 5 => 3, 6 or 7 => 2, 8 => 1, _ => 0 };
            var rows = Enumerable.Range(0, count).Select(slot => Row(slot) with
                { OccupancyEvidence = new([new(0, .99)]) }).ToArray();
            var at = Start.AddMilliseconds(frame * 200);
            var actual = candidate.ProcessObservations(rows, at);
            var expected = historical.ProcessObservations(rows, at);
            Assert.Equal(expected.Totals.OrderBy(pair => pair.Key), actual.Totals.OrderBy(pair => pair.Key));
            Assert.Equal(expected.SupportedDropCount, actual.SupportedDropCount);
            Assert.Equal(expected.Lanes, actual.Lanes);
        }
    }

    [Fact]
    public void FadingOlderRowsDoNotHideAVisiblyOccupiedFourthRow()
    {
        var counter = Counter();
        var births = new[] { 150, 550, 950, 1150 };
        LifetimeSnapshot? result = null;
        for (var milliseconds = 0; milliseconds <= 3200; milliseconds += 200)
        {
            var ages = births.Where(born => born <= milliseconds && milliseconds - born < 1400)
                .OrderDescending().Select(born => milliseconds - born).ToArray();
            var rows = ages.Select((age, slot) => Row(slot) with
            {
                Quantity = 37,
                RawText = "Any Item x37",
                OccupancyEvidence = new([new(0, .99)]),
                FadeEvidence = age >= 1000 ? new((1400 - age) / 400d, .99) : null,
            }).ToArray();
            result = counter.ProcessObservations(rows, Start.AddMilliseconds(milliseconds));
        }

        Assert.Equal(4, result!.SupportedDropCount);
        Assert.Equal(148, result.Totals["Any Item"]);
        Assert.Equal(0, result.VisualCoverageFallbackCount);
    }

    [Fact]
    public void IndistinguishableBrightFullPanelsStillAllowNewArrivals()
    {
        var counter = Counter();
        var rows = Enumerable.Range(0, 5).Select(slot => Row(slot) with
        {
            Quantity = 37,
            RawText = "Any Item x37",
            OccupancyEvidence = new([new(0, 1)]),
        }).ToArray();

        LifetimeSnapshot? result = null;
        for (var frame = 0; frame < 20; frame++)
            result = counter.ProcessObservations(rows, Start.AddMilliseconds(frame * 200));

        // Bright identical glyphs establish occupied positions, not identities.
        // Several notification lifetimes pass while the panel remains full.
        Assert.True(result!.SupportedDropCount > 5);
        Assert.Equal(result.SupportedDropCount * 37, result.Totals["Any Item"]);
        Assert.Equal(4, result.Lanes.Count);
        Assert.All(result.Lanes, lane => Assert.True(double.IsFinite(lane.LogEvidence)));
        Assert.Equal(0, result.VisualCoverageFallbackCount);
    }

    [Fact]
    public void AConfirmedFadingTailDoesNotRequireAnotherArrivalAtEstimatedExpiry()
    {
        var counter = Counter();
        var captureTimes = new[] { 0, 200, 400, 600, 800, 1000, 1200, 1420, 1620, 1800, 2000, 2200 };
        var counts = new[] { 2, 3, 4, 5, 5, 3, 3, 2, 2, 2, 0, 0 };
        LifetimeSnapshot? result = null;
        for (var frame = 0; frame < captureTimes.Length; frame++)
        {
            var rows = Enumerable.Range(0, counts[frame]).Select(slot => Row(slot) with
            {
                OccupancyEvidence = new(Enumerable.Range(0, 5).Select(previous => new NormalLootOccupancyMatch(previous, .99)).ToArray()),
                FadeEvidence = (frame, slot) switch
                {
                    (8, 0) => new(.71, .99),
                    (8, 1) => new(.44, .99),
                    (9, 0) => new(.26, .99),
                    _ => null,
                },
            }).ToArray();
            result = counter.ProcessObservations(rows, Start.AddMilliseconds(captureTimes[frame]));
        }

        // The same five rows fade; the oldest surviving glyph is slightly later
        // than one lane's estimated expiry, within its original capture margin.
        Assert.Equal(5, result!.SupportedDropCount);
        Assert.Equal(20, result.Totals["Any Item"]);
        Assert.Equal(0, result.VisualCoverageFallbackCount);
    }

    [Fact]
    public void NewArrivalsRemainPossibleWhileAFullPanelsOldestRowsFade()
    {
        var counter = Counter();
        var births = new[] { -400, -300, -200, -100, 0, 1000, 1200, 1400, 1600, 1800 };
        LifetimeSnapshot? result = null;
        for (var milliseconds = 0; milliseconds <= 3800; milliseconds += 200)
        {
            var ages = births.Where(born => born <= milliseconds && milliseconds - born < 1400)
                .OrderDescending().Take(5).Select(born => milliseconds - born).ToArray();
            var rows = ages.Select((age, slot) => Row(slot) with
            {
                OccupancyEvidence = new(Enumerable.Range(0, 5).Select(previous => new NormalLootOccupancyMatch(previous, .99)).ToArray()),
                FadeEvidence = age >= 1000 ? new((1400 - age) / 400d, .99) : null,
            }).ToArray();
            result = counter.ProcessObservations(rows, Start.AddMilliseconds(milliseconds));
        }

        Assert.Equal(10, result!.SupportedDropCount);
        Assert.Equal(40, result.Totals["Any Item"]);
        Assert.Equal(0, result.VisualCoverageFallbackCount);
    }

    private static LootObservation Row(int slot) =>
        new(LootSource.Normal, slot, "Any Item x4", "Any Item", 4, 1, 1, null, null);
}

