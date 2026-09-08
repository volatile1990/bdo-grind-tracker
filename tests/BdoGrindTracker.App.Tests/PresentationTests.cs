using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class PresentationTests
{
    [Theory]
    [InlineData("Black Crystal Fragment", "black-crystal-fragment")]
    [InlineData("Nev's Fragment", "nevs-fragment")]
    [InlineData("Embers of Ynix - Helmet", "embers-of-ynix-helmet")]
    [InlineData("  Twilight of the End - Ring  ", "twilight-of-the-end-ring")]
    public void IconSlugMatchesPackagedAssetNames(string name, string expected) =>
        Assert.Equal(expected, AssetNames.ItemSlug(name));

    [Theory]
    [InlineData("Maegu", "maegu.png")]
    [InlineData("Dark Knight", "dark-knight.png")]
    [InlineData("Wukong", "wukong.png")]
    [InlineData("Witch · Awakening · Demo", "witch.png")]
    [InlineData("Witch Â· Awakening Â· Demo", "witch.png")]
    [InlineData("Dark Knight Â· Awakening Â· Demo", "dark-knight.png")]
    public void ClassIconsSupportStoredNamesIncludingOlderCorruptedSeparators(string className, string fileName)
    {
        Assert.Equal("assets/class-icons/" + fileName, Presentation.ClassIcon(className));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "data", "class-icons", fileName)));
    }

    [Theory]
    [InlineData(1_310_000_000, "1,31 Mrd.")]
    [InlineData(994_000_000, "994,0 Mio.")]
    [InlineData(155_127, "155,1 Tsd.")]
    public void SilverUsesGermanReadableMagnitude(decimal silver, string expected) =>
        Assert.Equal(expected, Presentation.Silver(silver));

    [Theory]
    [InlineData(20_823, "20.823")]
    [InlineData(153_000, "153.000")]
    [InlineData(1_250_000, "1.250.000")]
    public void QuantitiesRetainTheirExactWholeNumber(decimal quantity, string expected) =>
        Assert.Equal(expected, Presentation.Number(quantity));

    [Fact]
    public void HourlyValuesUseTheActualDurationIncludingFractionalHours()
    {
        Assert.Equal(120m, Presentation.Hourly(90m, TimeSpan.FromMinutes(45)));
        Assert.Equal(0m, Presentation.Hourly(90m, TimeSpan.Zero));
        Assert.Equal("100:05:09", Presentation.Duration(TimeSpan.FromHours(100) + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(9)));
        Assert.Equal("1:05 h", Presentation.CompactDuration(TimeSpan.FromMinutes(65)));
        Assert.Equal("45 min", Presentation.CompactDuration(TimeSpan.FromMinutes(45)));
    }
}
