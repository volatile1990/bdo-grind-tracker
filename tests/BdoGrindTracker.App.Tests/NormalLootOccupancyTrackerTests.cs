using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using OpenCvSharp;
using DrawingRectangle = System.Drawing.Rectangle;
using CvPoint = OpenCvSharp.Point;

namespace BdoGrindTracker.App.Tests;

public sealed class NormalLootOccupancyTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 11, 6, 24, 0, TimeSpan.Zero);
    private static readonly DrawingRectangle[] Slots = [new(0, 50, 400, 50), new(0, 0, 400, 50)];
    private static readonly DrawingRectangle[] RecordedSlots =
    [new(60, 373, 451, 74), new(60, 298, 451, 75), new(60, 224, 451, 75),
        new(60, 149, 451, 75), new(60, 75, 451, 75), new(60, 0, 451, 75)];

    [Theory]
    [InlineData(282, 283)]
    [InlineData(290, 291)]
    public void RecordedFourthRowHasGlyphEvidenceEvenWhenItsCurrentTextIsFading(int previousFrame, int currentFrame)
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var before = Fixture(previousFrame);
        using var after = Fixture(currentFrame);
        var previous = Enumerable.Range(0, 3).Select(Observation).ToArray();
        var current = Enumerable.Range(0, 4).Select(Observation).ToArray();
        if (currentFrame == 291)
            current[3] = current[3] with { RawText = "io Follower's Helmet x 4", NameConfidence = .8695652186870575 };
        Assert.All(tracker.Observe(before, RecordedSlots, previous, Start, 1.49f), row => Assert.Null(row.OccupancyEvidence));

        var result = tracker.Observe(after, RecordedSlots, current, Start.AddMilliseconds(200), 1.49f);

        Assert.NotNull(result[3].OccupancyEvidence);
        Assert.All(result[3].OccupancyEvidence!.Matches, match => Assert.InRange(match.Correlation, .90, 1));
        if (currentFrame == 291)
        {
            var expectedTemplate = Assert.Single(result[3].OccupancyEvidence!.Matches, match => match.PreviousSlot == 2);
            Assert.Equal(.9085547949300876, expectedTemplate.Correlation, 10);
            // The stronger equally named template is not the true predecessor.
            Assert.Contains(result[3].OccupancyEvidence!.Matches, match => match.PreviousSlot == 1 && match.Correlation > expectedTemplate.Correlation);
            using var forward = new NormalLootAppearanceTracker();
            forward.Observe(before, RecordedSlots, previous, Start, 1.49f);
            Assert.Null(forward.Observe(after, RecordedSlots, current, Start.AddMilliseconds(200), 1.49f)[3].AppearanceEvidence);
        }
        Assert.Equal(current, result.Select(row => row with { OccupancyEvidence = null }).ToArray());
    }

    [Fact]
    public void RecordedEmptyAdditionalSlotDoesNotGainEvidenceFromInventedAcceptedOcr()
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var before = Fixture(282);
        using var after = Fixture(283);
        tracker.Observe(before, RecordedSlots, Enumerable.Range(0, 3).Select(Observation).ToArray(), Start, 1.49f);
        var current = Enumerable.Range(0, 6).Select(Observation).ToArray();

        var result = tracker.Observe(after, RecordedSlots, current, Start.AddMilliseconds(200), 1.49f);

        Assert.NotNull(result[3].OccupancyEvidence);
        Assert.Null(result[4].OccupancyEvidence);
        Assert.Null(result[5].OccupancyEvidence);
        Assert.Equal(current, result.Select(row => row with { OccupancyEvidence = null }).ToArray());
    }

    [Theory]
    [InlineData(427, 428, false)]
    [InlineData(2383, 2384, true)]
    public void OptInRecordedFifthRowRetainsOccupancyWhenOcrOmitsOrCannotReadIt(
        int previousFrame, int currentFrame, bool hasUnreadableRow)
    {
        using var tracker = new NormalLootOccupancyTracker(allowUnreadableRows: true);
        using var before = UnreadableFixture(previousFrame);
        using var after = UnreadableFixture(currentFrame);
        var previous = Enumerable.Range(0, previousFrame == 427 ? 3 : 4).Select(Observation).ToArray();
        var current = Enumerable.Range(0, 4).Select(Observation).ToList();
        var unreadable = new LootObservation(LootSource.Normal, 4, "Elioti", null, null, 0, 0, null, "ocr-width-or-empty")
        {
            NativeY = RecordedSlots[4].Y
        };
        if (hasUnreadableRow) current.Add(unreadable);
        tracker.Observe(before, RecordedSlots, previous, Start, 1.49f);

        var result = tracker.Observe(after, RecordedSlots, current, Start.AddMilliseconds(200), 1.49f);

        Assert.Equal(5, result.Count);
        var fifth = Assert.Single(result, row => row.Slot == 4);
        Assert.NotNull(fifth.OccupancyEvidence);
        Assert.All(fifth.OccupancyEvidence!.Matches, match => Assert.InRange(match.Correlation, .90, 1));
        Assert.Null(fifth.ItemName);
        Assert.Null(fifth.Quantity);
        Assert.Equal(RecordedSlots[4].Y, fifth.NativeY);
        Assert.Equal(hasUnreadableRow ? unreadable :
            new LootObservation(LootSource.Normal, 4, "", null, null, 0, 0, null, "visual-occupancy-only")
            { NativeY = RecordedSlots[4].Y }, fifth with { OccupancyEvidence = null });
        Assert.Equal(current.Take(4), result.Take(4).Select(row => row with { OccupancyEvidence = null }));
        Assert.DoesNotContain(result, row => row.Slot == 5);
    }

    [Fact]
    public void OptInUnknownRowPreservesRawFieldsAndEveryMatchingPreviousSlot()
    {
        using var tracker = new NormalLootOccupancyTracker(allowUnreadableRows: true);
        using var image = Panel(1, 1);
        tracker.Observe(image, Slots, [Observation(0), Observation(1) with { ItemName = "Black Stone" }], Start, 1);
        var unknown = new LootObservation(LootSource.Normal, 0, "Unreadable x 500", null, 500, .2, .3, 17, "native-catalog-miss")
        {
            NativeY = Slots[0].Y,
            QuantityBounds = new(2, 1000)
        };

        var result = tracker.Observe(image, Slots, [unknown], Start.AddMilliseconds(200), 1);

        var measured = Assert.Single(result, row => row.Slot == 0);
        Assert.Equal(unknown, measured with { OccupancyEvidence = null });
        Assert.Equal(new[] { 0, 1 }, measured.OccupancyEvidence!.Matches.Select(match => match.PreviousSlot));
        Assert.Null(measured.AppearanceEvidence);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("anchor")]
    [InlineData("low-confidence")]
    [InlineData("different-name")]
    [InlineData("outside-spot-pool")]
    public void OptInCannotReplaceAnExplicitIneligibleNormalRowWithAnonymousOccupancy(string kind)
    {
        using var tracker = new NormalLootOccupancyTracker(allowUnreadableRows: true);
        using var image = Panel(1, 0);
        tracker.Observe(image, Slots, [Observation(0)], Start, 1);
        var current = kind == "outside-spot-pool"
            ? Observation(0) with { ItemName = null, RejectionReason = AutomaticLootSpotLock.OutsideSpotPoolReason }
            : Ineligible(Observation(0), kind);
        current = current with { OccupancyEvidence = new([new(0, .99)]) };

        var result = tracker.Observe(image, Slots, [current], Start.AddMilliseconds(200), 1);

        Assert.Equal(current with { OccupancyEvidence = null }, Assert.Single(result));
    }

    [Fact]
    public void OptInCannotBypassExplicitConflictThroughADuplicateUnknownRow()
    {
        using var tracker = new NormalLootOccupancyTracker(allowUnreadableRows: true);
        using var image = Panel(1, 0);
        tracker.Observe(image, Slots, [Observation(0)], Start, 1);
        var unknown = Observation(0) with { ItemName = null, Quantity = null, RejectionReason = "ocr-geometry" };
        var conflict = Observation(0) with { ItemName = "Black Stone" };

        var result = tracker.Observe(image, Slots, [unknown, conflict], Start.AddMilliseconds(200), 1);

        Assert.Equal(new[] { unknown, conflict }, result);
        Assert.All(result, row => Assert.Null(row.OccupancyEvidence));
    }

    [Fact]
    public void OptInAnonymousEvidenceDoesNotBecomeATemplateForTheFollowingFrame()
    {
        using var tracker = new NormalLootOccupancyTracker(allowUnreadableRows: true);
        using var image = Panel(1, 0);
        tracker.Observe(image, Slots, [Observation(0)], Start, 1);
        var anonymous = Assert.Single(tracker.Observe(image, Slots, [], Start.AddMilliseconds(200), 1));
        Assert.NotNull(anonymous.OccupancyEvidence);

        Assert.Empty(tracker.Observe(image, Slots, [], Start.AddMilliseconds(400), 1));
    }

    [Fact]
    public void OptInEmptyBackgroundNeverCreatesAnAnonymousRow()
    {
        using var tracker = new NormalLootOccupancyTracker(allowUnreadableRows: true);
        using var before = Panel(1, 1);
        using var background = Panel(0, 0);
        tracker.Observe(before, Slots, [Observation(0), Observation(1)], Start, 1);

        Assert.Empty(tracker.Observe(background, Slots, [], Start.AddMilliseconds(200), 1));
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("reset")]
    [InlineData("scale")]
    [InlineData("geometry")]
    [InlineData("stale")]
    public void OptInDiscontinuityCannotCreateAnonymousRowsFromAnOldTemplate(string change)
    {
        using var tracker = new NormalLootOccupancyTracker(allowUnreadableRows: true);
        using var image = Panel(1, 0);
        tracker.Observe(image, Slots, [Observation(0)], Start, 1);
        if (change == "reset") tracker.Reset();
        var slots = change == "geometry" ? new[] { new DrawingRectangle(1, 50, 399, 50), Slots[1] } : Slots;
        var at = Start.AddMilliseconds(change == "gap" ? 601 : change == "stale" ? 0 : 200);

        Assert.Empty(tracker.Observe(image, slots, [], at, change == "scale" ? .95f : 1));
    }

    [Fact]
    public void EqualItemTemplatesConfirmOccupancyWithoutSelectingAnIdentityOrChangingOcr()
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var image = Panel(1, 1);
        tracker.Observe(image, Slots, [Observation(0), Observation(1)], Start, 1);
        var current = new[]
        {
            Observation(1) with { Quantity = 500, QuantityConfidence = .3, RawText = "original OCR text" },
            Observation(0) with { Quantity = null, QuantityConfidence = 0, VisualFingerprint = 17 }
        };

        var result = tracker.Observe(image, Slots, current, Start.AddMilliseconds(200), 1);

        Assert.Equal(current, result.Select(row => row with { OccupancyEvidence = null }).ToArray());
        Assert.All(result, row =>
        {
            Assert.NotNull(row.OccupancyEvidence);
            Assert.Equal(new[] { 0, 1 }, row.OccupancyEvidence!.Matches.Select(match => match.PreviousSlot));
            row.OccupancyEvidence.Validate();
            Assert.Null(row.AppearanceEvidence);
        });
    }

    [Fact]
    public void AllSixPhysicalSlotsRemainAvailableWithoutCreatingObservations()
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var image = Panel(1, 1, 1, 1, 1, 1);
        var slots = Enumerable.Range(0, 6).Select(slot => new DrawingRectangle(0, (5 - slot) * 50, 400, 50)).ToArray();
        tracker.Observe(image, slots, [Observation(5)], Start, 1);

        var result = tracker.Observe(image, slots, [Observation(5)], Start.AddMilliseconds(200), 1);

        var observation = Assert.Single(result);
        Assert.Equal(5, observation.Slot);
        Assert.Equal(5, Assert.Single(observation.OccupancyEvidence!.Matches).PreviousSlot);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("rejected")]
    [InlineData("rare")]
    [InlineData("anchor")]
    [InlineData("low-confidence")]
    [InlineData("no-name")]
    [InlineData("different-name")]
    public void PreviousTemplateRequiresAnActuallyAcceptedCompatibleName(string kind)
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var image = Panel(1, 0);
        var previous = kind == "missing" ? Array.Empty<LootObservation>() : new[] { Ineligible(Observation(0), kind) };
        tracker.Observe(image, Slots, previous, Start, 1);
        var result = tracker.Observe(image, Slots, [Observation(0)], Start.AddMilliseconds(200), 1);
        Assert.Null(Assert.Single(result).OccupancyEvidence);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("rare")]
    [InlineData("anchor")]
    [InlineData("low-confidence")]
    [InlineData("no-name")]
    [InlineData("different-name")]
    public void IneligibleCurrentOcrNeverReceivesOrRetainsOccupancyEvidence(string kind)
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var image = Panel(1, 0);
        tracker.Observe(image, Slots, [Observation(0)], Start, 1);
        var current = Ineligible(Observation(0), kind) with { OccupancyEvidence = new([new(0, .99)]) };
        var result = tracker.Observe(image, Slots, [current], Start.AddMilliseconds(200), 1);
        Assert.Equal(current with { OccupancyEvidence = null }, Assert.Single(result));
        Assert.NotNull(current.OccupancyEvidence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.01)]
    public void BlankOrVanishedPreviousGlyphsCannotServeAsTemplates(double alpha)
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var before = Panel(alpha, 0);
        using var after = Panel(1, 0);
        tracker.Observe(before, Slots, [Observation(0)], Start, 1);
        Assert.Null(tracker.Observe(after, Slots, [Observation(0)], Start.AddMilliseconds(200), 1)[0].OccupancyEvidence);
    }

    [Fact]
    public void FadingCurrentGlyphsCanUseThePreviousBrightTemplate()
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var before = Panel(1, 0);
        using var after = Panel(.25, 0);
        tracker.Observe(before, Slots, [Observation(0)], Start, 1);
        Assert.NotNull(tracker.Observe(after, Slots, [Observation(0)], Start.AddMilliseconds(200), 1)[0].OccupancyEvidence);
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("scale")]
    [InlineData("geometry")]
    [InlineData("panel-size")]
    [InlineData("reset")]
    public void ValidCaptureDiscontinuityReseedsBeforeMatchingAgain(string change)
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var image = Panel(1, 1);
        using var resized = new Mat();
        Cv2.CopyMakeBorder(image, resized, 0, 1, 0, 0, BorderTypes.Replicate);
        tracker.Observe(image, Slots, [Observation(0)], Start, 1);
        if (change == "reset") tracker.Reset();
        var slots = change == "geometry" ? new[] { new DrawingRectangle(1, 50, 399, 50), Slots[1] } : Slots;
        var at = Start.AddMilliseconds(change == "gap" ? 601 : 200);
        var scale = change == "scale" ? .95f : 1;
        var current = change == "panel-size" ? resized : image;

        Assert.Null(tracker.Observe(current, slots, [Observation(0)], at, scale)[0].OccupancyEvidence);
        Assert.NotNull(tracker.Observe(current, slots, [Observation(0)], at.AddMilliseconds(200), scale)[0].OccupancyEvidence);
    }

    [Fact]
    public void ExactlySixHundredMillisecondsStillMatches()
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var image = Panel(1, 0);
        tracker.Observe(image, Slots, [Observation(0)], Start, 1);
        Assert.NotNull(tracker.Observe(image, Slots, [Observation(0)], Start.AddMilliseconds(600), 1)[0].OccupancyEvidence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StaleFrameDoesNotReplaceOrderedImageAndClearsOnlyIncomingOccupancy(int offset)
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var image = Panel(1, 0);
        using var blank = Panel(0, 0);
        tracker.Observe(image, Slots, [Observation(0)], Start, 1);
        var current = Observation(0) with
        {
            OccupancyEvidence = new([new(0, .99)]),
            AppearanceEvidence = new(0, [new(0, .99, 1)])
        };
        var stale = tracker.Observe(blank, Slots, [current], Start.AddMilliseconds(offset), 1)[0];
        Assert.Equal(current with { OccupancyEvidence = null }, stale);
        Assert.NotNull(tracker.Observe(image, Slots, [Observation(0)], Start.AddMilliseconds(200), 1)[0].OccupancyEvidence);
    }

    [Theory]
    [InlineData("null-panel")]
    [InlineData("null-slots")]
    [InlineData("null-observations")]
    [InlineData("null-observation-entry")]
    [InlineData("empty-panel")]
    [InlineData("wrong-type")]
    [InlineData("nan-scale")]
    [InlineData("zero-scale")]
    [InlineData("too-many-slots")]
    [InlineData("outside-panel")]
    [InlineData("overflow-bounds")]
    [InlineData("empty-bounds")]
    public void InvalidInputThrowsBeforeChangingTheOrderedSnapshot(string kind)
    {
        using var tracker = new NormalLootOccupancyTracker();
        using var image = Panel(1, 0);
        using var empty = new Mat();
        using var gray = new Mat(100, 400, MatType.CV_8UC1, Scalar.All(80));
        tracker.Observe(image, Slots, [Observation(0)], Start, 1);
        var panel = kind switch { "null-panel" => null!, "empty-panel" => empty, "wrong-type" => gray, _ => image };
        var slots = kind switch
        {
            "null-slots" => null!,
            "too-many-slots" => Enumerable.Repeat(Slots[0], 7).ToArray(),
            "outside-panel" => [new DrawingRectangle(-1, 50, 400, 50)],
            "overflow-bounds" => [new DrawingRectangle(int.MaxValue, 0, int.MaxValue, 50)],
            "empty-bounds" => [DrawingRectangle.Empty],
            _ => Slots
        };
        var observations = kind switch
        {
            "null-observations" => null!,
            "null-observation-entry" => new LootObservation[] { null! },
            _ => new[] { Observation(0) }
        };
        var scale = kind switch { "nan-scale" => float.NaN, "zero-scale" => 0, _ => 1 };

        Assert.ThrowsAny<ArgumentException>(() => tracker.Observe(panel, slots, observations, Start.AddMilliseconds(100), scale));
        Assert.NotNull(tracker.Observe(image, Slots, [Observation(0)], Start.AddMilliseconds(200), 1)[0].OccupancyEvidence);
    }

    [Fact]
    public void DisposeIsIdempotentAndObserveAfterDisposeThrows()
    {
        var tracker = new NormalLootOccupancyTracker();
        using var image = Panel(1, 0);
        tracker.Observe(image, Slots, [Observation(0)], Start, 1);
        tracker.Dispose();
        tracker.Dispose();
        Assert.Throws<ObjectDisposedException>(() => tracker.Observe(image, Slots, [Observation(0)], Start.AddMilliseconds(200), 1));
    }

    private static LootObservation Observation(int slot) => new(LootSource.Normal, slot,
        "Elion Follower's Helmet x 4", "Elion Follower's Helmet", 4, .96, 1, null, null);

    private static LootObservation Ineligible(LootObservation row, string kind) => kind switch
    {
        "rejected" => row with { RejectionReason = "ocr-geometry" },
        "rare" => row with { Source = LootSource.Rare },
        "anchor" => row with { IsAlignmentAnchor = true },
        "low-confidence" => row with { NameConfidence = .799 },
        "no-name" => row with { ItemName = null },
        "different-name" => row with { ItemName = "Black Stone" },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static Mat Fixture(int sequence) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "normal-appearance", $"occupancy-r4-{sequence:000000}.png"));

    private static Mat UnreadableFixture(int sequence) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "normal-appearance", $"occupancy-unreadable-{sequence:000000}.png"));

    private static Mat Panel(params double[] newestFirstAlpha)
    {
        var panel = new Mat(newestFirstAlpha.Length * 50, 400, MatType.CV_8UC3);
        var height = panel.Height;
        var width = panel.Width;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var luminance = (byte)(80 + 3 * Math.Sin(x / 7d) + 3 * Math.Cos(y / 5d));
                panel.Set(y, x, new Vec3b(luminance, luminance, luminance));
            }
        for (var slot = 0; slot < newestFirstAlpha.Length; slot++)
        {
            using var target = new Mat(panel, new Rect(0, (newestFirstAlpha.Length - slot - 1) * 50, 400, 50));
            using var ink = target.Clone();
            Cv2.PutText(ink, "Some Item x 4", new CvPoint(20, 32), HersheyFonts.HersheySimplex, .62, Scalar.All(5), 3, LineTypes.AntiAlias);
            Cv2.PutText(ink, "Some Item x 4", new CvPoint(20, 32), HersheyFonts.HersheySimplex, .62, Scalar.All(245), 1, LineTypes.AntiAlias);
            Cv2.AddWeighted(ink, newestFirstAlpha[slot], target, 1 - newestFirstAlpha[slot], 0, target);
        }
        return panel;
    }
}
