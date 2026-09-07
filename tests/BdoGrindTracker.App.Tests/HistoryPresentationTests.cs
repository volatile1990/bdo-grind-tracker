using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class HistoryPresentationTests
{
    [Fact]
    public void SpotProfilesMatchTheInGameValuesAndProvidedTraits()
    {
        Assert.Collection(LootSpotPresentationCatalog.Profiles,
            profile => AssertProfile(profile, LootSpotCatalog.AphrodonId, 2090, 2120, 810,
                "Branch of Abundance", 155_127, "#Knockdown/Bound", "Adamantine", "adamantine.png"),
            profile => AssertProfile(profile, LootSpotCatalog.HermesiaId, 2220, 2250, 830,
                "Black Crystal Fragment", 160_539, "#Knockback/Floating", "Fighting Spirit", "fighting-spirit.png"),
            profile => AssertProfile(profile, LootSpotCatalog.MagaiaId, 2340, 2370, 840,
                "Elion Follower's Helmet", 181_042, "#Stun/Stiffness/Freezing", "Giant", "giant.png"),
            profile => AssertProfile(profile, LootSpotCatalog.AresionId, 2455, 2485, 850,
                "Scorched Belt Ornament", 182_049, "#DivineAuthority", "Adamantine", "adamantine.png"),
            profile => AssertProfile(profile, LootSpotCatalog.ScalesOfJudgmentId, 2455, 2485, 860,
                "Elion Follower's Mark", 186_458, "#PartyOf3", "Giant", "giant.png"),
            profile => AssertProfile(profile, LootSpotCatalog.EventHorizonId, 2570, 2600, 870,
                "Broken Gloves of the Void", 196_501, "#FeverPowerfulMobs", "Giant", "giant.png"));

        Assert.All(LootSpotPresentationCatalog.Profiles, profile =>
        {
            Assert.Contains("#CombatEXP", profile.Traits);
            Assert.Contains("#HighestTier", profile.Traits);
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "data", "spot-backgrounds", profile.BackgroundFileName)),
                $"Packaged background is missing: {profile.BackgroundFileName}");
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "data", "spot-icons", profile.IconFileName)),
                $"Packaged spot icon is missing: {profile.IconFileName}");
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "data", "crystal-icons", profile.RecommendedCrystalFileName)),
                $"Packaged crystal icon is missing: {profile.RecommendedCrystalFileName}");
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
                "data", "icons", AssetNames.ItemSlug(profile.TrashItemName) + ".png")),
                $"Packaged trash icon is missing: {profile.TrashItemName}");
        });
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory,
            "data", "ui-icons", "silver.png")), "Packaged silver UI icon is missing.");
    }


    [Fact]
    public void LootColumnsSortBySilverPerHourAcrossAllLocallyTrackedSessions()
    {
        var profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.AphrodonId);
        var now = new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(2));
        var valuableItem = "Broken Vestige of Goldroot";
        var sessions = new[]
        {
            CreateEntry(profile.SpotId, profile.TrashItemName, 1_000, now,
                totals: new Dictionary<string, long>
                {
                    [profile.TrashItemName] = 1_000,
                    [valuableItem] = 1
                }),
            CreateEntry(profile.SpotId, profile.TrashItemName, 1_000, now.AddHours(-2),
                totals: new Dictionary<string, long> { [profile.TrashItemName] = 1_000 })
        };
        var prices = new LootPriceSnapshot("eu",
        [
            new LootPriceQuote(profile.TrashItemName, 0, 155_127,
                LootPriceOrigin.FixedCatalog, null),
            new LootPriceQuote(valuableItem, 0, 3_000_000_000,
                LootPriceOrigin.FixedCatalog, null)
        ]);

        var items = HistoryPresentation.BuildLootItems(profile, sessions, prices,
            SilverTaxOptions.Default);

        Assert.Equal(valuableItem, items[0].Key);
        Assert.Equal(1, items[0].Value);
        Assert.Equal(2_000, Assert.Single(items, item => item.Key == profile.TrashItemName).Value);
        Assert.True(items.ToList().FindIndex(item => item.Key == valuableItem) <
                    items.ToList().FindIndex(item => item.Key == profile.TrashItemName));

        var columns = HistoryPresentation.BuildLootColumns(profile, sessions, prices, SilverTaxOptions.Default);
        Assert.Equal(1_500_000_000m, columns.Single(column => column.ItemName == valuableItem).SilverPerHour);
        Assert.Equal(155_127_000m, columns.Single(column => column.ItemName == profile.TrashItemName).SilverPerHour);
    }


    [Fact]
    public void CollapsedChronologicalCardShowsTrashAndEveryDropWorthOverTwoHundredMillion()
    {
        var profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.AphrodonId);
        var entry = CreateEntry(profile.SpotId, profile.TrashItemName, 18_432,
            new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(2)),
            totals: new Dictionary<string, long>
            {
                [profile.TrashItemName] = 18_432,
                ["Rare Relic"] = 2,
                ["Exact Threshold Relic"] = 1,
                ["Cheap Relic"] = 9
            });
        var prices = new LootPriceSnapshot("eu",
        [
            new LootPriceQuote(profile.TrashItemName, 0, 155_127,
                LootPriceOrigin.FixedCatalog, null),
            // Market value controls the threshold; selling tax must not hide a 250M drop.
            new LootPriceQuote("Rare Relic", 250_000_000, 0,
                LootPriceOrigin.LiveMarket, null),
            new LootPriceQuote("Exact Threshold Relic", 0, 200_000_000,
                LootPriceOrigin.FixedCatalog, null),
            new LootPriceQuote("Cheap Relic", 0, 199_999_999,
                LootPriceOrigin.FixedCatalog, null)
        ]);

        var compactItems = HistoryPresentation.BuildCollapsedLootItems(entry, profile,
            prices, SilverTaxOptions.Default);

        Assert.Equal([profile.TrashItemName, "Rare Relic"],
            compactItems.Select(static item => item.Key));
    }


    [Fact]
    public void SpotMetricsUseWeightedRecentAndBestFiveGrindingHours()
    {
        var profile = LootSpotPresentationCatalog.GetRequired(LootSpotCatalog.AphrodonId);
        var now = new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(2));
        var sessions = new[]
        {
            CreateEntry(profile.SpotId, profile.TrashItemName, 40_000, now,
                TimeSpan.FromHours(2), 4_000_000_000m),
            CreateEntry(profile.SpotId, profile.TrashItemName, 40_000, now.AddDays(-1),
                TimeSpan.FromHours(4), 4_000_000_000m),
            CreateEntry(profile.SpotId, profile.TrashItemName, 60_000, now.AddDays(-2),
                TimeSpan.FromHours(2), 6_000_000_000m)
        };

        var metrics = HistoryPresentation.CalculateMetrics(profile, sessions);

        Assert.Equal(8m, metrics.TotalHours);
        Assert.Equal(14_000_000_000m, metrics.TotalSilver);
        Assert.Equal(1_750_000_000m, metrics.AverageSilverPerHour);
        Assert.Equal(17_500m, metrics.TrashPerHour);
        Assert.Equal(14_000m, metrics.RecentFiveHourTrashPerHour);
        Assert.Equal(22_000m, metrics.BestFiveHourTrashPerHour);
        Assert.Equal(1_750_000_000m, metrics.BestFiveHourAverageSilverPerHour);

        var charts = HistoryPresentation.BuildChartData(profile, sessions);
        Assert.Equal([6_000_000_000m, 10_000_000_000m, 14_000_000_000m], charts.CumulativeSilver);
        Assert.Equal([3_000_000_000m, 1_000_000_000m, 2_000_000_000m], charts.SilverPerHour);
        Assert.Equal([30_000m, 10_000m, 20_000m], charts.TrashPerHour);
        Assert.Equal([30_000m, 10_000m, 20_000m], charts.RecentFiveTrashPerHour);
        Assert.Equal([30_000m, 20_000m, 10_000m], charts.BestFiveTrashPerHour);
    }


    [Theory]
    [InlineData(0, 0, 30, "gerade eben")]
    [InlineData(0, 45, 0, "vor 45 Min.")]
    [InlineData(5, 0, 0, "vor 5 Std.")]
    [InlineData(336, 0, 0, "vor 14 Tagen")]
    public void TimeAgoFormattingIsCompactAndGerman(int hours, int minutes, int seconds, string expected)
    {
        var now = new DateTimeOffset(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(2));
        var timestamp = now - TimeSpan.FromHours(hours) - TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(seconds);
        Assert.Equal(expected, HistoryPresentation.FormatTimeAgo(timestamp, now));
    }


    private static void AssertProfile(LootSpotPresentation profile, string spotId,
        int recommendedAp, int maxAp, int recommendedDp, string trashName, long trashSilver,
        string distinctiveTrait, string crystalName, string crystalFileName)
    {
        Assert.Equal(spotId, profile.SpotId);
        Assert.Equal(recommendedAp, profile.RecommendedAp);
        Assert.Equal(maxAp, profile.MaxApLimit);
        Assert.Equal(recommendedDp, profile.RecommendedDp);
        Assert.Equal(trashName, profile.TrashItemName);
        Assert.Equal(trashSilver, profile.TrashSilver);
        Assert.Contains(distinctiveTrait, profile.Traits);
        Assert.Equal(crystalName, profile.RecommendedCrystalName);
        Assert.Equal(crystalFileName, profile.RecommendedCrystalFileName);
    }

    private static LootHistoryEntry CreateEntry(string spotId, string trashName, long trash,
        DateTimeOffset updatedAt, TimeSpan? duration = null, decimal silver = 1_310_000_000m,
        IReadOnlyDictionary<string, long>? totals = null) =>
        new()
        {
            SessionId = Guid.NewGuid(),
            StartedAt = updatedAt - (duration ?? TimeSpan.FromHours(1)),
            UpdatedAt = updatedAt,
            Duration = duration ?? TimeSpan.FromHours(1),
            SpotId = spotId,
            CharacterClass = "Maegu · Awakening",
            Totals = totals is null
                ? new Dictionary<string, long>
                {
                    [trashName] = trash,
                    ["Caphras Stone"] = 124
                }
                : totals.ToDictionary(static pair => pair.Key, static pair => pair.Value,
                    StringComparer.Ordinal),
            SilverBeforeTax = silver,
            SilverAfterTax = silver,
            SilverIsComplete = true
        };

}
