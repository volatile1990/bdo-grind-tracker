using System.Reflection;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LootDiagnosticRecordingTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(), "BdoGrindTracker-DiagnosticTests-" + Guid.NewGuid().ToString("N"));

    private static DateTimeOffset StartTime => new(2026, 9, 4, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureAndRecoveryMetadataDoNotChangeCounterReplayOrAddImages(bool? isToneMapped)
    {
        using var source = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory, "hermesia");
        var tracker = new CompanionDiagnosticCounter(Header().Catalog);
        var observations = new[] { Observation() };
        var recovery = new NormalLootRecoveryDiagnostics(2, 4, 1, 1, 0);
        recording.RecordFrame(StartTime, observations,
            tracker.ProcessFrame(StartTime, observations, false), source, null, null, recovery,
            isHdr: true, isToneMapped: isToneMapped);
        recording.RecordCompletion(StartTime.AddSeconds(1), tracker.CompleteSession(StartTime.AddSeconds(1)));
        recording.Dispose();

        var lines = File.ReadAllLines(recording.RecordingPath!);
        var entry = JsonSerializer.Deserialize<LootDiagnosticEntry>(lines[1], LootDiagnosticFormat.JsonOptions)!;
        Assert.Equal(recovery, entry.Recovery);
        Assert.True(entry.IsHdr);
        Assert.Equal(isToneMapped, entry.IsToneMapped);
        Assert.Equal(isToneMapped.HasValue, lines[1].Contains("\"isToneMapped\"", StringComparison.Ordinal));
        Assert.Equal(observations, entry.Observations);
        Assert.Empty(entry.Crops);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(recording.RecordingPath!)!));
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
    }

    [Fact]
    public void RecordsOnlyRequestedLootCropsAndMachineReadableEvidence()
    {
        using var source = new Bitmap(120, 80);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.Red);
            graphics.FillRectangle(Brushes.Lime, 80, 40, 20, 10);
            graphics.FillRectangle(Brushes.Blue, 50, 20, 12, 8);
        }

        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory, "hermesia");
        var observation = Observation();
        var eventId = Guid.NewGuid();
        var result = new TrackerFrameResult(
            [new TrackedLootEvent(eventId, StartTime, "BON Origin Shard", 1)],
            [new LootTrackingDecision(observation, eventId, LootTrackingDecisionStatus.Counted, "confirmed once")]);
        recording.RecordFrame(StartTime, [observation], result, source,
            new Rectangle(80, 40, 20, 10), new Rectangle(50, 20, 12, 8), isHdr: true);
        recording.Dispose();

        Assert.Null(recording.LastError);
        var directory = Path.GetDirectoryName(recording.RecordingPath!)!;
        Assert.Equal(3, Directory.GetFiles(directory).Length);
        using var normal = new Bitmap(Path.Combine(directory, "000001-normal.png"));
        using var rare = new Bitmap(Path.Combine(directory, "000001-rare.png"));
        Assert.Equal(new Size(20, 10), normal.Size);
        Assert.Equal(Color.Lime.ToArgb(), normal.GetPixel(0, 0).ToArgb());
        Assert.Equal(new Size(12, 8), rare.Size);
        Assert.Equal(Color.Blue.ToArgb(), rare.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), source.GetPixel(0, 0).ToArgb());
        var lines = File.ReadAllLines(recording.RecordingPath!);
        var entry = JsonSerializer.Deserialize<LootDiagnosticEntry>(lines[1], LootDiagnosticFormat.JsonOptions)!;
        var header = JsonSerializer.Deserialize<LootDiagnosticHeader>(lines[0], LootDiagnosticFormat.JsonOptions)!;
        Assert.Equal(LootDiagnosticFormat.EngineVersion, header.EngineVersion);
        Assert.NotEmpty(header.Catalog);
        Assert.True(entry.RareEnabled);
        Assert.True(entry.IsHdr);
        Assert.Null(entry.IsToneMapped);
        Assert.Equal(observation, Assert.Single(entry.Observations));
        Assert.Equal(eventId, Assert.Single(entry.Events).EventId);
        Assert.Equal("confirmed once", Assert.Single(entry.Decisions).Reason);
        Assert.Collection(
            entry.Crops,
            crop => Assert.Equal(new LootDiagnosticCrop("normal", "000001-normal.png", 20, 10), crop),
            crop => Assert.Equal(new LootDiagnosticCrop("rare", "000001-rare.png", 12, 8), crop));
    }

    [Fact]
    public void ReplayReproducesRecordedEventsIncludingCompletionAndResume()
    {
        using var source = new Bitmap(24, 24);
        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory, "hermesia");
        var tracker = new CompanionDiagnosticCounter(Header().Catalog);
        var observations = new[] { Observation() };
        for (var index = 0; index < 8; index++)
        {
            var timestamp = StartTime.AddMilliseconds(index * 450);
            recording.RecordFrame(timestamp, observations, tracker.ProcessFrame(timestamp, observations, false),
                source, new Rectangle(0, 0, 12, 12), null);
        }

        var pausedAt = StartTime.AddSeconds(4);
        recording.RecordCompletion(pausedAt, tracker.CompleteSession(pausedAt));
        for (var index = 0; index < 8; index++)
        {
            var timestamp = StartTime.AddSeconds(10).AddMilliseconds(index * 450);
            recording.RecordFrame(timestamp, observations, tracker.ProcessFrame(timestamp, observations, false), source, null, null);
        }

        var endedAt = StartTime.AddSeconds(14);
        recording.RecordCompletion(endedAt, tracker.CompleteSession(endedAt));
        recording.Dispose();

        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
        Assert.True(replay.HasFinalCompletion);
        Assert.Equal(16, replay.FrameCount);
        Assert.Equal(2, replay.CompletionCount);
        Assert.NotEmpty(replay.Totals);
        Assert.Equal("hermesia", replay.SpotId);
        Assert.Contains("OCR-Erkennung wird nicht erneut ausgeführt", replay.ToDisplayText());
    }

    [Fact]
    public void PartialReplayDoesNotInventAnUnrecordedCompletion()
    {
        using var source = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory);
        var tracker = new CompanionDiagnosticCounter(Header().Catalog);
        var observations = new[] { Observation() with { NameConfidence = 0.7, QuantityConfidence = 0.7 } };
        recording.RecordFrame(StartTime, observations, tracker.ProcessFrame(StartTime, observations, false), source, null, null);
        recording.Dispose();

        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
        Assert.False(replay.HasFinalCompletion);
        Assert.Equal(0, replay.CompletionCount);
        Assert.Contains("Teilaufnahme", replay.ToDisplayText());
    }

    [Fact]
    public void ExplicitFrameLimitStopsOnlyRecordingAndKeepsPrefixReplayable()
    {
        using var source = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory, null, 100_000, 1);
        recording.RecordFrame(StartTime, [], new TrackerFrameResult([], []), source, null, null);
        recording.RecordFrame(StartTime.AddSeconds(1), [], new TrackerFrameResult([], []), source, null, null);
        recording.RecordCompletion(StartTime.AddSeconds(2), new TrackerFrameResult([], []));

        Assert.False(recording.IsRecording);
        Assert.Equal(1, recording.RecordedFrameCount);
        Assert.Contains("Frames erreicht", recording.LastError);
        Assert.Equal(1, LootDiagnosticReplay.Run(recording.RecordingPath!).FrameCount);
    }

    [Fact]
    public void DefaultRecordingAndReplayContinuePastTheFormerFrameAndActionLimits()
    {
        using var source = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory);
        var emptyResult = new TrackerFrameResult([], []);
        const int frames = 6_001;
        for (var index = 0; index < frames; index++)
            recording.RecordFrame(StartTime.AddMilliseconds(index * 450), [], emptyResult, source, null, null);
        recording.RecordCompletion(StartTime.AddHours(1), emptyResult);

        Assert.True(recording.IsRecording);
        Assert.Null(recording.LastError);
        Assert.Equal(frames, recording.RecordedFrameCount);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal(frames, replay.FrameCount);
        Assert.Equal(1, replay.CompletionCount);
        Assert.True(replay.HasFinalCompletion);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
    }

    [Fact]
    public void DefaultRecordingContinuesAfterTheFormerCombinedStorageLimit()
    {
        using var source = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory);
        // Simulate crops already saved in a long session without creating a
        // 250-MiB fixture; the following frame and crop are genuinely written.
        typeof(DiagnosticRecordingSession).GetField("writtenBytes", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(recording, 250L * 1024 * 1024 + 1);
        recording.RecordFrame(StartTime, [], new TrackerFrameResult([], []), source,
            new Rectangle(0, 0, 1, 1), null);
        recording.RecordCompletion(StartTime.AddSeconds(1), new TrackerFrameResult([], []));

        Assert.True(recording.IsRecording);
        Assert.Null(recording.LastError);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(recording.RecordingPath!)!, "*.png"));
        Assert.True(new FileInfo(recording.RecordingPath!).Length < 100_000);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal(1, replay.FrameCount);
        Assert.True(replay.HasFinalCompletion);
        recording.Dispose();
        var frame = JsonSerializer.Deserialize<LootDiagnosticEntry>(File.ReadLines(recording.RecordingPath!).Skip(1).First(),
            LootDiagnosticFormat.JsonOptions)!;
        Assert.Null(frame.IsHdr);
    }

    [Fact]
    public void ExplicitStorageLimitStopsRecordingWithoutWritingAnOversizedEntry()
    {
        using var source = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory, null, 16_000, 10);
        Assert.True(recording.IsRecording);
        var observation = Observation() with { RawText = new string('X', 2000) };
        recording.RecordFrame(StartTime, Enumerable.Repeat(observation, 8).ToArray(), new TrackerFrameResult([], []), source, null, null);

        Assert.False(recording.IsRecording);
        Assert.Contains("Speicherlimit", recording.LastError);
        Assert.True(new FileInfo(recording.RecordingPath!).Length <= 16_000);
        Assert.Equal(0, LootDiagnosticReplay.Run(recording.RecordingPath!).FrameCount);
    }

    [Theory]
    [InlineData(-1, 0, 2, 2)]
    [InlineData(0, 0, 5, 5)]
    [InlineData(0, 0, 4, 4)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(int.MaxValue, 0, 2, 2)]
    public void InvalidCropNeverFallsBackToAFullScreenshot(int x, int y, int width, int height)
    {
        using var source = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory);
        recording.RecordFrame(StartTime, [], new TrackerFrameResult([], []), source,
            new Rectangle(x, y, width, height), null);

        Assert.False(recording.IsRecording);
        Assert.Contains("Loot-Ausschnitt", recording.LastError);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(recording.RecordingPath!)!, "*.png"));
    }

    [Fact]
    public void UnwritableRecordingLocationReturnsAnErrorInsteadOfThrowing()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var filePath = Path.Combine(temporaryDirectory, "not-a-directory");
        File.WriteAllText(filePath, "owned test file");
        using var recording = DiagnosticRecordingSession.Start(filePath);
        Assert.False(recording.IsRecording);
        Assert.NotNull(recording.LastError);
    }

    [Fact]
    public void SessionsAlwaysUseDistinctFoldersAndDisposeIsIdempotent()
    {
        using var first = DiagnosticRecordingSession.Start(temporaryDirectory);
        using var second = DiagnosticRecordingSession.Start(temporaryDirectory);
        Assert.NotEqual(first.RecordingPath, second.RecordingPath);
        first.Dispose();
        first.Dispose();
        using var source = new Bitmap(4, 4);
        first.RecordFrame(StartTime, [], new TrackerFrameResult([], []), source, null, null);
        Assert.Equal(0, first.RecordedFrameCount);
        Assert.Null(first.LastError);
    }

    [Fact]
    public void ReplayReportsTamperedOriginalTotalsWithoutFollowingRecordedImagePaths()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var recordingPath = Path.Combine(temporaryDirectory, "tampered.jsonl");
        var header = Header();
        var entry = new LootDiagnosticEntry(
            "frame", 1, StartTime, [],
            [new TrackedLootEvent(Guid.NewGuid(), StartTime, "BON Origin Shard", 3)], [],
            [new LootDiagnosticCrop("normal", "Z:/do-not-open/missing-image.png", 10, 10)]);
        File.WriteAllLines(recordingPath, [Serialize(header), Serialize(entry)]);

        var replay = LootDiagnosticReplay.Run(recordingPath);
        Assert.False(replay.TotalsMatch);
        Assert.False(replay.EventTimelineMatches);
        Assert.Equal(1, replay.FirstDifferentSequence);
        Assert.Equal(3, replay.RecordedTotals["BON Origin Shard"]);
        Assert.Empty(replay.Totals);
    }

    [Theory]
    [InlineData("{\"kind\":\"header\",\"formatVersion\":999}")]
    [InlineData("{")]
    [InlineData("")]
    public void ReplayRejectsUnsupportedOrBrokenHeaders(string header)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var recordingPath = Path.Combine(temporaryDirectory, "invalid.jsonl");
        File.WriteAllText(recordingPath, header);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(recordingPath));
    }

    [Fact]
    public void ReplayRejectsOutOfOrderFramesAndMissingInputs()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var recordingPath = Path.Combine(temporaryDirectory, "invalid-frame.jsonl");
        var entry = new LootDiagnosticEntry("frame", 2, StartTime, [], [], [], []);
        File.WriteAllLines(recordingPath, [Serialize(Header()), Serialize(entry)]);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(recordingPath));

        File.WriteAllLines(recordingPath, [Serialize(Header()), "{\"kind\":\"frame\",\"sequence\":1}"]);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(recordingPath));
    }

    [Fact]
    public void ReplayUsesRecordedNativeYAndRepairsUnknownQuantityWithoutConfidenceGates()
    {
        using var source = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory);
        var tracker = new CompanionDiagnosticCounter(Header().Catalog);
        var unknown = Observation() with { Quantity = null, NameConfidence = 0.1, QuantityConfidence = 0 };
        var known = unknown with { Quantity = 17 };
        recording.RecordFrame(StartTime, [unknown], tracker.ProcessFrame(StartTime, [unknown], false), source, null, null);
        var next = StartTime.AddMilliseconds(450);
        recording.RecordFrame(next, [known], tracker.ProcessFrame(next, [known], false), source, null, null);
        recording.RecordCompletion(next, tracker.CompleteSession(next));
        recording.Dispose();

        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(17, replay.Totals["BON Origin Shard"]);
    }

    [Fact]
    public void ReplayPreservesRareClassificationSharedLedgerAndSignedCorrections()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "native-rare.jsonl");
        var header = Header() with
        {
            Catalog = [
                new CompanionRareCatalogEntry("Alpha Treasure", "new_icon/18_belt/alpha.dds"),
                new CompanionRareCatalogEntry("Beta Prize", "new_icon/18_belt/beta.dds"),
            ],
        };
        var tracker = new CompanionDiagnosticCounter(header.Catalog);
        var lines = new List<string> { Serialize(header) };
        var first = Observation() with { ItemName = "Alpha Treasure" };
        LootObservation[][] frames = [
            [first, first with { Source = LootSource.Rare, NativeY = 0 }],
            [first with { Source = LootSource.Rare, ItemName = "Beta Prize", Quantity = 2, NativeY = 0 }],
            [first with { Source = LootSource.Rare, ItemName = "Beta Prize", Quantity = 3, NativeY = 0 }],
        ];
        for (var index = 0; index < frames.Length; index++)
        {
            var timestamp = StartTime.AddMilliseconds(index * 450);
            var result = tracker.ProcessFrame(timestamp, frames[index], true);
            lines.Add(Serialize(new LootDiagnosticEntry(
                "frame", index + 1, timestamp, frames[index], result.NewEvents, [], [])
            { RareEnabled = true }));
        }

        var completedAt = StartTime.AddSeconds(2);
        var complete = tracker.CompleteSession(completedAt);
        Assert.Contains(complete.NewEvents, static entry => entry.ItemName == "Alpha Treasure" && entry.Quantity == -1);
        lines.Add(Serialize(new LootDiagnosticEntry("complete", 4, completedAt, [], complete.NewEvents, [], [])));
        File.WriteAllLines(path, lines);

        var replay = LootDiagnosticReplay.Run(path);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(1, replay.Totals["Alpha Treasure"]);
        Assert.Equal(3, replay.Totals["Beta Prize"]);
    }

    [Fact]
    public void RareCorrectionsRemoveZeroTotals()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "rare-zero.jsonl");
        var entry = new LootDiagnosticEntry("frame", 1, StartTime, [],
            [new TrackedLootEvent(Guid.NewGuid(), StartTime, "BON Origin Shard", 1),
             new TrackedLootEvent(Guid.NewGuid(), StartTime, "BON Origin Shard", -1)], [], []);
        File.WriteAllLines(path, [Serialize(Header()), Serialize(entry)]);

        var replay = LootDiagnosticReplay.Run(path);
        Assert.True(replay.TotalsMatch);
        Assert.Empty(replay.RecordedTotals);
        Assert.False(replay.EventTimelineMatches);
    }

    [Fact]
    public void ConfiguredRareChannelAdvancesAcrossRecordedEmptyFrames()
    {
        using var source = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(temporaryDirectory);
        var tracker = new CompanionDiagnosticCounter(Header().Catalog);
        var rare = Observation() with { Source = LootSource.Rare, NativeY = 0 };
        for (var index = 0; index <= 14; index++)
        {
            LootObservation[] observations = index is 0 or 14 ? [rare] : [];
            var timestamp = StartTime.AddMilliseconds(index * 450);
            recording.RecordFrame(timestamp, observations, tracker.ProcessFrame(timestamp, observations, true),
                source, null, new Rectangle(0, 0, 1, 1));
        }

        var completedAt = StartTime.AddSeconds(7);
        recording.RecordCompletion(completedAt, tracker.CompleteSession(completedAt));
        recording.Dispose();
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(2, replay.Totals["BON Origin Shard"]);
    }

    [Fact]
    public void ReplayRejectsMissingNativeCoordinatesInsteadOfGuessingFromSlots()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "missing-y.jsonl");
        var entry = new LootDiagnosticEntry("frame", 1, StartTime,
            [Observation() with { NativeY = null }], [], [], []);
        File.WriteAllLines(path, [Serialize(Header()), Serialize(entry)]);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(path));
    }

    [Fact]
    public void ReplayRejectsRareModeChangesAndObsoletePersistentEngine()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "wrong-engine.jsonl");
        File.WriteAllLines(path, [Serialize(Header() with { EngineVersion = "persistent-events-v2" })]);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(path));

        File.WriteAllLines(path, [Serialize(Header()),
            Serialize(new LootDiagnosticEntry("frame", 1, StartTime, [], [], [], []) { RareEnabled = true }),
            Serialize(new LootDiagnosticEntry("frame", 2, StartTime.AddSeconds(1), [], [], [], [])),
        ]);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(path));
    }

    [Theory]
    [InlineData(LootDiagnosticFormat.EngineVersion, 4, true)]
    [InlineData(LootDiagnosticFormat.PreviousEngineVersion, 4, false)]
    [InlineData(LootDiagnosticFormat.ExperimentalEngineVersion, 1, false)]
    public void ReplayDistinguishesCurrentRecordingsFromPreviousCounterComparisons(
        string engine, int recordedBookings, bool usesCurrentEngine)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "counter-version.jsonl");
        var lines = new List<string> { Serialize(Header() with { EngineVersion = engine }) };
        for (var frame = 1; frame <= 10; frame++)
        {
            var timestamp = StartTime.AddMilliseconds(frame * 450);
            var events = frame == 10
                ? Enumerable.Range(0, recordedBookings).Select(_ =>
                    new TrackedLootEvent(Guid.NewGuid(), timestamp, "BON Origin Shard", 8)).ToArray()
                : [];
            lines.Add(Serialize(new LootDiagnosticEntry("frame", frame, timestamp,
                [Observation() with { RawText = "BON Origin Shard x8", Quantity = 8 }], events, [], [])));
        }
        File.WriteAllLines(path, lines);

        var replay = LootDiagnosticReplay.Run(path);
        Assert.Equal(engine, replay.RecordingEngineVersion);
        Assert.Equal(32, replay.Totals["BON Origin Shard"]);
        Assert.Equal(8 * recordedBookings, replay.RecordedTotals["BON Origin Shard"]);
        Assert.Equal(usesCurrentEngine, replay.UsesCurrentEngine);
        Assert.Equal(recordedBookings == 4, replay.TotalsMatch);
        Assert.Equal(recordedBookings == 4, replay.EventTimelineMatches);
        if (!replay.UsesCurrentEngine)
            Assert.Contains("Versionsvergleich", replay.ToDisplayText());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static LootObservation Observation() =>
        new(LootSource.Normal, 0, "BON Origin Shard x1", "BON Origin Shard", 1, 1, 1, 0xAAAA, null)
        {
            NativeY = 250,
        };

    private static LootDiagnosticHeader Header() =>
        new("header", LootDiagnosticFormat.Version, LootDiagnosticFormat.EngineVersion, StartTime, null, "companion-counter-only")
        {
            Catalog = [new CompanionRareCatalogEntry("BON Origin Shard")],
        };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, LootDiagnosticFormat.JsonOptions);
}
