using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionLootFrameAnalyzerTests
{
    public static IEnumerable<object[]> ConfirmedFixedDrops() => DropQuantityCatalog.Entries
        .Where(entry => entry.Bounds!.IsFixedUnit).SelectMany(entry => new[] { LootSource.Normal, LootSource.Rare }
            .Select(source => new object[] { entry.SpotId, entry.ItemName, source }));

    [Theory]
    [MemberData(nameof(ConfirmedFixedDrops))]
    public async Task EveryConfirmedUnitDropCountsOneInBothChannels(string spotId, string item, LootSource source)
    {
        var trash = TrashLootMinimumCatalog.Entries.Single(entry => entry.SpotId == spotId).ItemName;
        var rows = new Rows(new Input(250, trash, 17)) { RareText = source == LootSource.Rare ? item + " x7" : "" };
        if (source == LootSource.Normal) rows.Values = [.. rows.Values, new(200, item + " x7", 7)];
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration() with
        {
            HasRareLootAnchor = source == LootSource.Rare, RareLootAnchorX = 600, RareLootAnchorY = 300,
        }, new CompanionItemMatcher([trash, item]), rows, new Names(rows),
            rareRowPipeline: source == LootSource.Rare ? new RareRows() : null);
        analyzer.ConfigureLootFilter(true);
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        var observed = Assert.Single(result.Observations, observation => observation.ItemName == item);
        Assert.True(observed.UsesFixedUnitQuantity);
        Assert.Equal(1, observed.Quantity);
        DiagnosticRecordingSession.ValidateObservations(result.Observations);
        Assert.Equal(1, Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents,
            change => change.ItemName == item).Quantity);
    }

    [Theory]
    [InlineData("Black Crystal Fragment", -1, 4)]
    [InlineData("Black Crystal Fragment", 1, 1)]
    [InlineData("Black Crystal Fragment", 4000, 1000)]
    [InlineData("Elion Follower's Helmet", -1, 7)]
    [InlineData("Elion Follower's Mark", -1, 2)]
    [InlineData("Elion Follower's Mark", 4000, 2000)]
    public async Task UserTrashMinimumsAreFallbacksAndMaximumsCapSingleDrops(string trash, int quantity, int expected)
    {
        var rows = new Rows(new Input(250, trash, quantity));
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration(), new CompanionItemMatcher([trash]), rows, new Names(rows));
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.False(Assert.Single(result.Observations).UsesFixedUnitQuantity);
        Assert.Equal(expected, Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents).Quantity);
    }

    [Fact]
    public async Task AUnitItemRejectedByTheNewSpotLockStillHasValidDiagnosticMetadata()
    {
        var rows = new Rows(new Input(200, "BON Wandering Origin Crystal", 7), new(250, "Branch of Abundance", 17));
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration(),
            new CompanionItemMatcher(["BON Wandering Origin Crystal", "Branch of Abundance"]), rows, new Names(rows));
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal(AutomaticLootSpotLock.OutsideSpotPoolReason,
            Assert.Single(result.Observations, row => row.ItemName == "BON Wandering Origin Crystal").RejectionReason);
        DiagnosticRecordingSession.ValidateObservations(result.Observations);
    }

    // Deliberately synthetic until the user's researched quantities are supplied.
    [Theory]
    [InlineData(LootSource.Normal)]
    [InlineData(LootSource.Rare)]
    public async Task FixedUnitBoundsSupplyOneWhilePreservingTheRawOcrText(LootSource source)
    {
        const string crystal = "BON Wandering Origin Crystal";
        var rows = new Rows(new Input(250, "Black Crystal Fragment", 17))
        {
            RareText = source == LootSource.Rare ? crystal + " x7" : "",
        };
        if (source == LootSource.Normal)
            rows.Values = [.. rows.Values, new(200, crystal + " x7", 7)];
        var calibration = Calibration() with
        {
            HasRareLootAnchor = source == LootSource.Rare,
            RareLootAnchorX = 600, RareLootAnchorY = 300,
        };
        using var analyzer = new CompanionLootFrameAnalyzer(calibration,
            new CompanionItemMatcher(["Black Crystal Fragment", crystal]), rows, new Names(rows),
            rareRowPipeline: source == LootSource.Rare ? new RareRows() : null,
            quantityBoundsResolver: (spot, item) => spot == LootSpotCatalog.HermesiaId && item == crystal
                ? new(1, 1) : null);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        var observation = Assert.Single(result.Observations, row => row.ItemName == crystal);
        Assert.Equal(1, observation.Quantity);
        Assert.True(observation.UsesFixedUnitQuantity);
        Assert.Contains("x7", observation.RawText);
        Assert.Equal(new DropQuantityBounds(1, 1), observation.QuantityBounds);
        Assert.Contains(result.TrackingResult!.Decisions,
            decision => decision.Reason == LootDiagnosticFormat.FixedUnitQuantityReason);
        var completed = analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1));
        Assert.Equal(1, Assert.Single(completed.NewEvents, entry => entry.ItemName == crystal).Quantity);
        Assert.Equal(17, Assert.Single(completed.NewEvents, entry => entry.ItemName == "Black Crystal Fragment").Quantity);
    }

    [Theory]
    [InlineData("BON Origin Shard", 5)]
    [InlineData("BON Origin Shard x1", 1)]
    [InlineData("BON Origin Shard x9", 8)]
    public async Task ImplicitRareUnitUsesConfiguredMinimumButExplicitOneIsPreserved(string text, int expected)
    {
        var rows = new Rows { RareText = text };
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration() with
        {
            HasRareLootAnchor = true, RareLootAnchorX = 600, RareLootAnchorY = 300,
        }, Matcher(), rows, new Names(rows), rareRowPipeline: new RareRows(),
            quantityBoundsResolver: (_, _) => new(5, 8));
        using var frame = new Bitmap(800, 600);
        var result = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal(text == "BON Origin Shard", Assert.Single(result.Observations).UsesImplicitUnitQuantity);
        Assert.Equal(expected, Assert.Single(analyzer.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents).Quantity);
    }

    [Fact]
    public async Task LaterExplicitRareQuantityWinsOverTheImplicitMinimumWithinTheBatch()
    {
        var rows = new Rows { RareText = "BON Origin Shard" };
        using var analyzer = new CompanionLootFrameAnalyzer(Calibration() with
        {
            HasRareLootAnchor = true, RareLootAnchorX = 600, RareLootAnchorY = 300,
        }, Matcher(), rows, new Names(rows), rareRowPipeline: new RareRows(),
            quantityBoundsResolver: (_, _) => new(5, 8));
        using var frame = new Bitmap(800, 600);
        var counter = new CompanionDiagnosticCounter([new("BON Origin Shard")]);
        var first = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch, CancellationToken.None);
        counter.ProcessFrame(DateTimeOffset.UnixEpoch, first.Observations, true);
        rows.RareText = "BON Origin Shard x6";
        var second = await analyzer.AnalyzeAsync(frame, DateTimeOffset.UnixEpoch.AddMilliseconds(450), CancellationToken.None);
        counter.ProcessFrame(DateTimeOffset.UnixEpoch.AddMilliseconds(450), second.Observations, true);

        var completed = analyzer.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1));
        Assert.Equal(6, Assert.Single(completed.NewEvents).Quantity);
        Assert.Equal(6, Assert.Single(counter.CompleteSession(DateTimeOffset.UnixEpoch.AddSeconds(1)).NewEvents).Quantity);
    }
}

public sealed class DropQuantityDiagnosticTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest-drop-bounds-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(LootSource.Normal, 7, false, 1, 1, 1)]
    [InlineData(LootSource.Rare, 9, false, 1, 2, 2)]
    [InlineData(LootSource.Rare, 1, true, 5, 8, 5)]
    [InlineData(LootSource.Normal, null, false, 3, 8, 3)]
    public void RecordingReplaysCapturedBoundsWithoutConsultingTheCurrentCatalog(
        LootSource source, int? quantity, bool implicitUnit, uint minimum, uint maximum, int expected)
    {
        var start = DateTimeOffset.UnixEpoch;
        var counter = new CompanionDiagnosticCounter([new("BON Wandering Origin Crystal")]);
        var observation = new LootObservation(source, 0, "raw OCR", "BON Wandering Origin Crystal", quantity, 1, 0, null, null)
        {
            NativeY = source == LootSource.Rare ? 0 : 250,
            QuantityBounds = new(minimum, maximum), UsesImplicitUnitQuantity = implicitUnit,
        };
        using var image = new Bitmap(2, 2);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var result = counter.ProcessFrame(start, [observation], source == LootSource.Rare);
        recording.RecordFrame(start, [observation], result, image, null,
            source == LootSource.Rare ? new Rectangle(0, 0, 1, 1) : null);
        var complete = counter.CompleteSession(start.AddSeconds(1));
        recording.RecordCompletion(start.AddSeconds(1), complete);
        recording.Dispose();

        Assert.Null(recording.LastError);
        Assert.Equal(expected, Assert.Single(complete.NewEvents).Quantity);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
        Assert.Equal(expected, replay.Totals[observation.ItemName!]);
        var saved = JsonSerializer.Deserialize<LootDiagnosticEntry>(File.ReadLines(recording.RecordingPath!).Skip(1).First(),
            LootDiagnosticFormat.JsonOptions)!;
        Assert.Equal(observation, Assert.Single(saved.Observations));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 2147483648L)]
    public void ReplayRejectsInvalidPerDropBounds(long minimum, long maximum)
    {
        Directory.CreateDirectory(directory);
        var header = new LootDiagnosticHeader("header", LootDiagnosticFormat.Version, LootDiagnosticFormat.EngineVersion,
            DateTimeOffset.UnixEpoch, null, "counter-only") { Catalog = [new("BON Origin Shard")] };
        var observation = new LootObservation(LootSource.Normal, 0, "raw", "BON Origin Shard", 1, 1, 0, null, null)
            { NativeY = 250, QuantityBounds = new(1, 1) };
        var entry = JsonSerializer.SerializeToNode(new LootDiagnosticEntry("frame", 1, DateTimeOffset.UnixEpoch,
            [observation], [], [], []), LootDiagnosticFormat.JsonOptions)!;
        entry["observations"]![0]!["quantityBounds"]!["minimum"] = minimum;
        entry["observations"]![0]!["quantityBounds"]!["maximum"] = maximum;
        var path = Path.Combine(directory, "invalid.jsonl");
        File.WriteAllLines(path, [JsonSerializer.Serialize(header, LootDiagnosticFormat.JsonOptions),
            entry.ToJsonString(LootDiagnosticFormat.JsonOptions)]);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
