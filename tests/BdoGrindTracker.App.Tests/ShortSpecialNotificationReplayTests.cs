using System.Text.Json;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class ShortSpecialNotificationReplayTests : IDisposable
{
    private const string Warmup = "Deboreka Necklace";
    private const string Earring = "Twilight of the End - Earring";
    private const string Ring = "Twilight of the End - Ring";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly LifetimeParsingContext Context = new(0,
        [new(Warmup, []), new(Earring, []), new(Ring, [])]);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Grindcrest-ShortSpecialReplay-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(LootDiagnosticFormat.EngineVersion, false, false)]
    [InlineData(LootDiagnosticFormat.EngineVersion, true, true)]
    [InlineData(LootDiagnosticFormat.LegacyFadeAwareLifetimeEngineVersion, true, true)]
    [InlineData(LootDiagnosticFormat.LegacyUnreadableVisualLifetimeEngineVersion, true, false)]
    [InlineData(LootDiagnosticFormat.LegacyIndependentSpecialEngineVersion, false, false)]
    public void ShortConsecutiveNotificationUsesItsRecordedEngineSemantics(string engine, bool unreadable, bool fade)
    {
        var historical = engine != LootDiagnosticFormat.EngineVersion;
        using var bitmap = new Bitmap(4, 4);
        using var recording = DiagnosticRecordingSession.Start(directory);
        var counter = new CompanionDiagnosticCounter([new(Warmup), new(Earring), new(Ring)],
            parsingContext: Context, independentSpecial: true, unreadableVisualLifetime: unreadable,
            useFadeEvidence: fade, useLegacySingleRowLearning: historical);
        var variant = (fade ? "test+lifetime-v5+visual-occupancy-v2+visual-fade-v1" :
            unreadable ? "test+lifetime-v4+visual-occupancy-v2" : "test+lifetime-v3+visual-occupancy-v1") +
            "+independent-special-v1";
        var frame = 0;
        foreach (var item in Enumerable.Repeat(Warmup, 50).Concat(Enumerable.Repeat(Earring, 1)).Concat(Enumerable.Repeat(Ring, 50)))
        {
            var at = Start.AddMilliseconds(frame++ * 200);
            LootObservation[] rows = [new(LootSource.Rare, 0, item + " x 1", item, 1, 1, 1, null, null) { NativeY = 0 }];
            recording.RecordFrame(at, rows, counter.ProcessFrame(at, rows, true), bitmap, null, new(0, 0, 2, 2),
                recognitionVariant: variant);
        }
        var end = Start.AddMilliseconds(frame * 200);
        var completed = counter.CompleteSession(end);
        recording.RecordCompletion(end, completed);
        recording.Dispose();
        Assert.Null(recording.LastError);
        // v4-v6 lost this short banner after learning from the long preceding
        // notification. Historical replay must preserve that original result.
        Assert.Equal(historical ? 0 : 1, completed.LootProjection!.Totals.GetValueOrDefault(Earring));
        Assert.Equal(historical ? 2 : 3, completed.LootProjection.ConfirmedDropCount);

        var lines = File.ReadAllLines(recording.RecordingPath!);
        var header = JsonSerializer.Deserialize<LootDiagnosticHeader>(lines[0], LootDiagnosticFormat.JsonOptions)!;
        Assert.Equal(LootDiagnosticFormat.EngineVersion, header.EngineVersion);
        lines[0] = JsonSerializer.Serialize(header with { EngineVersion = engine }, LootDiagnosticFormat.JsonOptions);
        File.WriteAllLines(recording.RecordingPath!, lines);

        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal(!historical, replay.UsesCurrentEngine);
        Assert.True(replay.TotalsMatch, replay.ToDisplayText());
        Assert.True(replay.EventTimelineMatches, replay.ToDisplayText());
        Assert.Equal(1, replay.Totals[Warmup]);
        Assert.Equal(1, replay.Totals[Ring]);
        Assert.Equal(historical ? 0 : 1, replay.Totals.GetValueOrDefault(Earring));
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(directory);
        var allowed = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Grindcrest-ShortSpecialReplay-");
        if (!resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
