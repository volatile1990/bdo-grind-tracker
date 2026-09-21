using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimeLootSourceParserTests
{
    private const string Vestige = "Broken Vestige of Everlight";
    private const string Dust = "Ancient Spirit Dust";

    private static LifetimeParsingContext Context() => new(0,
    [
        new(Vestige, [ItemLocalizationCatalog.GermanNames[Vestige]], true, LootSource.Rare),
        new(Dust, [ItemLocalizationCatalog.GermanNames[Dust]], false, LootSource.Normal),
    ]);

    [Theory]
    [InlineData(LootSource.Normal, Vestige)]
    [InlineData(LootSource.Rare, Dust)]
    public void AcceptedWrongChannelNameCannotReturnThroughTheOriginalQuantityFallback(LootSource source, string name)
    {
        var parser = new LifetimeLootTextParser(Context(), source);
        var row = Row(source, "unreadable", name);
        Assert.True(parser.Parse(row)!.IsExcluded);
        var counter = new LifetimeNormalReconciliationAdapter(Context(), false, source,
            source == LootSource.Normal ? LifetimeLootReconciler.SlotCount : 1);
        counter.ProcessObservations([row], DateTimeOffset.UnixEpoch);
        Assert.Empty(counter.Projection!.Totals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawEnglishAndGermanNamesAreExcludedInTheWrongChannel(bool german)
    {
        foreach (var (name, source) in new[] { (Vestige, LootSource.Normal), (Dust, LootSource.Rare) })
        {
            var text = (german ? ItemLocalizationCatalog.GermanNames[name] : name) + " x 7";
            var parsed = new LifetimeLootTextParser(Context(), source).Parse(Row(source, text));
            Assert.NotNull(parsed);
            Assert.True(parsed.IsExcluded);
        }
    }

    [Fact]
    public void RankingIncludesTheOtherChannelAndDoesNotReplaceItsWinnerWithAWeakerCandidate()
    {
        var context = new LifetimeParsingContext(0,
            [new(Vestige, [], true, LootSource.Rare),
                new("Broken Vestige of Ebonmere", [], true, LootSource.Normal)]);
        var parser = new LifetimeLootTextParser(context);
        Assert.True(parser.Parse(Row(LootSource.Normal, Vestige + " x 1"))!.IsExcluded);
        Assert.True(parser.Parse(Row(LootSource.Normal, "Broken Vestige of Everlght x 1"))!.IsExcluded);
    }

    [Fact]
    public void ExplicitWrongSourceRejectionCannotBeReinterpretedAsAnAllowedRawItem()
    {
        var parser = new LifetimeLootTextParser(Context());
        var row = Row(LootSource.Normal, Dust + " x 4") with { RejectionReason = LootSourceCatalog.WrongSourceReason };
        Assert.True(parser.Parse(row)!.IsExcluded);
    }

    [Fact]
    public void AllowedChannelsKeepTheirQuantityRules()
    {
        var rare = new LifetimeLootTextParser(Context(), LootSource.Rare).Parse(Row(LootSource.Rare, Vestige + " x 7"));
        var normal = new LifetimeLootTextParser(Context()).Parse(Row(LootSource.Normal, Dust + " x 7"));
        Assert.Equal(Vestige, rare!.Name);
        Assert.Equal(1, rare.Quantity);
        Assert.Equal(Dust, normal!.Name);
        Assert.Equal(7, normal.Quantity);
    }

    [Fact]
    public void LegacyContextKeepsBothChannelsAndContextUpdatesInvalidateCachedPermission()
    {
        var legacy = new LifetimeParsingContext(0, [new(Vestige, [], true)]);
        var row = Row(LootSource.Normal, Vestige + " x 7");
        var parser = new LifetimeLootTextParser(legacy);
        Assert.Equal(Vestige, parser.Parse(row)!.Name);
        Assert.Equal(Vestige, new LifetimeLootTextParser(legacy, LootSource.Rare)
            .Parse(row with { Source = LootSource.Rare })!.Name);
        parser.UpdateContext(new(1, [new(Vestige, [], true, LootSource.Rare)]));
        Assert.True(parser.Parse(row)!.IsExcluded);
        parser.UpdateContext(legacy);
        Assert.Equal(Vestige, parser.Parse(row)!.Name);
    }

    private static LootObservation Row(LootSource source, string text, string? acceptedName = null) =>
        new(source, 0, text, acceptedName, acceptedName is null ? null : 1, 1, 1, null, null)
        { RejectionReason = acceptedName is null ? "item-catalog" : null };
}
