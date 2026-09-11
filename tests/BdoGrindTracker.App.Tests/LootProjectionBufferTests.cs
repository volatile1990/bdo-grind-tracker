using System.Globalization;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class LootProjectionBufferTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Fact]
    public void ShortOvershootIsRemovedBeforePublicationAndUnchangedFramesReleaseTheBalance()
    {
        var buffer = new LootProjectionBuffer();
        Assert.Empty(buffer.Observe(Projection(1, 8, 2), Start).Totals);
        Assert.Empty(buffer.Observe(Projection(2, 4, 1), Start.AddMilliseconds(400)).Totals);
        var released = buffer.Observe(Projection(2, 4, 1), Start.AddSeconds(2));
        Assert.Equal(4, released.Totals["Helmet"]);
        Assert.Equal(1, released.ConfirmedDropCount);
        Assert.Equal(Start, released.LatestArrivalAt);
    }

    [Fact]
    public void GenuineSuccessiveDropsAreDelayedWithoutLosingAnyQuantity()
    {
        var buffer = new LootProjectionBuffer();
        for (var tick = 0; tick <= 30; tick++)
        {
            var quantity = Math.Min(tick + 1, 20) * 4;
            var raw = Projection(Math.Min(tick + 1, 20), quantity, quantity / 4);
            var stable = buffer.Observe(raw, Start.AddMilliseconds(tick * 200));
            Assert.Equal(Math.Clamp(tick - 9, 0, 20) * 4, stable.Totals.GetValueOrDefault("Helmet"));
        }
    }

    [Fact]
    public void ItemsAreConfirmedIndependentlyAndRetractedRareFallbackNeverLeaks()
    {
        var buffer = new LootProjectionBuffer();
        buffer.Observe(Projection(1, 4, 1), Start);
        var rare = new LootTotalsProjection(2, new Dictionary<string, long>
            { ["Helmet"] = 4, ["Black Stone"] = 1 }, 2, Start);
        buffer.Observe(rare, Start.AddSeconds(1));
        var normalOnly = buffer.Observe(Projection(3, 4, 1), Start.AddSeconds(1.4));
        Assert.Empty(normalOnly.Totals);
        var released = buffer.Observe(Projection(3, 4, 1), Start.AddSeconds(2));
        Assert.Single(released.Totals);
        Assert.Equal(4, released.Totals["Helmet"]);
    }

    [Fact]
    public void LongCaptureGapKeepsTheLastEstimateAtTheWindowBoundary()
    {
        var buffer = new LootProjectionBuffer();
        buffer.Observe(Projection(1, 4, 1), Start);
        var afterGap = buffer.Observe(Projection(2, 8, 2), Start.AddSeconds(60));
        Assert.Equal(4, afterGap.Totals["Helmet"]);
        Assert.Equal(8, buffer.Observe(Projection(2, 8, 2), Start.AddSeconds(62)).Totals["Helmet"]);
    }

    [Fact]
    public void LateCorrectionRemainsAccurateInsteadOfPermanentlyClampingAnOvercount()
    {
        var buffer = new LootProjectionBuffer();
        buffer.Observe(Projection(1, 8, 2), Start);
        Assert.Equal(8, buffer.Observe(Projection(1, 8, 2), Start.AddSeconds(2)).Totals["Helmet"]);
        var corrected = buffer.Observe(Projection(2, 4, 1), Start.AddSeconds(30));
        Assert.Equal(4, corrected.Totals["Helmet"]);
        Assert.Equal(1, corrected.ConfirmedDropCount);
    }

    [Fact]
    public void CompletionFlushesExactTailAndResumeCanUseTheSameRawRevision()
    {
        var buffer = new LootProjectionBuffer();
        var raw = Projection(1, 4, 1);
        buffer.Observe(raw, Start);
        var completed = buffer.Observe(raw, Start, flush: true);
        Assert.Equal(4, completed.Totals["Helmet"]);
        Assert.Same(completed, buffer.Observe(raw, Start, flush: true));
        Assert.Same(completed, buffer.Observe(raw, Start.AddMinutes(1)));
        Assert.Equal(4, buffer.Observe(Projection(2, 8, 2), Start.AddMinutes(1)).Totals["Helmet"]);
        Assert.Equal(8, buffer.Observe(Projection(2, 8, 2), Start.AddSeconds(62)).Totals["Helmet"]);
    }

    [Fact]
    public void StaleFramesCannotAdvanceTimeAndResetStartsANewSession()
    {
        var buffer = new LootProjectionBuffer();
        var first = buffer.Observe(Projection(2, 4, 1), Start);
        Assert.Same(first, buffer.Observe(Projection(1, 40, 10), Start.AddSeconds(5)));
        Assert.Same(first, buffer.Observe(Projection(3, 40, 10), Start.AddSeconds(-1)));
        Assert.Equal(4, buffer.Observe(Projection(2, 4, 1), Start.AddSeconds(2)).Totals["Helmet"]);
        buffer.Reset();
        Assert.Empty(buffer.Observe(Projection(0, 8, 2), Start).Totals);
        Assert.Equal(8, buffer.Observe(Projection(0, 8, 2), Start.AddSeconds(2)).Totals["Helmet"]);
    }

    [Fact]
    public void CasingChangeCannotCreateAnArtificialRetraction()
    {
        var buffer = new LootProjectionBuffer();
        buffer.Observe(Projection(1, 4, 1), Start);
        var confirmed = buffer.Observe(Projection(1, 4, 1), Start.AddSeconds(2));
        var recased = new LootTotalsProjection(2, new Dictionary<string, long> { ["helmet"] = 4 }, 1, Start);
        Assert.Same(confirmed, buffer.Observe(recased, Start.AddSeconds(2.2)));
        Assert.Equal(4, buffer.Observe(recased, Start.AddSeconds(4.2)).Totals.Values.Single());
    }

    [Fact]
    public void InvalidInputDoesNotConsumeRevisionOrTimestamp()
    {
        var buffer = new LootProjectionBuffer();
        buffer.Observe(Projection(1, 4, 1), Start);
        var invalid = new LootTotalsProjection(2, new Dictionary<string, long>
            { ["Helmet"] = 4, ["helmet"] = 4 }, 2, Start);
        Assert.Throws<ArgumentException>(() => buffer.Observe(invalid, Start.AddDays(1)));
        Assert.Equal(4, buffer.Observe(Projection(1, 4, 1), Start.AddSeconds(2)).Totals["Helmet"]);
    }

    [Fact]
    public void Recorded576HelmetSessionHasNoPublishedDecreasesAndRetainsEveryFinalItem()
    {
        using var mailbox = new FrameUiMailbox();
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "lifetime", "recording1-projections.txt");
        IReadOnlyDictionary<string, long> previousRaw = new Dictionary<string, long>();
        IReadOnlyDictionary<string, long> displayed = new Dictionary<string, long>();
        var rawDecreases = 0;
        var frames = 0;
        foreach (var line in File.ReadLines(path).Where(line => !line.StartsWith('#') && line.Length > 0))
        {
            var parts = line.Split('|');
            var at = DateTimeOffset.Parse(parts[2], CultureInfo.InvariantCulture);
            var totals = parts[6].Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('='))
                .ToDictionary(pair => pair[0], pair => long.Parse(pair[1], CultureInfo.InvariantCulture));
            rawDecreases += previousRaw.Count(pair => totals.GetValueOrDefault(pair.Key) < pair.Value);
            previousRaw = totals;
            var raw = new LootTotalsProjection(long.Parse(parts[3], CultureInfo.InvariantCulture), totals,
                int.Parse(parts[4], CultureInfo.InvariantCulture), parts[5].Length == 0 ? null :
                    DateTimeOffset.Parse(parts[5], CultureInfo.InvariantCulture));
            var frame = new FrameAnalysisResult([], [], 1, "lifetime-v1", 0, 0, 0, 0, null)
                { LootProjection = raw };
            mailbox.Publish(frame, capturedAt: at, flushProjection: parts[0] == "complete",
                onPublished: (published, _) =>
                {
                    Assert.All(displayed, pair => Assert.True(published.GetValueOrDefault(pair.Key) >= pair.Value,
                        $"Visible decrease at {parts[1]}: {pair.Key}"));
                    displayed = new Dictionary<string, long>(published);
                });
            if (parts[0] == "frame") frames++;
        }
        Assert.Equal(1012, frames);
        Assert.Equal(10, rawDecreases);
        Assert.Equal(576, displayed["Elion Follower's Helmet"]);
        Assert.Equal(3, displayed["Black Stone"]);
        Assert.Equal(1, displayed["Caphras Stone"]);
        Assert.Equal(1, displayed["Twilight of the End - Ring"]);
        Assert.Equal(previousRaw.OrderBy(pair => pair.Key), displayed.OrderBy(pair => pair.Key));
    }

    private static LootTotalsProjection Projection(long revision, long quantity, int drops) =>
        new(revision, new Dictionary<string, long> { ["Helmet"] = quantity }, drops, Start);
}
