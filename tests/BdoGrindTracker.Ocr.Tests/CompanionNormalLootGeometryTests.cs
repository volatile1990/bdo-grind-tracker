using System.Drawing;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CompanionNormalLootGeometryTests
{
    [Fact]
    public void ScaleOnePointFourNineUsesFloorForLeadingAndCeilingForTrailingEdges()
    {
        var calibration = CreateCalibration(
            anchorX: 1000,
            anchorY: 500,
            width: 3840,
            height: 2160,
            scale: 1.49f);

        var actual = CompanionNormalLootGeometry.CalculatePanelBounds(calibration);

        Assert.Equal(Rectangle.FromLTRB(755, 277, 1388, 724), actual);
        Assert.Equal(633, actual.Width);
        Assert.Equal(447, actual.Height);
    }

    [Fact]
    public void PanelBoundsAreClampedToTheCapturedScreen()
    {
        var calibration = CreateCalibration(
            anchorX: 100,
            anchorY: 100,
            width: 800,
            height: 600,
            scale: 1f);

        var actual = CompanionNormalLootGeometry.CalculatePanelBounds(calibration);

        Assert.Equal(Rectangle.FromLTRB(0, 0, 360, 250), actual);
    }

    [Fact]
    public void SlotCropsRetainCompanionsIndependentRoundingAndOverlaps()
    {
        var calibration = CreateCalibration(
            anchorX: 1000,
            anchorY: 500,
            width: 3840,
            height: 2160,
            scale: 1.49f);

        var actual = CompanionNormalLootGeometry.CalculateSlotCrops(calibration);

        Assert.Equal(6, actual.Count);
        Assert.Collection(
            actual,
            crop => Assert.Equal(new Rectangle(815, 650, 573, 74), crop),
            crop => Assert.Equal(new Rectangle(815, 575, 573, 75), crop),
            crop => Assert.Equal(new Rectangle(815, 501, 573, 75), crop),
            crop => Assert.Equal(new Rectangle(815, 426, 573, 75), crop),
            crop => Assert.Equal(new Rectangle(815, 352, 573, 75), crop),
            crop => Assert.Equal(new Rectangle(815, 277, 573, 75), crop));

        Assert.Equal(actual[0].Top, actual[1].Bottom);
        Assert.Equal(actual[2].Bottom - 1, actual[1].Top);
        Assert.Equal(actual[4].Bottom - 1, actual[3].Top);
    }

    [Fact]
    public void RarePanelUsesUiData161AnchorAndModeOneLeftExtent()
    {
        var calibration = CreateCalibration(
            anchorX: 1000,
            anchorY: 500,
            width: 3840,
            height: 2160,
            scale: 1.49f) with
        {
            HasRareLootAnchor = true,
            RareLootAnchorX = 2000,
            RareLootAnchorY = 1000,
        };

        var actual = CompanionNormalLootGeometry.CalculateRarePanelBounds(calibration);

        Assert.Equal(Rectangle.FromLTRB(1814, 777, 2388, 1224), actual);
    }

    [Fact]
    public void RarePanelRequiresVisibleUiData161Anchor()
    {
        var calibration = CreateCalibration(1000, 500, 3840, 2160, 1.49f);

        Assert.Throws<InvalidOperationException>(
            () => CompanionNormalLootGeometry.CalculateRarePanelBounds(calibration));
    }

    [Fact]
    public void RareModeUsesOneRoundedMiddleBandInsteadOfNormalSlots()
    {
        var calibration = CreateCalibration(
            anchorX: 1000,
            anchorY: 500,
            width: 3840,
            height: 2160,
            scale: 1.49f) with
        {
            HasRareLootAnchor = true,
            RareLootAnchorX = 2000,
            RareLootAnchorY = 1000,
        };

        var actual = CompanionNormalLootGeometry.CalculateRareBandCrop(calibration);

        Assert.Equal(new Rectangle(1814, 956, 574, 89), actual);
    }

    private static CompanionCalibration CreateCalibration(
        int anchorX,
        int anchorY,
        int width,
        int height,
        float scale) =>
        new(
            ProfileDirectoryPath: "profile",
            GameVariablePath: "gamevariable.xml",
            GameOptionPath: "GameOption.txt",
            LootAnchorX: anchorX,
            LootAnchorY: anchorY,
            ScreenWidth: width,
            ScreenHeight: height,
            UiScale: scale,
            FontType: CompanionFontType.StrongSword,
            WindowedMode: 0,
            CustomHp: false);
}
