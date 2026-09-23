using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class RarePaddleVerificationIntegrationTests
{
    private const string Ring = "Twilight of the End - Ring";
    private const string Earring = "Twilight of the End - Earring";
    private const string Apeiron = "Apeiron Earring";
    private static readonly string[] Items = [Ring, Earring, Apeiron];
    private static LifetimeParsingContext Context() => new(0,
        Items.Select(name => new LifetimeParsingCatalogEntry(name,
            [ItemLocalizationCatalog.GermanNames[name]], true, LootSource.Rare)).ToArray());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsecutiveTwilightDropsKeepTheirIdentityThroughBriefBadPrimaryReadings(bool german)
    {
        var source = new Readings();
        var matcher = new CompanionItemMatcher(Items);
        using var review = new BackgroundLootRowReview(matcher, _ => new Secondary(source));
        review.ConfigureLanguage(german ? "de-DE" : "en-US");
        using var analyzer = new CompanionLootFrameAnalyzer(
            new CompanionCalibration("profile", "gamevariable.xml", "GameOption.txt", 400, 300,
                800, 600, 1f, CompanionFontType.StrongSword, 0, false)
            { HasRareLootAnchor = true, RareLootAnchorX = 600, RareLootAnchorY = 300 },
            matcher, new EmptyNormalRows(), new Primary(source),
            reconciliation: new LifetimeNormalReconciliationAdapter(Context()),
            rareRowPipeline: new RareRows(source), rowReview: review,
            specialReconciliation: new LifetimeNormalReconciliationAdapter(Context(), false, LootSource.Rare, 1),
            quantityBoundsResolver: (_, _) => new(1, 1),
            lootSourceResolver: _ => LootSource.Rare);
        var replay = new CompanionDiagnosticCounter(matcher.CatalogEntries,
            parsingContext: Context(), independentSpecial: true);
        using var frame = new Bitmap(800, 600);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.Black);
            for (var y = 0; y < frame.Height; y += 8)
                graphics.DrawLine(Pens.White, 0, y, frame.Width - 1, y);
        }
        var sample = 0;

        foreach (var item in new[] { Ring, Earring })
        {
            await Observe(item, item, 20);
            // The same visible Twilight notification is briefly mislabeled by
            // Windows. The two matching exact Paddle views select the original
            // identity, retaining this drop in both the live and recorded counter.
            await Observe(Apeiron, item, 2, expectCorrection: true);
            await Observe(item, item, 20);
        }
        // The next rare notification replaces the first without a blank frame.
        await Observe(null, null, 12);

        var completedAt = DateTimeOffset.UnixEpoch.AddMilliseconds(sample * 200);
        var completed = analyzer.CompleteSession(completedAt);
        var replayed = replay.CompleteSession(completedAt);
        Assert.Equal(0, completed.LootProjection!.Totals.GetValueOrDefault(Apeiron));
        Assert.Equal(1, completed.LootProjection.Totals[Ring]);
        Assert.Equal(1, completed.LootProjection.Totals[Earring]);
        Assert.Equal(2, completed.LootProjection.ConfirmedDropCount);
        Assert.Equal(completed.LootProjection.Totals.OrderBy(pair => pair.Key),
            replayed.LootProjection!.Totals.OrderBy(pair => pair.Key));
        Assert.Equal(168, source.SecondaryCalls);

        async Task Observe(string? primary, string? secondary, int count, bool expectCorrection = false)
        {
            source.Primary = Text(primary);
            source.Paddle = Text(secondary);
            for (var index = 0; index < count; index++)
            {
                var at = DateTimeOffset.UnixEpoch.AddMilliseconds(sample++ * 200);
                var result = await analyzer.AnalyzeAsync(frame, at, CancellationToken.None);
                Assert.DoesNotContain(result.NewEvents, drop => drop.ItemName == Apeiron && drop.Quantity > 0);
                Assert.Equal(0, result.LootProjection!.Totals.GetValueOrDefault(Apeiron));
                if (primary is not null)
                {
                    var observed = Assert.Single(result.Observations);
                    var diagnostic = Assert.Single(result.RowReviews);
                    Assert.Equal(2, diagnostic.Readings.Count);
                    Assert.Equal("rare-ocr-review", diagnostic.Reason);
                    Assert.Null(observed.RejectionReason);
                    Assert.Equal(secondary, observed.ItemName);
                    Assert.Equal(1, observed.Quantity);
                    if (expectCorrection)
                    {
                        Assert.Equal(Apeiron, diagnostic.Before!.ItemName);
                        Assert.Equal("rare-secondary-selected", diagnostic.Outcome);
                        Assert.Equal(Text(secondary), observed.RawText);
                    }
                    else Assert.Equal("rare-primary-selected", diagnostic.Outcome);
                }
                // Replay must use the selected reading, including its corrected
                // raw text, and never resurrect the brief false primary identity.
                var serialized = JsonSerializer.Serialize(result.Observations, LootDiagnosticFormat.JsonOptions);
                var restored = JsonSerializer.Deserialize<LootObservation[]>(serialized, LootDiagnosticFormat.JsonOptions)!;
                var replayResult = replay.ProcessFrame(at, restored, true);
                Assert.Equal(0, replayResult.LootProjection!.Totals.GetValueOrDefault(Apeiron));
            }
        }

        string? Text(string? name) => name is null ? null :
            (german ? ItemLocalizationCatalog.GermanNames[name] : name) + " x 1";
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalUnconfirmedRareLogsRemainExcludedFromReplay(bool acceptedPrimary)
    {
        var parser = new LifetimeLootTextParser(Context(), LootSource.Rare);
        var row = new LootObservation(LootSource.Rare, 0, Apeiron + " x 1",
            acceptedPrimary ? Apeiron : null, acceptedPrimary ? 1 : null, 1, 1, null, null);
        Assert.Equal(Apeiron, parser.Parse(row)!.Name);
        var rejected = row with { RejectionReason = BackgroundLootRowReview.UnconfirmedRareReason };
        Assert.True(parser.Parse(rejected)!.IsExcluded);
        var counter = new LifetimeNormalReconciliationAdapter(Context(), false, LootSource.Rare, 1);
        for (var frame = 0; frame < 30; frame++)
            counter.ProcessObservations([rejected], DateTimeOffset.UnixEpoch.AddMilliseconds(frame * 200));
        counter.Complete();
        Assert.Empty(counter.Projection!.Totals);
    }

    private sealed class Readings
    {
        public string? Primary { get; set; }
        public string? Paddle { get; set; }
        public int SecondaryCalls { get; set; }
    }

    private sealed class Primary(Readings readings) : ICompanionNameRecognizer
    {
        public string BackendName => "test-primary";
        public string LanguageTag => "en-US";
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken) =>
            new(readings.Primary ?? "", new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
    }

    private sealed class Secondary(Readings readings) : ISecondaryLootOcrRecognizer
    {
        public string BackendName => "test-paddle";
        public string LanguageTag => "en-US";
        public SecondaryLootOcrResult Recognize(Mat image, CancellationToken cancellationToken)
        {
            readings.SecondaryCalls++;
            return new(readings.Paddle ?? "", .99f, new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
        }
        public void Dispose() { }
    }

    private sealed class EmptyNormalRows : ICompanionNormalRowPipeline
    {
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) => new Row(y, true);
        public void Dispose() { }
    }

    private sealed class RareRows(Readings readings) : ICompanionRareRowPipeline
    {
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) =>
            new Row(y, readings.Primary is null);
        public void Dispose() { }
    }

    private sealed class Row(int y, bool blank) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => blank;
        public int RecognizedTextWidth => 100;
        public int TemplateQuantity => -1;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => 0;
        public float NameScale => 1;
        public Mat? NameImage { get; } = blank ? null : new Mat(1, 1, MatType.CV_8UC1, Scalar.White);
        public void Dispose() => NameImage?.Dispose();
    }
}
