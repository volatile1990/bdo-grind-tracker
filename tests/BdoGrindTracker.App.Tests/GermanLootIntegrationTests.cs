using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionLootFrameAnalyzerTests
{
    [Theory]
    [InlineData("Chilled Soul Piece")]
    [InlineData("Contaminated Coral Piece")]
    public async Task UserConfirmedSingleTrashDropsCountThroughBothLanguagePipelines(string canonical)
    {
        foreach (var name in new[] { canonical, ItemLocalizationCatalog.GermanNames[canonical] })
        foreach (var quantity in new[] { 1, 2 })
        {
            var rows = new Rows(new Input(250, name + " x" + quantity, quantity));
            using var analyzer = new CompanionLootFrameAnalyzer(Calibration(),
                new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys), rows, new Names(rows));
            using var frame = new Bitmap(800, 600);
            await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
            var counted = analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1)).NewEvents;
            var drop = Assert.Single(counted);
            Assert.Equal(canonical, drop.ItemName);
            Assert.Equal(quantity, drop.Quantity);
        }
    }

    public static IEnumerable<object[]> LocalizedDropCombinations() => DropQuantityCatalog.Entries
        .SelectMany(entry => new[] { LootSource.Normal, LootSource.Rare }
            .Where(source => source == LootSource.Normal || !TrashLootMinimumCatalog.Entries.Any(trash => trash.ItemName == entry.ItemName))
            .SelectMany(source => new[] { "de", "en" }
                .Select(language => new object[] { entry.SpotId, entry.ItemName, source, language })));

    [Theory]
    [MemberData(nameof(LocalizedDropCombinations))]
    public async Task LocalizedDropsKeepSpotDetectionMinimumMaximumAndFixedUnitRules(
        string spotId, string item, LootSource source, string language)
    {
        var trash = TrashLootMinimumCatalog.Entries.First(entry => entry.SpotId == spotId).ItemName;
        var bounds = DropQuantityCatalog.GetBounds(spotId, item);
        // Exercise the imported per-spot minimum fallback and maximum in both
        // languages. Future unverified pairs can still accept explicit amounts.
        foreach (var hasQuantity in bounds is null ? new[] { true } : new[] { false, true })
        {
            var itemText = ItemLocalizationCatalog.DisplayName(item, language) + (hasQuantity ? " x9999" : "");
            var rows = new Rows(new Input(250, ItemLocalizationCatalog.DisplayName(trash, language) + " x17", 17));
            if (item == trash) rows.Values = [];
            if (source == LootSource.Normal) rows.Values = [.. rows.Values, new(item == trash ? 250 : 200, itemText, -1)];
            else rows.RareText = itemText;
            using var analyzer = new CompanionLootFrameAnalyzer(Calibration() with
            {
                HasRareLootAnchor = source == LootSource.Rare, RareLootAnchorX = 600, RareLootAnchorY = 300,
            }, new CompanionItemMatcher(ItemLocalizationCatalog.GermanNames.Keys), rows, new Names(rows),
                rareRowPipeline: source == LootSource.Rare ? new RareRows() : null);
            using var frame = new Bitmap(800, 600);
            var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
            var observation = Assert.Single(result.Observations, row => row.ItemName == item);
            Assert.Null(observation.RejectionReason);
            Assert.Equal(bounds?.IsFixedUnit == true, observation.UsesFixedUnitQuantity);
            Assert.Equal(bounds, observation.QuantityBounds);
            DiagnosticRecordingSession.ValidateObservations(result.Observations);
            var counted = Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1)).NewEvents,
                change => change.ItemName == item);
            Assert.Equal((int)(hasQuantity ? Math.Min(9999u, bounds?.Maximum ?? uint.MaxValue) : bounds!.Minimum), counted.Quantity);
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
