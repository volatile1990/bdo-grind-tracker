#if DEBUG
using BdoGrindTracker.App.Development;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class DemoSessionGeneratorTests
{
    [Fact]
    public void OfflineDatasetGeneratesTenHoursPerSpotWithRoundedHourlyRates()
    {
        var data = DemoSessionGenerator.LoadDataset();
        Assert.Equal(6, data.Spots.Count);
        Assert.Equal(data.Names.Length, data.Prices.Length);
        var entries = DemoSessionGenerator.Generate(data, "all", 10, DateTimeOffset.UtcNow);
        Assert.Equal(60, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.True(entry.GarmothUploadBlocked);
            Assert.True(DemoSessionGenerator.IsDemo(entry));
            Assert.Equal(TimeSpan.FromHours(1), entry.Duration);
            Assert.All(entry.Totals, pair => Assert.True(pair.Value > 0));
        });
        foreach (var (spotId, rates) in data.Spots)
        {
            Assert.Equal(data.Names.Length, rates.Length);
            var spot = LootSpotCatalog.GetRequired(spotId);
            for (var i = 0; i < rates.Length; i++)
                if (spot.Allows(data.Names[i]))
                    Assert.Equal((long)Math.Round(rates[i] * 10, MidpointRounding.AwayFromZero),
                        entries.Where(e => e.SpotId == spotId).Sum(e => e.Totals.GetValueOrDefault(data.Names[i])));
        }
    }
    [Fact]
    public void SingleSpotDoesNotForceRareLootIntoEverySession()
    {
        var entries = DemoSessionGenerator.Generate(DemoSessionGenerator.LoadDataset(), "aphrodon", 10, DateTimeOffset.UtcNow);
        Assert.Equal(10, entries.Count);
        Assert.All(entries, entry => Assert.Equal("aphrodon", entry.SpotId));
        Assert.Equal(15, entries.Sum(e => e.Totals.GetValueOrDefault("Twilight of the End - Earring")));
        Assert.Equal(0, entries.Sum(e => e.Totals.GetValueOrDefault("Apeiron Earring")));
    }
}
#endif
