using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using OpenCvSharp;
using DrawingRectangle = System.Drawing.Rectangle;
using CvPoint = OpenCvSharp.Point;

namespace BdoGrindTracker.App.Tests;

public sealed class NormalLootAppearanceTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 19, 0, 0, TimeSpan.Zero);
    private static readonly DrawingRectangle[] Slots = [new(0, 50, 400, 50), new(0, 0, 400, 50)];

    [Fact]
    public void IdenticalOcrRowsExposeFadedOlderSlotReplacedByFreshGlyphs()
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var old = Panel(1, .35);
        using var fresh = Panel(1, 1);
        var input = new[] { Observation(0), Observation(1) };
        Assert.All(tracker.Observe(old, Slots, input, Start, 1), row => Assert.Null(row.AppearanceEvidence));

        var result = tracker.Observe(fresh, Slots, input, Start.AddMilliseconds(200), 1);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[1].AppearanceEvidence!.FadedPreviousSlots);
        var match = Assert.Single(result[1].AppearanceEvidence!.Matches, match => match.PreviousSlot == 1);
        Assert.InRange(match.Correlation, .98, 1);
        Assert.InRange(match.PreviousContrastRatio, .32, .38);
        Assert.Equal(input[1], result[1] with { AppearanceEvidence = null });
    }

    [Fact]
    public void StoresOriginalPixelsEvenWhenPreviousOcrWasEmpty()
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var faded = Panel(.4, 0);
        using var fresh = Panel(1, 0);
        tracker.Observe(faded, Slots, [], Start, 1);
        var result = tracker.Observe(fresh, Slots, [Observation(0)], Start.AddMilliseconds(200), 1);
        Assert.Equal(1, Assert.Single(result).AppearanceEvidence!.FadedPreviousSlots);
    }

    [Fact]
    public void StableGlyphsIgnoreChangedSceneBackground()
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var before = Panel(1, 1, backgroundPhase: 0);
        using var after = Panel(1, 1, backgroundPhase: 7);
        var input = new[] { Observation(0), Observation(1) };
        tracker.Observe(before, Slots, input, Start, 1);
        var result = tracker.Observe(after, Slots, input, Start.AddMilliseconds(200), 1);
        Assert.All(result, row =>
        {
            Assert.NotNull(row.AppearanceEvidence);
            Assert.Equal(0, row.AppearanceEvidence.FadedPreviousSlots);
            Assert.All(row.AppearanceEvidence.Matches, match => Assert.InRange(match.PreviousContrastRatio, .9, 1.1));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.05)]
    [InlineData(.8)]
    [InlineData(1)]
    public void BlankExtremelyFaintOrStillBrightRowsDoNotClaimFadeReset(double beforeAlpha)
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var before = Panel(beforeAlpha, 0);
        using var after = Panel(1, 0);
        tracker.Observe(before, Slots, [Observation(0)], Start, 1);
        var result = tracker.Observe(after, Slots, [Observation(0)], Start.AddMilliseconds(200), 1);
        Assert.Equal(0, result[0].AppearanceEvidence?.FadedPreviousSlots ?? 0);
    }

    [Fact]
    public void UniformWholeLogOpacityChangeSuppressesArrivalConstraints()
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var before = Panel(.4, .4);
        using var after = Panel(1, 1);
        var input = new[] { Observation(0), Observation(1) };
        tracker.Observe(before, Slots, input, Start, 1);
        var result = tracker.Observe(after, Slots, input, Start.AddMilliseconds(200), 1);
        Assert.All(result, row =>
        {
            Assert.NotNull(row.AppearanceEvidence);
            Assert.Equal(0, row.AppearanceEvidence.FadedPreviousSlots);
            Assert.All(row.AppearanceEvidence.Matches, match => Assert.InRange(match.PreviousContrastRatio, .37, .43));
        });
    }

    [Fact]
    public void QuantityTextChangeDoesNotTurnIntoAppearanceArrival()
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var before = Panel(1, 0, text: "Some Item x 4");
        using var after = Panel(1, 0, text: "Some Item x 500");
        tracker.Observe(before, Slots, [Observation(0)], Start, 1);
        var result = tracker.Observe(after, Slots, [Observation(0) with { Quantity = 500 }], Start.AddMilliseconds(200), 1);
        Assert.Equal(0, result[0].AppearanceEvidence?.FadedPreviousSlots ?? 0);
        Assert.Equal(500, result[0].Quantity);
    }

    [Fact]
    public void DifferentGlyphsAndKnownDifferentItemDoNotMatch()
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var before = Panel(.4, .4, text: "Other completely distinct text");
        using var after = Panel(1, 1);
        tracker.Observe(before, Slots, [Observation(0), Observation(1) with { ItemName = "Other item" }], Start, 1);
        var result = tracker.Observe(after, Slots, [Observation(0)], Start.AddMilliseconds(200), 1);
        Assert.Null(result[0].AppearanceEvidence);
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("scale")]
    [InlineData("geometry")]
    [InlineData("reset")]
    public void CaptureDiscontinuityReseedsWithoutOldAppearanceEvidence(string change)
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var before = Panel(.4, 0);
        using var after = Panel(1, 0);
        tracker.Observe(before, Slots, [Observation(0)], Start, 1);
        if (change == "reset") tracker.Reset();
        var slots = change == "geometry" ? new[] { new DrawingRectangle(1, 50, 399, 50), Slots[1] } : Slots;
        var result = tracker.Observe(after, slots, [Observation(0)],
            Start.AddMilliseconds(change == "gap" ? 601 : 200), change == "scale" ? .95f : 1);
        Assert.Null(result[0].AppearanceEvidence);
    }

    [Fact]
    public void StaleFrameDoesNotOverwriteOrderedSnapshot()
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var faded = Panel(.4, 0);
        using var fresh = Panel(1, 0);
        tracker.Observe(faded, Slots, [], Start, 1);
        Assert.Null(tracker.Observe(fresh, Slots, [Observation(0)], Start, 1)[0].AppearanceEvidence);
        Assert.Equal(1, tracker.Observe(fresh, Slots, [Observation(0)], Start.AddMilliseconds(200), 1)[0].AppearanceEvidence!.FadedPreviousSlots);
    }

    [Fact]
    public void RejectedRareAndAlignmentObservationsKeepTheirContentsWithoutEvidence()
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var before = Panel(.4, 0);
        using var after = Panel(1, 0);
        tracker.Observe(before, Slots, [], Start, 1);
        var input = new[] { Observation(0) with { RejectionReason = "unknown" },
            Observation(0) with { Source = LootSource.Rare }, Observation(0) with { IsAlignmentAnchor = true } };
        var result = tracker.Observe(after, Slots, input, Start.AddMilliseconds(200), 1);
        Assert.Equal(input, result);
        Assert.All(result, row => Assert.Null(row.AppearanceEvidence));
    }

    [Theory]
    [InlineData("800", "801", 1)]
    [InlineData("097", "098", 0)]
    public void RecordedFadedRowsProduceEvidenceWithoutNewOcr(string oldFrame, string newFrame, int targetSlot)
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var before = Fixture(oldFrame);
        using var after = Fixture(newFrame);
        var bounds = new[] { new DrawingRectangle(0, 75, 451, 74), new DrawingRectangle(0, 0, 451, 75) };
        tracker.Observe(before, bounds, oldFrame == "097" ? [] : [Observation(0), Observation(1)], Start, 1.49f);
        var result = tracker.Observe(after, bounds, [Observation(0), Observation(1)], Start.AddMilliseconds(200), 1.49f);
        Assert.True((result[targetSlot].AppearanceEvidence!.FadedPreviousSlots & (1 << targetSlot)) != 0);
        var match = Assert.Single(result[targetSlot].AppearanceEvidence!.Matches, match => match.PreviousSlot == targetSlot);
        Assert.InRange(match.PreviousContrastRatio, .25, .5);
        Assert.InRange(match.Correlation, .9, 1);
    }

    [Fact]
    public void RecordedUnchangedFreshRowsDoNotCreateRefreshEvidence()
    {
        using var tracker = new NormalLootAppearanceTracker();
        using var before = Fixture("801");
        using var after = Fixture("802");
        var bounds = new[] { new DrawingRectangle(0, 75, 451, 74), new DrawingRectangle(0, 0, 451, 75) };
        tracker.Observe(before, bounds, [Observation(0), Observation(1)], Start, 1.49f);
        var result = tracker.Observe(after, bounds, [Observation(0), Observation(1)], Start.AddMilliseconds(200), 1.49f);
        Assert.All(result, row => Assert.Equal(0, row.AppearanceEvidence!.FadedPreviousSlots));
    }

    private static LootObservation Observation(int slot) => new(LootSource.Normal, slot,
        "Some Item x 4", "Some Item", 4, .96, 1, null, null);

    private static Mat Fixture(string sequence) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "normal-appearance", $"{sequence}-text.png"));

    private static Mat Panel(double bottomAlpha, double upperAlpha, int backgroundPhase = 0,
        string text = "Some Item x 4")
    {
        var panel = new Mat(100, 400, MatType.CV_8UC3);
        var panelHeight = panel.Height;
        var panelWidth = panel.Width;
        for (var y = 0; y < panelHeight; y++)
            for (var x = 0; x < panelWidth; x++)
            {
                var luminance = (byte)(80 + 3 * Math.Sin((x + backgroundPhase) / 7d) + 3 * Math.Cos((y + backgroundPhase) / 5d));
                panel.Set(y, x, new Vec3b(luminance, luminance, luminance));
            }
        foreach (var (alpha, y) in new[] { (bottomAlpha, 50), (upperAlpha, 0) })
        {
            using var target = new Mat(panel, new Rect(0, y, 400, 50));
            using var ink = target.Clone();
            Cv2.PutText(ink, text, new CvPoint(20, 32), HersheyFonts.HersheySimplex, .62, Scalar.All(5), 3, LineTypes.AntiAlias);
            Cv2.PutText(ink, text, new CvPoint(20, 32), HersheyFonts.HersheySimplex, .62, Scalar.All(245), 1, LineTypes.AntiAlias);
            Cv2.AddWeighted(ink, alpha, target, 1 - alpha, 0, target);
        }
        return panel;
    }
}
