using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LifetimeRawDiagnosticsTests : IDisposable
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Stone = "Black Stone";
    private const string Alias = "Verborgene Helmreste";
    private const string Variant = "test+lifetime-v2";
    private static readonly DateTimeOffset Start = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest-RawDiagnostics-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RejectedRawRowsAndRecordedAliasesRoundTripWithoutInstalledCatalogLookups()
    {
        var file = RecordRawFrames();
        var frames = ReadEntries(file);
        Assert.All(frames.Where(entry => entry.Kind == "frame"), entry =>
        {
            var observation = Assert.Single(entry.Observations);
            Assert.Null(observation.ItemName);
            Assert.NotNull(observation.RejectionReason);
            Assert.Equal(4, entry.LootProjection!.Totals[Helmet]);
        });
        Assert.Single(frames, entry => entry.LifetimeParsingContext is not null);
        Assert.Equal(Alias, Assert.Single(Assert.Single(frames[0].LifetimeParsingContext!.Catalog).Aliases));
        var replay = LootDiagnosticReplay.Run(file);
        Assert.True(replay.UsesCurrentEngine);
        Assert.Equal(LifetimeLootReconciler.RawTextAlgorithmName, replay.NormalTrackingAlgorithm);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(4, replay.Totals[Helmet]);
    }

    [Fact]
    public void ContextRevisionChangeReinterpretsRetainedRawRowsAndReplaysAtCompletion()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var initial = new LifetimeParsingContext(0, [new(Helmet, [Alias]), new(Stone, [])]);
        var changed = new LifetimeParsingContext(1, [new(Helmet, []), new(Stone, [Alias])]);
        var counter = new CompanionDiagnosticCounter([new(Helmet), new(Stone)], rawLifetime: true, parsingContext: initial);
        for (var frame = 0; frame < 3; frame++)
        {
            var at = Start.AddMilliseconds(frame * 200);
            recording.RecordFrame(at, [RawRow()], counter.ProcessFrame(at, [RawRow()], false), bitmap, null, null,
                recognitionVariant: Variant);
        }
        var completed = counter.CompleteSession(Start.AddMilliseconds(500), changed);
        recording.RecordCompletion(Start.AddMilliseconds(500), completed);
        recording.Dispose();
        Assert.Null(recording.LastError);
        Assert.Equal(4, completed.LootProjection!.Totals[Stone]);
        Assert.False(completed.LootProjection.Totals.ContainsKey(Helmet));
        var entries = ReadEntries(recording.RecordingPath!);
        Assert.Equal(2, entries.Count(entry => entry.LifetimeParsingContext is not null));
        Assert.Equal(1, entries[^1].LifetimeParsingContext!.Revision);
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
    }

    [Theory]
    [InlineData("missing-context")]
    [InlineData("legacy-engine")]
    [InlineData("legacy-mode")]
    [InlineData("conflicting-revision")]
    [InlineData("backward-revision")]
    [InlineData("duplicate-name")]
    [InlineData("null-alias")]
    public void ReplayRejectsMissingOrContradictoryRawParsingContext(string corruption)
    {
        var file = RecordRawFrames();
        var lines = File.ReadAllLines(file);
        var header = JsonNode.Parse(lines[0])!;
        var first = JsonNode.Parse(lines[1])!;
        var second = JsonNode.Parse(lines[2])!;
        switch (corruption)
        {
            case "missing-context": first["lifetimeParsingContext"] = null; break;
            case "legacy-engine": header["engineVersion"] = LootDiagnosticFormat.LegacyLifetimeEngineVersion; break;
            case "legacy-mode": first["recognitionVariant"] = "test+lifetime-v1"; break;
            case "conflicting-revision":
                second["lifetimeParsingContext"] = first["lifetimeParsingContext"]!.DeepClone();
                second["lifetimeParsingContext"]!["catalog"]![0]!["aliases"]![0] = "Changed alias";
                break;
            case "backward-revision":
                first["lifetimeParsingContext"]!["revision"] = 1;
                second["lifetimeParsingContext"] = first["lifetimeParsingContext"]!.DeepClone();
                second["lifetimeParsingContext"]!["revision"] = 0;
                break;
            case "duplicate-name":
                var catalog = first["lifetimeParsingContext"]!["catalog"]!.AsArray();
                catalog.Add(catalog[0]!.DeepClone());
                break;
            case "null-alias": first["lifetimeParsingContext"]!["catalog"]![0]!["aliases"]![0] = null; break;
        }
        lines[0] = header.ToJsonString();
        lines[1] = first.ToJsonString();
        lines[2] = second.ToJsonString();
        File.WriteAllLines(file, lines);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(file));
    }

    [Fact]
    public void ParsingContextSnapshotsCallerCollectionsAndRejectsDuplicateCanonicalNames()
    {
        var aliases = new List<string> { Alias };
        var catalog = new List<LifetimeParsingCatalogEntry> { new(Helmet, aliases) };
        var context = new LifetimeParsingContext(0, catalog);
        aliases[0] = "Changed";
        catalog.Clear();
        Assert.Equal(Alias, Assert.Single(Assert.Single(context.Catalog).Aliases));
        Assert.Throws<ArgumentException>(() => new LifetimeParsingContext(0, [new(Helmet, []), new(Helmet, [])]));
    }

    private string RecordRawFrames()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var context = new LifetimeParsingContext(0, [new(Helmet, [Alias])]);
        var counter = new CompanionDiagnosticCounter([new(Helmet)], rawLifetime: true, parsingContext: context);
        for (var frame = 0; frame < 3; frame++)
        {
            var at = Start.AddMilliseconds(frame * 200);
            recording.RecordFrame(at, [RawRow()], counter.ProcessFrame(at, [RawRow()], false), bitmap, null, null,
                recognitionVariant: Variant);
        }
        recording.RecordCompletion(Start.AddMilliseconds(500), counter.CompleteSession(Start.AddMilliseconds(500)));
        recording.Dispose();
        Assert.Null(recording.LastError);
        return recording.RecordingPath!;
    }

    private static LootObservation RawRow() => new(LootSource.Normal, 0, Alias + " x 4", null, null, 0.8, 0, null,
        "name-not-matched") { NativeY = 250 };
    private static LootDiagnosticEntry[] ReadEntries(string path) => File.ReadLines(path).Skip(1)
        .Select(line => JsonSerializer.Deserialize<LootDiagnosticEntry>(line, LootDiagnosticFormat.JsonOptions)!).ToArray();
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
