using System.Globalization;

namespace BdoGrindTracker.Core.Tests;

public sealed class LifetimeRawTextTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("recording1", 576)]
    [InlineData("recording2", 264)]
    public void AcceptedInputOnlyPreservesEveryHistoricalProjection(string fixture, long helmets)
    {
        var raw = new LifetimeLootReconciler(_ => null);
        var original = new LifetimeLootReconciler();
        LifetimeSnapshot? actual = null;
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "lifetime", fixture + ".txt");
        foreach (var line in File.ReadLines(path).Where(line => !line.StartsWith('#')))
        {
            var parts = line.Split('|');
            var at = DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture);
            var rows = parts[1].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value =>
            {
                var fields = value.Split(',');
                return Row(fields[1], fields[2] == "?" ? null : int.Parse(fields[2], CultureInfo.InvariantCulture),
                    int.Parse(fields[0], CultureInfo.InvariantCulture)) with
                    { NameConfidence = double.Parse(fields[3], CultureInfo.InvariantCulture) };
            }).ToArray();
            actual = raw.ProcessObservations(rows, at);
            var expected = original.ProcessFrame(rows.Select(row => new CompanionRecognizedEntry(row.ItemName!,
                row.Quantity is { } quantity ? (uint)quantity : uint.MaxValue, 250 - row.Slot * 50)
                { Slot = row.Slot, NameConfidence = row.NameConfidence }).ToArray(), at);
            Assert.Equal(expected.Totals.OrderBy(pair => pair.Key), actual.Totals.OrderBy(pair => pair.Key));
            Assert.Equal(expected.Deltas.OrderBy(pair => pair.Key), actual.Deltas.OrderBy(pair => pair.Key));
            Assert.Equal(expected.Lanes, actual.Lanes);
            Assert.Equal(expected.ObservedDrops, actual.ObservedDrops);
        }
        Assert.Equal(helmets, actual!.Totals[Helmet]);
    }

    [Fact]
    public void IncompleteTextSupportsAnExistingRowWithoutVotingForItsMultiplier()
    {
        var tracker = new LifetimeLootReconciler(_ => null);
        var initial = tracker.ProcessObservations([Row(Helmet, 4)], Start);
        var identity = Assert.Single(initial.ObservedDrops).EventId;
        LifetimeSnapshot last = initial;
        for (var index = 1; index < 6; index++)
        {
            last = tracker.ProcessObservations(index < 3 ? [Row(Helmet, 4)] :
                [Unknown("Follower's Hel x999")], Start.AddMilliseconds(index * 200));
            Assert.Equal(4, last.Totals[Helmet]);
            Assert.Equal(identity, Assert.Single(last.ObservedDrops).EventId);
        }
        Assert.Equal(1, last.SupportedDropCount);
    }

    [Fact]
    public void QuantitylessParserInterpretationCannotCreateADrop()
    {
        var tracker = new LifetimeLootReconciler(_ => new(Helmet, null, .9));
        for (var index = 0; index < 10; index++)
        {
            var actual = tracker.ProcessObservations([Unknown("Follower's Hel")], Start.AddMilliseconds(index * 200));
            Assert.Empty(actual.Totals);
            Assert.Equal(0, actual.SupportedDropCount);
        }
    }

    [Fact]
    public void SameFrameFullReadingEstablishesPartialContextRegardlessOfInputOrder()
    {
        var left = new LifetimeLootReconciler(_ => null);
        var right = new LifetimeLootReconciler(_ => null);
        var rows = new[] { Unknown("Follower's Hel x4", 1), Row(Helmet, 4) };
        var first = left.ProcessObservations(rows, Start);
        var second = right.ProcessObservations(rows.Reverse().ToArray(), Start);
        Assert.Equal(first.Lanes, second.Lanes);
        Assert.Equal(first.ObservedDrops, second.ObservedDrops);
        Assert.Equal(4, first.Totals[Helmet]);
    }

    [Fact]
    public void LocalizedPartialUsesAliasesOfTheCurrentlyKnownCanonicalItem()
    {
        var german = new LifetimeLootReconciler(_ => null,
            name => name == Helmet ? ["Helm eines Elion-Anhängers"] : []);
        var english = new LifetimeLootReconciler(_ => null);
        for (var index = 0; index < 6; index++)
        {
            var at = Start.AddMilliseconds(index * 200);
            var actual = german.ProcessObservations(index < 3 ? [Row(Helmet, 4)] :
                [Unknown("Elion-Anhängers x999")], at);
            var expected = english.ProcessObservations(index < 3 ? [Row(Helmet, 4)] :
                [Unknown("Follower's Hel x999")], at);
            Assert.Equal(expected.Lanes, actual.Lanes);
            Assert.Equal(expected.ObservedDrops, actual.ObservedDrops);
            Assert.Equal(4, actual.Totals[Helmet]);
        }
    }

    [Fact]
    public void StalePartialDoesNotReadTheCurrentAliasContext()
    {
        var aliasReads = 0;
        IReadOnlyList<string> aliases = [];
        var tracker = new LifetimeLootReconciler(_ => null, _ => { aliasReads++; return aliases; });
        var baseline = new LifetimeLootReconciler(_ => null,
            _ => ["Helm eines Elion-Anhängers"]);
        for (var index = 0; index < 3; index++)
        {
            tracker.ProcessObservations([Row(Helmet, 4)], Start.AddMilliseconds(index * 200));
            baseline.ProcessObservations([Row(Helmet, 4)], Start.AddMilliseconds(index * 200));
        }
        aliases = ["Helm eines Elion-Anhängers"];
        tracker.ProcessObservations([Unknown("Elion-Anhängers")], Start);
        Assert.Equal(0, aliasReads);
        var actual = tracker.ProcessObservations([Unknown("Elion-Anhängers")], Start.AddMilliseconds(600));
        var expected = baseline.ProcessObservations([Unknown("Elion-Anhängers")], Start.AddMilliseconds(600));
        Assert.True(aliasReads > 0);
        Assert.Equal(expected.Lanes, actual.Lanes);
        Assert.Equal(expected.ObservedDrops, actual.ObservedDrops);
    }

    [Fact]
    public void ChangedParserContextReevaluatesOldReadingsWithoutReplayingFrames()
    {
        var resolved = false;
        var tracker = new LifetimeLootReconciler(row => resolved && row.RawText == "Follower's Hel x4"
            ? new(Helmet, 4, 1) : null);
        var rows = new[] { Unknown("Follower's Hel x4", 1), Row(Helmet, 4) };
        var before = tracker.ProcessObservations(rows, Start);
        Assert.Equal(4, before.Totals[Helmet]);
        resolved = true;
        var after = tracker.Complete(Start.AddMilliseconds(1));
        Assert.Equal(8, after.Totals[Helmet]);
        Assert.Equal(before.Frame, after.Frame);
        Assert.Equal(before.Lanes, after.Lanes);
        Assert.Equal(4, Assert.Single(after.Deltas).Value);
        Assert.Equal(4, before.Totals[Helmet]);
    }

    [Fact]
    public void ReinterpretationCanRenameOrExcludeHistoryButKeepsValidatedQuantity()
    {
        var mode = 0;
        var tracker = new LifetimeLootReconciler(_ => mode switch
        {
            1 => new("Corrected Helmet", 400, 1),
            2 => LifetimeParsedReading.Excluded,
            _ => null,
        });
        var initial = tracker.ProcessObservations([Row(Helmet, 4)], Start);
        mode = 1;
        var renamed = tracker.Complete(Start.AddMilliseconds(1));
        Assert.Equal(4, renamed.Totals["Corrected Helmet"]);
        Assert.False(renamed.Totals.ContainsKey(Helmet));
        Assert.Equal(Assert.Single(initial.ObservedDrops).EventId, Assert.Single(renamed.ObservedDrops).EventId);
        mode = 2;
        var excluded = tracker.Complete(Start.AddMilliseconds(2));
        Assert.Empty(excluded.Totals);
        Assert.Equal(0, excluded.SupportedDropCount);
    }

    [Fact]
    public void StaleFramesDoNotCallParserOrTeachPartialContext()
    {
        var calls = 0;
        var tracker = new LifetimeLootReconciler(_ => { calls++; return null; });
        tracker.ProcessObservations([Row(Helmet, 4)], Start);
        var before = calls;
        var stale = tracker.ProcessObservations([Row("Injected Item", 4)], Start);
        Assert.Equal(before, calls);
        Assert.Empty(stale.Deltas);
        var next = tracker.ProcessObservations([Unknown("Injected")], Start.AddMilliseconds(200));
        var baseline = new LifetimeLootReconciler(_ => null);
        baseline.ProcessObservations([Row(Helmet, 4)], Start);
        var expected = baseline.ProcessObservations([], Start.AddMilliseconds(200));
        Assert.Equal(expected.Lanes, next.Lanes);
    }

    [Fact]
    public void ResetForgetsKnownNamesAndReparseCache()
    {
        var tracker = new LifetimeLootReconciler(_ => null);
        tracker.ProcessObservations([Row(Helmet, 4)], Start);
        tracker.Reset();
        var partial = tracker.ProcessObservations([Unknown("Follower's Hel x4")], Start.AddSeconds(1));
        Assert.Empty(partial.Totals);
        Assert.Equal(0, partial.Frame);
        Assert.True(tracker.UsesRawText);
        Assert.False(new LifetimeLootReconciler().UsesRawText);
    }

    [Fact]
    public void InvalidInputAndInterpretationsAreRejected()
    {
        var tracker = new LifetimeLootReconciler(_ => null);
        foreach (var rows in new[]
        {
            new[] { Row(Helmet, 4), Row(Helmet, 4) },
            new[] { Row(Helmet, 4) with { Slot = -1 } },
            new[] { Row(Helmet, 4) with { Quantity = 0 } },
            new[] { Row(Helmet, 4) with { NameConfidence = double.NaN } },
            new[] { Unknown(new string('a', 16385)) },
        }) Assert.Throws<ArgumentException>(() => tracker.ProcessObservations(rows, Start));
        Assert.Empty(tracker.Complete(Start).Totals);
        var invalidParser = new LifetimeLootReconciler(_ => new(Helmet, 0, 1));
        Assert.Throws<ArgumentException>(() => invalidParser.ProcessObservations([Row(Helmet, 4)], Start));
        Assert.Empty(invalidParser.Complete(Start).Totals);
        Assert.Throws<InvalidOperationException>(() => new LifetimeLootReconciler().ProcessObservations([], Start));
    }

    [Theory]
    [InlineData("martha", "marhta", .9611111111111111)]
    [InlineData("dwayne", "duane", .84)]
    [InlineData("abc", "xyz", 0)]
    [InlineData("a", "a", 1)]
    [InlineData("", "x", 0)]
    public void SimilarityMatchesEstablishedJaroWinklerExamples(string left, string right, double expected) =>
        Assert.Equal(expected, LifetimeTextSimilarity.JaroWinkler(left, right), 10);

    [Fact]
    public void NameFragmentNormalizationDoesNotTreatSuffixOrPunctuationAsIdentity() =>
        Assert.Equal("elionfollowershel", LifetimeTextSimilarity.NormalizeNameFragment("Elion Follower’s Hel × 4"));

    private static LootObservation Row(string name, int? quantity, int slot = 0) =>
        new(LootSource.Normal, slot, name, name, quantity, 1, 1, null, null);

    private static LootObservation Unknown(string text, int slot = 0) =>
        new(LootSource.Normal, slot, text, null, null, 0, 0, null, "native-catalog-miss");
}
