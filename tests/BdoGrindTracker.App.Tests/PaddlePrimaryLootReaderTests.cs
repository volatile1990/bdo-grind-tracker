using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class PaddlePrimaryLootReaderTests
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Crystal = "BON Wandering Origin Crystal";

    [Theory]
    [InlineData("en-US", "Elion Follower's Helmet x 4", 4)]
    [InlineData("de-DE", "Helm eines Anhängers Elions x 2", 2)]
    [InlineData("en-US", "Elion Follower's Helmet x 338", 338)]
    public void TwoViewsReturnTheExplicitAmountAndCanonicalItem(string language, string text, int expected)
    {
        var engine = new Engine([Reading(text), Reading(text)]);
        using var reader = Reader(engine);
        reader.ConfigureLanguage(language);
        using var band = Band();
        var result = Read(reader, band);
        Assert.Equal(Helmet, result.Observation!.ItemName);
        Assert.Equal(expected, result.Observation.Quantity);
        Assert.Equal(LootSource.Normal, result.Observation.Source);
        Assert.Equal(2, result.Observation.Slot);
        Assert.Equal(149, result.Observation.NativeY);
        Assert.Equal(new DropQuantityBounds(2, 1000), result.Observation.QuantityBounds);
        Assert.False(result.Observation.UsesImplicitUnitQuantity);
        Assert.False(result.Observation.IsAlignmentAnchor);
        Assert.Equal(new[] { 3, 1 }, engine.Channels);
        Assert.Equal("primary-ocr", result.Diagnostics.Reason);
        Assert.Equal("accepted", result.Diagnostics.Outcome);
        Assert.Equal(language, result.Diagnostics.Language);
        Assert.Null(result.Diagnostics.Before);
        Assert.Same(result.Observation, result.Diagnostics.After);
        Assert.Equal(2, result.Diagnostics.Readings.Count);
    }

    [Theory]
    [InlineData("4", "43")]
    [InlineData("4", "45")]
    [InlineData("6", "0")]
    [InlineData("6", "")]
    [InlineData("4O", "4")]
    [InlineData("4 5", "4 5")]
    [InlineData("9999999999", "9999999999")]
    public void MissingOrConflictingAmountsAbstainWithoutFabricatingAQuantity(string first, string second)
    {
        var engine = new Engine([Reading(Helmet + " x " + first), Reading(Helmet + " x " + second)]);
        using var reader = Reader(engine);
        using var band = Band();
        var result = Read(reader, band);
        Assert.Null(result.Observation);
        Assert.Null(result.Diagnostics.After);
        Assert.Equal(2, engine.Channels.Count);
        Assert.NotEqual("accepted", result.Diagnostics.Outcome);
    }

    [Theory]
    [InlineData(.94f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(1.1f)]
    public void OneUntrustedReadIsNotConsensus(float confidence)
    {
        var engine = new Engine([Reading(Helmet + " x 4"), Reading(Helmet + " x 4", confidence)]);
        using var reader = Reader(engine);
        using var band = Band();
        var result = Read(reader, band);
        Assert.Null(result.Observation);
        Assert.Equal("no-consensus", result.Diagnostics.Outcome);
    }

    [Fact]
    public void DifferentItemsAndItemsOutsideTheSpotAbstain()
    {
        using var band = Band();
        using var mixed = new PaddlePrimaryLootReader(new CompanionItemMatcher([Helmet, "Black Stone"]),
            _ => new Engine([Reading(Helmet + " x 4"), Reading("Black Stone x 4")]));
        Assert.Null(Read(mixed, band).Observation);
        using var outside = Reader(new Engine([Reading(Helmet + " x 4"), Reading(Helmet + " x 4")]));
        Assert.Null(outside.Read(band, LootSource.Normal, 2, 149, 1, _ => new(2, 1000), _ => false, default).Observation);
    }

    [Fact]
    public void UnknownSceneryDoesNotBecomeACatalogItem()
    {
        using var reader = Reader(new Engine([Reading("strange stones x 4"), Reading("strange stones x 4")]));
        using var band = Band();
        Assert.Null(Read(reader, band).Observation);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void PrimaryMatchingRetainsTheExistingCountOneTrashFilter(int amount, bool accepted)
    {
        const string item = "Decayed Cloth";
        using var reader = new PaddlePrimaryLootReader(new CompanionItemMatcher([item]),
            _ => new Engine([Reading(item + " x " + amount), Reading(item + " x " + amount)]));
        using var band = Band();
        var result = reader.Read(band, LootSource.Normal, 0, 0, 1, _ => new(1, 1000), _ => true, default);
        Assert.Equal(accepted, result.Observation is not null);
        if (accepted) Assert.Equal(amount, result.Observation!.Quantity);
    }

    [Theory]
    [InlineData(LootSource.Normal)]
    [InlineData(LootSource.Rare)]
    public void OnlyFixedUnitItemsCanBeAcceptedWithoutAQuantity(LootSource source)
    {
        using var band = Band();
        using var fixedReader = new PaddlePrimaryLootReader(new CompanionItemMatcher([Crystal]),
            _ => new Engine([Reading(Crystal), Reading(Crystal)]));
        var result = fixedReader.Read(band, source, 0, 0, 1, _ => new(1, 1), _ => true, default);
        Assert.Equal(1, result.Observation!.Quantity);
        Assert.True(result.Observation.UsesFixedUnitQuantity);
        Assert.False(result.Observation.UsesImplicitUnitQuantity);
        using var variableReader = Reader(new Engine([Reading(Helmet), Reading(Helmet)]));
        Assert.Null(variableReader.Read(band, source, 0, 0, 1, _ => new(2, 1000), _ => true, default).Observation);
    }

    [Fact]
    public void FixedUnitPolicyCannotTurnAnUnmatchedSuffixIntoACatalogName()
    {
        using var band = Band();
        using var reader = new PaddlePrimaryLootReader(new CompanionItemMatcher([Crystal]),
            _ => new Engine([Reading(Crystal + " x unknown"), Reading(Crystal + " x unknown")]));
        Assert.Null(reader.Read(band, LootSource.Normal, 0, 0, 1,
            _ => new(1, 1), _ => true, default).Observation);
    }

    [Fact]
    public void ReaderPassesQuantityBoundsToTheExistingCounterWithoutClamping()
    {
        using var reader = Reader(new Engine([Reading(Helmet + " x 2"), Reading(Helmet + " x 2")]));
        using var band = Band();
        var result = reader.Read(band, LootSource.Normal, 0, 0, 1, _ => new(4, 1000), _ => true, default);
        Assert.Equal(2, result.Observation!.Quantity);
        Assert.Equal(4u, result.Observation.QuantityBounds!.Minimum);
    }

    [Fact]
    public void UniformBandsDoNotLoadOrRunTheModel()
    {
        using var reader = new PaddlePrimaryLootReader(new CompanionItemMatcher([Helmet]),
            _ => throw new Exception("Should not create model for blank pixels"));
        using var band = new Mat(50, 385, MatType.CV_8UC3, Scalar.White);
        var result = Read(reader, band);
        Assert.Null(result.Observation);
        Assert.Equal("no-image-information", result.Diagnostics.Outcome);
        Assert.Empty(result.Diagnostics.Readings);
        Assert.Equal(0, result.Diagnostics.Errors);
    }

    [Fact]
    public void ModelFailureAndFailureAfterOneReadAbstainForWindowsFallback()
    {
        using var band = Band();
        using var missing = new PaddlePrimaryLootReader(new CompanionItemMatcher([Helmet]),
            _ => throw new FileNotFoundException("model missing"));
        var absent = Read(missing, band);
        Assert.Null(absent.Observation);
        Assert.Equal("primary-error:FileNotFoundException", absent.Diagnostics.Outcome);
        Assert.Equal(1, absent.Diagnostics.Errors);
        using var partial = Reader(new Engine([Reading(Helmet + " x 4")]));
        var result = Read(partial, band);
        Assert.Null(result.Observation);
        Assert.Single(result.Diagnostics.Readings);
        Assert.Equal(1, result.Diagnostics.Errors);
    }

    [Fact]
    public void CancellationPropagatesInsteadOfReturningAPartialObservation()
    {
        using var cancellation = new CancellationTokenSource();
        var engine = new Engine([Reading(Helmet + " x 4"), Reading(Helmet + " x 4")])
            { AfterRead = () => cancellation.Cancel() };
        using var reader = Reader(engine);
        using var band = Band();
        Assert.Throws<OperationCanceledException>(() => reader.Read(band, LootSource.Normal, 0, 0, 1,
            _ => new(2, 1000), _ => true, cancellation.Token));
        Assert.Single(engine.Channels);
    }

    [Fact]
    public void LanguageChangeDisposesTheOldModelAndNewModelIsCreatedLazily()
    {
        var languages = new List<string>();
        var engines = new List<Engine>();
        using var reader = new PaddlePrimaryLootReader(new CompanionItemMatcher([Helmet]), language =>
        {
            languages.Add(language);
            var engine = new Engine([Reading(Helmet + " x 4"), Reading(Helmet + " x 4")]);
            engines.Add(engine);
            return engine;
        });
        using var band = Band();
        Assert.Empty(languages);
        Read(reader, band);
        reader.ConfigureLanguage("de-DE");
        Assert.Equal(1, engines[0].DisposeCount);
        Assert.Single(languages);
        Read(reader, band);
        Assert.Equal(new[] { "en-US", "de-DE" }, languages);
        reader.Dispose();
        reader.Dispose();
        Assert.Equal(1, engines[1].DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => Read(reader, band));
    }

    private static PaddlePrimaryLootReader Reader(Engine engine) =>
        new(new CompanionItemMatcher([Helmet]), _ => engine);

    private static PrimaryReadResult Read(ILootPrimaryRowReader reader, Mat band) =>
        reader.Read(band, LootSource.Normal, 2, 149, 1, _ => new(2, 1000), _ => true, default);

    private static Mat Band()
    {
        var image = new Mat(50, 385, MatType.CV_8UC3, Scalar.Black);
        Cv2.Line(image, new(5, 12), new(270, 30), Scalar.White, 2);
        return image;
    }

    private static SecondaryLootOcrResult Reading(string text, float confidence = .99f) =>
        new(text, confidence, new(CompanionOcrGeometryStatus.Missing, 0, 0, 0, 0));

    private sealed class Engine(IEnumerable<SecondaryLootOcrResult> readings) : ISecondaryLootOcrRecognizer
    {
        private readonly Queue<SecondaryLootOcrResult> _readings = new(readings);
        public List<int> Channels { get; } = [];
        public Action? AfterRead { get; init; }
        public int DisposeCount { get; private set; }
        public string BackendName => "paddle-test";
        public string LanguageTag => "en-US";
        public SecondaryLootOcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Channels.Add(image.Channels());
            var result = _readings.Dequeue();
            AfterRead?.Invoke();
            return result;
        }
        public void Dispose() => DisposeCount++;
    }
}
