using System.Security.Cryptography;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class ConsumablesPresentationTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-21T12:00:00Z");

    [Theory]
    [InlineData("tent-adventures-boon")]
    [InlineData("tent-body-enhancement")]
    [InlineData("tent-turning-gates")]
    public void TentDurationsKeepSeparateTilesWithTheirRecordedPricesAndLegacyMetadata(string family)
    {
        var consumptions = new[]
        {
            Use(family + "-60", 100, start: true),
            Use(family + "-120", 200) with { Price = new(200, "eu", At, true) },
            Use(family + "-300", 300),
            new BuffConsumption("automatic-" + family, "Legacy family", null, At, null),
        };
        var snapshot = new BuffLedgerSnapshot(consumptions, [], []);
        var before = JsonSerializer.Serialize(snapshot);

        var result = ConsumablesPresentation.Create(snapshot, "en");

        Assert.Equal(4, result.Items.Count);
        Assert.All(result.Items, item => { Assert.Equal(1, item.Count); Assert.NotNull(item.IconPath); });
        foreach (var minutes in new[] { 60, 120, 300 })
            Assert.Contains($"{minutes} min", Assert.Single(result.Items, item => item.Id == family + "-" + minutes).Name);
        var shortUse = Assert.Single(result.Items, item => item.Id == family + "-60");
        var mediumUse = Assert.Single(result.Items, item => item.Id == family + "-120");
        var longUse = Assert.Single(result.Items, item => item.Id == family + "-300");
        var unresolved = Assert.Single(result.Items, item => item.Id == "automatic-" + family);
        Assert.Equal(100m, shortUse.KnownCost);
        Assert.Equal(200m, mediumUse.KnownCost);
        Assert.Equal(300m, longUse.KnownCost);
        Assert.Equal(0m, unresolved.KnownCost);
        Assert.Equal(1, unresolved.UnpricedCount);
        Assert.Equal("600 silver *", result.Cost);
        Assert.Contains("Unit price: 100 silver", shortUse.Tooltip);
        Assert.Contains("Active when first detected: 1", shortUse.Tooltip);
        Assert.Contains("1 without a price", unresolved.Tooltip);
        Assert.Contains("Some costs use cached market prices.", mediumUse.Tooltip);
        Assert.True(result.HasMissingPrices);
        Assert.Equal(before, JsonSerializer.Serialize(snapshot));
    }

    [Theory]
    [InlineData("tent-body-enhancement-90")]
    [InlineData("tent-body-enhancement-180")]
    [InlineData("tent-turning-gates-90")]
    [InlineData("tent-turning-gates-180")]
    public void LegacyOnlyTentBookingKeepsItsOwnDurationWithoutRepricing(string savedId)
    {
        var saved = Use(savedId, 1234);
        var item = Assert.Single(ConsumablesPresentation.Create(new([saved], [], []), "en").Items);

        Assert.Equal(savedId, item.Id);
        Assert.Equal(1, item.Count);
        Assert.Equal(1234m, item.KnownCost);
        Assert.Equal(savedId, saved.BuffId);
    }

    [Fact]
    public void DistinctDurationsFamiliesAndUnrecognizedIdsNeverMerge()
    {
        var value = new BuffLedgerSnapshot(
            [Use("tent-body-enhancement-60", 100), Use("tent-body-enhancement-300", 200),
             Use("tent-turning-gates-60", 100), Use("tent-turning-gates-300", 200),
             Use("tent-adventures-boon-60", 100), Use("tent-adventures-boon-300", 200),
             Use("tent-adventurers-luck-i", 100), Use("tent-adventurers-luck-ii", 200),
             Use("harmony-draught", 100), Use("immortal-harmony-draught", 200),
             new("tent-body-enhancement-999", "Custom", null, At, null),
             new("automatic-tent-body-enhancement-999", "Custom family", null, At, null)], [], []);

        var items = ConsumablesPresentation.Create(value, "en").Items;

        Assert.Equal(12, items.Count);
        Assert.All(items, item => Assert.Equal(1, item.Count));
        Assert.Equal(value.Consumptions.Select(item => item.BuffId).Order(), items.Select(item => item.Id).Order());
        Assert.Equal(1500m, items.Sum(item => item.KnownCost));
    }

    [Theory]
    [InlineData("de", "12,50 Mio. Silber", "12.500.000 Silber", "Arznei der Harmonie", "Bei erster Erkennung aktiv: 1")]
    [InlineData("en", "12.50 M silver", "12,500,000 silver", "Harmony Draught", "Active when first detected: 1")]
    public void GroupsOnlyBookingsAndKeepsTheirOriginalPricesAndInitialCount(
        string language, string cost, string exact, string name, string starting)
    {
        var value = new BuffLedgerSnapshot(
            [Use("harmony-draught", 6_000_000m, start: true), Use("harmony-draught", 6_500_000m)],
            [new("simple-cron-meal", "Simple Cron Meal", 9692, TimeSpan.FromHours(1), 99_000_000, TimeSpan.Zero)],
            [new("harmony-draught", "Harmony Draught", 1399, TimeSpan.FromMinutes(5), At,
                new(1, "eu", At, false), false)]);

        var result = ConsumablesPresentation.Create(value, language);

        var item = Assert.Single(result.Items);
        Assert.Equal("harmony-draught", item.Id);
        Assert.Equal(name, item.Name);
        Assert.Equal(2, item.Count);
        Assert.Equal(12_500_000m, item.KnownCost);
        Assert.Equal(0, item.UnpricedCount);
        Assert.Equal(cost, result.Cost);
        Assert.Contains(exact, result.CostDescription);
        Assert.Contains(exact, item.Tooltip);
        Assert.Contains(language == "de" ? "6.000.000–6.500.000 Silber" : "6,000,000–6,500,000 silver", item.Tooltip);
        Assert.Contains(starting, item.Tooltip);
        Assert.DoesNotContain("Grindstart", item.Tooltip);
        Assert.DoesNotContain("grind start", item.Tooltip);
        Assert.False(result.HasMissingPrices);
    }

    [Theory]
    [InlineData("de", "Preis fehlt", "250 Silber *", "1 ohne Preis")]
    [InlineData("en", "Price missing", "250 silver *", "1 without a price")]
    public void UnknownAndPartiallyKnownPricesNeverLookComplete(
        string language, string unknownCost, string partialCost, string missing)
    {
        var unknown = ConsumablesPresentation.Create(new([Use("simple-cron-meal", null)], [], []), language);
        Assert.Equal(unknownCost, unknown.Cost);
        Assert.True(unknown.HasMissingPrices);
        Assert.Contains(missing, Assert.Single(unknown.Items).Tooltip);
        Assert.DoesNotContain("0 silver", unknown.CostDescription);
        Assert.DoesNotContain("0 Silber", unknown.CostDescription);

        var partial = ConsumablesPresentation.Create(new(
            [Use("simple-cron-meal", 250), Use("simple-cron-meal", null)], [], []), language);
        Assert.Equal(partialCost, partial.Cost);
        Assert.True(partial.HasMissingPrices);
        var item = Assert.Single(partial.Items);
        Assert.Equal(2, item.Count);
        Assert.Equal(250m, item.KnownCost);
        Assert.Equal(1, item.UnpricedCount);
        Assert.Contains(missing, item.Tooltip);
    }

    [Fact]
    public void UnknownReadingsAndUnbookedActiveBuffsCreateNoItems()
    {
        var unknown = ConsumablesPresentation.Create(null, "de");
        Assert.Empty(unknown.Items);
        Assert.Equal("—", unknown.Cost);
        Assert.False(unknown.HasMissingPrices);

        var active = ConsumablesPresentation.Create(new([], [],
            [new("simple-cron-meal", "Simple Cron Meal", 9692, TimeSpan.FromMinutes(60), At,
                new(500, "eu", At, false), true)]), "de");
        Assert.Empty(active.Items);
        Assert.Equal("0 Silber", active.Cost);
    }

    [Fact]
    public void OrdinaryAndImmortalKeepSeparateCountsNamesAndProvenIcons()
    {
        var value = new BuffLedgerSnapshot(
            [Use("harmony-draught", 100) with { Name = "Same saved name" },
             Use("immortal-harmony-draught", 200) with { Name = "Same saved name" }], [], []);

        var items = ConsumablesPresentation.Create(value, "en").Items;

        Assert.Equal(2, items.Count);
        var normal = Assert.Single(items, item => item.Id == "harmony-draught");
        var immortal = Assert.Single(items, item => item.Id == "immortal-harmony-draught");
        Assert.Equal("Harmony Draught", normal.Name);
        Assert.Equal("Immortal: Harmony Draught", immortal.Name);
        Assert.NotEqual(normal.IconPath, immortal.IconPath);
        Assert.All(items, item => Assert.Equal(1, item.Count));
    }

    [Fact]
    public void CurrentUnknownGroupsUseTheirSharedIconAndLegacyGroupsStayNeutral()
    {
        var current = AutomaticBuffCatalog.Default.GroupDefinitions.Single(item => item.Id == "automatic-tent-adventurers-luck");
        var value = new BuffLedgerSnapshot(
            [new(current.Id, current.Name, null, At, null),
             new("automatic-harmony-draught", "Immortal: Harmony Draught (variant unknown)", null, At, null),
             new("automatic-cron-meal", "Cron-Mahlzeiten (variant unknown)", null, At, null),
             Use("frenzy-draught", 500)], [], []);

        var items = ConsumablesPresentation.Create(value, "en").Items;

        var luck = Assert.Single(items, item => item.Id == current.Id);
        Assert.Equal("Adventurer's Luck (variant unknown)", luck.Name);
        Assert.NotNull(luck.IconPath);
        var legacy = Assert.Single(items, item => item.Id == "automatic-harmony-draught");
        Assert.Equal("Harmony Draught (variant unknown)", legacy.Name);
        Assert.Null(legacy.IconPath);
        var legacyMeal = Assert.Single(items, item => item.Id == "automatic-cron-meal");
        Assert.Equal("Cron meals (variant unknown)", legacyMeal.Name);
        Assert.Null(legacyMeal.IconPath);
        Assert.Null(Assert.Single(items, item => item.Id == "frenzy-draught").IconPath);
    }

    [Fact]
    public void EveryActiveDefinitionAndCurrentFamilyHasTheVerifiedPackagedAsset()
    {
        var definitions = BuffPriceCatalog.Definitions.Concat(AutomaticBuffCatalog.Default.GroupDefinitions).ToArray();
        var snapshot = new BuffLedgerSnapshot(definitions.Select(definition =>
            new BuffConsumption(definition.Id, definition.Name, definition.MarketItemId, At, null)).ToArray(), [], []);
        var items = ConsumablesPresentation.Create(snapshot, "en").Items;
        Assert.Equal(definitions.Select(definition => definition.Id)
            .Distinct(StringComparer.Ordinal).Count(), items.Count);
        Assert.All(items, item => Assert.StartsWith("assets/buffs/client-", item.IconPath));

        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "data", "ocr", "buffs", "client-mapping.json")));
        var expectedHashes = metadata.RootElement.GetProperty("templates").EnumerateArray().ToDictionary(
            item => "assets/buffs/" + item.GetProperty("imagePath").GetString(),
            item => item.GetProperty("imageSha256").GetString());
        var paths = items.Select(item => item.IconPath!).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(37, paths.Length);
        foreach (var path in paths)
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "wwwroot", path));
            Assert.Equal(expectedHashes[path], Convert.ToHexStringLower(SHA256.HashData(bytes)));
            Assert.True(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        }
    }

    private static BuffConsumption Use(string id, decimal? price, bool start = false)
    {
        var definition = BuffPriceCatalog.HistoryDefinitions.Single(item => item.Id == id);
        return new(id, definition.Name, definition.MarketItemId, At,
            price is { } cost ? new(cost, "eu", At, false) : null) { IsSessionStart = start };
    }
}
