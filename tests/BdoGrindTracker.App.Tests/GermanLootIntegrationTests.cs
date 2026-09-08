using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionLootFrameAnalyzerTests
{
    public static IEnumerable<object[]> GermanDropCombinations() => DropQuantityCatalog.Entries
        .SelectMany(entry => new[] { LootSource.Normal, LootSource.Rare }
            .Where(source => source == LootSource.Normal || !TrashLootMinimumCatalog.Entries.Any(trash => trash.ItemName == entry.ItemName))
            .Select(source => new object[] { entry.SpotId, entry.ItemName, source }));

    [Theory]
    [MemberData(nameof(GermanDropCombinations))]
    public async Task GermanDropsKeepSpotDetectionMinimumMaximumAndFixedUnitRules(string spotId, string item, LootSource source)
    {
        var trash = TrashLootMinimumCatalog.Entries.Single(entry => entry.SpotId == spotId).ItemName;
        var bounds = DropQuantityCatalog.GetBounds(spotId, item)!;
        foreach (var hasQuantity in new[] { false, true })
        {
            var itemText = ItemLocalizationCatalog.GermanNames[item] + (hasQuantity ? " x9999" : "");
            var rows = new Rows(new Input(250, ItemLocalizationCatalog.GermanNames[trash] + " x17", 17));
            if (item == trash) rows.Values = [];
            if (source == LootSource.Normal) rows.Values = [.. rows.Values, new(item == trash ? 250 : 200, itemText, -1)];
            else rows.RareText = itemText;
            using var analyzer = new CompanionLootFrameAnalyzer(Calibration() with
            {
                HasRareLootAnchor = source == LootSource.Rare, RareLootAnchorX = 600, RareLootAnchorY = 300,
            }, new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys), rows, new Names(rows),
                rareRowPipeline: source == LootSource.Rare ? new RareRows() : null);
            analyzer.ConfigureLootFilter(true);
            using var frame = new Bitmap(800, 600);
            var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
            var observation = Assert.Single(result.Observations, row => row.ItemName == item);
            Assert.Null(observation.RejectionReason);
            Assert.Equal(bounds.IsFixedUnit, observation.UsesFixedUnitQuantity);
            Assert.Equal(bounds, observation.QuantityBounds);
            DiagnosticRecordingSession.ValidateObservations(result.Observations);
            var counted = Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1)).NewEvents,
                change => change.ItemName == item);
            Assert.Equal((int)(hasQuantity ? Math.Min(9999u, bounds.Maximum!.Value) : bounds.Minimum), counted.Quantity);
        }
    }

    [Theory]
    [InlineData("ON-Kristall des wandernden Ursprungs")]
    [InlineData("Dämmerung des Endes")]
    public async Task TruncatedGermanRareItemDoesNotBecomeAFixedUnitFalsePositive(string text)
    {
        var rows = new Rows(new Input(250, "Schwarzkristallfragment x17", 17)) { RareText = text };
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration() with
        {
            HasRareLootAnchor = true, RareLootAnchorX = 600, RareLootAnchorY = 300,
        }, new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys), rows, new Names(rows),
            rareRowPipeline: new RareRows());
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.NotNull(Assert.Single(result.Observations, row => row.Source == LootSource.Rare).RejectionReason);
        Assert.All(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents,
            change => Assert.Equal("Black Crystal Fragment", change.ItemName));
    }
}
