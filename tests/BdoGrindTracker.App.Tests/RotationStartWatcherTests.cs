using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class RotationStartWatcherTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    [Theory]
    // Only banners that can mean nothing but the start of a fresh rotation.
    [InlineData("The sinners are summoned.", LootSpotCatalog.MagaiaId, "start")]
    [InlineData("A golden fragrance rides the wind.", LootSpotCatalog.AphrodonId, "restart")]
    // The overseer's offering opens a Hermesia rotation but repeats about twenty times inside it.
    [InlineData("The overseer orders the Black Crystals to be offered up.", null, null)]
    [InlineData("The history of sin begins to repeat itself once more.", null, null)]
    [InlineData("Anomaly detected: Flow violation. Removing anomaly.", null, null)]
    [InlineData("Flames of prayer rise from the brazier.", null, null)]
    [InlineData("You have obtained Elion Follower's Helmet x8.", null, null)]
    public void OnlyUnmistakableStartBannersAreReported(string text, string? spotId, string? kind)
    {
        using var watcher = new RotationStartWatcher(_ => text);
        using var frame = new Bitmap(2560, 1440);

        var sighting = watcher.Observe(frame, Epoch);

        Assert.Equal(spotId, sighting?.SpotId);
        Assert.Equal(kind, sighting?.Kind);
        if (sighting is not null) Assert.Equal(Epoch, sighting.At);
    }

    [Fact]
    public void ItReadsAtMostEveryThreeSecondsAndSkipsUnusableFrames()
    {
        var reads = 0;
        using var watcher = new RotationStartWatcher(_ => { reads++; return "The sinners are summoned."; });
        using var frame = new Bitmap(2560, 1440);

        Assert.Equal(LootSpotCatalog.MagaiaId, watcher.Observe(frame, Epoch)?.SpotId);
        var first = reads;
        // Only the watched spots are read, and the match ends the probe: never once per known spot.
        Assert.InRange(first, 1, 2);
        Assert.Null(watcher.Observe(frame, Epoch.AddSeconds(2)));
        Assert.Equal(first, reads);
        Assert.Equal(LootSpotCatalog.MagaiaId, watcher.Observe(frame, Epoch.AddSeconds(3))?.SpotId);
        Assert.True(reads > first);

        // A window too small for the banner area is skipped instead of throwing.
        using var tiny = new Bitmap(8, 6);
        Assert.Null(watcher.Observe(tiny, Epoch.AddSeconds(10)));
    }
}
