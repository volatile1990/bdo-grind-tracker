using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimeLootTextParserTests
{
    [Theory]
    [InlineData("Twilight of the End - Ring x 1", "Twilight of the End - Ring")]
    [InlineData("Twilight of the End - Rlng x 1", "Twilight of the End - Ring")]
    [InlineData("Twilight of the End - R1ng x 1", "Twilight of the End - Ring")]
    [InlineData("Dämmerung des Endes – Ohrrlng x 1", "Twilight of the End - Earring")]
    [InlineData("Dämmerung des Endes – Ring x 1", "Twilight of the End - Ring")]
    [InlineData("Twilight of the End x 1", null)]
    [InlineData("TRI: Twilight of the End - Ring x 1", null)]
    [InlineData("TRI Twilight of the End - Ring x 1", null)]
    [InlineData("TRITwilight of the End - Ring x 1", null)]
    [InlineData("tri twilight of the end - ring x 1", null)]
    [InlineData("V Twilight of the End - Ring x 1", null)]
    [InlineData("Unrelated Foreign Treasure x 1", null)]
    [InlineData("Other family Rlng x 1", null)]
    public void SpecialTextUsesTheSharedParserWithoutLosingIdentitySafety(string text, string? expected)
    {
        var parser = new LifetimeLootTextParser(new(0,
            [new("Twilight of the End - Ring", ["Dämmerung des Endes – Ring"], true),
                new("Twilight of the End - Earring", ["Dämmerung des Endes – Ohrring"], true),
                new("Twilight of the End - Belt", [], true), new("Twilight of the End - Necklace", [], true)]), LootSource.Rare);
        var row = Raw(text) with { Source = LootSource.Rare };
        Assert.Equal(expected, parser.Parse(row)?.Name);
        Assert.True(parser.Parse(row with { Source = LootSource.Normal })!.IsExcluded);
        Assert.True(parser.Parse(row with { RejectionReason = AutomaticLootSpotLock.OutsideSpotPoolReason })!.IsExcluded);
        Assert.True(parser.Parse(row with { RejectionReason = "ocr-geometry" })!.IsExcluded);
    }

    [Fact]
    public void SpecialEnhancementCannotReuseAnAcceptedBaseAccessoryFallback()
    {
        const string ring = "Twilight of the End - Ring";
        var parser = new LifetimeLootTextParser(new(0, [new(ring, [], true)]), LootSource.Rare);
        var accepted = Raw("TRI: " + ring + " x 1") with
            { Source = LootSource.Rare, ItemName = ring, Quantity = 1, RejectionReason = null };
        Assert.True(parser.Parse(accepted)!.IsExcluded);
        var adapter = new LifetimeNormalReconciliationAdapter(parser.Context, false, LootSource.Rare, 1);
        adapter.ProcessObservations([accepted], Start);
        Assert.Empty(adapter.Projection!.Totals);
    }

    [Theory]
    [InlineData("l Apeiron Earring x 1")]
    [InlineData("ll Apeiron Earring x 1")]
    [InlineData("lll Apeiron Earring x 1")]
    [InlineData("lV Apeiron Earring x 1")]
    [InlineData("lVApeironEarring x 1")]
    [InlineData("l Apeiron-Ohrring x 1")]
    [InlineData("lV Apeiron-Ohrring x 1")]
    [InlineData("lVApeiron-Ohrring x 1")]
    [InlineData("1V Apeiron-Ohrring x 1")]
    [InlineData("lV Twilight of the End - Earring x 1")]
    [InlineData("lV Dämmerung des Endes – Ohrring x 1")]
    public void RomanEnhancementGlyphErrorsDoNotBecomeBaseAccessories(string text)
    {
        var parser = new LifetimeLootTextParser(AccessoryCatalog(), LootSource.Rare);
        Assert.Null(parser.Parse(Raw(text) with { Source = LootSource.Rare }));
    }

    [Theory]
    [InlineData("l Apeiron-Ohrring")]
    [InlineData("ll Apeiron-Ohrring")]
    [InlineData("lV Apeiron-Ohrring")]
    [InlineData("1V Apeiron-Ohrring")]
    [InlineData("1 Apeiron-Ohrring x 1")]
    [InlineData("11 Apeiron Earring x 1")]
    public void RomanEnhancementGlyphErrorsExcludeAcceptedFixedUnitFallback(string text)
    {
        var parser = new LifetimeLootTextParser(AccessoryCatalog(), LootSource.Rare);
        var row = Raw(text) with
        {
            Source = LootSource.Rare, ItemName = "Apeiron Earring", Quantity = 1,
            NameConfidence = 1, RejectionReason = null, UsesFixedUnitQuantity = true,
        };
        Assert.True(parser.Parse(row)!.IsExcluded);
    }

    [Theory]
    [InlineData("1 Apeiron Earring")]
    [InlineData("11 Apeiron Earring")]
    [InlineData("1 Apeiron-Ohrring")]
    [InlineData("11 Apeiron-Ohrring")]
    public void LeadingNumericQuantitiesDoNotBecomeRomanEnhancements(string text)
    {
        var parser = new LifetimeLootTextParser(AccessoryCatalog(), LootSource.Rare);
        var row = Raw(text) with { Source = LootSource.Rare };
        foreach (var reading in new[] { row, row with
            { ItemName = "Apeiron Earring", Quantity = 1, RejectionReason = null } })
        {
            var parsed = parser.Parse(reading);
            Assert.False(parsed!.IsExcluded);
            Assert.Equal("Apeiron Earring", parsed.Name);
            Assert.Equal(1, parsed.Quantity);
        }
    }

    [Theory]
    [InlineData("Apeiron Earring x 1", "Apeiron Earring")]
    [InlineData("Apeiron Earrlng x 1", "Apeiron Earring")]
    [InlineData("Apeiron-Ohrrlng x 1", "Apeiron Earring")]
    [InlineData("Apeiron Rlng x 1", "Apeiron Ring")]
    [InlineData("Apeiron R1ng x 1", "Apeiron Ring")]
    [InlineData("Laila's Petal x 1", "Laila's Petal")]
    [InlineData("Lailas Blütenblatt x 1", "Laila's Petal")]
    public void RomanEnhancementGlyphGuardPreservesBaseNamesAndItemGlyphRepairs(string text, string expected)
    {
        var parser = new LifetimeLootTextParser(AccessoryCatalog(), LootSource.Rare);
        var row = Raw(text) with { Source = LootSource.Rare };
        Assert.Equal(expected, parser.Parse(row)!.Name);
        Assert.False(parser.Parse(row with
            { ItemName = expected, Quantity = 1, RejectionReason = null })!.IsExcluded);
    }

    [Fact]
    public void RomanEnhancementGlyphGuardDoesNotChangeNormalRawParsing()
    {
        var parser = new LifetimeLootTextParser(AccessoryCatalog());
        Assert.Equal("Apeiron Earring", parser.Parse(Raw("lV Apeiron Earring x 1"))!.Name);
    }

    private static LifetimeParsingContext AccessoryCatalog() => new(0,
        new[] { "Apeiron Earring", "Apeiron Ring", "Apeiron Belt", "Apeiron Necklace",
            "Twilight of the End - Earring", "Twilight of the End - Ring", "Laila's Petal" }
            .Select(name => new LifetimeParsingCatalogEntry(name,
                [ItemLocalizationCatalog.GermanNames[name]], true)).ToArray());

    [Fact]
    public void AccessoryGlyphRepairDoesNotChangeTheHistoricalNormalParser()
    {
        var context = new LifetimeParsingContext(0,
            [new("Twilight of the End - Ring", [], true), new("Twilight of the End - Earring", [], true),
                new("Twilight of the End - Belt", [], true), new("Twilight of the End - Necklace", [], true)]);
        var normal = new LifetimeLootTextParser(context);
        Assert.Null(normal.Parse(Raw("Twilight of the End - Rlng x 1")));
        Assert.Null(normal.Parse(Raw("Twilight of the End - R1ng x 1")));
    }

    private const string Helmet = "Elion Follower's Helmet";
    private const string Dust = "Ancient Spirit Dust";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static LifetimeParsingContext Catalog(long revision = 0) => new(revision,
        [new(Helmet, ["Helm eines Elion-Anhängers"]), new(Dust, ["Uralter Geisterstaub"]), new("Black Stone", [])]);

    [Theory]
    [InlineData("Elion Follower's Helmet x 4", Helmet, 4)]
    [InlineData("Elion Follower's Helmot×4", Helmet, 4)]
    [InlineData("4 Elion Follower's Helmet", Helmet, 4)]
    [InlineData("Elion Follower's Helmet 4", Helmet, 4)]
    [InlineData("Ancient Spirit Dust x 1,234", Dust, 1234)]
    [InlineData("Uralter Geisterstaub × 2", Dust, 2)]
    [InlineData("Helm eines Elion-Anhängers x 4", Helmet, 4)]
    public void ParsesActualNumbersAndRecordedAliases(string text, string name, int quantity)
    {
        var parsed = new LifetimeLootTextParser(Catalog()).Parse(Raw(text));
        Assert.NotNull(parsed);
        Assert.Equal(name, parsed.Name);
        Assert.Equal(quantity, parsed.Quantity);
    }

    [Theory]
    [InlineData("Elion Follower's Helmotx 4 10 2115")]
    [InlineData("Arcient Spirit Dust x2b3 1021:15")]
    [InlineData("Ancient Spirit Dust x 1,23")]
    [InlineData("Ancient Spirit Dust x 0")]
    [InlineData("Ancient Spirit Dust x 100000000")]
    [InlineData("[Quint] appeared! / [Muraka] appeared!")]
    public void RejectsMixedPopupFieldsAndInvalidAmounts(string text) =>
        Assert.Null(new LifetimeLootTextParser(Catalog()).Parse(Raw(text)));

    [Theory]
    [InlineData("Elion Follower's Helm")]
    [InlineData("Elion Follower's Helmet x")]
    [InlineData("Elion Follower's Helmet x 4X")]
    public void NameEvidenceDoesNotInventAnUnreadableAmount(string text)
    {
        var parsed = new LifetimeLootTextParser(Catalog()).Parse(Raw(text));
        Assert.True(parsed is null || parsed.Quantity is null);
    }

    [Fact]
    public void AmbiguousAdjacentItemsDoNotBecomeAnArbitraryWinner()
    {
        var parser = new LifetimeLootTextParser(new(0,
            [new("BON Origin Shard", []), new("WON Origin Shard", [])]));
        Assert.Null(parser.Parse(Raw("ON Origin Shard x 1")));
        Assert.Equal("BON Origin Shard", parser.Parse(Raw("BON Origin Shard x 1"))!.Name);
    }

    [Fact]
    public void ReplacingContextInvalidatesMemoizedTextAndExcludesOldAcceptedNames()
    {
        var parser = new LifetimeLootTextParser(new(0, [new(Helmet, ["Verborgene Helmreste"])]));
        var raw = Raw("Verborgene Helmreste x 4");
        Assert.Equal(Helmet, parser.Parse(raw)!.Name);
        parser.UpdateContext(new(1, [new(Dust, ["Verborgene Helmreste"])]));
        Assert.Equal(Dust, parser.Parse(raw)!.Name);
        Assert.True(parser.Parse(raw with { ItemName = Helmet, Quantity = 4, RejectionReason = null })!.IsExcluded);
    }

    [Fact]
    public void SpotExclusionAndAlignmentAnchorsCannotBeReintroducedByRawText()
    {
        var parser = new LifetimeLootTextParser(Catalog());
        var raw = Raw("Elion Follower's Helmet x 4");
        Assert.True(parser.Parse(raw with { RejectionReason = AutomaticLootSpotLock.OutsideSpotPoolReason })!.IsExcluded);
        Assert.True(parser.Parse(raw with { IsAlignmentAnchor = true })!.IsExcluded);
        Assert.True(parser.Parse(raw with { Source = LootSource.Rare })!.IsExcluded);
    }

    [Fact]
    public void NewlyRecoveredFixedUnitItemUsesRecordedPolicyWithoutInventingMissingAmount()
    {
        const string ring = "Twilight of the End - Ring";
        var parser = new LifetimeLootTextParser(new(0, [new(ring, [], isFixedUnit: true)]));
        Assert.Equal(1, parser.Parse(Raw(ring + " x 99"))!.Quantity);
        Assert.Null(parser.Parse(Raw(ring))!.Quantity);
        parser.UpdateContext(new(1, [new(ring, [], isFixedUnit: false)]));
        Assert.Equal(99, parser.Parse(Raw(ring + " x 99"))!.Quantity);
    }

    [Fact]
    public void ProductionAdapterReinterpretsEarlierIncompleteRowsWithoutChangingValidatedAmounts()
    {
        var context = new LifetimeParsingContext(0, [new(Helmet, [])]);
        var adapter = new LifetimeNormalReconciliationAdapter(context);
        var first = Raw("Verborgene Helmreste x 4") with
            { ItemName = Helmet, Quantity = 4, RejectionReason = null, NameConfidence = 1 };
        adapter.ProcessObservations([first], Start);
        adapter.ProcessObservations([first], Start.AddMilliseconds(200));
        adapter.UpdateParsingContext(new(1, [new(Helmet, []), new(Dust, ["Verborgene Helmreste"])]));
        adapter.Complete();
        Assert.Equal(4, adapter.Projection!.Totals[Dust]);
        Assert.False(adapter.Projection.Totals.ContainsKey(Helmet));
        adapter.Reset();
        var amountConflict = Raw("Elion Follower's Helmet x 400") with
            { ItemName = Helmet, Quantity = 4, RejectionReason = null, NameConfidence = 1 };
        adapter.ProcessObservations([amountConflict], Start);
        Assert.Equal(4, adapter.Projection!.Totals[Helmet]);
    }

    private static LootObservation Raw(string text) =>
        new(LootSource.Normal, 0, text, null, null, 0, 0, null, "native-catalog-miss");
}
