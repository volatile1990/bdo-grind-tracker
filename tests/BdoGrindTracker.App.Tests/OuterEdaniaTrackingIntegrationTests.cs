using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class OuterEdaniaTrackingIntegrationTests
{
    [Theory]
    [InlineData("aetherion", "Chilled Soul Piece", 105640L)]
    [InlineData("nymphamare", "Contaminated Coral Piece", 116200L)]
    [InlineData("orbita", "Lightlost Core", 140600L)]
    [InlineData("tenebraum", "Ancient Soldier Fragment", 147630L)]
    [InlineData("zephyros", "Hardened Lava Chunk", 126980L)]
    [InlineData("dark-energy-floodlands", "Tainted Armor Fragment", 100507L)]
    [InlineData("dark-energy-floodlands", "Faded Dark Energy", 597680L)]
    public void RecognizedOuterLootSurvivesSpotLockValuationAndHistory(
        string spotId, string trash, long unitPrice)
    {
        var vocabulary = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "data", "items.en.txt"))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#')).ToArray();
        var matcher = new CompanionItemMatcher(vocabulary);
        var spotLock = new AutomaticLootSpotLock();
        foreach (var observedName in new[] { trash, ItemLocalizationCatalog.GermanNames[trash] })
        {
            Assert.True(matcher.TryMatch(observedName, 4, false, out var matched), observedName);
            Assert.Equal(trash, matched!.CanonicalName);
            spotLock.ObserveConfirmed(Enumerable.Range(0, 3).Select(_ =>
                new CompanionRecognizedEntry(matched.CanonicalName, 4)
                {
                    EventId = Guid.NewGuid(), QuantityDelta = 4, TotalDropQuantity = 4,
                }));
            Assert.Equal(spotId, spotLock.Spot!.Id);
            Assert.True(spotLock.Allows(trash));
            Assert.False(spotLock.Allows("Black Crystal Fragment"));
            Assert.False(spotLock.Allows("Black Gem Fragment"));
            spotLock.Reset();
        }

        var totals = new Dictionary<string, long> { [trash] = 12 };
        var prices = LootPriceCatalog.FixedSnapshot("eu");
        var valuation = SilverValuation.Calculate(totals, prices, SilverTaxOptions.Default);
        Assert.True(valuation.IsComplete);
        Assert.Equal(unitPrice * 12, valuation.AfterTax);
        Assert.Equal(valuation.BeforeTax, valuation.AfterTax);
        var started = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var entry = new LootHistoryEntry
        {
            SessionId = Guid.NewGuid(), SpotId = spotId, StartedAt = started,
            UpdatedAt = started.AddHours(1), Duration = TimeSpan.FromHours(1),
            CharacterClass = "Warrior · Awakening", Totals = totals,
            SilverBeforeTax = valuation.BeforeTax, SilverAfterTax = valuation.AfterTax,
            SilverIsComplete = valuation.IsComplete,
        };
        var directory = Path.Combine(Path.GetTempPath(), "grindcrest-outer-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "history.json");
            new LootHistoryStore(path).Save([entry]);
            var restored = Assert.Single(new LootHistoryStore(path).Load());
            Assert.Equal(entry.SpotId, restored.SpotId);
            Assert.Equal(12, restored.Totals[trash]);
            Assert.Equal(unitPrice * 12, restored.SilverAfterTax);
            var preview = GarmothUploadPreview.ForHistory(restored, prices, SilverTaxOptions.Default);
            if (spotId == "dark-energy-floodlands")
            {
                Assert.False(preview.IsReady);
                Assert.Contains("Gebiet", preview.Error);
            }
            else
            {
                Assert.True(preview.IsReady, preview.Error);
                Assert.Equal(unitPrice * 12, preview.Payload!.Total);
                Assert.Equal(12, Assert.Single(preview.Payload.Drops).Value);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void FloodlandsSecondaryTrashContributesSilverWithoutChangingPrimaryTrashRate()
    {
        var totals = new Dictionary<string, long>
        {
            ["Tainted Armor Fragment"] = 100, ["Faded Dark Energy"] = 7,
        };
        var value = SilverValuation.Calculate(totals, LootPriceCatalog.FixedSnapshot("eu"), SilverTaxOptions.Default);
        Assert.True(value.IsComplete);
        Assert.Equal(14_234_460, value.AfterTax);
        Assert.Equal(100, Presentation.Trash(totals, "dark-energy-floodlands"));
    }

    [Fact]
    public void OuterLootAppearsInTheOverlaysTrashAndRareFilters()
    {
        var totals = new Dictionary<string, long>
        {
            ["Tainted Armor Fragment"] = 100, ["Faded Dark Energy"] = 7,
            ["Deboreka Earring"] = 1, ["HAN Crystal of Dusky Ruin"] = 1,
            ["Flawless Herald's Crystal"] = 1, ["Black Stone"] = 20,
        };
        var snapshot = new OverlayMetrics().Update(new TrackerState
        {
            SpotId = "dark-energy-floodlands", Loot = new(totals, 130, 8),
        }, new());
        var widget = OverlayCatalog.CreateWidget("drop-grid");
        var trash = OverlayLootPresentation.Create(widget with { ItemFilter = "trash" }, snapshot).Items;
        Assert.Equal(2, trash.Count);
        Assert.Contains(trash, item => item.CanonicalName == "Faded Dark Energy" && item.Quantity == 7);
        var rare = OverlayLootPresentation.Create(widget with { ItemFilter = "rare" }, snapshot).Items;
        Assert.Equal(3, rare.Count);
        Assert.DoesNotContain(rare, item => item.CanonicalName is "Faded Dark Energy" or "Black Stone");
        Assert.Equal("100", snapshot.Metrics["trash"].Value);
    }
}
