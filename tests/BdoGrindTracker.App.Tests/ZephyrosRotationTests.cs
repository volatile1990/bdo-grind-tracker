using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class ZephyrosRotationTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("Caphras's powerful energy stirs within the Lava Field Hatchery.", "start")]
    [InlineData("Caphras's gathered energy in the Hatchery spills forth, flooding the land.", "knights")]
    [InlineData("Seeking to absorb Caphras's energy, the Zephyros Shadow Knight emerges.", "knights")]
    [InlineData("Absorbing the last of Caphras's energy, Beelzebub emerges.", "boss")]
    [InlineData("As Beelzebub's energy wanes, the Hatchery's energy weakens.", "afk")]
    [InlineData("The Hatchery begins to unleash a surge of its remaining energy.", "afk")]
    [InlineData("The Hatchery stirs once more, returning to life.", "end")]
    public void FullBannerRecognizesOneBoundary(string text, string kind) =>
        Assert.Equal(kind, Assert.Single(ZephyrosMessages.Parse(text, "en")).Kind);

    [Theory]
    [InlineData("Tiring of the prolonged battle, Beelzebub hides its presence.")]
    [InlineData("Unable to detect the Adventurer's energy, the Hatchery falls silent.")]
    [InlineData("With no Caphras energy remaining, Beelzebub remains hidden.")]
    [InlineData("Too much time has passed. The Hatchery has lost Caphras's energy.")]
    [InlineData("The Manticore is liberated from the mind control.")]
    public void UnverifiedOrUnrelatedBannersNeverBecomeSuccess(string text) =>
        Assert.Empty(ZephyrosMessages.Parse(text));

    [Fact]
    public void PairedBannersAreOneTransition()
    {
        Assert.Equal("knights", Assert.Single(ZephyrosMessages.Parse(
            "Caphras's gathered energy in the Hatchery spills forth, flooding the land.\n" +
            "Seeking to absorb Caphras's energy, the Zephyros Shadow Knight emerges.")).Kind);
        Assert.Equal("afk", Assert.Single(ZephyrosMessages.Parse(
            "As Beelzebub's energy wanes, the Hatchery's energy weakens.\n" +
            "The Hatchery begins to unleash a surge of its remaining energy.")).Kind);
    }

    [Fact]
    public void RecordedCyclesCompleteAndTheNextActivationOverridesTheLootLockout()
    {
        var tracker = new RotationPlatform(RotationDefinition.Zephyros);
        Assert.False(tracker.ObserveLoot(Epoch));
        // First visible samples at 2-second intervals, not frame-exact timings.
        foreach (var times in new[] { new[] { 14, 136, 548, 578, 708 },
                     new[] { 712, 834, 1254, 1286, 1416 }, new[] { 1418, 1542, 1958, 1988, 2118 },
                     new[] { 2122, 2244, 2654, 2684, 2814 }, new[] { 2818, 2940, 3360, 3394, 3524 } })
        {
            Replay(tracker, times);
            var completed = Assert.Single(tracker.DrainCompleted());
            Assert.True(completed.Run.EligibleForStatistics);
            Assert.Equal(times[4] - times[0], completed.Run.Duration);
            Assert.Equal(4, RotationPhases.Create(LootSpotCatalog.ZephyrosId, completed.Run.Events, completed.Run.Duration).Count);
        }
        Assert.Equal(5, tracker.Snapshot(Epoch.AddSeconds(3526)).Completed);
        Assert.False(RotationDefinition.Zephyros.HasSpecialEvents);
        Assert.Equal(RotationMessageProfile.Hermesia.Crop(1920, 1080), RotationMessageProfile.Zephyros.Crop(1920, 1080));
        Assert.Contains(LootSpotCatalog.ZephyrosId, RotationProfiles.SupportedSpotIds);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("knights")]
    [InlineData("boss")]
    [InlineData("afk")]
    public void MissingMandatoryBoundaryCannotTrainBestTimes(string missing)
    {
        var tracker = new RotationPlatform(RotationDefinition.Zephyros);
        Replay(tracker, [14, 136, 548, 578, 708], missing);
        Assert.All(tracker.DrainCompleted(), r => Assert.False(r.Run.EligibleForStatistics));
        Assert.Null(tracker.Snapshot(Epoch.AddSeconds(710)).Best);
    }

    [Fact]
    public void InterruptionPreservesAnAbortAndAllowsAFreshCycle()
    {
        var tracker = new RotationPlatform(RotationDefinition.Zephyros);
        tracker.Observe("start", "Start", Epoch.AddSeconds(14));
        tracker.Observe("knights", "Ritter", Epoch.AddSeconds(136));
        tracker.InterruptAt("Aufnahme unterbrochen", Epoch.AddSeconds(200));
        Assert.Equal("aborted", Assert.Single(tracker.DrainCompleted()).Run.Outcome);
        Replay(tracker, [712, 834, 1254, 1286, 1416]);
        Assert.True(Assert.Single(tracker.DrainCompleted()).Run.EligibleForStatistics);
    }

    [Fact]
    public void BossFirstIsPartialAndReadinessDoesNotAutomaticallyStartAnotherCycle()
    {
        var tracker = new RotationPlatform(RotationDefinition.Zephyros);
        tracker.Observe("boss", "Beelzebub", Epoch.AddSeconds(548));
        tracker.Observe("afk", "AFK", Epoch.AddSeconds(578));
        tracker.Observe("end", "Bereit", Epoch.AddSeconds(708));
        Assert.False(Assert.Single(tracker.DrainCompleted()).Run.EligibleForStatistics);
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(710)).Synchronized);
        Assert.False(tracker.ObserveLoot(Epoch.AddSeconds(720)));
    }

    private static void Replay(RotationPlatform tracker, int[] times, string? omit = null)
    {
        string[] kinds = ["start", "knights", "boss", "afk", "end"];
        for (var i = 0; i < kinds.Length; i++)
            if (kinds[i] != omit) tracker.Observe(kinds[i], kinds[i], Epoch.AddSeconds(times[i]));
    }

    [ZephyrosRecordingFact]
    public void NativeOcrReadsEveryBoundaryInsideTheUnchangedSharedCrop()
    {
        var directory = Environment.GetEnvironmentVariable("ZEPHYROS_RECORDING_DIRECTORY")!;
        var profile = RotationProfiles.Messages(LootSpotCatalog.ZephyrosId, "en")!;
        var engine = CompanionWindowsOcrRecognizer.TryCreate("en-US", true, true)!;
        using var video = new VideoCapture(Path.Combine(directory, "UtAX9-qc-RY-direct.mp4"));
        Assert.True(video.IsOpened());
        foreach (var (time, kind) in new[] { (14, "start"), (138, "knights"), (552, "boss"), (580, "afk"), (710, "end") })
        {
            video.Set(VideoCaptureProperties.PosMsec, time * 1000);
            using var frame = new Mat();
            Assert.True(video.Read(frame));
            var crop = profile.Crop(frame.Width, frame.Height);
            using var pixels = new Mat(frame, new Rect(crop.X, crop.Y, crop.Width, crop.Height));
            var text = profile.Recognize(pixels, engine);
            Assert.Contains(profile.Parse(text), e => e.Kind == kind);
        }
    }
}

public sealed class ZephyrosRecordingFactAttribute : FactAttribute
{
    public ZephyrosRecordingFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZEPHYROS_RECORDING_DIRECTORY")))
            Skip = "Set ZEPHYROS_RECORDING_DIRECTORY to the downloaded Zephyros recording directory.";
    }
}
