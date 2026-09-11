using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class TemporalTrackingIntegrationTests : IDisposable
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Variant = "test+temporal-v2+visual-appearance-v1";
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest-TemporalTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AnalyzerPassesRepeatedReadingsAndCaptureTimesThroughRecordingAndReplay()
    {
        var rows = new Rows();
        using var analyzer = Analyzer(rows);
        using var bitmap = new Bitmap(800, 600);
        using var recording = DiagnosticRecordingSession.Start(directory, targetFrameInterval: TimeSpan.FromMilliseconds(200),
            maximumQueuedFrames: 4);
        var events = new List<LootEventView>();
        foreach (var (quantity, index) in new[] { 5, 500, 5, 5 }.Select((quantity, index) => (quantity, index)))
        {
            rows.Quantity = quantity;
            var at = Start.AddMilliseconds(index * 200);
            var result = await analyzer.AnalyzeAsync(bitmap, at, CancellationToken.None);
            Assert.Contains("+temporal-v2+visual-appearance-v1", result.VariantName);
            Assert.Null(result.SpotId); // Multiple sightings of one drop cannot establish three independent drops.
            events.AddRange(result.NewEvents);
            recording.RecordFrame(at, result.Observations, result.TrackingResult, bitmap, result.PanelRegion, null,
                recognitionVariant: result.VariantName);
        }
        var completed = analyzer.CompleteSession(Start.AddSeconds(1));
        events.AddRange(completed.NewEvents);
        recording.RecordCompletion(Start.AddSeconds(1), completed.TrackingResult);
        recording.Dispose();

        Assert.Equal(5, events.Sum(entry => entry.Quantity));
        Assert.Single(events.Select(entry => entry.EventId).Distinct());
        Assert.All(events, entry => Assert.Equal(Start, entry.DetectedAt));
        Assert.Empty(analyzer.CompleteSession(Start.AddSeconds(2)).NewEvents);
        Assert.Null(recording.LastError);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal(TemporalLootReconciler.AlgorithmName, replay.NormalTrackingAlgorithm);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(5, replay.Totals[Helmet]);
    }

    [Fact]
    public async Task TemporalSpotLockRequiresThreeSeparateConfirmedDrops()
    {
        var rows = new Rows { Quantity = 6 };
        using var analyzer = Analyzer(rows);
        using var bitmap = new Bitmap(800, 600);
        var events = new List<LootEventView>();
        for (var drop = 0; drop < 3; drop++)
        {
            rows.Present = true;
            var first = await analyzer.AnalyzeAsync(bitmap, Start.AddMilliseconds(drop * 1000), CancellationToken.None);
            events.AddRange(first.NewEvents);
            Assert.Null(first.SpotId); // The raw third trash reading is not a third confirmed drop yet.
            var confirmed = await analyzer.AnalyzeAsync(bitmap, Start.AddMilliseconds(drop * 1000 + 200), CancellationToken.None);
            events.AddRange(confirmed.NewEvents);
            if (drop < 2) Assert.Null(confirmed.SpotId);
            else Assert.Equal(LootSpotCatalog.MagaiaId, confirmed.SpotId);
            rows.Present = false;
            for (var missing = 0; missing < 2; missing++)
            {
                var empty = await analyzer.AnalyzeAsync(bitmap,
                    Start.AddMilliseconds(drop * 1000 + 400 + missing * 200), CancellationToken.None);
                events.AddRange(empty.NewEvents);
            }
        }
        events.AddRange(analyzer.CompleteSession(Start.AddSeconds(3)).NewEvents);
        Assert.Equal(18, events.Sum(entry => entry.Quantity));
        Assert.Equal(3, events.Select(entry => entry.EventId).Distinct().Count());
    }

    [Fact]
    public void PauseFlushAndLaterReadPreserveIdRevisionAndOriginalCaptureTime()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = Counter();
        // A completion before any capture must not force the replay to use legacy tracking.
        recording.RecordCompletion(Start.AddMilliseconds(-1), counter.CompleteSession(Start.AddMilliseconds(-1)));
        Record(Start, Observation(null));
        var initial = counter.CompleteSession(Start.AddMilliseconds(50));
        recording.RecordCompletion(Start.AddMilliseconds(50), initial);
        Record(Start.AddMilliseconds(200), Observation(6));
        Record(Start.AddMilliseconds(400), Observation(6));
        var final = counter.CompleteSession(Start.AddMilliseconds(500));
        recording.RecordCompletion(Start.AddMilliseconds(500), final);
        recording.Dispose();

        var first = Assert.Single(initial.NewEvents);
        Assert.Equal(4, first.Quantity);
        var allEvents = Entries(recording.RecordingPath!).SelectMany(entry => entry.Events).ToArray();
        var revision = Assert.Single(allEvents, entry => entry.Revision > 0);
        Assert.Equal(first.EventId, revision.EventId);
        Assert.Equal(2, revision.Quantity);
        Assert.Equal(6, revision.TotalDropQuantity);
        Assert.Equal(Start, revision.DetectedAt);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(6, replay.Totals[Helmet]);

        void Record(DateTimeOffset at, LootObservation observation) =>
            recording.RecordFrame(at, [observation], counter.ProcessFrame(at, [observation], false),
                bitmap, new Rectangle(0, 0, 2, 2), null, recognitionVariant: Variant);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemporalReplayDetectsChangedEventMetadataEvenWithIdenticalTotals(bool changeIdentity)
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = Counter();
        var observation = Observation(6);
        recording.RecordFrame(Start, [observation], counter.ProcessFrame(Start, [observation], false),
            bitmap, null, null, recognitionVariant: Variant);
        recording.RecordCompletion(Start.AddSeconds(1), counter.CompleteSession(Start.AddSeconds(1)));
        recording.Dispose();
        var lines = File.ReadAllLines(recording.RecordingPath!);
        var last = Deserialize<LootDiagnosticEntry>(lines[^1]);
        lines[^1] = Serialize(last with { Events = last.Events.Select(entry => changeIdentity
            ? entry with { EventId = Guid.NewGuid() }
            : entry with { DetectedAt = entry.DetectedAt.AddMilliseconds(1) }).ToArray() });
        File.WriteAllLines(recording.RecordingPath!, lines);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch);
        Assert.False(replay.EventTimelineMatches);
        Assert.Equal(2, replay.FirstDifferentSequence);
    }

    [Fact]
    public void LegacyV8RecordingKeepsItsOriginalRowCounter()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Helmet)], trackRows: true);
        for (var index = 0; index < 4; index++)
        {
            var at = Start.AddMilliseconds(index * 200);
            var observation = Observation(6);
            recording.RecordFrame(at, [observation], counter.ProcessFrame(at, [observation], false),
                bitmap, null, null, recognitionVariant: "test+row-tracks-v1");
        }
        recording.RecordCompletion(Start.AddSeconds(1), counter.CompleteSession(Start.AddSeconds(1)));
        recording.Dispose();
        var lines = File.ReadAllLines(recording.RecordingPath!);
        lines[0] = Serialize(Deserialize<LootDiagnosticHeader>(lines[0]) with
            { EngineVersion = LootDiagnosticFormat.LegacyRowTracksEngineVersion });
        File.WriteAllLines(recording.RecordingPath!, lines);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal("row-tracks-v1", replay.NormalTrackingAlgorithm);
        Assert.Equal(12, replay.Totals[Helmet]);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
        Assert.False(replay.UsesCurrentEngine);
    }

    [Fact]
    public void LegacyTemporalV1RecordingKeepsItsCounterAndExactEventMetadata()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Helmet)], temporal: true, legacyTemporal: true);
        for (var index = 0; index < 8; index++)
        {
            var at = Start.AddMilliseconds(index * 200);
            var observation = Observation(4);
            recording.RecordFrame(at, [observation], counter.ProcessFrame(at, [observation], false),
                bitmap, null, null, recognitionVariant: "test+temporal-v1");
        }
        recording.RecordCompletion(Start.AddSeconds(2), counter.CompleteSession(Start.AddSeconds(2)));
        recording.Dispose();
        var lines = File.ReadAllLines(recording.RecordingPath!);
        lines[0] = Serialize(Deserialize<LootDiagnosticHeader>(lines[0]) with
            { EngineVersion = LootDiagnosticFormat.LegacyTemporalEngineVersion });
        File.WriteAllLines(recording.RecordingPath!, lines);

        var only = Assert.Single(Entries(recording.RecordingPath!).SelectMany(entry => entry.Events));
        Assert.Equal(4, only.Quantity);
        Assert.Equal(Start, only.DetectedAt);
        Assert.Equal(0, only.Revision);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal(TemporalLootReconciler.LegacyAlgorithmName, replay.NormalTrackingAlgorithm);
        Assert.Equal(4, replay.Totals[Helmet]);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
        Assert.False(replay.UsesCurrentEngine);
    }

    [Fact]
    public void VisualEvidenceRoundTripsAndChangesTheVisibleRowIdentity()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = Counter();
        var oldCounter = new CompanionDiagnosticCounter([new(Helmet)], temporal: true, legacyTemporal: true);
        var oldEvents = new List<TrackedLootEvent>();
        var evidence = new NormalLootAppearanceEvidence(1, [new(0, 1, .4)]);
        for (var index = 0; index < 4; index++)
        {
            var at = Start.AddMilliseconds(index * 200);
            var row = Observation(4) with { AppearanceEvidence = index == 2 ? evidence : null };
            recording.RecordFrame(at, [row], counter.ProcessFrame(at, [row], false),
                bitmap, null, null, recognitionVariant: Variant);
            oldEvents.AddRange(oldCounter.ProcessFrame(at, [row with { AppearanceEvidence = null }], false).NewEvents);
        }
        recording.RecordCompletion(Start.AddSeconds(1), counter.CompleteSession(Start.AddSeconds(1)));
        oldEvents.AddRange(oldCounter.CompleteSession(Start.AddSeconds(1)).NewEvents);
        recording.Dispose();

        Assert.Null(recording.LastError);
        var saved = Assert.Single(Entries(recording.RecordingPath!).SelectMany(entry => entry.Observations),
            row => row.AppearanceEvidence is not null).AppearanceEvidence!;
        Assert.Equal(evidence.FadedPreviousSlots, saved.FadedPreviousSlots);
        Assert.Equal(evidence.Matches, saved.Matches);
        var events = Entries(recording.RecordingPath!).SelectMany(entry => entry.Events).ToArray();
        Assert.Equal(8, events.Sum(entry => entry.Quantity));
        Assert.Equal(2, events.Select(entry => entry.EventId).Distinct().Count());
        Assert.Equal(4, oldEvents.Sum(entry => entry.Quantity));
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal(TemporalLootReconciler.AlgorithmName, replay.NormalTrackingAlgorithm);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("hdr")]
    [InlineData("tone-mapping")]
    [InlineData("language")]
    [InlineData("session")]
    [InlineData("pause")]
    public async Task AnalyzerRetainsPixelsAcrossEmptyOcrAndResetsIncompatibleRepresentations(string transition)
    {
        var rows = new Rows { Quantity = 4, Present = false };
        var receiver = new AppearanceReceiver();
        using var analyzer = Analyzer(rows, receiver);
        using var faded = AppearanceFrame(90);
        using var fresh = AppearanceFrame(225);
        var first = await analyzer.AnalyzeAsync(faded, Start, CancellationToken.None);
        Assert.Empty(first.Observations);
        if (transition == "language") analyzer.ConfigureGameLanguage("de-DE");
        if (transition == "session") analyzer.Reset();
        if (transition == "pause") analyzer.CompleteSession(Start.AddMilliseconds(100));
        rows.Present = true;
        var next = await analyzer.AnalyzeAsync(fresh, Start.AddMilliseconds(200),
            transition == "hdr", transition == "tone-mapping", CancellationToken.None);
        var observation = Assert.Single(next.Observations);
        var forwarded = Assert.Single(receiver.Entries);
        if (transition == "none")
        {
            Assert.Equal(1, observation.AppearanceEvidence!.FadedPreviousSlots);
            Assert.Same(observation.AppearanceEvidence, forwarded.AppearanceEvidence);
        }
        else
        {
            Assert.Null(observation.AppearanceEvidence);
            Assert.Null(forwarded.AppearanceEvidence);
        }
    }

    [Fact]
    public async Task LegacyTemporalAnalyzerKeepsItsMarkerAndDoesNotMeasureAppearance()
    {
        var rows = new Rows { Quantity = 4 };
        using var analyzer = Analyzer(rows, new TemporalNormalReconciliationAdapter(legacyMode: true));
        using var faded = AppearanceFrame(90);
        using var fresh = AppearanceFrame(225);
        await analyzer.AnalyzeAsync(faded, Start, CancellationToken.None);
        var next = await analyzer.AnalyzeAsync(fresh, Start.AddMilliseconds(200), CancellationToken.None);
        Assert.Contains("+temporal-v1", next.VariantName);
        Assert.DoesNotContain("visual-appearance-v1", next.VariantName);
        Assert.Null(Assert.Single(next.Observations).AppearanceEvidence);
    }

    [Theory]
    [InlineData("test+temporal-v1")]
    [InlineData("test+row-tracks-v1")]
    [InlineData("test+temporal-v2")]
    [InlineData("test+temporal-v2+visual-appearance-v1+temporal-v1")]
    [InlineData("test+temporal-v2+visual-appearance-v1+temporal-v3")]
    public void RecorderRejectsAppearanceWithoutItsVersionedMode(string variant)
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        recording.RecordFrame(Start, [Observation(4) with
            { AppearanceEvidence = new(1, [new(0, 1, .4)]) }], new([], []),
            bitmap, null, null, recognitionVariant: variant);
        Assert.NotNull(recording.LastError);
        Assert.False(recording.IsRecording);
    }

    [Theory]
    [InlineData("test+temporal-v1")]
    [InlineData("test+row-tracks-v1")]
    [InlineData("test+temporal-v2")]
    [InlineData("test+temporal-v3+visual-appearance-v1")]
    [InlineData("test+temporal-v2+visual-appearance-v1+temporal-v1")]
    public void ReplayRejectsAppearanceInHistoricalOrMismatchedModes(string variant)
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        recording.RecordFrame(Start, [Observation(4) with
            { AppearanceEvidence = new(1, [new(0, 1, .4)]) }], new([], []),
            bitmap, null, null, recognitionVariant: Variant);
        recording.Dispose();
        var lines = File.ReadAllLines(recording.RecordingPath!);
        lines[1] = Serialize(Deserialize<LootDiagnosticEntry>(lines[1]) with { RecognitionVariant = variant });
        File.WriteAllLines(recording.RecordingPath!, lines);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(recording.RecordingPath!));
    }

    [Fact]
    public void HistoricalHeaderCannotSelectVisualTemporalMode()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        recording.RecordFrame(Start, [], new([], []), bitmap, null, null, recognitionVariant: Variant);
        recording.Dispose();
        var lines = File.ReadAllLines(recording.RecordingPath!);
        lines[0] = Serialize(Deserialize<LootDiagnosticHeader>(lines[0]) with
            { EngineVersion = LootDiagnosticFormat.LegacyTemporalEngineVersion });
        File.WriteAllLines(recording.RecordingPath!, lines);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(recording.RecordingPath!));
    }

    [Theory]
    [InlineData(-1, 0, 1, .4)]
    [InlineData(64, 0, 1, .4)]
    [InlineData(1, 6, 1, .4)]
    [InlineData(1, 0, 1.1, .4)]
    [InlineData(1, 0, double.NaN, .4)]
    [InlineData(1, 0, 1, -1)]
    [InlineData(1, 0, 1, double.PositiveInfinity)]
    public void AppearanceValidationRejectsMalformedFields(int mask, int slot, double correlation, double ratio)
    {
        var observation = Observation(4) with
            { AppearanceEvidence = new(mask, [new(slot, correlation, ratio)]) };
        Assert.Throws<InvalidDataException>(() => DiagnosticRecordingSession.ValidateObservations([observation]));
    }

    [Fact]
    public void CaptureTimingAndActualSettingsRoundTripWithoutChangingCounting()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory,
            targetFrameInterval: TimeSpan.FromMilliseconds(320), maximumQueuedFrames: 2);
        var counter = Counter();
        var timing = new LootCaptureTiming(320, 7, null, 20, 30, 40);
        recording.RecordFrame(Start, [], counter.ProcessFrame(Start, [], false), bitmap, null, null,
            recognitionVariant: Variant, captureTiming: timing);
        recording.RecordCompletion(Start.AddSeconds(1), counter.CompleteSession(Start.AddSeconds(1)));
        recording.Dispose();
        var lines = File.ReadAllLines(recording.RecordingPath!);
        var header = Deserialize<LootDiagnosticHeader>(lines[0]);
        Assert.Equal(320, header.TargetFrameIntervalMilliseconds);
        Assert.Equal(2, header.MaximumQueuedFrames);
        var entry = Deserialize<LootDiagnosticEntry>(lines[1]);
        Assert.Equal(timing, entry.CaptureTiming);
        Assert.Equal(70, entry.CaptureTiming!.CaptureToResultMilliseconds);
        Assert.True(LootDiagnosticReplay.Run(recording.RecordingPath!).EventTimelineMatches);

        lines[1] = Serialize(entry with { CaptureTiming = timing with { QueueDelayMilliseconds = -1 } });
        File.WriteAllLines(recording.RecordingPath!, lines);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(recording.RecordingPath!));
    }

    [Fact]
    public void RecordingCannotChangeNormalCounterMidSession()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        recording.RecordFrame(Start, [], new([], []), bitmap, null, null, recognitionVariant: Variant);
        recording.RecordFrame(Start.AddMilliseconds(200), [], new([], []), bitmap, null, null,
            recognitionVariant: "test+row-tracks-v1");
        recording.Dispose();
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(recording.RecordingPath!));
    }

    private static CompanionDiagnosticCounter Counter() => new([new(Helmet)], temporal: true);
    private static LootObservation Observation(int? quantity) => new(LootSource.Normal, 0,
        quantity is { } value ? $"{Helmet} x {value}" : Helmet, Helmet, quantity, 1, 1, null, null)
        { NativeY = 250, QuantityBounds = new(4, 1000) };
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, LootDiagnosticFormat.JsonOptions);
    private static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, LootDiagnosticFormat.JsonOptions)!;
    private static LootDiagnosticEntry[] Entries(string path) => File.ReadAllLines(path).Skip(1)
        .Select(Deserialize<LootDiagnosticEntry>).ToArray();

    private static CompanionCalibration Calibration() => new("profile", "gamevariable.xml", "GameOption.txt",
        400, 300, 800, 600, 1, CompanionFontType.StrongSword, 0, false);

    private static CompanionLootFrameAnalyzer Analyzer(Rows rows, ICompanionReconciliation? reconciliation = null) => new(
        Calibration(),
        new CompanionItemMatcher([Helmet]), rows, new Names(rows),
        reconciliation: reconciliation ?? new TemporalNormalReconciliationAdapter(),
        quantityBoundsResolver: (_, _) => new(1, 1000));

    private static Bitmap AppearanceFrame(int strength)
    {
        var bitmap = new Bitmap(800, 600);
        var slot = CompanionNormalLootGeometry.CalculateSlotCrops(Calibration())[0];
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        using var brush = new SolidBrush(Color.FromArgb(strength, strength, strength));
        using var font = new Font("Arial", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.DrawString(Helmet, font, brush, slot.Left + 10, slot.Top + 12);
        return bitmap;
    }

    private sealed class AppearanceReceiver : ICompanionReconciliation
    {
        public string AlgorithmName => TemporalLootReconciler.AlgorithmName;
        public bool TracksRows => true;
        public IReadOnlyList<CompanionRecognizedEntry> Entries { get; private set; } = [];
        public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries)
        {
            Entries = entries;
            return [];
        }
        public IReadOnlyList<CompanionRecognizedEntry> Complete() => [];
        public void Reset() => Entries = [];
    }

    private sealed class Rows : ICompanionNormalRowPipeline
    {
        public int Quantity { get; set; }
        public bool Present { get; set; } = true;
        public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType, bool isHdr) =>
            new Row(y, !Present || y != 250, Quantity);
        public void Dispose() { }
    }

    private sealed class Row(int y, bool blank, int quantity) : ICompanionPreparedRow
    {
        public int Y => y;
        public bool IsBlank => blank;
        public int RecognizedTextWidth => 300;
        public int TemplateQuantity => blank ? -1 : quantity;
        public int LeftmostQuantityX => 0;
        public float QuantityScore => 1;
        public float NameScale => 1;
        public Mat? NameImage { get; } = blank ? null : new Mat(1, 1, MatType.CV_8UC1, Scalar.White);
        public void Dispose() => NameImage?.Dispose();
    }

    private sealed class Names(Rows rows) : ICompanionNameRecognizer
    {
        public string BackendName => "test";
        public string LanguageTag => "en-US";
        public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken) =>
            new($"{Helmet} x {rows.Quantity}", new(CompanionOcrGeometryStatus.Success, 0, 30, 100, 20));
    }

    public void Dispose()
    {
        var target = Path.GetFullPath(directory);
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(target).StartsWith("Grindcrest-TemporalTests-", StringComparison.Ordinal) && Directory.Exists(target))
            Directory.Delete(target, recursive: true);
    }
}
