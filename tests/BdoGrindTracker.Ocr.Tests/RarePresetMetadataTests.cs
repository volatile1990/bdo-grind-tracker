namespace BdoGrindTracker.Ocr.Tests;

public sealed class RarePresetMetadataTests
{
    [Theory]
    [InlineData("ScreenWidth='1280'")]
    [InlineData("ResolutionHeight='1080'")]
    [InlineData("UiScale='1.00'")]
    [InlineData("ScreenHeight='not-a-number'")]
    [InlineData("UiScale='NaN'")]
    public void MismatchedOrInvalidPresetAttributesDisableOnlyRareLoot(string attributes)
    {
        using var fixture = new Fixture();

        AssertRejected(fixture.Read(attributes));
    }

    [Fact]
    public void MatchingPresetAttributesAllowTheSavedPosition()
    {
        using var fixture = new Fixture();

        AssertRecovered(fixture.Read(
            "ScreenWidth='2560' ResolutionWidth='2560' ScreenHeight='1440' ResolutionHeight='1440' UiScale='0.89'"));
    }

    [Fact]
    public void AbsentPresetMetadataAllowsTheSavedPosition()
    {
        using var fixture = new Fixture();

        AssertRecovered(fixture.Read());
    }

    [Theory]
    [InlineData("<Resolution Width='1920' Height='1440' /><UiScale Value='0.89' />")]
    [InlineData("<Resolution Width='2560' Height='1440' /><UiScale Value='1.00' />")]
    public void MismatchedMetadataUnderThePresetMarkerDisablesOnlyRareLoot(string metadata)
    {
        using var fixture = new Fixture();

        AssertRejected(fixture.Read(markerMetadata: metadata));
    }

    [Fact]
    public void MatchingMetadataUnderThePresetMarkerAllowsTheSavedPosition()
    {
        using var fixture = new Fixture();

        AssertRecovered(fixture.Read(markerMetadata:
            "<Resolution Width='2560' Height='1440' /><UiScale Value='0.89' />"));
    }

    private static void AssertRejected(CompanionCalibration calibration)
    {
        Assert.False(calibration.HasRareLootAnchor);
        Assert.Equal(RareLootAnchorStatus.Invalid, calibration.RareLootResolution?.Status);
        Assert.Equal((816, 898), (calibration.LootAnchorX, calibration.LootAnchorY));
        Assert.False(CompanionNormalLootGeometry.CalculatePanelBounds(calibration).IsEmpty);
    }

    private static void AssertRecovered(CompanionCalibration calibration)
    {
        Assert.True(calibration.HasRareLootAnchor);
        Assert.Equal(RareLootAnchorStatus.PresetFallback, calibration.RareLootResolution?.Status);
        Assert.Equal((1741, 905), (calibration.RareLootAnchorX, calibration.RareLootAnchorY));
        Assert.Equal(new System.Drawing.Rectangle(1630, 879, 343, 53),
            CompanionNormalLootGeometry.CalculateRareBandCrop(calibration));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "bdo-rare-metadata-" + Guid.NewGuid().ToString("N"));
        private string ProfilePath => Path.Combine(_rootPath, "UserCache", "42");

        public Fixture()
        {
            Directory.CreateDirectory(ProfilePath);
            File.WriteAllText(Path.Combine(_rootPath, "GameOption.txt"),
                "width = 2560\nheight = 1440\nuiScale = 0.89\nUIFontType = 2\nwindowed = 1\n");
        }

        public CompanionCalibration Read(string attributes = "", string markerMetadata = "")
        {
            File.WriteAllText(Path.Combine(ProfilePath, "gameVariable.xml"),
                "<Resolution Width='2560' Height='1440' /><UiScale Value='0.89' />" +
                "<UIData><UIData Index='159' IsShow='true' RelativePosX='0.3188456297' RelativePosY='0.6242274642' />" +
                "<UIData Index='161' IsShow='true' PendingType='RightBottom' PosX='812' PosY='465' RelativePosX='0' RelativePosY='0' /></UIData>" +
                "<UISettingPreset Version='1'>" +
                $"<UISettingPreset0>{markerMetadata}</UISettingPreset0>" +
                "<UISettingPreset0 Index='159' IsShow='true' RelativePosX='0.318846' RelativePosY='0.624227' />" +
                $"<UISettingPreset0 Index='161' IsShow='true' RelativePosX='0.680111' RelativePosY='0.628554' {attributes} />" +
                "</UISettingPreset>");
            return new CompanionCalibrationReader().Read(_rootPath);
        }

        public void Dispose()
        {
            File.Delete(Path.Combine(ProfilePath, "gameVariable.xml"));
            File.Delete(Path.Combine(_rootPath, "GameOption.txt"));
            Directory.Delete(ProfilePath);
            Directory.Delete(Path.Combine(_rootPath, "UserCache"));
            Directory.Delete(_rootPath);
        }
    }
}
