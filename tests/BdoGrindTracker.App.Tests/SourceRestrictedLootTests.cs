using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionLootFrameAnalyzerTests
{
    private const string SourcePolicyVestige = "Broken Vestige of Everlight";
    private const string SourcePolicyStone = "Black Stone";
    private const string SourcePolicyTrash = "Elion Follower's Mark";
    private static readonly string[] SourcePolicyBaselineItems = [SourcePolicyVestige, SourcePolicyStone, SourcePolicyTrash];
    private static readonly string[] SourcePolicyLusterItems =
    [
        "Crimson Primordial Luster - Sovereign",
        "Sunset Primordial Luster - Edana",
        "Turquoise Primordial Luster - Edana",
        "Turquoise Primordial Luster - Sovereign",
        "Violet Primordial Luster - Edana",
        "Violet Primordial Luster - Sovereign",
        "White Primordial Luster - Edana",
        "White Primordial Luster - Sovereign",
    ];
    private static readonly string[] SourcePolicyItems = [.. SourcePolicyBaselineItems, .. SourcePolicyLusterItems];

    public static IEnumerable<object[]> SourceRestrictedLocalizedItems() =>
        SourcePolicyBaselineItems.SelectMany(item => new[] { "de", "en" }.Select(language => new object[] { item, language }));

    public static IEnumerable<object[]> SourceRestrictedLusterItems() =>
        SourcePolicyLusterItems.Select(item => new object[] { item, "en" });

    [Theory]
    [MemberData(nameof(SourceRestrictedLocalizedItems))]
    [MemberData(nameof(SourceRestrictedLusterItems))]
    public async Task ItemFromTheWrongLogNeverCountsBeforeSpotDetectionOrAfterReset(string item, string language)
    {
        var text = SourcePolicyText(item, language);
        var allowed = LootSourceCatalog.GetRequired(item);
        var rows = new Rows { RareText = allowed == LootSource.Normal ? text : "" };
        if (allowed == LootSource.Rare) rows.Values = [new(250, text)];
        using var analyzer = SourceRestrictedAnalyzer(rows);
        using var frame = new Bitmap(800, 600);

        for (var session = 0; session < 2; session++)
        {
            for (var capture = 0; capture < 4; capture++)
            {
                var result = await analyzer.AnalyzeAsync(frame,
                    DateTimeOffset.UnixEpoch.AddMilliseconds(capture * 200), default);
                Assert.Null(result.SpotId);
                Assert.Equal(LootSourceCatalog.WrongSourceReason,
                    Assert.Single(result.Observations, row => row.ItemName == item).RejectionReason);
                Assert.Empty(result.NewEvents);
                Assert.Empty(result.LootProjection!.Totals);
            }
            Assert.Empty(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1)).LootProjection!.Totals);
            analyzer.Reset();
        }
    }

    [Theory]
    [MemberData(nameof(SourceRestrictedLocalizedItems))]
    [MemberData(nameof(SourceRestrictedLusterItems))]
    public async Task TheSameItemInBothLogsProducesExactlyOneDropFromItsAssignedLog(string item, string language)
    {
        var text = SourcePolicyText(item, language);
        var rows = new Rows(new Input(250, text)) { RareText = text };
        using var analyzer = SourceRestrictedAnalyzer(rows);
        using var frame = new Bitmap(800, 600);

        for (var capture = 0; capture < 4; capture++)
        {
            var result = await analyzer.AnalyzeAsync(frame,
                DateTimeOffset.UnixEpoch.AddMilliseconds(capture * 200), default);
            var accepted = Assert.Single(result.Observations, row => row.ItemName == item && row.RejectionReason is null);
            Assert.Equal(LootSourceCatalog.GetRequired(item), accepted.Source);
            Assert.Single(result.Observations, row => row.ItemName == item &&
                row.RejectionReason == LootSourceCatalog.WrongSourceReason);
            AssertSourcePolicySingleDrop(result, item);
        }
        AssertSourcePolicySingleDrop(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1)), item);
    }

    [Theory]
    [MemberData(nameof(SourceRestrictedLocalizedItems))]
    public async Task RawTextWithoutAMatchedIdentityCannotBypassTheAssignedLog(string item, string language)
    {
        var text = SourcePolicyText(item, language);
        var rows = new Rows(new Input(250, text)) { RareText = text };
        var review = new SourcePolicyReview(item, text, rawOnly: true);
        using var analyzer = SourceRestrictedAnalyzer(rows, review: review);
        using var frame = new Bitmap(800, 600);

        for (var capture = 0; capture < 4; capture++)
        {
            var result = await analyzer.AnalyzeAsync(frame,
                DateTimeOffset.UnixEpoch.AddMilliseconds(capture * 200), default);
            Assert.Equal(2, result.Observations.Count);
            Assert.All(result.Observations, observation => Assert.Null(observation.ItemName));
            AssertSourcePolicySingleDrop(result, item);
        }
        AssertSourcePolicySingleDrop(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1)), item);
    }

    [Theory]
    [InlineData(SourcePolicyVestige, "recovery")]
    [InlineData(SourcePolicyStone, "recovery")]
    [InlineData(SourcePolicyTrash, "recovery")]
    [InlineData(SourcePolicyVestige, "review")]
    [InlineData(SourcePolicyStone, "review")]
    [InlineData(SourcePolicyTrash, "review")]
    public async Task RecoveredAndReviewedItemsAreCheckedAgainstTheActualBand(string item, string stage)
    {
        var text = SourcePolicyText(item, "en");
        var rows = stage == "recovery"
            ? new Rows { RareText = "" }
            : new Rows(new Input(250, "unreadable scenery")) { RareText = "unreadable scenery" };
        var normalRecovery = stage == "recovery" ? new SourcePolicyRecovery(item, 250) : null;
        var rareRecovery = stage == "recovery" ? new SourcePolicyRecovery(item, 0) : null;
        var review = stage == "review" ? new SourcePolicyReview(item, text) : null;
        using var analyzer = SourceRestrictedAnalyzer(rows, normalRecovery, rareRecovery, review);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, default);

        AssertSourcePolicySingleDrop(result, item);
        AssertSourcePolicySingleDrop(analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1)), item);
        var accepted = Assert.Single(result.Observations, row => row.ItemName == item && row.RejectionReason is null);
        Assert.Equal(LootSourceCatalog.GetRequired(item), accepted.Source);
        Assert.Equal(0, accepted.Slot);
        Assert.Equal(accepted.Source == LootSource.Normal ? 250 : 0, accepted.NativeY);
        if (stage == "recovery")
        {
            Assert.Equal(1, normalRecovery!.ReturnedCount);
            Assert.Equal(1, rareRecovery!.ReturnedCount);
        }
        else
        {
            Assert.Equal(2, review!.Inputs.Count);
            Assert.All(review.Inputs, input =>
                Assert.Equal(input.Source == LootSourceCatalog.GetRequired(item), input.Allows(item)));
        }
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public async Task ScalesSpotLockRetainsRareVestigeAndRejectsItsNormalCopy(string language)
    {
        var trashText = ItemLocalizationCatalog.DisplayName(SourcePolicyTrash, language) + " x17";
        var rows = new Rows(Enumerable.Range(1, 5).Select(index => new Input(index * 50, trashText, 17)).ToArray())
            { RareText = "" };
        using var analyzer = SourceRestrictedAnalyzer(rows);
        using var frame = new Bitmap(800, 600);
        var first = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, default);
        Assert.Equal(LootSpotCatalog.ScalesOfJudgmentId, first.SpotId);

        var vestige = SourcePolicyText(SourcePolicyVestige, language);
        rows.Values = [new(250, vestige)];
        rows.RareText = vestige;
        for (var capture = 1; capture <= 4; capture++)
        {
            var result = await analyzer.AnalyzeAsync(frame,
                DateTimeOffset.UnixEpoch.AddMilliseconds(capture * 200), default);
            Assert.Equal(LootSpotCatalog.ScalesOfJudgmentId, result.SpotId);
            Assert.Equal(1, result.LootProjection!.Totals.GetValueOrDefault(SourcePolicyVestige));
            Assert.Equal(LootSourceCatalog.WrongSourceReason,
                Assert.Single(result.Observations, row => row.Source == LootSource.Normal).RejectionReason);
            Assert.Null(Assert.Single(result.Observations, row => row.Source == LootSource.Rare).RejectionReason);
        }
        Assert.Equal(1, analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1))
            .LootProjection!.Totals.GetValueOrDefault(SourcePolicyVestige));
    }

    private static string SourcePolicyText(string item, string language) =>
        ItemLocalizationCatalog.DisplayName(item, language) + " x" + SourcePolicyQuantity(item);

    private static int SourcePolicyQuantity(string item) => item == SourcePolicyTrash ? 2 : 1;

    private static void AssertSourcePolicySingleDrop(FrameAnalysisResult result, string item)
    {
        var total = Assert.Single(result.LootProjection!.Totals);
        Assert.Equal(item, total.Key);
        Assert.Equal(SourcePolicyQuantity(item), total.Value);
        Assert.Equal(1, result.LootProjection.ConfirmedDropCount);
    }

    private static CompanionLootFrameAnalyzer SourceRestrictedAnalyzer(Rows rows,
        INormalLootRecovery? normalRecovery = null, INormalLootRecovery? rareRecovery = null,
        ILootRowReview? review = null)
    {
        // Leave the initial context unrestricted: the live analyzer must attach
        // the configured source policy before either counter sees its first frame.
        var context = new LifetimeParsingContext(0, SourcePolicyItems.Select(item =>
            new LifetimeParsingCatalogEntry(item, [ItemLocalizationCatalog.GermanNames[item]],
                item == SourcePolicyVestige)).ToArray());
        return new(Calibration() with
        {
            HasRareLootAnchor = true, RareLootAnchorX = 600, RareLootAnchorY = 300,
        }, new CompanionItemMatcher(SourcePolicyItems), rows, new Names(rows),
            reconciliation: new LifetimeNormalReconciliationAdapter(context, useVisualSlotCoverage: true),
            rareRowPipeline: new RareRows(), normalRecovery: normalRecovery, rareRecovery: rareRecovery,
            rowReview: review,
            specialReconciliation: new LifetimeNormalReconciliationAdapter(context, useVisualSlotCoverage: false,
                source: LootSource.Rare, slotCount: 1), lootSourceResolver: LootSourceCatalog.GetRequired);
    }

    private sealed class SourcePolicyRecovery(string item, int targetY) : INormalLootRecovery
    {
        public int ReturnedCount { get; private set; }
        public LootObservation? Recover(Mat sourceBand, ICompanionPreparedRow original,
            LootObservation? baseline, int slot, float uiScale, NormalLootRecoveryBudget budget,
            CancellationToken cancellationToken)
        {
            if (original.Y != targetY) return null;
            ReturnedCount++;
            // A worker's metadata must never move this row into the allowed log.
            return new(LootSourceCatalog.GetRequired(item), 99, SourcePolicyText(item, "en"), item,
                SourcePolicyQuantity(item), 1, 0, null, null) { NativeY = 999 };
        }
    }

    private sealed class SourcePolicyReview(string item, string text, bool rawOnly = false) : ILootRowReview
    {
        public List<LootRowReviewInput> Inputs { get; } = [];
        public void ConfigureLanguage(string languageTag) { }
        public Task<LootRowReviewResult> ReviewAsync(Mat originalBand, LootRowReviewInput input,
            CancellationToken cancellationToken)
        {
            Inputs.Add(input);
            var observation = new LootObservation(LootSourceCatalog.GetRequired(item), 99, text,
                rawOnly ? null : item, rawOnly ? null : SourcePolicyQuantity(item), 1, 0, null,
                rawOnly ? "unmatched-name" : null) { NativeY = 999 };
            return Task.FromResult(new LootRowReviewResult(observation, null));
        }
        public void Dispose() { }
    }
}
