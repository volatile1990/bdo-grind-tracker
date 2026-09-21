using BdoGrindTracker.App.Analysis;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffIconSimilarityTests
{
    [Theory]
    [InlineData("harmony-draught", 1399, "immortal-harmony-draught", 1400)]
    [InlineData("harmony-draught-human", 1401, "immortal-harmony-draught-human", 1402)]
    [InlineData("harmony-draught-demihuman", 1403, "immortal-harmony-draught-demihuman", 1404)]
    [InlineData("harmony-draught-kamasylvia", 1405, "immortal-harmony-draught-kamasylvia", 1406)]
    [InlineData("harmony-draught-edania", 1407, "immortal-harmony-draught-edania", 1408)]
    public void DistinctHarmonyArtworkKeepsItsOwnNormalOrImmortalPriceIdentity(
        string normalId, int normalMarketId, string immortalId, int immortalMarketId)
    {
        var catalog = AutomaticBuffCatalog.Default;
        var normal = catalog.Templates.Single(template => template.CandidateBuffIds.SequenceEqual([normalId]));
        var immortal = catalog.Templates.Single(template => template.CandidateBuffIds.SequenceEqual([immortalId]));

        Assert.NotEqual(normal.GroupId, immortal.GroupId);
        Assert.NotEqual(normal.IconPath, immortal.IconPath);
        Assert.Equal(normalMarketId, catalog.DefinitionFor(normal).MarketItemId);
        Assert.Equal(immortalMarketId, catalog.DefinitionFor(immortal).MarketItemId);
    }

    [Fact]
    public void SharedPerfumeArtworkRetainsBothPriceCandidates()
    {
        var catalog = AutomaticBuffCatalog.Default;
        foreach (var family in new[] { "bracing-spirits", "charm", "courage", "deep-sea", "insight", "khalk", "spirits" })
        {
            var id = "perfume-of-" + family;
            var template = Assert.Single(catalog.Templates, template => template.CandidateBuffIds.Contains(id));

            Assert.Equal(2, template.CandidateBuffIds.Length);
            Assert.Contains("immortal-" + id, template.CandidateBuffIds);
            Assert.Null(catalog.DefinitionFor(template).MarketItemId);
        }
    }

    [Theory]
    [InlineData(99, "harmony-draught-demihuman")]
    [InlineData(149, "simple-cron-meal")]
    public void RecordedHdrArtworkSeparatesTheConfirmedVariantFromEveryCompetitor(int x, string expectedId)
    {
        using var references = new References();
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs", "bar-13m-114m.png"));
        using var observed = new Mat(frame, new Rect(x, 60, 48, 48));

        var (winner, runnerUp) = references.Classify(observed);

        Assert.Equal(expectedId, winner.GroupId);
        Assert.True(BuffIconSimilarity.IsDecisive(winner.Score, runnerUp.Score),
            $"{winner.GroupId}: {winner.Score:F5}; nearest different identity {runnerUp.GroupId}: {runnerUp.Score:F5}");
    }

    [Theory]
    [InlineData(.5, 48)]
    [InlineData(1, 32)]
    [InlineData(1.4, 64)]
    public void EveryBundledIdentitySurvivesToneResponseNeutralOpacityAndScaling(double gamma, int size)
    {
        using var references = new References();
        using var transfer = new Mat(1, 256, MatType.CV_8UC1);
        for (var value = 0; value < 256; value++)
            transfer.Set(0, value, (byte)Math.Round(255 * (.12 + .75 * Math.Pow(value / 255d, gamma))));

        foreach (var reference in references.Items)
        {
            using var resized = new Mat();
            Cv2.Resize(reference.Image, resized, new(size, size), interpolation: InterpolationFlags.Linear);
            using var observed = new Mat();
            Cv2.LUT(resized, transfer, observed);

            var (winner, runnerUp) = references.Classify(observed);

            Assert.Equal(reference.GroupId, winner.GroupId);
            Assert.True(BuffIconSimilarity.IsDecisive(winner.Score, runnerUp.Score),
                $"{reference.Id}, gamma {gamma}, size {size}: {winner.Score:F5} vs {runnerUp.GroupId} {runnerUp.Score:F5}");
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(20, 80, 160)]
    [InlineData(255, 255, 255)]
    public void FlatPatchHasNoSymbolEvidenceEvenWhenItsChannelsHaveDifferentMeans(int blue, int green, int red)
    {
        using var references = new References();
        using var observed = new Mat(48, 48, MatType.CV_8UC3, new Scalar(blue, green, red));

        Assert.All(references.Items, reference => Assert.Equal(0, BuffIconSimilarity.Score(observed, reference.Image)));
    }

    [Fact]
    public void MatchingColorHistogramCannotReplaceMatchingSpatialDetails()
    {
        using var references = new References();
        var reference = references.Items.Single(item => item.Id == "client-simple-cron-meal");
        using var reversed = new Mat();
        Cv2.Flip(reference.Image, reversed, FlipMode.XY);

        var (winner, runnerUp) = references.Classify(reversed);

        Assert.False(BuffIconSimilarity.IsDecisive(winner.Score, runnerUp.Score));
    }

    private sealed class References : IDisposable
    {
        internal sealed record Item(string Id, string GroupId, Mat Image);
        internal sealed record Result(string GroupId, double Score);
        internal Item[] Items { get; }

        internal References()
        {
            var catalog = AutomaticBuffCatalog.Default;
            Items = catalog.Templates.Select(template =>
            {
                using var stream = catalog.OpenIcon(template)!;
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                return new Item(template.Id, template.GroupId, Cv2.ImDecode(bytes.ToArray(), ImreadModes.Color));
            }).ToArray();
        }

        internal (Result Winner, Result RunnerUp) Classify(Mat observed)
        {
            var scores = Items.Select(item => new Result(item.GroupId, BuffIconSimilarity.Score(observed, item.Image)))
                .OrderByDescending(result => result.Score).ToArray();
            return (scores[0], scores.First(result => result.GroupId != scores[0].GroupId));
        }

        public void Dispose()
        {
            foreach (var item in Items) item.Image.Dispose();
        }
    }
}
