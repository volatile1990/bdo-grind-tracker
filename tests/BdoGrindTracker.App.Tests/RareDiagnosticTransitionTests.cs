using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionLootFrameAnalyzerTests
{
    [Theory]
    [InlineData(0, 3, 1)]
    [InlineData(0, 20, 2)]
    [InlineData(1, 3, 1)]
    [InlineData(1, 20, 2)]
    [InlineData(2, 3, 1)]
    [InlineData(2, 20, 2)]
    [InlineData(3, 3, 1)]
    [InlineData(3, 20, 2)]
    public async Task RareDiagnosticTransitionsReplayTheLiveAnalyzerWithoutLosingEitherCounter(
        int normalMode, int disabledFrames, int expectedRareCount)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grindcrest-rare-replay-{Guid.NewGuid():N}");
        try
        {
            var visible = Calibration() with
            {
                HasRareLootAnchor = true,
                RareLootAnchorX = 520,
                RareLootAnchorY = 300,
                RareLootResolution = new(RareLootAnchorStatus.Active, "active-ui", "visible"),
            };
            var hidden = visible with
            {
                HasRareLootAnchor = false,
                RareLootAnchorX = 0,
                RareLootAnchorY = 0,
                RareLootResolution = new(RareLootAnchorStatus.Hidden, "active-ui", "hidden"),
            };
            var current = hidden;
            var rows = new Rows(new Input(250, "Black Crystal Fragment x17", 17))
            {
                RareText = "BON Origin Shard x1",
            };
            var matcher = Matcher();
            var context = normalMode >= 2
                ? new LifetimeParsingContext(0, matcher.CatalogEntries.Select(
                    item => new LifetimeParsingCatalogEntry(item.Name, [])).ToArray())
                : null;
            ICompanionReconciliation normal = normalMode == 0
                ? new CompanionReconciliationAdapter(trackRows: true)
                : new LifetimeNormalReconciliationAdapter(context, normalMode == 3);
            using var analyzer = new CompanionLootFrameAnalyzer(visible, matcher, rows, new Names(rows),
                reconciliation: normal, rareRowPipeline: new RareRows(),
                captureGuard: new LootPanelCaptureGuard(visible, () => current));
            using var frame = new Bitmap(800, 600);
            using var recording = DiagnosticRecordingSession.Start(root, "hermesia");
            var events = new List<LootEventView>();
            var sample = 0;
            FrameAnalysisResult? latest = null;

            // Keep the injected OCR pipeline while the first recorded layout is
            // hidden. One empty batch leaves the live rare counter pristine;
            // the replay must create its own counter only on the first true flag.
            foreach (var phase in new (bool Enabled, int Frames)[] { (false, 10), (true, 10),
                         (false, disabledFrames), (true, 10) })
            {
                current = phase.Enabled ? visible : hidden;
                analyzer.ValidateCaptureSetup(frame.Size);
                for (var index = 0; index < phase.Frames; index++)
                {
                    var timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(sample++ * 20);
                    latest = await analyzer.AnalyzeAsync(frame, timestamp, CancellationToken.None);
                    events.AddRange(latest.NewEvents);
                    Assert.Equal(phase.Enabled, latest.RareBandRegion is not null);
                    recording.RecordFrame(timestamp, latest.Observations, latest.TrackingResult, frame,
                        latest.PanelRegion, latest.RareBandRegion,
                        recognitionVariant: latest.VariantName,
                        captureCalibration: latest.CaptureCalibration);
                    Assert.Null(recording.LastError);
                }
            }

            var completedAt = DateTimeOffset.UnixEpoch.AddMilliseconds(sample * 20);
            var completion = analyzer.CompleteSession(completedAt);
            events.AddRange(completion.NewEvents);
            recording.RecordCompletion(completedAt, completion.TrackingResult);
            recording.Dispose();
            Assert.Null(recording.LastError);

            var recordedFrames = File.ReadLines(recording.RecordingPath!).Skip(1)
                .Select(line => JsonSerializer.Deserialize<LootDiagnosticEntry>(line, LootDiagnosticFormat.JsonOptions)!)
                .Where(entry => entry.Kind == "frame").ToArray();
            Assert.Equal(new[] { false, true, false, true }, recordedFrames
                .Where((entry, index) => index == 0 || entry.RareEnabled != recordedFrames[index - 1].RareEnabled)
                .Select(entry => entry.RareEnabled));
            var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
            Assert.True(replay.TotalsMatch, replay.ToDisplayText());
            Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
            Assert.Equal(expectedRareCount, replay.Totals["BON Origin Shard"]);
            if (normalMode != 0) Assert.Equal(17, replay.Totals["Black Crystal Fragment"]);
            else Assert.True(replay.Totals["Black Crystal Fragment"] > 0);
            var directTotals = events.GroupBy(entry => entry.ItemName)
                .ToDictionary(group => group.Key, group => group.Sum(entry => (long)entry.Quantity));
            Assert.Equal(directTotals.OrderBy(entry => entry.Key), replay.Totals.OrderBy(entry => entry.Key));
            Assert.Equal(normalMode == 0 ? "row-tracks-v1" : normal.AlgorithmName, replay.NormalTrackingAlgorithm);
            Assert.NotNull(latest);
            Assert.True(analyzer.IsAvailable);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RareDiagnosticCounterStartsLazilyAndFlushesPendingRareDropsWhileDisabled()
    {
        var counter = new CompanionDiagnosticCounter([new("BON Origin Shard")]);
        var rare = new LootObservation(LootSource.Rare, 0, "BON Origin Shard x1",
            "BON Origin Shard", 1, 1, 0, null, null) { NativeY = 0 };
        for (var index = 0; index < 3; index++)
            Assert.Empty(counter.ProcessFrame(DateTimeOffset.UnixEpoch, [], false).NewEvents);
        for (var index = 0; index < 9; index++)
            Assert.Empty(counter.ProcessFrame(DateTimeOffset.UnixEpoch, [rare], true).NewEvents);

        // The first disabled capture completes the existing ten-frame batch.
        var pending = counter.ProcessFrame(DateTimeOffset.UnixEpoch, [], false);
        Assert.Equal(1, Assert.Single(pending.NewEvents).Quantity);
        Assert.Equal("BON Origin Shard", Assert.Single(pending.NewEvents).ItemName);

        // Re-enabling cannot reset the recent-item memory or replay the old drop.
        for (var index = 0; index < 9; index++)
            Assert.Empty(counter.ProcessFrame(DateTimeOffset.UnixEpoch, [rare], true).NewEvents);
        Assert.Empty(counter.ProcessFrame(DateTimeOffset.UnixEpoch, [], false).NewEvents);
        Assert.Empty(counter.CompleteSession(DateTimeOffset.UnixEpoch).NewEvents);
    }

    [Fact]
    public void RareDiagnosticCounterStillRejectsRareObservationsWhileDisabled()
    {
        var counter = new CompanionDiagnosticCounter([new("BON Origin Shard")]);
        var rare = new LootObservation(LootSource.Rare, 0, "BON Origin Shard x1",
            "BON Origin Shard", 1, 1, 0, null, null) { NativeY = 0 };

        Assert.Throws<InvalidDataException>(() =>
            counter.ProcessFrame(DateTimeOffset.UnixEpoch, [rare], false));
    }
}
