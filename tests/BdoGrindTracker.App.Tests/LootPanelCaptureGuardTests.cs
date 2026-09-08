using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed class LootPanelCaptureGuardTests
{
    private static CompanionCalibration Calibration() => new("profile", "gamevariable.xml", "GameOption.txt",
        960, 540, 1920, 1080, 1f, CompanionFontType.StrongSword, 0, false);

    [Theory]
    [InlineData("missing")]
    [InlineData("hidden")]
    [InlineData("malformed")]
    public void UnreadableMainPanelHasAnActionableError(string kind)
    {
        Exception cause = kind switch
        {
            "missing" => new FileNotFoundException("private path"),
            "hidden" => new InvalidDataException("hidden UIData 159"),
            _ => new System.Xml.XmlException("invalid fragment"),
        };
        var error = Assert.Throws<LootPanelUnavailableException>(() =>
            LootPanelCaptureGuard.ReadCalibration(() => throw cause));
        Assert.Equal(LootPanelCaptureGuard.MissingPanelMessage, error.Message);
        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public void ValidPositionRemainsAvailableWithoutAnyLootAndChecksAreThrottled()
    {
        var reads = 0;
        var calibration = Calibration();
        var guard = new LootPanelCaptureGuard(calibration, () => { reads++; return calibration; });
        for (var i = 0; i < 5; i++)
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddMilliseconds(i * 450));
        Assert.Equal(1, reads);
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2));
        Assert.Equal(2, reads);
        Assert.Null(guard.Error);
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2.1), force: true);
        Assert.Equal(3, reads);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("scale")]
    [InlineData("resolution")]
    [InlineData("font")]
    [InlineData("profile")]
    public void ChangedCalibrationStopsUseOfTheOldCoordinates(string field)
    {
        var calibration = Calibration();
        var current = calibration;
        var guard = new LootPanelCaptureGuard(calibration, () => current);
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch);
        current = field switch
        {
            "position" => calibration with { LootAnchorX = 800 },
            "scale" => calibration with { UiScale = 1.2f },
            "resolution" => calibration with { ScreenWidth = 2560 },
            "font" => calibration with { FontType = CompanionFontType.DejaVu },
            _ => calibration with { GameVariablePath = "another-profile/gameVariable.xml" },
        };
        var error = Assert.Throws<LootPanelUnavailableException>(() =>
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2)));
        Assert.Contains("geändert", error.Message);
        Assert.Equal(error.Message, guard.Error);
        Assert.Throws<LootPanelUnavailableException>(() =>
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(3)));
    }

    [Fact]
    public void PanelDisappearingDuringCaptureIsFatal()
    {
        var readable = true;
        var guard = new LootPanelCaptureGuard(Calibration(), () =>
            readable ? Calibration() : throw new InvalidDataException("No visible UIData 159"));
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch);
        readable = false;
        Assert.Throws<LootPanelUnavailableException>(() =>
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2)));
        Assert.Equal(LootPanelCaptureGuard.MissingPanelMessage, guard.Error);
    }

    [Fact]
    public void WrongMonitorSizeCannotUseCoordinatesFromAnotherResolution()
    {
        var guard = new LootPanelCaptureGuard(Calibration());
        var error = Assert.Throws<LootPanelUnavailableException>(() =>
            guard.Validate(new Size(3840, 2160), DateTimeOffset.UnixEpoch));
        Assert.Contains("Spielmonitor", error.Message);
        Assert.NotNull(guard.Error);
    }
}
