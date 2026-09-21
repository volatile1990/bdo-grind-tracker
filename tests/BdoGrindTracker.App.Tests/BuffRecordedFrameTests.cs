using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;
using BdoGrindTracker.Ocr;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffRecordedFrameTests(ITestOutputHelper output)
{
    [WindowsOcrFact]
    [Trait("Category", "WindowsOcr")]
    public void RealCronAndBoonAppearWithCostsEvenWhenTheFirstScanMissesThem()
    {
        using var reader = new AutomaticBuffFrameReader();
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs", "bar-cron67m-boon2h.png"));
        var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));
        const string cron = "simple-cron-meal", boonFamily = "automatic-tent-adventures-boon", boon = "tent-adventures-boon-300";
        Assert.Empty(reading.UnknownBuffIds);
        Assert.Equal(7, reading.Observations.Count);
        Assert.Equal(TimeSpan.FromMinutes(67), Assert.Single(reading.Observations, item => item.BuffId == cron).Remaining);
        var observedBoon = Assert.Single(reading.Observations, item => item.BuffId == boonFamily);
        Assert.Equal(TimeSpan.FromHours(2), observedBoon.Remaining);
        Assert.Equal(TimeSpan.FromHours(1), observedBoon.TimerPrecision);

        var ledger = new BuffLedger(BuffPriceCatalog.HistoryDefinitions.Concat(AutomaticBuffCatalog.Default.HistoricalGroupDefinitions));
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        BuffPrice Price(BuffDefinition definition) => new(definition.FixedUnitPrice ?? 120_000m, "eu", at, false);
        ledger.Apply(reading.Observations.Where(item => item.BuffId != cron && item.BuffId != boonFamily), at, Price,
            [cron, boonFamily]);
        ledger.Apply(reading.Observations, at.AddSeconds(10), Price);
        var result = ledger.Apply(reading.Observations, at.AddSeconds(20), Price);
        var tiles = BdoGrindTracker.App.Components.ConsumablesPresentation.Create(result, "en").Items;

        Assert.Equal(7, tiles.Count);
        Assert.Equal(120_000m, Assert.Single(tiles, item => item.Id == cron).KnownCost);
        Assert.Equal(12_000_000m, Assert.Single(tiles, item => item.Id == boon).KnownCost);
        Assert.All(tiles, item => { Assert.Equal(1, item.Count); Assert.NotNull(item.IconPath); });
        Assert.All(result.Consumptions, item => Assert.True(item.IsSessionStart));
        Assert.Equal(result.Consumptions, ledger.Apply(reading.Observations, at.AddSeconds(30), Price).Consumptions);
    }

    [WindowsOcrFact]
    [Trait("Category", "WindowsOcr")]
    public void RealTenacityAndHarmonyRenewalsSurviveOneIndividualUnreadableScan()
    {
        using var reader = new AutomaticBuffFrameReader();
        using var beforeFrame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs", "bar-edania-tenacity-8m.png"));
        using var afterFrame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs", "bar-tenacity-harmony-17m.png"));
        var before = Assert.IsType<BuffFrameReading>(reader.Read(beforeFrame, CancellationToken.None));
        var after = Assert.IsType<BuffFrameReading>(reader.Read(afterFrame, CancellationToken.None));
        const string tenacity = "perfume-of-tenacity", harmony = "harmony-draught-edania";
        foreach (var id in new[] { tenacity, harmony })
        {
            Assert.Equal(TimeSpan.FromMinutes(8), Assert.Single(before.Observations, item => item.BuffId == id).Remaining);
            Assert.Equal(TimeSpan.FromMinutes(17), Assert.Single(after.Observations, item => item.BuffId == id).Remaining);
            Assert.DoesNotContain(id, after.UnknownBuffIds);
        }

        var ledger = new BuffLedger(BuffPriceCatalog.HistoryDefinitions.Concat(AutomaticBuffCatalog.Default.GroupDefinitions));
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        BuffPrice Price(BuffDefinition definition) => new(definition.Id == tenacity ? 200 : 100, "eu", at, false);
        ledger.Apply(before.Observations, at, Price);
        ledger.Apply(before.Observations, at.AddSeconds(10), Price);
        ledger.Apply(before.Observations.Where(item => item.BuffId != tenacity), at.AddSeconds(20), Price, [tenacity]);
        var renewed = ledger.Apply(after.Observations, at.AddSeconds(30), Price);
        Assert.Single(renewed.Consumptions, item => item.BuffId == tenacity && !item.IsSessionStart);
        Assert.Single(renewed.Consumptions, item => item.BuffId == harmony && !item.IsSessionStart);
        var result = ledger.Apply(after.Observations, at.AddSeconds(40), Price);

        foreach (var id in new[] { tenacity, harmony })
        {
            Assert.Equal(2, result.Consumptions.Count(item => item.BuffId == id));
            var renewal = Assert.Single(result.Consumptions, item => item.BuffId == id && !item.IsSessionStart);
            Assert.Equal(at.AddSeconds(30), renewal.ConsumedAt);
            Assert.Equal(id == tenacity ? 200 : 100, renewal.Cost);
        }
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(result.Usage, item => item.BuffId == tenacity).ObservedDuration);
    }

    [WindowsOcrFact]
    [Trait("Category", "WindowsOcr")]
    public void TransparentItemArtworkAndEightMinuteTimersAreRecognizedTogether()
    {
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs", "bar-edania-tenacity-8m.png"));
        using var reader = new AutomaticBuffFrameReader();

        var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));

        Assert.Empty(reading.UnknownBuffIds);
        Assert.Equal(7, reading.Observations.Count);
        Assert.Equal(TimeSpan.FromMinutes(8), Assert.Single(reading.Observations,
            item => item.BuffId == "harmony-draught-edania").Remaining);
        Assert.Equal(TimeSpan.FromMinutes(8), Assert.Single(reading.Observations,
            item => item.BuffId == "perfume-of-tenacity").Remaining);
        Assert.Equal(TimeSpan.FromMinutes(29), Assert.Single(reading.Observations,
            item => item.BuffId == "mystic-beasts-all-ap").Remaining);
        Assert.Equal(TimeSpan.FromMinutes(28), Assert.Single(reading.Observations,
            item => item.BuffId == "simple-cron-meal").Remaining);
        Assert.DoesNotContain(reading.Observations, item => item.BuffId.StartsWith("immortal-", StringComparison.Ordinal));
    }

    [WindowsOcrFact]
    [Trait("Category", "WindowsOcr")]
    public void AutomaticRealHudCountsConfirmedRefreshesWithCatalogPrices()
    {
        using var reader = new AutomaticBuffFrameReader();
        var ledger = new BuffLedger(BuffPriceCatalog.HistoryDefinitions.Concat(AutomaticBuffCatalog.Default.GroupDefinitions));
        var files = new[] { "bar-9m-110m.png", "bar-9m-109m.png", "bar-13m-114m.png", "bar-13m-114m.png" };
        var at = DateTimeOffset.Parse("2026-09-21T12:00:00Z");
        var harmony = BuffPriceCatalog.Definitions.Single(item => item.Id == "harmony-draught-demihuman");
        var meal = BuffPriceCatalog.Definitions.Single(item => item.Id == "simple-cron-meal");
        var prices = new LootPriceSnapshot("eu", [
            new(harmony.Name, 1_200_000m, 0, LootPriceOrigin.LiveMarket, at),
            new(meal.Name, 120_000m, 0, LootPriceOrigin.LiveMarket, at),
        ]);
        for (var index = 0; index < files.Length; index++)
        {
            using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs", files[index]));
            var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));
            Assert.Empty(reading.UnknownBuffIds);
            ledger.Apply(reading.Observations, at.AddSeconds(index * 10), definition => BuffPriceCatalog.GetPrice(definition, prices));
            Assert.Equal(index == 0 ? 0 : index == 1 ? 2 : 4, ledger.Snapshot.Consumptions.Count);
        }
        Assert.Equal(2, ledger.Snapshot.Active.Count);
        Assert.Equal(4, ledger.Snapshot.Consumptions.Count);
        foreach (var isSessionStart in new[] { true, false })
        {
            Assert.Equal(1_200_000m, Assert.Single(ledger.Snapshot.Consumptions,
                item => item.BuffId == harmony.Id && item.IsSessionStart == isSessionStart).Cost);
            Assert.Equal(120_000m, Assert.Single(ledger.Snapshot.Consumptions,
                item => item.BuffId == meal.Id && item.IsSessionStart == isSessionStart).Cost);
        }
        Assert.Equal(2_640_000m, ledger.Snapshot.ConsumedCost);
    }

    [WindowsOcrTheory]
    [Trait("Category", "WindowsOcr")]
    [InlineData("bar-13m-114m.png", "harmony-draught-demihuman", 13, 114)]
    [InlineData("bar-9m-110m.png", "harmony-draught-demihuman", 9, 110)]
    [InlineData("bar-body20m-cron119m.png", "automatic-tent-body-enhancement", 20, 119)]
    public void BundledCatalogReadsRealHudWithoutPersonalProfile(string file, string otherBuff, int otherMinutes, int mealMinutes)
    {
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs", file));
        using var reader = new AutomaticBuffFrameReader();
        var reading = reader.Read(frame, CancellationToken.None);
        output.WriteLine(reader.LastDiagnostic ?? "No diagnostic");
        Assert.NotNull(reading);
        Assert.Empty(reading.UnknownBuffIds);
        Assert.Equal(2, reading.Observations.Count);
        Assert.Equal(TimeSpan.FromMinutes(otherMinutes), reading.Observations.Single(item => item.BuffId == otherBuff).Remaining);
        var meal = Assert.Single(reading.Observations, item => item.BuffId.EndsWith("cron-meal", StringComparison.Ordinal));
        Assert.Equal(TimeSpan.FromMinutes(mealMinutes), meal.Remaining);
        if (otherBuff == "harmony-draught-demihuman") Assert.Equal("simple-cron-meal", meal.BuffId);
    }

    [WindowsOcrTheory]
    [Trait("Category", "WindowsOcr")]
    [InlineData("bar-13m-114m.png", 13, 114)]
    [InlineData("bar-9m-110m.png", 9, 110)]
    [InlineData("bar-9m-109m.png", 9, 109)]
    public void ReadsTwoRealBuffIconsAndMinuteTimers(string file, int harmonyMinutes, int mealMinutes)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        Assert.NotNull(engine);
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs");
        using var frame = new Bitmap(Path.Combine(directory, file));
        var profile = new BuffRecognitionProfile
        {
            ScreenWidth = frame.Width, ScreenHeight = frame.Height,
            Region = new(0, 0, frame.Width, frame.Height),
            Templates =
            [
                new("harmony-draught-demihuman", Path.Combine(directory, "harmony.png"), new(-5, 44, 46, 20))
                { ConsumptionAttributionConfirmed = true },
                new("simple-cron-meal", Path.Combine(directory, "cron.png"), new(-6, 44, 54, 20)),
            ]
        };
        using var reader = new BuffFrameReader(() => profile, (image, token) =>
        {
            var result = engine.Recognize(image, token);
            output.WriteLine($"{engine.LanguageTag}: {result.Text}");
            return result;
        });
        var reading = reader.Read(frame, CancellationToken.None);
        Assert.NotNull(reading);
        Assert.Empty(reading.UnknownBuffIds);
        Assert.Equal(2, reading.Observations.Count);
        Assert.Equal(TimeSpan.FromMinutes(harmonyMinutes), reading.Observations.Single(item => item.BuffId == "harmony-draught-demihuman").Remaining);
        Assert.Equal(TimeSpan.FromMinutes(mealMinutes), reading.Observations.Single(item => item.BuffId == "simple-cron-meal").Remaining);
        Assert.All(reading.Observations, item => Assert.Equal(TimeSpan.FromMinutes(1), item.TimerPrecision));
    }
}
