using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class MinimumTrashQuantityIntegrationTests : IDisposable
{
    private const string Trash = "Black Crystal Fragment";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        $"BdoGrindTracker-MinimumQuantityTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task AnalyzerMarksFinalMinimumEstimateWithoutReplacingRecordedOcrInput()
    {
        var rows = new Rows(-1);
        using var analyzer = CreateAnalyzer(rows);
        using var frame = new Bitmap(800, 600);

        for (var index = 0; index < 2; index++)
        {
            var result = await analyzer.AnalyzeAsync(frame, Start.AddMilliseconds(index * 450), CancellationToken.None);
            Assert.Null(Assert.Single(result.Observations).Quantity);
            Assert.Empty(result.NewEvents);
        }

        var completed = analyzer.CompleteSession(Start.AddSeconds(1));
        var counted = Assert.Single(completed.NewEvents);
        Assert.Equal(2, counted.Quantity); // Synthetic test policy, not a researched production value.
        var decision = Assert.Single(completed.TrackingResult!.Decisions);
        Assert.Equal(counted.EventId, decision.EventId);
        Assert.Equal(LootTrackingDecisionStatus.Counted, decision.Status);
        Assert.Equal(LootDiagnosticFormat.MinimumQuantityEstimateReason, decision.Reason);
    }

    [Theory]
    [InlineData(1, null, 1)]
    [InlineData(6, null, 6)]
    [InlineData(-1, 6, 6)]
    public async Task ExistingOrChatRecoveredQuantitiesTakePriorityOverMinimum(
        int templateQuantity, int? chatQuantity, int expected)
    {
        using var analyzer = CreateAnalyzer(new Rows(templateQuantity),
            chatQuantity is { } quantity ? new ChatQuantity(quantity) : null);
        using var frame = new Bitmap(800, 600);

        var result = await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);
        var completed = analyzer.CompleteSession(Start.AddSeconds(1));

        Assert.Equal(expected, Assert.Single(result.Observations).Quantity);
        Assert.Equal(expected, Assert.Single(completed.NewEvents).Quantity);
        Assert.DoesNotContain(completed.TrackingResult!.Decisions,
            decision => decision.Reason == LootDiagnosticFormat.MinimumQuantityEstimateReason);
    }

    [Fact]
    public async Task LaterActualReadWithinPendingBatchWinsOverTheMinimum()
    {
        var rows = new Rows(-1);
        using var analyzer = CreateAnalyzer(rows);
        using var frame = new Bitmap(800, 600);
        await analyzer.AnalyzeAsync(frame, Start, CancellationToken.None);
        await analyzer.AnalyzeAsync(frame, Start.AddMilliseconds(450), CancellationToken.None);
        rows.Quantity = 6;
        await analyzer.AnalyzeAsync(frame, Start.AddMilliseconds(900), CancellationToken.None);

        var completed = analyzer.CompleteSession(Start.AddSeconds(2));

        Assert.Equal(6, Assert.Single(completed.NewEvents).Quantity);
        Assert.DoesNotContain(completed.TrackingResult!.Decisions,
            decision => decision.Reason == LootDiagnosticFormat.MinimumQuantityEstimateReason);
    }

    [Fact]
    public void RecordingCopiesPolicyAndReplaysTheRecordedMinimum()
    {
        var policy = Policy(7);
        using var source = new Bitmap(2, 2);
        using var recording = DiagnosticRecordingSession.Start(_directory, minimumTrashQuantities: policy);
        var counter = new CompanionDiagnosticCounter([new(Trash)], policy);
        policy[Trash] = 99;
        var observations = new[] { Observation() };
        for (var index = 0; index < 2; index++)
        {
            var timestamp = Start.AddMilliseconds(index * 450);
            recording.RecordFrame(timestamp, observations, counter.ProcessFrame(timestamp, observations, false),
                source, null, null);
        }
        var completed = counter.CompleteSession(Start.AddSeconds(1));
        recording.RecordCompletion(Start.AddSeconds(1), completed);
        recording.Dispose();

        Assert.Equal(7, Assert.Single(completed.NewEvents).Quantity);
        Assert.Equal(LootDiagnosticFormat.MinimumQuantityEstimateReason, Assert.Single(completed.Decisions).Reason);
        var saved = JsonSerializer.Deserialize<LootDiagnosticHeader>(
            File.ReadLines(recording.RecordingPath!).First(), LootDiagnosticFormat.JsonOptions)!;
        Assert.Equal(7u, saved.MinimumTrashQuantities[Trash]);
        var writableView = Assert.IsAssignableFrom<IDictionary<string, uint>>(saved.MinimumTrashQuantities);
        Assert.Throws<NotSupportedException>(() => writableView[Trash] = 99);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal(7, replay.Totals[Trash]);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
    }

    [Fact]
    public void HistoricalV3RecordingWithoutPolicyRetainsItsOriginalOneEstimate()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "historical-v3.jsonl");
        var header = JsonSerializer.SerializeToNode(Header() with
            { EngineVersion = LootDiagnosticFormat.RecoveryEngineVersion }, LootDiagnosticFormat.JsonOptions)!;
        header.AsObject().Remove("minimumTrashQuantities");
        var counter = new CompanionDiagnosticCounter([new(Trash)]);
        var lines = new List<string> { header.ToJsonString(LootDiagnosticFormat.JsonOptions) };
        for (var index = 0; index < 2; index++)
        {
            var timestamp = Start.AddMilliseconds(index * 450);
            var result = counter.ProcessFrame(timestamp, [Observation()], false);
            lines.Add(JsonSerializer.Serialize(new LootDiagnosticEntry("frame", index + 1, timestamp,
                [Observation()], result.NewEvents, result.Decisions, []), LootDiagnosticFormat.JsonOptions));
        }
        var completed = counter.CompleteSession(Start.AddSeconds(1));
        lines.Add(JsonSerializer.Serialize(new LootDiagnosticEntry("complete", 3, Start.AddSeconds(1), [],
            completed.NewEvents, completed.Decisions, []), LootDiagnosticFormat.JsonOptions));
        File.WriteAllLines(path, lines);

        var replay = LootDiagnosticReplay.Run(path);

        Assert.Equal(1, replay.Totals[Trash]);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
        Assert.False(replay.UsesCurrentEngine);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"Black Crystal Fragment\":0}")]
    [InlineData("{\"Black Crystal Fragment\":2147483648}")]
    [InlineData("{\"Ancient Spirit Dust\":2}")]
    [InlineData("{\"unknown item\":2}")]
    [InlineData("{\"a\":1,\"b\":1,\"c\":1,\"d\":1,\"e\":1,\"f\":1,\"g\":1}")]
    public void ReplayRejectsInvalidMinimumPolicies(string policyJson)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "invalid-policy.jsonl");
        var header = JsonSerializer.SerializeToNode(Header(), LootDiagnosticFormat.JsonOptions)!;
        header["minimumTrashQuantities"] = JsonNode.Parse(policyJson);
        File.WriteAllText(path, header.ToJsonString(LootDiagnosticFormat.JsonOptions));

        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(path));
    }

    private static Dictionary<string, uint> Policy(uint value = 2) => new(StringComparer.Ordinal) { [Trash] = value };

    private static LootDiagnosticHeader Header() => new("header", LootDiagnosticFormat.Version,
        LootDiagnosticFormat.EngineVersion, Start, null, "companion-counter-only") { Catalog = [new(Trash)] };

    private static LootObservation Observation() =>
        new(LootSource.Normal, 0, Trash, Trash, null, 1, 0, null, null) { NativeY = 250 };

    private static CompanionLootFrameAnalyzer CreateAnalyzer(Rows rows, IPrivateItemChatFallback? chat = null) =>
        new(new CompanionCalibration("profile", "gamevariable.xml", "GameOption.txt", 400, 300,
                800, 600, 1f, CompanionFontType.StrongSword, 0, false),
            new CompanionItemMatcher([Trash]), rows, new Names(),
            reconciliation: new CompanionReconciliationAdapter(Policy()), chatFallback: chat);

    private sealed class ChatQuantity(int quantity) : IPrivateItemChatFallback
    {
        public ChatQuantityRecoveryResult Apply(Mat frame, IReadOnlyList<LootObservation> normal,
            DateTimeOffset capturedAt, CancellationToken cancellationToken) =>
            new(normal.Select(row => row with { Quantity = quantity }).ToArray(), null,
                new(3, "reading-private-items", 1, 1, 0));
        public void Reset() { }
    }

    private sealed class Rows(int quantity) : ICompanionNormalRowPipeline
    {
        public int Quantity { get; set; } = quantity;
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) => new Row(y, Quantity);
        public void Dispose() { }
    }

    private sealed class Row(int y, int quantity) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => y != 250;
        public int RecognizedTextWidth => 100;
        public int TemplateQuantity => quantity;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => 0;
        public float NameScale => 1;
        public Mat? NameImage { get; } = y == 250 ? new Mat(1, 1, MatType.CV_8UC1) : null;
        public void Dispose() => NameImage?.Dispose();
    }

    private sealed class Names : ICompanionNameRecognizer
    {
        public string BackendName => "test-ocr";
        public string LanguageTag => "en-US";
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken) =>
            new(Trash, new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
