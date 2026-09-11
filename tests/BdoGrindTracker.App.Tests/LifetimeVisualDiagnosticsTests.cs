using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimeVisualDiagnosticsTests : IDisposable
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Variant = "test+lifetime-v3+visual-occupancy-v1";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly LifetimeParsingContext Context = new(0, [new(Helmet, ["Helmreste"])]);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest-VisualDiagnostics-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AdapterSelectsVisualModeExplicitlyAndKeepsHistoricalConstructors()
    {
        Assert.Equal("lifetime-v1", new LifetimeNormalReconciliationAdapter().AlgorithmName);
        Assert.Equal("lifetime-v2", new LifetimeNormalReconciliationAdapter(Context).AlgorithmName);
        var visual = new LifetimeNormalReconciliationAdapter(Context, true);
        Assert.Equal("lifetime-v3", visual.AlgorithmName);
        Assert.True(visual.UsesRawText);
        visual.Reset();
        Assert.Equal("lifetime-v3", visual.AlgorithmName);
        Assert.Throws<ArgumentException>(() => new LifetimeNormalReconciliationAdapter(null, true));
    }

    [Fact]
    public void OnlyVisualTraceIncludesTheCoverageFallbackCount()
    {
        foreach (var visual in new[] { false, true })
        {
            var adapter = new LifetimeNormalReconciliationAdapter(Context, visual);
            adapter.ProcessObservations([Row(false)], Start);
            Assert.All(Assert.Single(adapter.LastTrace).OverlapAttempts, attempt =>
            {
                if (visual) Assert.EndsWith(";coverage-fallbacks:0", attempt.Reason);
                else Assert.DoesNotContain("coverage-fallbacks:", attempt.Reason);
            });
        }
    }

    [Fact]
    public void OccupancyAndRawContextRoundTripWithExactProjectionAndEventTimeline()
    {
        var file = RecordFrames(visual: true);
        var entries = File.ReadLines(file).Skip(1)
            .Select(line => JsonSerializer.Deserialize<LootDiagnosticEntry>(line, LootDiagnosticFormat.JsonOptions)!).ToArray();
        Assert.Null(entries[0].Observations[0].OccupancyEvidence);
        Assert.Equal(.96, Assert.Single(entries[1].Observations[0].OccupancyEvidence!.Matches).Correlation);
        Assert.Single(entries, entry => entry.LifetimeParsingContext is not null);
        var replay = LootDiagnosticReplay.Run(file);
        Assert.Equal("grindcrest-lifetime-v3", replay.RecordingEngineVersion);
        Assert.Equal("lifetime-v3", replay.NormalTrackingAlgorithm);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(4, replay.Totals[Helmet]);
    }

    [Fact]
    public void HistoricalRawEngineHeaderKeepsExactV2Replay()
    {
        var file = RecordFrames(visual: false);
        var lines = File.ReadAllLines(file);
        var header = JsonNode.Parse(lines[0])!;
        header["engineVersion"] = "grindcrest-lifetime-v2";
        lines[0] = header.ToJsonString();
        File.WriteAllLines(file, lines);
        var replay = LootDiagnosticReplay.Run(file);
        Assert.False(replay.UsesCurrentEngine);
        Assert.Equal("lifetime-v2", replay.NormalTrackingAlgorithm);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
    }

    [Theory]
    [InlineData("old-header")]
    [InlineData("missing-marker")]
    [InlineData("old-fade-marker")]
    [InlineData("old-mode")]
    [InlineData("missing-context")]
    [InlineData("unknown-marker")]
    [InlineData("mixed-algorithms")]
    [InlineData("invalid-match")]
    [InlineData("rare-evidence")]
    [InlineData("rejected-evidence")]
    public void ReplayRejectsMislabelledOrInvalidOccupancy(string corruption)
    {
        var file = RecordFrames(visual: true);
        var lines = File.ReadAllLines(file);
        var header = JsonNode.Parse(lines[0])!;
        var first = JsonNode.Parse(lines[1])!;
        var second = JsonNode.Parse(lines[2])!;
        switch (corruption)
        {
            case "old-header": header["engineVersion"] = LootDiagnosticFormat.LegacyRawLifetimeEngineVersion; break;
            case "missing-marker": first["recognitionVariant"] = "test+lifetime-v3"; break;
            case "old-fade-marker": first["recognitionVariant"] = Variant + "+visual-appearance-v1"; break;
            case "old-mode":
                first["recognitionVariant"] = "test+lifetime-v2";
                second["recognitionVariant"] = "test+lifetime-v2";
                break;
            case "missing-context": first["lifetimeParsingContext"] = null; break;
            case "unknown-marker": first["recognitionVariant"] = Variant + "+visual-occupancy-v2"; break;
            case "mixed-algorithms": first["recognitionVariant"] = Variant + "+temporal-v2"; break;
            case "invalid-match": second["observations"]![0]!["occupancyEvidence"]!["matches"]![0]!["correlation"] = 1.1; break;
            case "rare-evidence": second["observations"]![0]!["source"] = "Rare"; break;
            case "rejected-evidence": second["observations"]![0]!["rejectionReason"] = "ocr-geometry"; break;
        }
        lines[0] = header.ToJsonString();
        lines[1] = first.ToJsonString();
        lines[2] = second.ToJsonString();
        File.WriteAllLines(file, lines);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(file));
    }

    [Theory]
    [InlineData("test+lifetime-v3")]
    [InlineData("test+lifetime-v2+visual-occupancy-v1")]
    [InlineData("test+visual-occupancy-v1")]
    public void RecorderRefusesInconsistentOccupancyModeEvenWithoutMeasurements(string variant)
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        recording.RecordFrame(Start, [], new([], []), bitmap, null, null, recognitionVariant: variant);
        Assert.NotNull(recording.LastError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalCountersRefuseNewOccupancy(bool raw)
    {
        var counter = new CompanionDiagnosticCounter([new(Helmet)], lifetime: !raw,
            rawLifetime: raw, parsingContext: raw ? Context : null);
        Assert.Throws<InvalidDataException>(() => counter.ProcessFrame(Start, [Row(true)], false));
    }

    [Fact]
    public void SixthPhysicalSlotRemainsValidDiagnosticEvidenceWithoutBecomingACountedSlot()
    {
        var row = Row(true) with { Slot = 5, NativeY = 0, Quantity = 999,
            OccupancyEvidence = new([new(5, .99)]) };
        DiagnosticRecordingSession.ValidateObservations([row]);
        var counter = new CompanionDiagnosticCounter([new(Helmet)], parsingContext: Context, visualLifetime: true);
        var result = counter.ProcessFrame(Start, [row], false);
        Assert.Empty(result.LootProjection!.Totals);
    }

    private string RecordFrames(bool visual)
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Helmet)], rawLifetime: true,
            parsingContext: Context, visualLifetime: visual);
        for (var frame = 0; frame < 3; frame++)
        {
            var at = Start.AddMilliseconds(frame * 200);
            var rows = new[] { Row(visual && frame > 0) };
            recording.RecordFrame(at, rows, counter.ProcessFrame(at, rows, false), bitmap, null, null,
                recognitionVariant: visual ? Variant : "test+lifetime-v2");
        }
        recording.RecordCompletion(Start.AddMilliseconds(500), counter.CompleteSession(Start.AddMilliseconds(500)));
        recording.Dispose();
        Assert.Null(recording.LastError);
        return recording.RecordingPath!;
    }

    private static LootObservation Row(bool evidence) => new(LootSource.Normal, 0, "Helmreste x4", Helmet,
        4, .99, .99, null, null)
    {
        NativeY = 250,
        OccupancyEvidence = evidence ? new([new(0, .96)]) : null,
    };

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
