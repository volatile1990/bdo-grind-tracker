using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class FadeAwareRecordedSessionTests
{
    private const string Helmet = "Elion Follower's Helmet";

    [Fact]
    public void LatestRecordingRemovesTheThreePhantomDropsWithOriginalOcrAndMeasuredPixels()
    {
        var frames = Load(6);
        var historical = Replay(frames, fade: false);
        var current = Replay(frames, fade: true);

        Assert.Equal(3211, frames.Length);
        Assert.Equal(2440, historical.Totals[Helmet]);
        Assert.Equal(2428, current.Totals[Helmet]);
        Assert.Equal(historical.SupportedDropCount - 3, current.SupportedDropCount);
        Assert.Equal(0, current.VisualCoverageFallbackCount);
        Assert.Equal(historical.Totals.Where(pair => pair.Key != Helmet).OrderBy(pair => pair.Key),
            current.Totals.Where(pair => pair.Key != Helmet).OrderBy(pair => pair.Key));
        Assert.Equal(12, current.Totals["Black Stone"]);
        Assert.Equal(8, current.Totals["Caphras Stone"]);
        Assert.Equal(31, current.Totals["Ancient Spirit Dust"]);
        Assert.Equal(1, current.Totals["Nev's Fragment"]);
        Assert.Equal(1, current.Totals["JIN Origin Shard"]);
        // The ring was observed independently in the special panel. This fixture
        // contains only normal observations and must not invent the ring here.
        Assert.False(current.Totals.ContainsKey("Twilight of the End - Ring"));
    }

    [Theory]
    [InlineData(false, 2288)]
    [InlineData(true, 2292)]
    public void PreviousRecordingNeedsTheMeasuredFadingTailToAvoidASecondBooking(bool suppressTail, int expected)
    {
        var frames = Load(5);
        var historical = Replay(frames, fade: false);
        // At the following frame the old row is still visible, just beyond its
        // inferred expiry. Removing only its preceding fade measurement brings
        // back the independently image-rejected extra birth near frame 204.
        if (suppressTail)
            frames[205] = frames[205] with
            {
                Rows = frames[205].Rows.Select(row => row.Slot == 1 ? row with { FadeEvidence = null } : row).ToArray()
            };
        var current = Replay(frames, fade: true);

        Assert.Equal(3115, frames.Length);
        Assert.Equal(expected, current.Totals[Helmet]);
        Assert.Equal(historical.Totals.Where(pair => pair.Key != Helmet).OrderBy(pair => pair.Key),
            current.Totals.Where(pair => pair.Key != Helmet).OrderBy(pair => pair.Key));
        Assert.Equal(historical.SupportedDropCount + (suppressTail ? 1 : 0), current.SupportedDropCount);
        Assert.Equal(0, current.VisualCoverageFallbackCount);
    }

    [Theory]
    [InlineData(1, 2432)]
    [InlineData(2, 2432)]
    [InlineData(4, 2432)]
    [InlineData(7, 2440)]
    public void EachPhantomCorrectionDependsOnItsOwnMeasuredFadeWithoutChangingOtherDrops(int suppress, int expected)
    {
        var frames = Load(6).Select((frame, index) =>
        {
            var sequence = index + 1;
            var remove = ((suppress & 1) != 0 && sequence is >= 117 and <= 126) ||
                ((suppress & 2) != 0 && sequence is >= 1419 and <= 1435) ||
                ((suppress & 4) != 0 && sequence is >= 2433 and <= 2441);
            return remove ? frame with { Rows = frame.Rows.Select(row => row with { FadeEvidence = null }).ToArray() } : frame;
        }).ToArray();
        var current = Replay(frames, fade: true);

        Assert.Equal(expected, current.Totals[Helmet]);
        Assert.Equal(12, current.Totals["Black Stone"]);
        Assert.Equal(8, current.Totals["Caphras Stone"]);
        Assert.Equal(31, current.Totals["Ancient Spirit Dust"]);
        Assert.Equal(1, current.Totals["Nev's Fragment"]);
        Assert.Equal(1, current.Totals["JIN Origin Shard"]);
        Assert.Equal(0, current.VisualCoverageFallbackCount);
    }

    private static LifetimeSnapshot Replay(Frame[] frames, bool fade)
    {
        LifetimeNormalReconciliationAdapter? counter = null;
        foreach (var frame in frames)
        {
            if (frame.Context is { } context)
            {
                counter ??= new(context, true, useUnreadableSlotCoverage: true, useFadeEvidence: fade);
                counter.UpdateParsingContext(context);
            }
            var rows = fade ? frame.Rows : frame.Rows.Select(row => row with { FadeEvidence = null }).ToArray();
            counter!.ProcessObservations(rows, frame.At);
        }
        counter!.Complete();
        return counter.Projection!;
    }

    private static Frame[] Load(int recording)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "lifetime");
        var fade = new Dictionary<int, Dictionary<int, NormalLootFadeEvidence>>();
        foreach (var line in File.ReadLines(Path.Combine(directory, $"recording{recording}.fade.txt")).Where(line => !line.StartsWith('#')))
        {
            using var document = JsonDocument.Parse(line);
            var entry = document.RootElement;
            fade.Add(entry.GetProperty("sequence").GetInt32(), entry.GetProperty("rows").EnumerateArray()
                .ToDictionary(row => row.GetProperty("slot").GetInt32(), row =>
                    row.GetProperty("evidence").Deserialize<NormalLootFadeEvidence>(LootDiagnosticFormat.JsonOptions)!));
        }
        var filename = recording == 5 ? "recording5-unreadable.raw.txt" : $"recording{recording}.raw.txt";
        var frames = new List<Frame>();
        foreach (var line in File.ReadLines(Path.Combine(directory, filename)).Where(line => !line.StartsWith('#')))
        {
            using var document = JsonDocument.Parse(line);
            var entry = document.RootElement;
            var sequence = frames.Count + 1;
            var rows = entry.GetProperty("observations").Deserialize<LootObservation[]>(LootDiagnosticFormat.JsonOptions)!;
            var evidence = fade.GetValueOrDefault(sequence);
            rows = rows.Select(row => row with { FadeEvidence = evidence?.GetValueOrDefault(row.Slot) }).ToArray();
            var context = entry.TryGetProperty("lifetimeParsingContext", out var value) && value.ValueKind != JsonValueKind.Null
                ? value.Deserialize<LifetimeParsingContext>(LootDiagnosticFormat.JsonOptions) : null;
            frames.Add(new(entry.GetProperty("timestamp").GetDateTimeOffset(), rows, context));
        }
        return frames.ToArray();
    }

    private sealed record Frame(DateTimeOffset At, LootObservation[] Rows, LifetimeParsingContext? Context);
}
