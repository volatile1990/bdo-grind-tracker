using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class SourcePolicyDiagnosticsTests : IDisposable
{
    private const string Vestige = "Broken Vestige of Everlight";
    private const string RecordedItem = "Recorded Source Treasure";
    private const string RecordedAlias = "Recorded localized treasure";
    private const string Variant = "test+lifetime-v5+visual-occupancy-v2+visual-fade-v1+independent-special-v1";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest-SourcePolicyReplay-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(LootSource.Normal, LootSource.Normal, false)]
    [InlineData(LootSource.Normal, LootSource.Rare, false)]
    [InlineData(LootSource.Rare, LootSource.Normal, false)]
    [InlineData(LootSource.Rare, LootSource.Rare, false)]
    [InlineData(LootSource.Normal, LootSource.Normal, true)]
    [InlineData(LootSource.Normal, LootSource.Rare, true)]
    [InlineData(LootSource.Rare, LootSource.Normal, true)]
    [InlineData(LootSource.Rare, LootSource.Rare, true)]
    public void EmbeddedSourceAssignmentRoundTripsAndFiltersNativeAndRawRows(
        LootSource allowed, LootSource observed, bool rawOnly)
    {
        // This identity and alias do not exist in the installed catalog. Replay
        // must use only the policy and aliases embedded in this recording.
        var context = new LifetimeParsingContext(0,
            [new(RecordedItem, [RecordedAlias], allowedSource: allowed)]);
        var row = Row(observed, RecordedItem, 3) with
        {
            RawText = RecordedAlias + " x 3",
            ItemName = rawOnly ? null : RecordedItem,
            Quantity = rawOnly ? null : 3,
            RejectionReason = rawOnly ? "name-not-matched" : null,
        };
        var file = Record(context, Enumerable.Repeat(new[] { row }, 3));
        var entries = ReadEntries(file);
        var saved = Assert.Single(entries, entry => entry.LifetimeParsingContext is not null).LifetimeParsingContext!;
        Assert.Equal(allowed, Assert.Single(saved.Catalog).AllowedSource);
        var replay = LootDiagnosticReplay.Run(file);
        Assert.True(replay.UsesCurrentEngine);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(allowed == observed ? 3 : 0, replay.Totals.GetValueOrDefault(RecordedItem));
    }

    [Theory]
    [InlineData("test+lifetime-v2", false, false, false)]
    [InlineData("test+lifetime-v3+visual-occupancy-v1", false, false, false)]
    [InlineData("test+lifetime-v3+visual-occupancy-v1+independent-special-v1", true, false, false)]
    [InlineData("test+lifetime-v4+visual-occupancy-v2", false, true, false)]
    [InlineData("test+lifetime-v4+visual-occupancy-v2+independent-special-v1", true, true, false)]
    [InlineData("test+lifetime-v5+visual-occupancy-v2+visual-fade-v1", false, true, true)]
    [InlineData(Variant, true, true, true)]
    public void VersionSevenWithoutSourceMetadataKeepsItsRecordedNormalCount(
        string variant, bool independent, bool unreadable, bool fade)
    {
        // Before source restrictions, even a rare-only item could count in the
        // normal log. Historical replay must reproduce that recorded result.
        var context = new LifetimeParsingContext(0, [new(Vestige, [], true)]);
        var file = Record(context, Enumerable.Repeat(new[] { Row(LootSource.Normal, Vestige, 1) }, 3),
            variant, independent, unreadable, fade);
        RewriteAsVersionSevenWithoutSourceFields(file);
        var replay = LootDiagnosticReplay.Run(file);
        Assert.False(replay.UsesCurrentEngine);
        Assert.Equal(LootDiagnosticFormat.LegacyStableSpecialEngineVersion, replay.RecordingEngineVersion);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(1, replay.Totals[Vestige]);
    }

    [Fact]
    public void VersionSevenKeepsStableLearningForShortSpecialNotifications()
    {
        const string warmup = "Deboreka Necklace";
        const string earring = "Twilight of the End - Earring";
        const string ring = "Twilight of the End - Ring";
        var context = new LifetimeParsingContext(0, [new(warmup, []), new(earring, []), new(ring, [])]);
        var frames = Enumerable.Repeat(warmup, 50).Concat([earring]).Concat(Enumerable.Repeat(ring, 50))
            .Select(item => new[] { Row(LootSource.Rare, item, 1) });
        var file = Record(context, frames);
        Assert.Equal(1, ReadEntries(file)[^1].LootProjection!.Totals[earring]);
        RewriteAsVersionSevenWithoutSourceFields(file);
        var replay = LootDiagnosticReplay.Run(file);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(1, replay.Totals[earring]);
        Assert.Equal(1, replay.Totals[warmup]);
        Assert.Equal(1, replay.Totals[ring]);
    }

    [Fact]
    public void ChangedSourceAssignmentIsRecordedAndReplayedAtCompletion()
    {
        var initial = new LifetimeParsingContext(0, [new(RecordedItem, [], allowedSource: LootSource.Normal)]);
        var changed = new LifetimeParsingContext(1, [new(RecordedItem, [], allowedSource: LootSource.Rare)]);
        var file = Record(initial, Enumerable.Repeat(new[] { Row(LootSource.Normal, RecordedItem, 3) }, 3),
            completedContext: changed);
        var entries = ReadEntries(file);
        Assert.Equal(2, entries.Count(entry => entry.LifetimeParsingContext is not null));
        Assert.Equal(LootSource.Rare, Assert.Single(entries[^1].LifetimeParsingContext!.Catalog).AllowedSource);
        Assert.Equal(3, entries[0].LootProjection!.Totals[RecordedItem]);
        Assert.Empty(entries[^1].LootProjection!.Totals);
        var replay = LootDiagnosticReplay.Run(file);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Empty(replay.Totals);
    }

    [Theory]
    [InlineData("old-engine")]
    [InlineData("invalid-source")]
    [InlineData("same-revision-different-source")]
    public void ReplayRejectsInvalidOrContradictorySourceMetadata(string corruption)
    {
        var context = new LifetimeParsingContext(0, [new(RecordedItem, [], allowedSource: LootSource.Normal)]);
        var file = Record(context, Enumerable.Repeat(new[] { Row(LootSource.Normal, RecordedItem, 3) }, 3));
        var lines = File.ReadAllLines(file);
        var header = JsonNode.Parse(lines[0])!;
        var first = JsonNode.Parse(lines[1])!;
        var second = JsonNode.Parse(lines[2])!;
        switch (corruption)
        {
            case "old-engine":
                header["engineVersion"] = LootDiagnosticFormat.LegacyStableSpecialEngineVersion;
                break;
            case "invalid-source":
                first["lifetimeParsingContext"]!["catalog"]![0]!["allowedSource"] = "Unavailable";
                break;
            case "same-revision-different-source":
                second["lifetimeParsingContext"] = first["lifetimeParsingContext"]!.DeepClone();
                second["lifetimeParsingContext"]!["catalog"]![0]!["allowedSource"] = "Rare";
                break;
        }
        lines[0] = header.ToJsonString();
        lines[1] = first.ToJsonString();
        lines[2] = second.ToJsonString();
        File.WriteAllLines(file, lines);
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(file));
    }

    private string Record(LifetimeParsingContext context, IEnumerable<LootObservation[]> frames,
        string variant = Variant, bool independent = true, bool unreadable = true, bool fade = true,
        LifetimeParsingContext? completedContext = null)
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter(context.Catalog.Select(item => new CompanionRareCatalogEntry(item.Name)).ToArray(),
            rawLifetime: true, parsingContext: context, visualLifetime: !variant.Contains("lifetime-v2", StringComparison.Ordinal),
            independentSpecial: independent, unreadableVisualLifetime: unreadable, useFadeEvidence: fade);
        var index = 0;
        foreach (var rows in frames)
        {
            var at = Start.AddMilliseconds(index++ * 200);
            recording.RecordFrame(at, rows, counter.ProcessFrame(at, rows, independent), bitmap, null,
                independent ? new Rectangle(0, 0, 2, 2) : null, recognitionVariant: variant);
        }
        var end = Start.AddMilliseconds(index * 200);
        recording.RecordCompletion(end, counter.CompleteSession(end, completedContext));
        recording.Dispose();
        Assert.Null(recording.LastError);
        return recording.RecordingPath!;
    }

    private static LootObservation Row(LootSource source, string item, int quantity) =>
        new(source, 0, item + " x " + quantity, item, quantity, 1, 1, null, null) { NativeY = 0 };

    private static LootDiagnosticEntry[] ReadEntries(string path) => File.ReadLines(path).Skip(1)
        .Select(line => JsonSerializer.Deserialize<LootDiagnosticEntry>(line, LootDiagnosticFormat.JsonOptions)!).ToArray();

    private static void RewriteAsVersionSevenWithoutSourceFields(string path)
    {
        var lines = File.ReadAllLines(path);
        var header = JsonNode.Parse(lines[0])!;
        header["engineVersion"] = LootDiagnosticFormat.LegacyStableSpecialEngineVersion;
        lines[0] = header.ToJsonString();
        for (var index = 1; index < lines.Length; index++)
        {
            var entry = JsonNode.Parse(lines[index])!;
            if (entry["lifetimeParsingContext"] is { } context)
                foreach (var item in context["catalog"]!.AsArray()) item!.AsObject().Remove("allowedSource");
            lines[index] = entry.ToJsonString();
        }
        File.WriteAllLines(path, lines);
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(directory);
        var allowed = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Grindcrest-SourcePolicyReplay-");
        if (!resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
