using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task AManualPauseIsRecordedAtItsActiveTimeAndKeptWithTheSession()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 5));

        Assert.True((await fixture.Service.PauseAsync()).Succeeded);

        var pause = Assert.Single(fixture.Service.State.Pauses);
        Assert.Equal((SessionPause.Manual, true), (pause.Kind, pause.IsOpen));
        Assert.Equal(fixture.Service.State.Elapsed, pause.At);
        // Pausing again changes nothing: one pause is open at a time.
        Assert.True((await fixture.Service.PauseAsync()).Succeeded);
        Assert.Single(fixture.Service.State.Pauses);

        Assert.True((await fixture.Service.NewSessionAsync()).Succeeded);
        Assert.Empty(fixture.Service.State.Pauses);
        Assert.Equal(pause.At, Assert.Single(Assert.Single(fixture.HistoryStore.Load()).Pauses!).At);
    }

    [Fact]
    public async Task AnAutomaticPauseBeginsWhereTheIdleTimeWasRemoved()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromMinutes(2), ("Black Crystal Fragment", 5));
        fixture.Time.Advance(TimeSpan.FromMinutes(Math.Max(1, fixture.Service.Preferences.AutoPauseMinutes) + 1));

        await fixture.Service.TickAsync();

        Assert.False(fixture.Service.State.IsRunning);
        var pause = Assert.Single(fixture.Service.State.Pauses);
        Assert.Equal((SessionPause.Automatic, true), (pause.Kind, pause.IsOpen));
        Assert.Equal(fixture.Service.State.Elapsed, pause.At);
        Assert.Equal(TimeSpan.FromMinutes(2), pause.At);
    }
}

public sealed partial class TrackerSessionRestoreTests
{
    [Fact]
    public async Task ClosingWhileTrackingPausesTheSessionUntilItIsStartedAgain()
    {
        using var directory = new TestDirectory();
        await using var first = new Fixture(directory.Path);
        first.Begin();
        await first.ProcessProjection(TimeSpan.FromMinutes(2), 1, 12);
        await first.Service.ShutdownAsync();

        await using var second = new Fixture(directory.Path);

        var pause = Assert.Single(second.Service.State.Pauses);
        Assert.Equal((SessionPause.Closed, true, TimeSpan.FromMinutes(2)), (pause.Kind, pause.IsOpen, pause.At));
    }

    [Fact]
    public async Task APauseSurvivesARestartAndEndsWhenTrackingResumes()
    {
        using var directory = new TestDirectory();
        await using var first = new Fixture(directory.Path);
        first.Begin();
        await first.ProcessProjection(TimeSpan.FromMinutes(2), 1, 12);
        await first.Service.PauseAsync();
        await first.Service.ShutdownAsync();

        await using var second = new Fixture(directory.Path);
        // Already paused when Grindcrest was closed: the same pause goes on, no second one for the closing.
        var pause = Assert.Single(second.Service.State.Pauses);
        Assert.Equal((SessionPause.Manual, true), (pause.Kind, pause.IsOpen));

        Assert.Null((await second.Service.ToggleTrackingAsync()).Error);
        Assert.True(second.Service.State.IsRunning);
        Assert.False(Assert.Single(second.Service.State.Pauses).IsOpen);

        await second.Service.PauseAsync();
        Assert.Equal(2, second.Service.State.Pauses.Count);
        Assert.True(second.Service.State.Pauses[^1].IsOpen);
        Assert.Equal(2, new CurrentSessionStore(directory.CurrentPath).Load()!.Pauses!.Count);
    }

    [Fact]
    public async Task ASessionSavedWithoutPausesWasPausedByTheClosing()
    {
        using var directory = new TestDirectory();
        var snapshot = CurrentSessionStoreTests.Example();
        new CurrentSessionStore(directory.CurrentPath).Save(snapshot);

        await using var fixture = new Fixture(directory.Path);

        var pause = Assert.Single(fixture.Service.State.Pauses);
        Assert.Equal((SessionPause.Closed, snapshot.Duration, snapshot.UpdatedAt), (pause.Kind, pause.At, pause.StartedAt));
    }
}

