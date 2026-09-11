using System.Text.Json;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Diagnostics;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task CompletionAndResumeRemainInCaptureClockAfterWallClockJump(int captureOffsetDays)
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        using var recording = DiagnosticRecordingSession.Start(Path.Combine(fixture.DirectoryPath, "diagnostics"));
        SetField(fixture.Service, "_recording", recording);
        using var frame = new Bitmap(2, 2);
        // Model both a forward and a backward system-clock adjustment without
        // changing the machine clock. The capture stream retains its own epoch.
        var firstCapturedAt = DateTimeOffset.UtcNow.AddDays(captureOffsetDays);
        await fixture.Service.ProcessFrameAsync(frame, new(1, firstCapturedAt), CancellationToken.None);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(firstCapturedAt, fixture.Analyzer.LastCompletedAt);

        // Resume the existing synthetic session, keeping real desktop capture off.
        fixture.ResumeClocks();
        SetField(fixture.Service, "_captureSegmentCompleted", false);
        var resumedAt = firstCapturedAt.AddSeconds(1);
        await fixture.Service.ProcessFrameAsync(frame, new(2, resumedAt), CancellationToken.None);
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Equal(resumedAt, fixture.Analyzer.LastCompletedAt);
        recording.Dispose();

        Assert.Null(recording.LastError);
        var entries = File.ReadAllLines(recording.RecordingPath!).Skip(1)
            .Select(line => JsonSerializer.Deserialize<LootDiagnosticEntry>(line, LootDiagnosticFormat.JsonOptions)!).ToArray();
        Assert.Equal(new[] { firstCapturedAt, firstCapturedAt, resumedAt, resumedAt },
            entries.Select(entry => entry.Timestamp));
        var replay = LootDiagnosticReplay.Run(recording.RecordingPath!);
        Assert.Equal(2, replay.FrameCount);
        Assert.Equal(2, replay.CompletionCount);
        Assert.True(replay.HasFinalCompletion);
        Assert.True(replay.TotalsMatch);
        Assert.True(replay.EventTimelineMatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptySessionCompletionKeepsWallClockAndClearsPreviousSessionEpoch(bool hadPreviousSession)
    {
        await using var fixture = new Fixture(autoUpload: false);
        if (hadPreviousSession)
        {
            fixture.Begin();
            using var frame = new Bitmap(2, 2);
            await fixture.Service.ProcessFrameAsync(frame,
                new CapturedFrameMetadata(1, DateTimeOffset.UtcNow.AddDays(1)), CancellationToken.None);
            Assert.True((await fixture.Service.PauseAsync()).Succeeded);
            Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        }
        fixture.Begin();
        var beforePause = DateTimeOffset.UtcNow;
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        var afterPause = DateTimeOffset.UtcNow;
        Assert.NotNull(fixture.Analyzer.LastCompletedAt);
        Assert.InRange(fixture.Analyzer.LastCompletedAt.Value, beforePause, afterPause);
    }
}
