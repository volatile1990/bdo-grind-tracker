using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class UnreadableVisualDiagnosticsTests : IDisposable
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string HistoricalVariant = "test+lifetime-v3+visual-occupancy-v1";
    private const string Variant = "test+lifetime-v4+visual-occupancy-v2";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly LifetimeParsingContext Context = new(0, [new(Helmet, [])]);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest-UnreadableDiagnostics-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AdapterRequiresExplicitOptInAndResetPreservesTheSelectedMode()
    {
        Assert.Equal("lifetime-v3", new LifetimeNormalReconciliationAdapter(Context, true).AlgorithmName);
        var adapter = new LifetimeNormalReconciliationAdapter(Context, true, useUnreadableSlotCoverage: true);
        Assert.Equal("lifetime-v4", adapter.AlgorithmName);
        adapter.ProcessObservations([Known()], Start);
        adapter.Reset();
        Assert.Equal("lifetime-v4", adapter.AlgorithmName);
        Assert.Throws<ArgumentException>(() => new LifetimeNormalReconciliationAdapter(Context, false,
            useUnreadableSlotCoverage: true));
    }

    [Fact]
    public void UnreadableEvidenceRoundTripsWithoutInventingAnOcrNameOrAmount()
    {
        var path = Record(includeUnreadable: true);
        var frames = ReadEntries(path).Where(entry => entry.Kind == "frame").ToArray();
        var unknown = Assert.Single(frames[1].Observations, row => row.Slot == 1);
        Assert.Null(unknown.ItemName);
        Assert.Null(unknown.Quantity);
        Assert.Equal("Elioti", unknown.RawText);
        Assert.Equal("native-catalog-miss", unknown.RejectionReason);
        Assert.Equal(.96, Assert.Single(unknown.OccupancyEvidence!.Matches).Correlation);
        var replay = LootDiagnosticReplay.Run(path);
        Assert.True(replay.UsesCurrentEngine);
        Assert.Equal("lifetime-v4", replay.NormalTrackingAlgorithm);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());

        var emptyCounter = new CompanionDiagnosticCounter([new(Helmet)], parsingContext: Context,
            unreadableVisualLifetime: true);
        var empty = emptyCounter.ProcessFrame(Start, [Unreadable() with { Slot = 0, NativeY = 250 }], false);
        Assert.Empty(empty.LootProjection!.Totals);
    }

    [Theory]
    [InlineData(LootDiagnosticFormat.LegacyVisualLifetimeEngineVersion, false)]
    [InlineData(LootDiagnosticFormat.LegacyIndependentSpecialEngineVersion, false)]
    [InlineData(LootDiagnosticFormat.LegacyIndependentSpecialEngineVersion, true)]
    public void HistoricalVisualAndIndependentSpecialRecordingsKeepTheirOriginalCounter(string engine, bool independent)
    {
        var path = Record(unreadableMode: false, independent: independent);
        Rewrite(path, (header, _) => header["engineVersion"] = engine);
        var replay = LootDiagnosticReplay.Run(path);
        Assert.False(replay.UsesCurrentEngine);
        Assert.Equal("lifetime-v3", replay.NormalTrackingAlgorithm);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
    }

    [Theory]
    [InlineData("old-engine")]
    [InlineData("old-occupancy-marker")]
    [InlineData("old-algorithm")]
    [InlineData("both-occupancy-markers")]
    [InlineData("changed-mode")]
    public void ReplayRejectsMislabelledOrChangingUnreadableCounterModes(string corruption)
    {
        var path = Record();
        Rewrite(path, (header, entries) =>
        {
            switch (corruption)
            {
                case "old-engine": header["engineVersion"] = LootDiagnosticFormat.LegacyIndependentSpecialEngineVersion; break;
                case "old-occupancy-marker": entries[0]["recognitionVariant"] = "test+lifetime-v4+visual-occupancy-v1"; break;
                case "old-algorithm": entries[0]["recognitionVariant"] = "test+lifetime-v3+visual-occupancy-v2"; break;
                case "both-occupancy-markers": entries[0]["recognitionVariant"] = Variant + "+visual-occupancy-v1"; break;
                case "changed-mode": entries[1]["recognitionVariant"] = HistoricalVariant; break;
            }
        });
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(path));
    }

    [Fact]
    public void HistoricalModeCannotReplayUnreadableOccupancyEvenUnderTheCurrentHeader()
    {
        var path = Record(includeUnreadable: true);
        Rewrite(path, (_, entries) =>
        {
            foreach (var entry in entries.Where(entry => entry["kind"]!.GetValue<string>() == "frame"))
                entry["recognitionVariant"] = HistoricalVariant;
        });
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(path));
    }

    [Theory]
    [InlineData("rare")]
    [InlineData("anchor")]
    [InlineData("outside-pool")]
    [InlineData("known-rejected")]
    [InlineData("missing-geometry")]
    public void UnreadableModeStillRejectsIneligibleOccupancy(string corruption)
    {
        var row = corruption switch
        {
            "rare" => Unreadable() with { Source = LootSource.Rare },
            "anchor" => Unreadable() with { IsAlignmentAnchor = true },
            "outside-pool" => Unreadable() with { RejectionReason = AutomaticLootSpotLock.OutsideSpotPoolReason },
            "known-rejected" => Unreadable() with { ItemName = Helmet },
            "missing-geometry" => Unreadable() with { NativeY = null },
            _ => throw new InvalidOperationException(),
        };
        Assert.Throws<InvalidDataException>(() => DiagnosticRecordingSession.ValidateObservations([row], true));
    }

    [Fact]
    public void RecorderRefusesChangingVisualCounterMode()
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Helmet)], parsingContext: Context, unreadableVisualLifetime: true);
        recording.RecordFrame(Start, [Known()], counter.ProcessFrame(Start, [Known()], false), bitmap, null, null,
            recognitionVariant: Variant);
        Assert.Null(recording.LastError);
        recording.RecordFrame(Start.AddMilliseconds(200), [Known()],
            counter.ProcessFrame(Start.AddMilliseconds(200), [Known()], false), bitmap, null, null,
            recognitionVariant: HistoricalVariant);
        Assert.NotNull(recording.LastError);
        Assert.Equal(1, recording.RecordedFrameCount);
    }

    private string Record(bool unreadableMode = true, bool includeUnreadable = false, bool independent = false)
    {
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Helmet)], parsingContext: Context, visualLifetime: true,
            independentSpecial: independent, unreadableVisualLifetime: unreadableMode);
        for (var frame = 0; frame < 3; frame++)
        {
            var at = Start.AddMilliseconds(frame * 200);
            var rows = new List<LootObservation> { Known() };
            if (includeUnreadable && frame == 1) rows.Add(Unreadable());
            if (independent) rows.Add(Known() with { Source = LootSource.Rare, NativeY = 0 });
            recording.RecordFrame(at, rows, counter.ProcessFrame(at, rows, independent), bitmap, null,
                independent ? new Rectangle(0, 0, 2, 2) : null,
                recognitionVariant: (unreadableMode ? Variant : HistoricalVariant) + (independent ? "+independent-special-v1" : ""));
        }
        recording.RecordCompletion(Start.AddMilliseconds(500), counter.CompleteSession(Start.AddMilliseconds(500)));
        recording.Dispose();
        Assert.Null(recording.LastError);
        return recording.RecordingPath!;
    }

    private static LootObservation Known() => new(LootSource.Normal, 0, Helmet + " x 4", Helmet, 4, .99, .99, null, null)
        { NativeY = 250 };

    private static LootObservation Unreadable() => new(LootSource.Normal, 1, "Elioti", null, null, 0, 0, null, "native-catalog-miss")
        { NativeY = 200, OccupancyEvidence = new([new(0, .96)]) };

    private static LootDiagnosticEntry[] ReadEntries(string path) => File.ReadLines(path).Skip(1)
        .Select(line => JsonSerializer.Deserialize<LootDiagnosticEntry>(line, LootDiagnosticFormat.JsonOptions)!).ToArray();

    private static void Rewrite(string path, Action<JsonNode, JsonNode[]> change)
    {
        var lines = File.ReadAllLines(path);
        var header = JsonNode.Parse(lines[0])!;
        var entries = lines.Skip(1).Select(line => JsonNode.Parse(line)!).ToArray();
        change(header, entries);
        File.WriteAllLines(path, new[] { header.ToJsonString() }.Concat(entries.Select(entry => entry.ToJsonString())));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