public sealed class SessionPauseTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SavedPausesAreKeptInOrderWithOnlyTheLastOneOpen()
    {
        SessionPause[] saved =
        [
            new(TimeSpan.FromMinutes(30), Noon.AddMinutes(40), null, SessionPause.Automatic),
            new(TimeSpan.FromMinutes(10), Noon, null, SessionPause.Manual),
            new(TimeSpan.FromMinutes(90), Noon.AddHours(2), null, SessionPause.Manual),
            new(TimeSpan.FromMinutes(20), Noon, null, "unknown"),
            new(TimeSpan.FromMinutes(40), Noon.AddHours(1), null, SessionPause.Closed),
        ];

        var pauses = SessionPauses.Normalize(saved, TimeSpan.FromMinutes(60));

        Assert.Equal(new[] { 10d, 30, 40 }, pauses.Select(pause => pause.At.TotalMinutes));
        Assert.Equal(new DateTimeOffset?[] { Noon.AddMinutes(40), Noon.AddHours(1), null }, pauses.Select(pause => pause.EndedAt));
        Assert.Empty(SessionPauses.Normalize(null, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void APauseLastsUntilTrackingResumedOrUntilNow()
    {
        var closed = new SessionPause(TimeSpan.Zero, Noon, Noon.AddMinutes(12), SessionPause.Manual);
        var open = closed with { EndedAt = null };

        Assert.Equal(TimeSpan.FromMinutes(12), closed.Duration(Noon.AddHours(5)));
        Assert.Equal(TimeSpan.FromMinutes(3), open.Duration(Noon.AddMinutes(3)));
        Assert.Equal(TimeSpan.Zero, open.Duration(Noon.AddMinutes(-3)));
    }

    [Fact]
    public void EveryPauseIsAGapOfOneWidthOnTheAxis()
    {
        // 100 seconds on 1000 units with one pause at 50 seconds: 980 units share the time.
        var axis = new SessionTimelineAxis(TimeSpan.Zero, TimeSpan.FromSeconds(100), [TimeSpan.FromSeconds(50)], 1000, 20);

        Assert.Equal(20, axis.GapWidth);
        Assert.Equal(490, axis.X(TimeSpan.FromSeconds(50)), 6);
        Assert.Equal(510, axis.XAfter(TimeSpan.FromSeconds(50)), 6);
        Assert.Equal(1000, axis.X(TimeSpan.FromSeconds(100)), 6);
        Assert.Equal(490, axis.GapLeft(0), 6);
        Assert.Equal(new[] { (392d, 490d), (510d, 608d) },
            axis.Pieces(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60)).Select(piece => (Math.Round(piece.Left, 6), Math.Round(piece.Right, 6))));
        Assert.Equal((0, 1), (axis.Segment(TimeSpan.FromSeconds(50)), axis.Segment(TimeSpan.FromSeconds(51))));
    }

    [Fact]
    public void GapsAtTheEdgesAndManyPausesStayInsideTheAxis()
    {
        var edges = new SessionTimelineAxis(TimeSpan.Zero, TimeSpan.FromSeconds(100),
            [TimeSpan.Zero, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(200)], 1000, 20);
        // The same moment twice is one gap; one outside the window none.
        Assert.Equal(2, edges.Gaps.Count);
        Assert.Equal((0d, 20d), (edges.X(TimeSpan.Zero), edges.XAfter(TimeSpan.Zero)));
        Assert.Equal((980d, 1000d), (edges.X(TimeSpan.FromSeconds(100)), edges.XAfter(TimeSpan.FromSeconds(100))));

        var crowded = new SessionTimelineAxis(TimeSpan.Zero, TimeSpan.FromHours(1),
            Enumerable.Range(1, 100).Select(minute => TimeSpan.FromSeconds(minute * 30)), 300, 10);
        // A hundred pauses on 300 units take at most half of it.
        Assert.Equal(1.5, crowded.GapWidth, 6);
        Assert.Equal(300, crowded.X(TimeSpan.FromHours(1)), 6);
    }
}
