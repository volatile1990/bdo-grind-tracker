using System.Text.Json;
using System.Text.Json.Nodes;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class FadeAwareDiagnosticsTests : IDisposable
{
    private const string Helmet = "Elion Follower's Helmet";
    private const string Variant = "test+lifetime-v5+visual-occupancy-v2+visual-fade-v1";
    private const string UnreadableVariant = "test+lifetime-v4+visual-occupancy-v2";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly LifetimeParsingContext Context = new(0, [new(Helmet, [])]);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest-FadeDiagnostics-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AdapterRequiresFadeOptInAndResetKeepsTheSelectedAlgorithm()
    {
        Assert.Equal("lifetime-v4", new LifetimeNormalReconciliationAdapter(Context, true, useUnreadableSlotCoverage: true).AlgorithmName);
        var current = new LifetimeNormalReconciliationAdapter(Context, true, useUnreadableSlotCoverage: true, useFadeEvidence: true);
        Assert.Equal("lifetime-v5", current.AlgorithmName);
        current.ProcessObservations([Known()], Start);
        current.Reset();
        Assert.Equal("lifetime-v5", current.AlgorithmName);
        Assert.Throws<ArgumentException>(() => new LifetimeNormalReconciliationAdapter(Context, true, useFadeEvidence: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordedFadeAndIndependentSpecialRoundTripWithExactTimeline(bool independent)
    {
        var path = Record(fadeMode: true, independent: independent);
        var rows = File.ReadLines(path).Skip(1).Select(line => JsonSerializer.Deserialize<LootDiagnosticEntry>(line, LootDiagnosticFormat.JsonOptions)!)
            .SelectMany(entry => entry.Observations).Where(row => row.FadeEvidence is not null).ToArray();
        var fading = Assert.Single(rows);
        Assert.Equal(Helmet, fading.ItemName);
        Assert.Equal(4, fading.Quantity);
        Assert.Equal(Helmet + " x 4", fading.RawText);
        Assert.Equal(.7999999970197678, fading.NameConfidence);
        Assert.Equal(new NormalLootFadeEvidence(.55, .98), fading.FadeEvidence);
        var replay = LootDiagnosticReplay.Run(path);
        Assert.True(replay.UsesCurrentEngine);
        Assert.Equal("lifetime-v5", replay.NormalTrackingAlgorithm);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviousV5HeaderKeepsTheHistoricalUnreadableCounter(bool independent)
    {
        var path = Record(fadeMode: false, independent: independent);
        Rewrite(path, (header, _) => header["engineVersion"] = LootDiagnosticFormat.LegacyUnreadableVisualLifetimeEngineVersion);
        var replay = LootDiagnosticReplay.Run(path);
        Assert.False(replay.UsesCurrentEngine);
        Assert.Equal("lifetime-v4", replay.NormalTrackingAlgorithm);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
    }

    [Theory]
    [InlineData("old-header")]
    [InlineData("missing-fade-marker")]
    [InlineData("unknown-fade-marker")]
    [InlineData("duplicate-fade-marker")]
    [InlineData("old-algorithm")]
    [InlineData("wrong-occupancy")]
    [InlineData("changed-mode")]
    public void ReplayRejectsMislabeledOrChangingFadeModes(string corruption)
    {
        var path = Record(fadeMode: true);
        Rewrite(path, (header, entries) => {
            switch(corruption)
            {
                case "old-header": header["engineVersion"] = LootDiagnosticFormat.LegacyUnreadableVisualLifetimeEngineVersion; break;
                case "missing-fade-marker": entries[0]["recognitionVariant"] = "test+lifetime-v5+visual-occupancy-v2"; break;
                case "unknown-fade-marker": entries[0]["recognitionVariant"] = Variant.Replace("visual-fade-v1", "visual-fade-v2"); break;
                case "duplicate-fade-marker": entries[0]["recognitionVariant"] = Variant + "+visual-fade-v1"; break;
                case "old-algorithm": entries[0]["recognitionVariant"] = Variant.Replace("lifetime-v5", "lifetime-v4"); break;
                case "wrong-occupancy": entries[0]["recognitionVariant"] = Variant.Replace("visual-occupancy-v2", "visual-occupancy-v1"); break;
                case "changed-mode": entries[1]["recognitionVariant"] = UnreadableVariant; break;
            }
        });
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(path));
    }

    [Theory]
    [InlineData("rare")]
    [InlineData("anchor")]
    [InlineData("rejected")]
    [InlineData("anonymous")]
    [InlineData("missing-geometry")]
    [InlineData("invalid-slot")]
    [InlineData("ratio")]
    [InlineData("correlation")]
    public void FadeMetadataRequiresAnEligibleCalibratedNormalRow(string corruption)
    {
        var row = Known() with { FadeEvidence = new(.55, .98) };
        row = corruption switch {
            "rare" => row with { Source = LootSource.Rare }, "anchor" => row with { IsAlignmentAnchor = true },
            "rejected" => row with { RejectionReason = "outside-spot-pool" }, "anonymous" => row with { ItemName = null },
            "missing-geometry" => row with { NativeY = null }, "invalid-slot" => row with { Slot = 6 },
            "ratio" => row with { FadeEvidence = new(5, .98) }, "correlation" => row with { FadeEvidence = new(.55, double.NaN) },
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)) };
        Assert.Throws<InvalidDataException>(() => DiagnosticRecordingSession.ValidateObservations([row], true, true));
    }

    [Fact]
    public void HistoricalCountersAndMarkersCannotConsumeNonNullFadeMetadata()
    {
        var row = Known() with { FadeEvidence = new(.55, .98) };
        Assert.Throws<InvalidDataException>(() => DiagnosticRecordingSession.ValidateObservations([row], true));
        var counter = new CompanionDiagnosticCounter([new(Helmet)], parsingContext: Context, unreadableVisualLifetime: true);
        Assert.Throws<InvalidDataException>(() => counter.ProcessFrame(Start, [row], false));
        var path = Record(fadeMode: true);
        Rewrite(path, (_, entries) => {
            foreach(var entry in entries.Where(entry => entry["kind"]!.GetValue<string>() == "frame")) entry["recognitionVariant"] = UnreadableVariant;
        });
        Assert.Throws<InvalidDataException>(() => LootDiagnosticReplay.Run(path));
    }

    [Fact]
    public void RecorderRefusesSwitchingAwayFromFadeMode()
    {
        using var bitmap = new Bitmap(4,4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Helmet)], parsingContext: Context, useFadeEvidence: true);
        recording.RecordFrame(Start, [Known()], counter.ProcessFrame(Start, [Known()], false), bitmap, null, null, recognitionVariant: Variant);
        Assert.Null(recording.LastError);
        var later = Start.AddMilliseconds(200);
        recording.RecordFrame(later, [Known()], counter.ProcessFrame(later, [Known()], false), bitmap, null, null, recognitionVariant: UnreadableVariant);
        Assert.NotNull(recording.LastError);
        Assert.Equal(1, recording.RecordedFrameCount);
    }

    private string Record(bool fadeMode, bool independent = false)
    {
        using var bitmap = new Bitmap(4,4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Helmet)], parsingContext: Context, unreadableVisualLifetime: true,
            independentSpecial: independent, useFadeEvidence: fadeMode);
        for(var frame = 0; frame < 7; frame++)
        {
            var at = Start.AddMilliseconds(frame * 200);
            var rows = new List<LootObservation> { Known() with { FadeEvidence = fadeMode && frame == 4 ? new(.55,.98) : null,
                NameConfidence = fadeMode && frame == 4 ? .7999999970197678 : .99 } };
            if(independent) rows.Add(Known() with { Source = LootSource.Rare, NativeY = 0 });
            recording.RecordFrame(at, rows, counter.ProcessFrame(at, rows, independent), bitmap, null,
                independent ? new Rectangle(0,0,2,2) : null,
                recognitionVariant: (fadeMode ? Variant : UnreadableVariant) + (independent ? "+independent-special-v1" : ""));
        }
        recording.RecordCompletion(Start.AddMilliseconds(1400), counter.CompleteSession(Start.AddMilliseconds(1400)));
        recording.Dispose(); Assert.Null(recording.LastError); return recording.RecordingPath!;
    }
    private static LootObservation Known() => new(LootSource.Normal,0,Helmet + " x 4",Helmet,4,.99,.99,null,null) { NativeY = 250 };
    private static void Rewrite(string path,Action<JsonNode,JsonNode[]> change)
    {
        var lines = File.ReadAllLines(path); var header = JsonNode.Parse(lines[0])!; var entries = lines.Skip(1).Select(line => JsonNode.Parse(line)!).ToArray();
        change(header,entries); File.WriteAllLines(path,new[] { header.ToJsonString() }.Concat(entries.Select(entry => entry.ToJsonString())));
    }
    public void Dispose()
    {
        var resolved = Path.GetFullPath(directory);
        var allowed = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Grindcrest-FadeDiagnostics-");
        if(!resolved.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test directory.");
        if(Directory.Exists(resolved)) Directory.Delete(resolved,true);
    }
}
