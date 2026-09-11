using System.Drawing;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class RareLootCalibrationFallbackTests
{
    private const string MainPosition =
        "<UIData Index='159' IsShow='true' RelativePosX='0.3188456297' RelativePosY='0.6242274642' />";
    private const string UnresolvedRarePosition =
        "<UIData Index='161' IsShow='true' PendingType='RightBottom' PosX='812' PosY='465' RelativePosX='0' RelativePosY='0' />";

    [Fact]
    public void VisibleActivePositionWinsOverConflictingAndMalformedSavedPresets()
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(
            "<UIData Index='161' IsShow='true' RelativePosX='0.75' RelativePosY='0.4' />",
            Presets(
                Preset(0),
                Preset(1, rareX: "0.9", rareY: "0.8"),
                Preset(2, rareX: "not-a-number")));

        AssertAnchor(actual, RareLootAnchorStatus.Active, 1920, 576);
    }

    [Fact]
    public void HiddenActiveRarePanelCannotBeEnabledByAVisibleSavedPreset()
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(
            "<UIData Index='161' IsShow='false' RelativePosX='0.75' RelativePosY='0.4' />",
            Presets(Preset(0)));

        AssertDisabled(actual, RareLootAnchorStatus.Hidden);
    }

    [Fact]
    public void MissingActiveRarePanelCannotBeEnabledByAVisibleSavedPreset()
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(string.Empty, Presets(Preset(0)));

        AssertDisabled(actual, RareLootAnchorStatus.Missing);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RightBottomZeroSentinelUsesTheMatchingSavedLayout(int presetIndex)
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(UnresolvedRarePosition, Presets(Preset(presetIndex)));

        AssertAnchor(actual, RareLootAnchorStatus.PresetFallback, 1741, 905);
        Assert.Equal(816, actual.LootAnchorX);
        Assert.Equal(898, actual.LootAnchorY);
        Assert.Equal(new Rectangle(1630, 879, 343, 53),
            CompanionNormalLootGeometry.CalculateRareBandCrop(actual));
    }

    [Theory]
    [InlineData("")]
    [InlineData("RelativePosX='0.8'")]
    [InlineData("RelativePosY='0.4'")]
    [InlineData("RelativePosX='bad' RelativePosY='0.4'")]
    [InlineData("RelativePosX='NaN' RelativePosY='0.4'")]
    [InlineData("RelativePosX='0.8' RelativePosY='Infinity'")]
    [InlineData("RelativePosX='-0.1' RelativePosY='0.4'")]
    [InlineData("RelativePosX='0.8' RelativePosY='1.1'")]
    public void MissingOrInvalidVisibleActiveCoordinatesUseAMatchingPreset(string coordinates)
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(
            $"<UIData Index='161' IsShow='true' {coordinates} />",
            Presets(Preset(0)));

        AssertAnchor(actual, RareLootAnchorStatus.PresetFallback, 1741, 905);
    }

    [Theory]
    [InlineData("0", "0.4", "", 0, 576)]
    [InlineData("0.75", "0", "", 1920, 0)]
    [InlineData("0", "0", "", 0, 0)]
    [InlineData("0", "0", "PendingType='RightBottom' PosX='0' PosY='0'", 0, 0)]
    [InlineData("0", "0", "PendingType='LeftTop' PosX='812' PosY='465'", 0, 0)]
    [InlineData("0", "0.4", "PendingType='RightBottom' PosX='812' PosY='465'", 0, 576)]
    [InlineData("1", "1", "", 2560, 1440)]
    public void LegitimateActiveEdgeCoordinatesRemainAuthoritative(
        string x, string y, string extraAttributes, int expectedX, int expectedY)
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(
            $"<UIData Index='161' IsShow='true' RelativePosX='{x}' RelativePosY='{y}' {extraAttributes} />",
            Presets(Preset(0)));

        AssertAnchor(actual, RareLootAnchorStatus.Active, expectedX, expectedY);
    }

    [Fact]
    public void IdenticalRarePositionsAcrossMatchingPresetsAreOneCandidate()
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(UnresolvedRarePosition,
            Presets(Preset(0), Preset(1), Preset(2)));

        AssertAnchor(actual, RareLootAnchorStatus.PresetFallback, 1741, 905);
    }

    [Fact]
    public void DistinctRarePositionsAcrossMatchingPresetsDisableTheAmbiguousFallback()
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(UnresolvedRarePosition,
            Presets(Preset(0), Preset(1, rareX: "0.8")));

        AssertDisabled(actual, RareLootAnchorStatus.Ambiguous);
    }

    [Fact]
    public void ASubpixelDifferenceInTheMainAnchorStillIdentifiesTheSavedLayout()
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(UnresolvedRarePosition,
            Presets(Preset(0, mainX: "0.3191", mainY: "0.6246")));

        AssertAnchor(actual, RareLootAnchorStatus.PresetFallback, 1741, 905);
    }

    [Theory]
    [InlineData("0.3200", "0.624227")]
    [InlineData("0.318846", "0.6260")]
    public void AMainAnchorMoreThanOneScreenPixelAwayDoesNotIdentifyTheSavedLayout(
        string mainX, string mainY)
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(UnresolvedRarePosition,
            Presets(Preset(0, mainX: mainX, mainY: mainY)));

        AssertDisabled(actual, RareLootAnchorStatus.Invalid);
    }

    [Theory]
    [InlineData("IsShow='false' RelativePosX='0.680111' RelativePosY='0.628554'")]
    [InlineData("IsShow='true' RelativePosX='0.680111'")]
    [InlineData("IsShow='true' RelativePosX='bad' RelativePosY='0.628554'")]
    [InlineData("IsShow='true' RelativePosX='NaN' RelativePosY='0.628554'")]
    [InlineData("IsShow='true' RelativePosX='0.680111' RelativePosY='Infinity'")]
    [InlineData("IsShow='true' RelativePosX='-0.1' RelativePosY='0.628554'")]
    [InlineData("IsShow='true' RelativePosX='0.680111' RelativePosY='1.1'")]
    public void InvalidOrHiddenSavedRarePositionsDoNotBecomeFallbacks(string rareAttributes)
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(UnresolvedRarePosition,
            Presets(PresetMain(0) + $"<UISettingPreset0 Index='161' {rareAttributes} />"));

        AssertDisabled(actual, RareLootAnchorStatus.Invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<UISettingPreset0 Index='159' IsShow='false' RelativePosX='0.318846' RelativePosY='0.624227' />")]
    [InlineData("<UISettingPreset0 Index='159' IsShow='true' RelativePosX='NaN' RelativePosY='0.624227' />")]
    public void SavedRarePositionRequiresAVisibleValidMainPanelInTheSamePreset(string main)
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(UnresolvedRarePosition,
            Presets(main + "<UISettingPreset0 Index='161' IsShow='true' RelativePosX='0.680111' RelativePosY='0.628554' />"));

        AssertDisabled(actual, RareLootAnchorStatus.Invalid);
    }

    [Fact]
    public void MainAndRarePanelsFromDifferentPresetsCannotBeCombined()
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(UnresolvedRarePosition,
            Presets(PresetMain(0),
                "<UISettingPreset1 Index='161' IsShow='true' RelativePosX='0.680111' RelativePosY='0.628554' />"));

        AssertDisabled(actual, RareLootAnchorStatus.Invalid);
    }

    [Fact]
    public void ForeignNestedRevertAndBattleLayoutsCannotSupplyAFallback()
    {
        using var fixture = new Fixture();
        var alternateLayouts =
            "<Foreign>" + Presets(Preset(0)) + "</Foreign>" +
            Presets(
                "<Foreign>" + Preset(0) + "</Foreign>",
                Preset(0).Replace("UISettingPreset0", "UISettingPresetRevert", StringComparison.Ordinal),
                Preset(0).Replace("UISettingPreset0", "UISettingPresetBattle", StringComparison.Ordinal),
                Preset(3));
        var actual = fixture.Read(UnresolvedRarePosition, alternateLayouts);

        AssertDisabled(actual, RareLootAnchorStatus.Invalid);
    }

    [Fact]
    public void AForeignNestedRarePanelCannotOverrideTheDirectActivePanel()
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(
            "<UIData Index='161' IsShow='true' RelativePosX='0.75' RelativePosY='0.4' />" +
            "<Foreign><UIData Index='161' IsShow='true' RelativePosX='0.9' RelativePosY='0.8' /></Foreign>");

        AssertAnchor(actual, RareLootAnchorStatus.Active, 1920, 576);
    }

    [Theory]
    [InlineData("<UIData Index='161' IsShow='true' />")]
    [InlineData("<UIData Index='161' IsShow='true' RelativePosX='NaN' RelativePosY='0.4' />")]
    [InlineData("<UIData Index='161' IsShow='true' RelativePosX='-0.1' RelativePosY='0.4' />")]
    public void InvalidOptionalRarePanelDoesNotPreventNormalLootCalibration(string rarePanel)
    {
        using var fixture = new Fixture();
        var actual = fixture.Read(rarePanel);

        AssertDisabled(actual, RareLootAnchorStatus.Invalid);
        Assert.Equal(816, actual.LootAnchorX);
        Assert.Equal(898, actual.LootAnchorY);
        Assert.False(CompanionNormalLootGeometry.CalculatePanelBounds(actual).IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<UIData Index='159' IsShow='false' RelativePosX='0.318846' RelativePosY='0.624227' />")]
    public void SavedPresetsCannotRescueAMissingOrHiddenRequiredActiveMainPanel(string main)
    {
        using var fixture = new Fixture();

        Assert.Throws<InvalidDataException>(() => fixture.Read(
            UnresolvedRarePosition, Presets(Preset(0)), main));
    }

    private static void AssertAnchor(
        CompanionCalibration actual, RareLootAnchorStatus status, int x, int y)
    {
        Assert.True(actual.HasRareLootAnchor);
        Assert.Equal(x, actual.RareLootAnchorX);
        Assert.Equal(y, actual.RareLootAnchorY);
        AssertResolution(actual, status);
    }

    private static void AssertDisabled(CompanionCalibration actual, RareLootAnchorStatus status)
    {
        Assert.False(actual.HasRareLootAnchor);
        Assert.Equal(0, actual.RareLootAnchorX);
        Assert.Equal(0, actual.RareLootAnchorY);
        AssertResolution(actual, status);
    }

    private static void AssertResolution(CompanionCalibration actual, RareLootAnchorStatus status)
    {
        Assert.NotNull(actual.RareLootResolution);
        Assert.Equal(status, actual.RareLootResolution.Status);
        Assert.False(string.IsNullOrWhiteSpace(actual.RareLootResolution.Reason));
    }

    private static string Presets(params string[] contents) =>
        "<UISettingPreset Version='1'>" + string.Concat(contents) + "</UISettingPreset>";

    private static string Preset(
        int index, string mainX = "0.318846", string mainY = "0.624227",
        string rareX = "0.680111", string rareY = "0.628554") =>
        $"<UISettingPreset{index} />" + PresetMain(index, mainX, mainY) +
        $"<UISettingPreset{index} Index='161' IsShow='true' PendingType='LeftTop' PosX='1748' PosY='975' RelativePosX='{rareX}' RelativePosY='{rareY}' />";

    private static string PresetMain(
        int index, string x = "0.318846", string y = "0.624227") =>
        $"<UISettingPreset{index} Index='159' IsShow='true' PendingType='LeftTop' PosX='742' PosY='860' RelativePosX='{x}' RelativePosY='{y}' />";

    private sealed class Fixture : IDisposable
    {
        private readonly string _rootPath = Path.Combine(
            Path.GetTempPath(), $"bdo-rare-calibration-{Guid.NewGuid():N}");

        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(_rootPath, "UserCache", "42"));
            File.WriteAllText(Path.Combine(_rootPath, "GameOption.txt"),
                "width = 2560\nheight = 1440\nuiScale =  0.89\nUIFontType = 2\nwindowed = 1\n");
        }

        public CompanionCalibration Read(
            string rarePanel, string savedLayouts = "", string mainPanel = MainPosition)
        {
            // Minimal synthetic XML fragments only: no account, character, or full user cache data.
            File.WriteAllText(Path.Combine(_rootPath, "UserCache", "42", "gameVariable.xml"),
                $"<UIData Version='2'>{mainPanel}{rarePanel}</UIData>" +
                "<Resolution Width='2560' Height='1440' /><UiScale Value='0.89' />" + savedLayouts);
            return new CompanionCalibrationReader().Read(_rootPath);
        }

        public void Dispose() => Directory.Delete(_rootPath, recursive: true);
    }
}
