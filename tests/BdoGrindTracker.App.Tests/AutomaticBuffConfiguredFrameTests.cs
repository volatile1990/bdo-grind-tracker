using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class AutomaticBuffConfiguredFrameTests(ITestOutputHelper output)
{
    [WindowsOcrFact]
    [Trait("Category", "WindowsOcr")]
    public void ConfiguredHudFindsTransparentHarmonyAndTenacityWithTheirRealTimers()
    {
        using var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(readCalibration: () => fixture.Calibration);
        using var frame = Screen(2442, 1940, "bar-edania-tenacity-8m.png");

        var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.Empty(reading.UnknownBuffIds);
        Assert.Equal(7, reading.Observations.Count);
        Assert.Equal(TimeSpan.FromMinutes(8), Assert.Single(reading.Observations,
            item => item.BuffId == "harmony-draught-edania").Remaining);
        Assert.Equal(TimeSpan.FromMinutes(8), Assert.Single(reading.Observations,
            item => item.BuffId == "perfume-of-tenacity").Remaining);
        Assert.DoesNotContain(reading.Observations, item => item.BuffId.StartsWith("immortal-", StringComparison.Ordinal));
    }

    [WindowsOcrFact]
    [Trait("Category", "WindowsOcr")]
    public void SavedBuffPanelFindsRealTimersIgnoresOutsideDuplicatesAndFollowsSavedMoves()
    {
        using var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(readCalibration: () => fixture.Calibration);
        using var first = Screen(2350, 1920);

        var firstReading = reader.Read(first, DateTimeOffset.UtcNow, CancellationToken.None);
        output.WriteLine(reader.LastDiagnostic ?? "No diagnostic");
        AssertRecordedBuffs(firstReading);
        Assert.Contains("BDO-Buffbereich", reader.LastDiagnostic);

        // The bound file stays the same; only the saved panel center changes.
        fixture.Save(.5, .5);
        using var moved = Screen(1518, 982);
        var movedReading = reader.Read(moved, DateTimeOffset.UtcNow, CancellationToken.None);
        output.WriteLine(reader.LastDiagnostic ?? "No diagnostic");
        AssertRecordedBuffs(movedReading);
    }

    [Fact]
    public void VisibleConfiguredEmptyPanelEstablishesKnownAbsence()
    {
        using var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(readCalibration: () => fixture.Calibration,
            recognize: (_, _) => throw new InvalidOperationException("An empty panel needs no OCR."));
        using var frame = new Bitmap(3840, 2160);

        var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.Empty(reading.Observations);
        Assert.Empty(reading.UnknownBuffIds);
    }

    [Fact]
    public void UnconfiguredEmptyScreenshotDoesNotEstablishKnownAbsence()
    {
        using var reader = new AutomaticBuffFrameReader();
        using var frame = new Bitmap(400, 240);

        Assert.Null(reader.Read(frame, CancellationToken.None));
    }

    [Fact]
    public void HiddenConfiguredPanelDoesNotEstablishKnownAbsence()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Calibration.GameVariablePath,
            "<UIData Version=\"2\"><UIData Index=\"119\" IsShow=\"false\" RelativePosX=\"0.5\" RelativePosY=\"0.5\"/></UIData>");
        using var reader = new AutomaticBuffFrameReader(readCalibration: () => fixture.Calibration);
        using var frame = new Bitmap(3840, 2160);

        Assert.Null(reader.Read(frame, DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public void MissingBoundCalibrationDoesNotSearchTheRestOfTheScreen()
    {
        using var reader = new AutomaticBuffFrameReader(
            readCalibration: () => null,
            recognize: (_, _) => throw new InvalidOperationException("No configured region means no OCR."));
        using var frame = Screen(2350, 1920);

        Assert.Null(reader.Read(frame, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Contains("BDO-Konfiguration", reader.LastDiagnostic);
    }

    private static void AssertRecordedBuffs(BuffFrameReading? reading)
    {
        Assert.NotNull(reading);
        Assert.Empty(reading.UnknownBuffIds);
        Assert.Equal(2, reading.Observations.Count);
        Assert.Equal(TimeSpan.FromMinutes(13), Assert.Single(reading.Observations,
            item => item.BuffId == "harmony-draught-demihuman").Remaining);
        Assert.Equal(TimeSpan.FromMinutes(114), Assert.Single(reading.Observations,
            item => item.BuffId == "simple-cron-meal").Remaining);
    }

    private static Bitmap Screen(int x, int y, string file = "bar-13m-114m.png")
    {
        using var crop = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs", file));
        var frame = new Bitmap(3840, 2160);
        using var canvas = Graphics.FromImage(frame);
        canvas.Clear(Color.Black);
        var pixels = new Rectangle(0, 0, crop.Width, crop.Height);
        canvas.DrawImage(crop, new Rectangle(x, y, crop.Width, crop.Height), pixels, GraphicsUnit.Pixel);
        // Explicit pixels retain the capture's 144-DPI artwork at its native size.
        canvas.DrawImage(crop, new Rectangle(100, 100, crop.Width, crop.Height), pixels, GraphicsUnit.Pixel);
        return frame;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "buff-hud-" + Guid.NewGuid().ToString("N"));
        internal CompanionCalibration Calibration { get; }

        internal Fixture()
        {
            Directory.CreateDirectory(_directory);
            Calibration = new(_directory, Path.Combine(_directory, "gameVariable.xml"),
                Path.Combine(_directory, "GameOption.txt"), 0, 0, 3840, 2160, 1.49f,
                CompanionFontType.StrongSword, 2, false);
            Save(.7167248726, .9344827533);
        }

        internal void Save(double x, double y)
        {
            File.WriteAllText(Calibration.GameVariablePath, FormattableString.Invariant($"""
                <UIData Version="2">
                  <UIData Index="119" IsShow="true" RelativePosX="{x}" RelativePosY="{y}"/>
                  <UIData Index="159" IsShow="true" RelativePosX="0.1" RelativePosY="0.1"/>
                </UIData>
                """));
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
