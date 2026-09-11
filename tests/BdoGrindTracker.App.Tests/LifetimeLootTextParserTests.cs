using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimeLootTextParserTests
{
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
