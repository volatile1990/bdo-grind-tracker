using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class SessionRotationStatsTests
{
    private static readonly DateTimeOffset Observed = new(2026, 9, 22, 20, 0, 0, TimeSpan.Zero);

    private static RotationMonitorSnapshot Session(params SessionRotationTiming[] rotations) =>
        new() { HasProfile = true, SessionRotations = rotations };

    [Fact]
    public void CountAverageAndFastestFollowTheCompletedRotations()
    {
        var session = Session(new(600, 20), new(540, 15), new(660, 25));

        Assert.Equal(3, SessionRotationStats.Count(session));
        Assert.Equal(600, SessionRotationStats.Average(session)!.Value, 8);
        Assert.Equal(540, SessionRotationStats.Fastest(session)!.Value, 8);
        Assert.Null(SessionRotationStats.Average(Session()));
        Assert.Null(SessionRotationStats.Fastest(Session()));
    }

    [Fact]
    public void TheTempoUsesTheLatestRotationsIncludingTheirWalkBack()
    {
        var session = Session(new(900, 30), new(600, 20), new(540, 15), new(660, 25));

        // The last three: (600+20 + 540+15 + 660+25) / 3 = 620 seconds.
        Assert.Equal(620, SessionRotationStats.Tempo(session)!.Value, 8);
        Assert.Equal(3600 / 620d, SessionRotationStats.PerHour(session)!.Value, 8);
        Assert.Equal(3, SessionRotationStats.RecentCount(session));
        Assert.True(SessionRotationStats.HasKnownWalkBack(session));

        // A still running walk back falls back to the session's average walk back.
        var open = Session(new(600, 20), new(540));
        Assert.Equal((600 + 20 + 540 + 20) / 2d, SessionRotationStats.Tempo(open)!.Value, 8);
        Assert.Null(SessionRotationStats.Tempo(Session()));
        Assert.Null(SessionRotationStats.PerHour(Session()));
    }

    [Fact]
    public void WithoutSpecialEventsTheTempoSkipsThoseRotations()
    {
        var session = Session(new(500, 10, Special: true), new(600, 20), new(640, 20));

        Assert.Equal((600 + 20 + 640 + 20) / 2d, SessionRotationStats.Tempo(session, includeSpecialEvents: false)!.Value, 8);
        Assert.Equal(2, SessionRotationStats.RecentCount(session, includeSpecialEvents: false));
    }

    [Fact]
    public void SpansPlaceTheRotationsOnTheSessionsTimeAxis()
    {
        var session = Session(
            new(600, 20, StartedAt: Observed.AddMinutes(-30), SpecialEventSeconds: [120, 300]),
            new(540, 15, StartedAt: Observed.AddMinutes(-18)),
            new(660, StartedAt: Observed.AddMinutes(-11)));

        var spans = SessionRotationStats.Spans(session, TimeSpan.FromMinutes(40), Observed);

        Assert.Equal([1, 2, 3], spans.Select(span => span.Number));
        Assert.Equal([TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(22), TimeSpan.FromMinutes(29)], spans.Select(span => span.Start));
        Assert.Equal(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(600), spans[0].End);
        Assert.Equal([false, true, false], spans.Select(span => span.IsFastest));
        // The special events keep the times at which they were recognized inside their rotation.
        Assert.Equal([TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(15)], spans[0].SpecialEvents);
        // Without a start time a rotation cannot be placed and is left out.
        Assert.Empty(SessionRotationStats.Spans(Session(new SessionRotationTiming(600, 20)), TimeSpan.FromMinutes(40), Observed));
    }

    [Fact]
    public void ARecordedSessionTimeOutweighsTheWallClock()
    {
        // The session ran for two hours but only one of them was active: the wall clock alone would place this
        // rotation an hour before the session began.
        var session = Session(new SessionRotationTiming(600, 20, StartedAt: Observed.AddHours(-2),
            StartedAfter: TimeSpan.FromMinutes(5)));

        var spans = SessionRotationStats.Spans(session, TimeSpan.FromHours(1), Observed);

        Assert.Equal(TimeSpan.FromMinutes(5), Assert.Single(spans).Start);
    }

    [Fact]
    public void OnlyARotationThatEndedBeforeTheAxisIsLeftOffIt()
    {
        // A session restored after a restart: the first rotation ran and ended during the offline gap the session
        // clock never counted. The second began before the clock ran - as it does when the automatic start measures
        // from a banner - and reaches into the session.
        var session = Session(new SessionRotationTiming(600, 20, StartedAt: Observed.AddHours(-3)),
            new SessionRotationTiming(1800, StartedAt: Observed.AddMinutes(-41)),
            new SessionRotationTiming(540, StartedAt: Observed.AddMinutes(-20)));

        var spans = SessionRotationStats.Spans(session, TimeSpan.FromMinutes(40), Observed);

        Assert.Equal([TimeSpan.FromMinutes(-1), TimeSpan.FromMinutes(20)], spans.Select(span => span.Start));
        // The statistics keep every one of them: they do not depend on a place on the timeline.
        Assert.Equal(3, SessionRotationStats.Count(session));

        var timeline = new SessionRotationTimeline();
        var mapped = timeline.Update(Guid.NewGuid(), TimeSpan.FromMinutes(40), Observed, null, session);
        Assert.Equal([TimeSpan.FromHours(-2) - TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(-1), TimeSpan.FromMinutes(20)],
            mapped.SessionRotations.Select(timing => timing.StartedAfter));
    }

    [Fact]
    public void TheTimelineRecordsEachRotationsSessionTimeOnceAndKeepsIt()
    {
        var timeline = new SessionRotationTimeline();
        var session = Guid.NewGuid();
        var snapshot = Session(new SessionRotationTiming(600, 20, StartedAt: Observed.AddMinutes(-20), RunId: Guid.NewGuid()));

        Assert.Equal(TimeSpan.FromMinutes(10), timeline.Update(session, TimeSpan.FromMinutes(30), Observed, null, snapshot)
            .SessionRotations[0].StartedAfter);

        // Ten more minutes of wall clock, all of them paused: the rotation keeps the place it was recorded at.
        Assert.Equal(TimeSpan.FromMinutes(10), timeline.Update(session, TimeSpan.FromMinutes(30), Observed.AddMinutes(10), null, snapshot)
            .SessionRotations[0].StartedAfter);

        // Another session starts over with its own axis.
        Assert.Equal(TimeSpan.FromMinutes(5), timeline.Update(Guid.NewGuid(), TimeSpan.FromMinutes(25), Observed, null, snapshot)
            .SessionRotations[0].StartedAfter);
    }

    [Fact]
    public void TheSessionsOwnStartAnchorsEveryRotationAndSurvivesARestart()
    {
        // The recorded session of 22.09.2026: the automatic start opened the first rotation seventeen seconds
        // before the session, the second one aborted half an hour in. Both are placed from the session's start,
        // so reopening the tracker fourteen hours later does not move them.
        var started = Observed;
        var snapshot = Session(new SessionRotationTiming(1866.6, StartedAt: started.AddSeconds(-17), RunId: Guid.NewGuid()),
            new SessionRotationTiming(13.8, StartedAt: started.AddSeconds(1849), RunId: Guid.NewGuid(), Outcome: "aborted"));
        var elapsed = TimeSpan.FromSeconds(1850);

        var live = new SessionRotationTimeline()
            .Update(Guid.NewGuid(), elapsed, started + elapsed, started, snapshot);
        var restored = new SessionRotationTimeline()
            .Update(Guid.NewGuid(), elapsed, started.AddHours(14), started, snapshot);

        Assert.Equal([TimeSpan.FromSeconds(-17), TimeSpan.FromSeconds(1849)],
            live.SessionRotations.Select(timing => timing.StartedAfter));
        Assert.Equal(live.SessionRotations.Select(timing => timing.StartedAfter),
            restored.SessionRotations.Select(timing => timing.StartedAfter));
        // Both are on the timeline; the first one reaches into the session although it began before it.
        Assert.Equal([1, 2], SessionRotationStats.Spans(restored, elapsed, started.AddHours(14)).Select(span => span.Number));
    }

    [Fact]
    public void EventsPerHourFollowTheActiveGrindTime()
    {
        Assert.Equal(2, SessionRotationStats.PerHour(3, TimeSpan.FromMinutes(90))!.Value, 8);
        Assert.Null(SessionRotationStats.PerHour(3, TimeSpan.Zero));
    }
}
