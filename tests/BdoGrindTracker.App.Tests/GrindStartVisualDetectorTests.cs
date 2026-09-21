using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed class GrindStartVisualDetectorTests
{
    private static readonly CompanionCalibration Calibration = new("", "", "", 245, 223, 511, 447,
        1.49f, CompanionFontType.StrongSword, 0, false);

    [Fact]
    public void ExistingRecordedLootIsOnlyABaselineAndRepeatedFrameNeverWakes()
    {
        var detector = new GrindStartVisualDetector();
        using var frame = RecordedLoot();
        Assert.False(detector.Observe(frame, Calibration));
        Assert.False(detector.Observe(frame, Calibration));
        detector.Reset();
        Assert.False(detector.Observe(frame, Calibration));
    }

    [Fact]
    public void RecordedNewestLootAppearingAfterBlankWakesOnce()
    {
        var detector = new GrindStartVisualDetector();
        using var blank = new Bitmap(Calibration.ScreenWidth, Calibration.ScreenHeight);
        using var frame = RecordedLoot();
        Assert.False(detector.Observe(blank, Calibration));
        Assert.True(detector.Observe(frame, Calibration));
        Assert.False(detector.Observe(frame, Calibration));
    }

    [Fact]
    public void SampleIsRestrictedToNewestRowTextLine()
    {
        Assert.True(GrindStartVisualDetector.TryGetTextBounds(new Size(511, 447), Calibration, out var bounds));
        Assert.Equal(new Rectangle(75, 391, 436, 36), bounds);
    }

    [Theory]
    [InlineData("scale")]
    [InlineData("anchor")]
    [InlineData("dimensions")]
    [InlineData("overflow")]
    public void InvalidGeometryReturnsToBaselineWithoutThrowing(string kind)
    {
        var detector = new GrindStartVisualDetector();
        using var frame = RecordedLoot();
        detector.Observe(frame, Calibration);
        var invalid = kind switch
        {
            "scale" => Calibration with { UiScale = float.NaN },
            "anchor" => Calibration with { LootAnchorX = -5000 },
            "dimensions" => Calibration with { ScreenWidth = 1000 },
            _ => Calibration with { UiScale = float.MaxValue }
        };
        Assert.False(detector.Observe(frame, invalid));
        Assert.False(detector.Observe(frame, Calibration));
    }

    private static Bitmap RecordedLoot() => new(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "normal-appearance", "occupancy-unreadable-001385.png"));
}
