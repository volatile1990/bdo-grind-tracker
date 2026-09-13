using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class IndependentSpecialDiagnosticsTests : IDisposable
{
    private const string Ring = "Twilight Ring";
    private const string Earring = "Twilight Earring";
    private const string HistoricalVariant = "test+lifetime-v3+visual-occupancy-v1";
    private const string Variant = HistoricalVariant + "+independent-special-v1";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly LifetimeParsingContext Context = new(0, [new(Ring, []), new(Earring, [])]);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest-SpecialDiagnostics-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void IndependentSourceQuantitiesAndCountsRoundTripWithoutSuppressingTheSameItem()
    {
        var file = Record(Enumerable.Range(0, 3).Select(_ => new[] { Row(LootSource.Normal, Ring), Row(LootSource.Rare, Ring, 3) }));
        var replay = LootDiagnosticReplay.Run(file);
        Assert.True(replay.UsesCurrentEngine);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(4, replay.Totals[Ring]);
        Assert.Equal(2, ReadEntries(file)[^1].LootProjection!.ConfirmedDropCount);
    }

    [Fact]
    public void TwilightEarringFollowedByTwilightRingSurvivesRecordingAndReplay()
    {
        var file = Record(Enumerable.Range(0, 50).Select(frame => new[]
            { Row(LootSource.Rare, frame < 20 ? Earring : Ring) }));
        var replay = LootDiagnosticReplay.Run(file);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(1, replay.Totals[Earring]);
        Assert.Equal(1, replay.Totals[Ring]);
        Assert.Equal(2, ReadEntries(file)[^1].LootProjection!.ConfirmedDropCount);
    }

    [Fact]
    public void HistoricalVisualEnginePreservesItsOriginalSameItemPriority()
    {
        // One legacy rare batch, still shorter than a normal row's lifetime.
        var file = Record(Enumerable.Range(0, 10).Select(_ => new[]
            { Row(LootSource.Normal, Ring), Row(LootSource.Rare, Ring, 3) }), independent: false);
        var lines = File.ReadAllLines(file);
        var header = JsonNode.Parse(lines[0])!;
        header["engineVersion"] = LootDiagnosticFormat.LegacyVisualLifetimeEngineVersion;
        lines[0] = header.ToJsonString();
        File.WriteAllLines(file, lines);
        var replay = LootDiagnosticReplay.Run(file);
        Assert.False(replay.UsesCurrentEngine);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(1, replay.Totals[Ring]);
    }

    [Fact]
    public void RejectedSpecialRawRowsUseTheRecordedContextIncludingCompletionChanges()
    {
        const string alias = "Hidden treasure";
        var initial = new LifetimeParsingContext(0, [new(Ring, [alias]), new(Earring, [])]);
        var changed = new LifetimeParsingContext(1, [new(Ring, []), new(Earring, [alias])]);
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Ring), new(Earring)], parsingContext: initial, independentSpecial: true);
        for (var frame = 0; frame < 3; frame++)
        {
            var at = Start.AddMilliseconds(frame * 100);
            LootObservation[] rows = [new(LootSource.Rare, 0, alias + " x 1", null, null, .8, 0, null,
                "name-not-matched") { NativeY = 0 }];
            recording.RecordFrame(at, rows, counter.ProcessFrame(at, rows, true), bitmap, null, new(0, 0, 2, 2),
                recognitionVariant: Variant);
        }
        var completed = counter.CompleteSession(Start.AddMilliseconds(300), changed);
        recording.RecordCompletion(Start.AddMilliseconds(300), completed);
        recording.Dispose();
        Assert.Null(recording.LastError);
        Assert.Equal(1, completed.LootProjection!.Totals[Earring]);
        Assert.DoesNotContain(Ring, completed.LootProjection.Totals.Keys);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
    }

    [Theory]
    [InlineData("old-header")]
    [InlineData("removed-marker")]
    [InlineData("unknown-marker")]
    [InlineData("duplicate-marker")]
    [InlineData("historical-algorithm")]
    public void ReplayRejectsUnknownOrChangingSpecialCounterModes(string corruption)
    {
        var file = Record(Enumerable.Range(0, 3).Select(_ => new[] { Row(LootSource.Rare, Ring) }));
        var lines = File.ReadAllLines(file);
        var header = JsonNode.Parse(lines[0])!;
        var second = JsonNode.Parse(lines[2])!;
        switch (corruption)
        {
            case "old-header": header["engineVersion"] = LootDiagnosticFormat.LegacyVisualLifetimeEngineVersion; break;
            case "removed-marker": second["recognitionVariant"] = HistoricalVariant; break;
            case "unknown-marker": second["recognitionVariant"] = HistoricalVariant + "+independent-special-v2"; break;
            case "duplicate-marker": second["recognitionVariant"] = Variant + "+independent-special-v1"; break;
            case "historical-algorithm": second["recognitionVariant"] = "test+lifetime-v2+independent-special-v1"; break;
        }
        lines[0] = header.ToJsonString();
        lines[2] = second.ToJsonString();
        File.WriteAllLines(file, lines);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(file));
    }

    [Fact]
    public void RecorderPreservesSpecialModeWhilePanelIsHiddenAndRejectsModeChanges()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Ring), new(Earring)], parsingContext: Context, independentSpecial: true);
        var initial = counter.ProcessFrame(Start, [], false);
        recording.RecordFrame(Start, [], initial, bitmap, null, null, recognitionVariant: Variant);
        Assert.Null(recording.LastError);
        recording.RecordFrame(Start.AddMilliseconds(100), [], counter.ProcessFrame(Start.AddMilliseconds(100), [], false),
            bitmap, null, null, recognitionVariant: HistoricalVariant);
        Assert.NotNull(recording.LastError);
    }

    [Fact]
    public void SpecialRecoveryDiagnosticsAreRecordedIndependentlyOfNormalRecovery()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        recording.RecordFrame(Start, [], new([], []), bitmap, null, null,
            rareRecovery: NormalLootRecoveryDiagnostics.Empty);
        recording.Dispose();
        Assert.Null(recording.LastError);
        var frame = Assert.Single(ReadEntries(recording.RecordingPath!));
        Assert.Null(frame.Recovery);
        Assert.Equal(NormalLootRecoveryDiagnostics.Empty, frame.RareRecovery);
    }

    [Fact]
    public void TemporarilyUnavailableSpecialPanelDoesNotInventABlankPhaseOrAnotherDrop()
    {
        var counter = new CompanionDiagnosticCounter([new(Ring), new(Earring)],
            parsingContext: Context, independentSpecial: true);
        Assert.Equal(1, counter.ProcessFrame(Start, [Row(LootSource.Rare, Ring)], true).LootProjection!.Totals[Ring]);
        for (var frame = 1; frame <= 20; frame++)
            counter.ProcessFrame(Start.AddMilliseconds(frame * 200), [], false);
        var returned = counter.ProcessFrame(Start.AddSeconds(5), [Row(LootSource.Rare, Ring)], true);
        Assert.Equal(1, returned.LootProjection!.Totals[Ring]);
        Assert.Equal(1, returned.LootProjection.ConfirmedDropCount);
        Assert.Empty(returned.NewEvents);
    }

    private string Record(IEnumerable<LootObservation[]> frames, bool independent = true)
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Ring), new(Earring)], parsingContext: Context,
            visualLifetime: true, independentSpecial: independent);
        var frame = 0;
        foreach (var rows in frames)
        {
            var at = Start.AddMilliseconds(frame++ * 100);
            recording.RecordFrame(at, rows, counter.ProcessFrame(at, rows, true), bitmap, null, new(0, 0, 2, 2),
                recognitionVariant: independent ? Variant : HistoricalVariant);
        }
        var end = Start.AddMilliseconds(frame * 100);
        recording.RecordCompletion(end, counter.CompleteSession(end));
        recording.Dispose();
        Assert.Null(recording.LastError);
        return recording.RecordingPath!;
    }

    private static LootObservation Row(LootSource source, string name, int quantity = 1) =>
        new(source, 0, name + " x " + quantity, name, quantity, .99, .99, null, null) { NativeY = 0 };

    private static LootDiagnosticEntry[] ReadEntries(string path) => File.ReadLines(path).Skip(1)
        .Select(line => JsonSerializer.Deserialize<LootDiagnosticEntry>(line, LootDiagnosticFormat.JsonOptions)!).ToArray();

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
