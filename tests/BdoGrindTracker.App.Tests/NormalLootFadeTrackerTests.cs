using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using OpenCvSharp;
using DrawingRectangle = System.Drawing.Rectangle;
using CvPoint = OpenCvSharp.Point;

namespace BdoGrindTracker.App.Tests;

public sealed class NormalLootFadeTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 13, 19, 45, 0, TimeSpan.Zero);
    private static readonly DrawingRectangle[] Slots = [new(0, 50, 400, 50), new(0, 0, 400, 50)];

    [Fact]
    public void TwoStableImagesAreNeededBeforeADeclineCanDescribeAge()
    {
        using var tracker = new NormalLootFadeTracker();
        using var bright = Panel(1, 0);
        using var declining = Panel(.7, 0);
        using var dimmer = Panel(.4, 0);
        var input = new[] { Observation(0) };

        AssertNoFade(tracker.Observe(bright, Slots, input, Start, 1));
        AssertNoFade(tracker.Observe(declining, Slots, input, Start.AddMilliseconds(200), 1));
        AssertNoFade(tracker.Observe(dimmer, Slots, input, Start.AddMilliseconds(400), 1));

        tracker.Observe(bright, Slots, input, Start.AddMilliseconds(600), 1);
        AssertNoFade(tracker.Observe(bright, Slots, input, Start.AddMilliseconds(800), 1));
        AssertFade(tracker.Observe(dimmer, Slots, input, Start.AddMilliseconds(1000), 1)[0], .4);
    }

    [Fact]
    public void DeclineAddsOnlyPixelEvidenceAndKeepsRawIdentityQuantityAndOtherEvidence()
    {
        using var tracker = new NormalLootFadeTracker();
        using var bright = Panel(1, 1);
        using var fading = Panel(.55, 1);
        Seed(tracker, bright, [Observation(0), Observation(1)]);
        var row = Observation(0) with
        {
            RawText = "Elion Followerts Helmet x 68",
            Quantity = 68,
            QuantityConfidence = .41,
            NativeY = 50,
            VisualFingerprint = 1234,
            OccupancyEvidence = new([new(0, .99)]),
            AppearanceEvidence = new(0, [new(0, .99, 1)]),
            FadeEvidence = new(.2, .99)
        };

        var result = tracker.Observe(fading, Slots, [row, Observation(1)], Start.AddMilliseconds(400), 1);

        AssertFade(result[0], .55);
        Assert.Equal(row with { FadeEvidence = null }, result[0] with { FadeEvidence = null });
        Assert.Null(result[1].FadeEvidence);
        Assert.Equal(.2, row.FadeEvidence.ContrastRatio);
    }

    [Fact]
    public void StableDimUpperPositionUsesItsOwnReferenceInsteadOfTheBrightBottomRow()
    {
        using var tracker = new NormalLootFadeTracker();
        using var initial = Panel(1, .65);
        using var changedScene = Panel(1, .65, backgroundPhase: 7);
        using var upperFade = Panel(1, .325, backgroundPhase: 7);
        var input = new[] { Observation(0), Observation(1) };
        Seed(tracker, initial, input);

        AssertNoFade(tracker.Observe(changedScene, Slots, input, Start.AddMilliseconds(400), 1));
        var result = tracker.Observe(upperFade, Slots, input, Start.AddMilliseconds(600), 1);

        Assert.Null(result[0].FadeEvidence);
        AssertFade(result[1], .5);
    }

    [Fact]
    public void StablePixelsAndSmallContrastFluctuationsDoNotDescribeAge()
    {
        using var tracker = new NormalLootFadeTracker();
        using var initial = Panel(.8, 1);
        using var fluctuation = Panel(.76, 1, backgroundPhase: 4);
        var input = new[] { Observation(0), Observation(1) };
        Seed(tracker, initial, input);

        AssertNoFade(tracker.Observe(fluctuation, Slots, input, Start.AddMilliseconds(400), 1));
        AssertNoFade(tracker.Observe(fluctuation, Slots, input, Start.AddMilliseconds(600), 1));
    }

    [Fact]
    public void RecordedFloatRoundoffAtConfidenceCutoffDoesNotBreakThePreviousGlyphHistory()
    {
        using var tracker = new NormalLootFadeTracker();
        using var bright = Panel(1, 0);
        using var beginningToFade = Panel(.94, 0);
        using var fading = Panel(.45, 0);
        Seed(tracker, bright, [Observation(0)]);
        var boundary = Observation(0) with { NameConfidence = .7999999970197678 };

        var middle = Assert.Single(tracker.Observe(beginningToFade, Slots, [boundary], Start.AddMilliseconds(400), 1));
        Assert.Equal(boundary, middle);
        AssertFade(tracker.Observe(fading, Slots, [Observation(0)], Start.AddMilliseconds(600), 1)[0], .45);
    }

    [Theory]
    [InlineData(.6)]
    [InlineData(1.15)]
    public void UniformExposureChangeClearsOldReferencesAndFollowingPlateauReseeds(double gain)
    {
        using var tracker = new NormalLootFadeTracker();
        using var original = Panel(.8, .8);
        using var changed = new Mat();
        original.ConvertTo(changed, MatType.CV_8UC3, gain);
        using var oneRowFades = changed.Clone();
        using (var bottom = new Mat(oneRowFades, new Rect(0, 50, 400, 50)))
            bottom.ConvertTo(bottom, MatType.CV_8UC3, .6);
        var input = new[] { Observation(0), Observation(1) };
        Seed(tracker, original, input);

        AssertNoFade(tracker.Observe(changed, Slots, input, Start.AddMilliseconds(400), 1));
        AssertNoFade(tracker.Observe(changed, Slots, input, Start.AddMilliseconds(600), 1));
        var result = tracker.Observe(oneRowFades, Slots, input, Start.AddMilliseconds(800), 1);

        // The ratio is relative to the new exposure, not the pre-gain baseline.
        AssertFade(result[0], .6);
        Assert.Null(result[1].FadeEvidence);
    }

    [Fact]
    public void NewItemCannotReuseAnotherItemsReferenceAtTheSamePosition()
    {
        using var tracker = new NormalLootFadeTracker();
        using var first = Panel(1, 0);
        using var replacement = Panel(.65, 0);
        using var replacementFade = Panel(.325, 0);
        Seed(tracker, first, [Observation(0)]);
        var other = Observation(0) with { ItemName = "Another item", RawText = "Another item x 4" };

        AssertNoFade(tracker.Observe(replacement, Slots, [other], Start.AddMilliseconds(400), 1));
        AssertNoFade(tracker.Observe(replacement, Slots, [other], Start.AddMilliseconds(600), 1));
        AssertFade(tracker.Observe(replacementFade, Slots, [other], Start.AddMilliseconds(800), 1)[0], .5);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("rejected")]
    [InlineData("outside-pool")]
    [InlineData("rare")]
    [InlineData("anchor")]
    [InlineData("low-confidence")]
    public void IneligibleNamesNeverSeedReferencesOrReceiveEvidence(string kind)
    {
        using var tracker = new NormalLootFadeTracker();
        using var bright = Panel(1, 0);
        using var fading = Panel(.5, 0);
        using var dimmer = Panel(.3, 0);
        var ineligible = Ineligible(Observation(0), kind) with
        {
            FadeEvidence = new(.3, .99),
            OccupancyEvidence = new([new(0, .99)])
        };

        foreach (var offset in new[] { 0, 200 })
        {
            var result = Assert.Single(tracker.Observe(bright, Slots, [ineligible], Start.AddMilliseconds(offset), 1));
            Assert.Equal(ineligible with { FadeEvidence = null }, result);
        }
        AssertNoFade(tracker.Observe(fading, Slots, [Observation(0)], Start.AddMilliseconds(400), 1));
        AssertNoFade(tracker.Observe(dimmer, Slots, [Observation(0)], Start.AddMilliseconds(600), 1));

        tracker.Reset();
        Seed(tracker, bright, [Observation(0)]);
        var rejected = Assert.Single(tracker.Observe(fading, Slots, [ineligible], Start.AddMilliseconds(400), 1));
        Assert.Equal(ineligible with { FadeEvidence = null }, rejected);
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("scale")]
    [InlineData("geometry")]
    [InlineData("panel-size")]
    [InlineData("reset")]
    public void CaptureDiscontinuityDiscardsOldBrightnessAndNeedsANewPlateau(string change)
    {
        using var tracker = new NormalLootFadeTracker();
        using var bright = Panel(1, 0);
        using var after = Panel(.7, 0);
        using var faded = Panel(.35, 0);
        using var resizedAfter = new Mat();
        using var resizedFaded = new Mat();
        Cv2.CopyMakeBorder(after, resizedAfter, 0, 1, 0, 0, BorderTypes.Replicate);
        Cv2.CopyMakeBorder(faded, resizedFaded, 0, 1, 0, 0, BorderTypes.Replicate);
        Seed(tracker, bright, [Observation(0)]);
        if (change == "reset") tracker.Reset();
        var slots = change == "geometry" ? new[] { new DrawingRectangle(1, 50, 399, 50), Slots[1] } : Slots;
        var scale = change == "scale" ? .95f : 1;
        var at = Start.AddMilliseconds(change == "gap" ? 801 : 400);
        var newPlateau = change == "panel-size" ? resizedAfter : after;
        var newFade = change == "panel-size" ? resizedFaded : faded;

        AssertNoFade(tracker.Observe(newPlateau, slots, [Observation(0)], at, scale));
        AssertNoFade(tracker.Observe(newPlateau, slots, [Observation(0)], at.AddMilliseconds(200), scale));
        AssertFade(tracker.Observe(newFade, slots, [Observation(0)], at.AddMilliseconds(400), scale)[0], .5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StaleFrameCannotReplaceTheOrderedImageOrReference(int offset)
    {
        using var tracker = new NormalLootFadeTracker();
        using var bright = Panel(1, 0);
        using var blank = Panel(0, 0);
        using var fading = Panel(.5, 0);
        Seed(tracker, bright, [Observation(0)]);
        var row = Observation(0) with { FadeEvidence = new(.3, .99), OccupancyEvidence = new([new(0, .99)]) };

        var stale = Assert.Single(tracker.Observe(blank, Slots, [row], Start.AddMilliseconds(200 + offset), 1));

        Assert.Equal(row with { FadeEvidence = null }, stale);
        AssertFade(tracker.Observe(fading, Slots, [Observation(0)], Start.AddMilliseconds(400), 1)[0], .5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.01)]
    public void BlankOrVanishedGlyphsCannotSeedAnAgeReference(double alpha)
    {
        using var tracker = new NormalLootFadeTracker();
        using var blank = Panel(alpha, 0);
        using var visible = Panel(.5, 0);
        Seed(tracker, blank, [Observation(0)]);

        AssertNoFade(tracker.Observe(visible, Slots, [Observation(0)], Start.AddMilliseconds(400), 1));
    }

    [Fact]
    public void DisposePreventsFurtherMeasurements()
    {
        using var image = Panel(1, 0);
        var tracker = new NormalLootFadeTracker();
        Seed(tracker, image, [Observation(0)]);
        tracker.Dispose();
        tracker.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tracker.Observe(image, Slots, [Observation(0)], Start.AddMilliseconds(400), 1));
    }

    private static void Seed(NormalLootFadeTracker tracker, Mat image, IReadOnlyList<LootObservation> input)
    {
        AssertNoFade(tracker.Observe(image, Slots, input, Start, 1));
        AssertNoFade(tracker.Observe(image, Slots, input, Start.AddMilliseconds(200), 1));
    }

    private static void AssertNoFade(IReadOnlyList<LootObservation> observations) =>
        Assert.All(observations, row => Assert.Null(row.FadeEvidence));

    private static void AssertFade(LootObservation observation, double ratio)
    {
        Assert.NotNull(observation.FadeEvidence);
        Assert.InRange(observation.FadeEvidence.Correlation, .95, 1);
        Assert.InRange(observation.FadeEvidence.ContrastRatio, ratio - .04, ratio + .04);
    }

    private static LootObservation Observation(int slot) => new(LootSource.Normal, slot,
        "Elion Follower's Helmet x 4", "Elion Follower's Helmet", 4, .96, 1, null, null);

    private static LootObservation Ineligible(LootObservation row, string kind) => kind switch
    {
        "unknown" => row with { ItemName = null, Quantity = null, RejectionReason = "ocr-width-or-empty" },
        "rejected" => row with { RejectionReason = "ocr-geometry" },
        "outside-pool" => row with { ItemName = null, RejectionReason = AutomaticLootSpotLock.OutsideSpotPoolReason },
        "rare" => row with { Source = LootSource.Rare },
        "anchor" => row with { IsAlignmentAnchor = true },
        "low-confidence" => row with { NameConfidence = .799 },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static Mat Panel(double bottomAlpha, double upperAlpha, double backgroundPhase = 0)
    {
        var panel = new Mat(100, 400, MatType.CV_8UC3);
        var height = panel.Height;
        var width = panel.Width;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var luminance = (byte)(80 + 3 * Math.Sin((x + backgroundPhase) / 7d) + 3 * Math.Cos((y + backgroundPhase) / 5d));
                panel.Set(y, x, new Vec3b(luminance, luminance, luminance));
            }
        var alpha = new[] { bottomAlpha, upperAlpha };
        for (var slot = 0; slot < alpha.Length; slot++)
        {
            using var target = new Mat(panel, new Rect(0, (1 - slot) * 50, 400, 50));
            using var ink = target.Clone();
            Cv2.PutText(ink, "Some Item x 4", new CvPoint(20, 32), HersheyFonts.HersheySimplex, .62, Scalar.All(5), 3, LineTypes.AntiAlias);
            Cv2.PutText(ink, "Some Item x 4", new CvPoint(20, 32), HersheyFonts.HersheySimplex, .62, Scalar.All(245), 1, LineTypes.AntiAlias);
            Cv2.AddWeighted(ink, alpha[slot], target, 1 - alpha[slot], 0, target);
        }
        return panel;
    }
}
