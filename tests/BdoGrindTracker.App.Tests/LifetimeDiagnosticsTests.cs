using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimeDiagnosticsTests : IDisposable
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Variant = "test+lifetime-v1";
    private static readonly DateTimeOffset Start = new(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest-LifetimeDiagnostics-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(LootDiagnosticFormat.EngineVersion)]
    [InlineData(LootDiagnosticFormat.LegacyRawLifetimeEngineVersion)]
    [InlineData(LootDiagnosticFormat.LegacyLifetimeEngineVersion)]
    public void RecordedAcceptedFixtureProduces576AndRoundTripsItsCompleteProjections(string engine)
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Helmet), new("Black Stone"), new("Caphras Stone")], lifetime: true);
        var at = Start;
        var negativeChanges = 0;
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "lifetime", "recording1.txt"))
                     .Where(line => !line.StartsWith('#')))
        {
            var fields = line.Split('|');
            at = DateTimeOffset.Parse(fields[0], CultureInfo.InvariantCulture);
            var rows = fields[1].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value =>
            {
                var row = value.Split(',');
                var slot = int.Parse(row[0], CultureInfo.InvariantCulture);
                return new LootObservation(LootSource.Normal, slot, row[1] + " x " + row[2], row[1],
                    row[2] == "?" ? null : int.Parse(row[2], CultureInfo.InvariantCulture),
                    double.Parse(row[3], CultureInfo.InvariantCulture), 0, null, null)
                    { NativeY = 250 - slot * 50 };
            }).ToArray();
            var result = counter.ProcessFrame(at, rows, false);
            if (result.NewEvents.Any(change => change.Quantity < 0)) negativeChanges++;
            recording.RecordFrame(at, rows, result, bitmap, null, null, recognitionVariant: Variant);
        }
        var completed = counter.CompleteSession(at.AddMilliseconds(1));
        recording.RecordCompletion(at.AddMilliseconds(1), completed);
        recording.Dispose();
        var lines = File.ReadAllLines(recording.RecordingPath!);
        lines[0] = Serialize(Deserialize<LootDiagnosticHeader>(lines[0]) with { EngineVersion = engine });
        File.WriteAllLines(recording.RecordingPath!, lines);

        Assert.Null(recording.LastError);
        Assert.True(negativeChanges > 0);
        Assert.Equal(576, completed.LootProjection!.Totals[Helmet]);
        Assert.Equal(148, completed.LootProjection.ConfirmedDropCount);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal(LifetimeLootReconciler.AlgorithmName, replay.NormalTrackingAlgorithm);
        Assert.Equal(engine == LootDiagnosticFormat.EngineVersion, replay.UsesCurrentEngine);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(576, replay.Totals[Helmet]);
        Assert.Equal(3, replay.Totals["Black Stone"]);
        Assert.Equal(1, replay.Totals["Caphras Stone"]);
    }

    [Fact]
    public void RetractionAndRepeatedCompletionsKeepReplayAndAuditTotalsAtZero()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = Counter();
        recording.RecordCompletion(Start.AddMilliseconds(-1), counter.CompleteSession(Start.AddMilliseconds(-1)));
        for (var frame = 0; frame <= 20; frame++)
        {
            var at = Start.AddMilliseconds(frame * 200);
            LootObservation[] rows = frame == 0 ? [Row()] : [];
            recording.RecordFrame(at, rows, counter.ProcessFrame(at, rows, false), bitmap, null, null,
                recognitionVariant: Variant);
        }
        var completed = counter.CompleteSession(Start.AddSeconds(5));
        recording.RecordCompletion(Start.AddSeconds(5), completed);
        var repeated = counter.CompleteSession(Start.AddSeconds(6));
        recording.RecordCompletion(Start.AddSeconds(6), repeated);
        recording.SaveCountSummary(Guid.NewGuid(), Start.AddSeconds(6), TimeSpan.FromSeconds(6), new Dictionary<string, long>());
        recording.Dispose();

        Assert.Null(recording.LastError);
        Assert.Empty(completed.LootProjection!.Totals);
        Assert.Empty(repeated.NewEvents);
        Assert.Equal(completed.LootProjection.Revision, repeated.LootProjection!.Revision);
        Assert.Contains(Entries(recording.RecordingPath!).SelectMany(entry => entry.Events), entry => entry.Quantity < 0);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Empty(replay.Totals);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(recording.RecordingPath!)!, LootDiagnosticFormat.CountSummaryFileName)));
        Assert.Equal(0, summary.RootElement.GetProperty("projectionConfirmedDropCount").GetInt32());
        Assert.Empty(summary.RootElement.GetProperty("totals").EnumerateArray());
    }

    [Fact]
    public void AuditReplacesRepeatedSignedDeltaMessagesInsteadOfAddingThemTwice()
    {
        var audit = new LootCountAudit();
        var first = new TrackerFrameResult([new(Guid.NewGuid(), Start, Helmet, 4)], [])
            { LootProjection = new(1, new Dictionary<string, long> { [Helmet] = 4 }, 1, Start), NormalCaptureIndex = 1 };
        audit.Observe(first, null);
        audit.Observe(first, null);
        using (var snapshot = JsonDocument.Parse(Serialize(audit.Snapshot(Guid.NewGuid(), Start, TimeSpan.Zero,
                   new Dictionary<string, long> { [Helmet] = 4 }))))
            Assert.Equal(4, Assert.Single(snapshot.RootElement.GetProperty("totals").EnumerateArray()).GetProperty("recorded").GetInt64());
        var retract = new TrackerFrameResult([new(Guid.NewGuid(), Start.AddSeconds(1), Helmet, -4)], [])
            { LootProjection = new(2, new Dictionary<string, long>(), 0, Start), NormalCaptureIndex = 2 };
        audit.Observe(retract, null);
        audit.Observe(retract, null);
        using var final = JsonDocument.Parse(Serialize(audit.Snapshot(Guid.NewGuid(), Start, TimeSpan.Zero, new Dictionary<string, long>())));
        Assert.Empty(final.RootElement.GetProperty("totals").EnumerateArray());
        Assert.Equal(2, final.RootElement.GetProperty("projectionUpdates").GetInt32());
    }

    [Fact]
    public void NormalPriorityKeepsTheIndependentRareBalanceAvailableAfterARetraction()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = Counter();
        var rare = new LootObservation(LootSource.Rare, 0, Helmet + " x 2", Helmet, 2, 1, 1, null, null) { NativeY = 40 };
        var initial = counter.ProcessFrame(Start, [Row(), rare], true);
        recording.RecordFrame(Start, [Row(), rare], initial, bitmap, null, new Rectangle(0, 0, 2, 1), recognitionVariant: Variant);
        var flushed = counter.CompleteSession(Start.AddMilliseconds(1));
        recording.RecordCompletion(Start.AddMilliseconds(1), flushed);
        Assert.Equal(4, flushed.LootProjection!.Totals[Helmet]); // Normal wins; channels are not summed to six.
        for (var frame = 1; frame <= 20; frame++)
        {
            var at = Start.AddMilliseconds(frame * 200);
            recording.RecordFrame(at, [], counter.ProcessFrame(at, [], true), bitmap, null,
                new Rectangle(0, 0, 2, 1), recognitionVariant: Variant);
        }
        var final = counter.CompleteSession(Start.AddSeconds(5));
        recording.RecordCompletion(Start.AddSeconds(5), final);
        recording.Dispose();
        Assert.Null(recording.LastError);
        Assert.Equal(2, final.LootProjection!.Totals[Helmet]); // The retracted normal estimate reveals the rare account.
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal(2, replay.Totals[Helmet]);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
    }

    [Theory]
    [InlineData("grindcrest-temporal-v1", true, "test+temporal-v1")]
    [InlineData("grindcrest-temporal-v2", false, "test+temporal-v2+visual-appearance-v1")]
    public void FormatTwoTemporalHeadersKeepTheirOriginalCounterAndEventMetadata(string engine, bool legacy, string variant)
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Helmet)], temporal: true, legacyTemporal: legacy);
        for (var frame = 0; frame < 4; frame++)
        {
            var at = Start.AddMilliseconds(frame * 200);
            recording.RecordFrame(at, [Row()], counter.ProcessFrame(at, [Row()], false), bitmap, null, null, recognitionVariant: variant);
        }
        recording.RecordCompletion(Start.AddSeconds(1), counter.CompleteSession(Start.AddSeconds(1)));
        recording.Dispose();
        var lines = File.ReadAllLines(recording.RecordingPath!);
        lines[0] = Serialize(Deserialize<LootDiagnosticHeader>(lines[0]) with { FormatVersion = 2, EngineVersion = engine });
        File.WriteAllLines(recording.RecordingPath!, lines);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.False(replay.UsesCurrentEngine);
        Assert.Equal(legacy ? TemporalLootReconciler.LegacyAlgorithmName : TemporalLootReconciler.AlgorithmName, replay.NormalTrackingAlgorithm);
        Assert.Equal(4, replay.Totals[Helmet]);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
    }

    [Fact]
    public void LifetimeAuditDeltaIdentityIsComparedEvenWithoutLegacyDropRevisionFields()
    {
        var file = SingleLifetimeFrame();
        var lines = File.ReadAllLines(file);
        var frame = Deserialize<LootDiagnosticEntry>(lines[1]);
        Assert.Null(Assert.Single(frame.Events).TotalDropQuantity);
        lines[1] = Serialize(frame with { Events = frame.Events.Select(change => change with { EventId = Guid.NewGuid() }).ToArray() });
        File.WriteAllLines(file, lines);
        var replay = LootDiagnosticReplay.Run(file);
        Assert.True(replay.TotalsMatch);
        Assert.False(replay.EventTimelineMatches);
        Assert.Equal(1, replay.FirstDifferentSequence);
    }

    [Theory]
    [InlineData("missing-projection")]
    [InlineData("wrong-mode")]
    [InlineData("old-format")]
    [InlineData("old-engine")]
    [InlineData("negative-total")]
    [InlineData("overflow-totals")]
    public void ReplayRejectsMalformedOrHistoricallyImpossibleProjections(string corruption)
    {
        var file = SingleLifetimeFrame();
        var lines = File.ReadAllLines(file);
        var header = JsonNode.Parse(lines[0])!;
        var frame = JsonNode.Parse(lines[1])!;
        switch (corruption)
        {
            case "missing-projection": frame["lootProjection"] = null; break;
            case "wrong-mode": frame["recognitionVariant"] = "test+temporal-v2+visual-appearance-v1"; break;
            case "old-format": header["formatVersion"] = 2; break;
            case "old-engine": header["engineVersion"] = LootDiagnosticFormat.LegacyVisualTemporalEngineVersion; break;
            case "negative-total": frame["lootProjection"]!["totals"]![Helmet] = -1; break;
            case "overflow-totals":
                frame["lootProjection"]!["totals"]![Helmet] = long.MaxValue;
                frame["lootProjection"]!["totals"]!["Black Stone"] = 1;
                break;
        }
        File.WriteAllLines(file, [header.ToJsonString(), frame.ToJsonString()]);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(file));
    }

    [Fact]
    public void ARepeatedProjectionRevisionCannotCarryDifferentTotals()
    {
        var file = SingleLifetimeFrame();
        var lines = File.ReadAllLines(file);
        var first = Deserialize<LootDiagnosticEntry>(lines[1]);
        var completion = first with
        {
            Kind = "complete", Sequence = 2, Timestamp = Start.AddSeconds(1), Observations = [], Events = [],
            LootProjection = new(first.LootProjection!.Revision, new Dictionary<string, long> { [Helmet] = 8 }, 2, Start),
        };
        File.AppendAllLines(file, [Serialize(completion)]);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(file));
    }

    private string SingleLifetimeFrame()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = Counter();
        recording.RecordFrame(Start, [Row()], counter.ProcessFrame(Start, [Row()], false), bitmap, null, null,
            recognitionVariant: Variant);
        recording.Dispose();
        Assert.Null(recording.LastError);
        return recording.RecordingPath!;
    }

    private static CompanionDiagnosticCounter Counter() => new([new(Helmet)], lifetime: true);
    private static LootObservation Row() => new(LootSource.Normal, 0, Helmet + " x 4", Helmet, 4, 1, 1, null, null) { NativeY = 250 };
    private static IEnumerable<LootDiagnosticEntry> Entries(string file) => File.ReadAllLines(file).Skip(1).Select(Deserialize<LootDiagnosticEntry>);
    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, LootDiagnosticFormat.JsonOptions)!;
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, LootDiagnosticFormat.JsonOptions);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
