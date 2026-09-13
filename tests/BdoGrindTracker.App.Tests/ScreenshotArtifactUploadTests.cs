using BdoGrindTracker.App.Integrations.Garmoth;

namespace BdoGrindTracker.App.Tests;

public sealed class ScreenshotArtifactUploadTests
{
    [Fact]
    public void DifferentCombatArtifactsShareOnlyTheVerifiedAggregateSlot()
    {
        var totals = new Dictionary<string, long>
        {
            ["Lesha's Artifact - All Damage Reduction"] = 2,
            ["Marsh's Artifact - Extra AP Against Monsters"] = 3,
            ["Black Stone"] = 41,
            ["Underwater Ancient Weapon Power Stone"] = 950,
        };

        var drops = GarmothSessionPayload.GetUploadableDrops("sycraia-abyssal-ruins-lower", totals);

        Assert.Equal(3, drops.Count);
        Assert.Equal(5, drops["100001004_0"]);
        Assert.Equal(41, drops["16001_0"]);
        Assert.Equal(950, drops["56341_0"]);
        Assert.Empty(GarmothSessionPayload.GetOmittedItems("sycraia-abyssal-ruins-lower", totals));
    }

    [Fact]
    public void ArtifactAggregateRejectsOverflowBeforeAnUploadCanBePrepared()
    {
        var totals = new Dictionary<string, long>
        {
            ["Lesha's Artifact - All Damage Reduction"] = long.MaxValue,
            ["Marsh's Artifact - Extra AP Against Monsters"] = 1,
        };

        Assert.Throws<ArgumentException>(() =>
            GarmothSessionPayload.GetUploadableDrops("sycraia-abyssal-ruins-lower", totals));
    }

    [Theory]
    [InlineData("tungrad-ruins", "Lafi Bedmountain's Upgraded Telescope Parts")]
    [InlineData("darkseekers-retreat", "Lafi Bedmountain's Upgraded Telescope Parts")]
    [InlineData("city-of-the-dead", "Lafi Bedmountain's Upgraded Telescope Parts")]
    [InlineData("dehkia-hystria-ruins", "Lafi Bedmountain's Upgraded Compass Parts")]
    public void IndistinguishableTreasurePartsAreOmittedWithoutDroppingOtherLoot(string spot, string treasure)
    {
        var totals = new Dictionary<string, long> { [treasure] = 1, ["Black Stone"] = 7 };

        var drops = GarmothSessionPayload.GetUploadableDrops(spot, totals);

        Assert.Single(drops);
        Assert.Equal(7, drops["16001_0"]);
        Assert.Equal([treasure], GarmothSessionPayload.GetOmittedItems(spot, totals));
    }

    [Theory]
    [InlineData("dehkia-ash-forest", "44518_0")]
    [InlineData("dehkia-ii-ash-forest", "56322_0")]
    public void AshForestUsesTheSelectedStagesTrashKey(string spot, string expectedKey)
    {
        var drops = GarmothSessionPayload.GetUploadableDrops(spot,
            new Dictionary<string, long> { ["Tainted Specter's Cloth"] = 123 });

        Assert.Single(drops);
        Assert.Equal(123, drops[expectedKey]);
    }

    [Fact]
    public void UnselectedAshStageCannotResolveAnUpload()
    {
        Assert.Throws<ArgumentException>(() => GarmothSessionPayload.GetUploadableDrops(
            "dehkia-ash-forest-unspecified",
            new Dictionary<string, long> { ["Tainted Specter's Cloth"] = 123 }));
    }
}
