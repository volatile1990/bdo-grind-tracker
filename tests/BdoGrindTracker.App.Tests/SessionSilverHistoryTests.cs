using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class SessionSilverHistoryTests
{
    [Fact]
    public void SamplesSessionTimeAndKeepsTheCurrentEndpointBetweenIntervals()
    {
        var history = new SessionSilverHistory();
        var state = Session();

        Assert.Empty(history.Update(state));
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(history.Update(At(state, 3))).Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(9), Assert.Single(history.Update(At(state, 9))).Elapsed);
        var first = history.Update(At(state, 10));
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(first).Elapsed);
        var current = history.Update(At(state, 13));
        Assert.Equal(new[] { 10d, 13d }, current.Select(sample => sample.Elapsed.TotalSeconds));
        Assert.Equal(Presentation.Hourly(state.Silver.AfterTax, TimeSpan.FromSeconds(13)), current[^1].SilverPerHour);
        var next = history.Update(At(state, 20));
        Assert.Equal(new[] { 10d, 20d }, next.Select(sample => sample.Elapsed.TotalSeconds));
    }

    [Fact]
    public void RetainsTheEntireSessionWithoutNeedingAnOverlay()
    {
        var history = new SessionSilverHistory();
        var state = Session();
        IReadOnlyList<SessionSilverSample> samples = [];

        for (var step = 1; step <= 400; step++)
            samples = history.Update(At(state, step * 10));

        Assert.Equal(400, samples.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), samples[0].Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(4000), samples[^1].Elapsed);
    }

    [Fact]
    public void PausingAndCorrectingLootReplaceOnlyTheCurrentPoint()
    {
        var history = new SessionSilverHistory();
        var state = Session();
        var first = history.Update(At(state, 10));
        var paused = At(state, 20) with { IsRunning = false };
        var beforeCorrection = history.Update(paused);
        var corrected = paused with { Silver = new(200, 200, 1, [], [], false) };

        var actual = history.Update(corrected);
        var repeated = history.Update(corrected);

        Assert.Equal(2, actual.Count);
        Assert.Equal(first[0], actual[0]);
        Assert.Equal(Presentation.Hourly(200, paused.Elapsed), actual[^1].SilverPerHour);
        Assert.Equal(actual, repeated);
        Assert.Equal(Presentation.Hourly(100, paused.Elapsed), beforeCorrection[^1].SilverPerHour);
    }

    [Fact]
    public void CorrectingAnEndpointDoesNotRewriteThePreviousSample()
    {
        var history = new SessionSilverHistory();
        var state = Session();
        var first = history.Update(At(state, 10));
        history.Update(At(state, 13));

        var corrected = history.Update(At(state, 15) with { Silver = new(200, 200, 1, [], [], false) });

        Assert.Equal(2, corrected.Count);
        Assert.Equal(first[0], corrected[0]);
        Assert.Equal(TimeSpan.FromSeconds(15), corrected[^1].Elapsed);
        Assert.Equal(48_000m, corrected[^1].SilverPerHour, precision: 12);
    }

    [Fact]
    public void ARewindRemovesOnlyTheDiscardedIdleTail()
    {
        var history = new SessionSilverHistory();
        var state = Session();
        for (var step = 1; step <= 5; step++) history.Update(At(state, step * 10));

        var actual = history.Update(At(state, 25) with { IsRunning = false });

        Assert.Equal(new[] { 10d, 20d, 25d }, actual.Select(sample => sample.Elapsed.TotalSeconds));
        var resumed = history.Update(At(state, 30));
        Assert.Equal(new[] { 10d, 20d, 30d }, resumed.Select(sample => sample.Elapsed.TotalSeconds));
        Assert.Empty(history.Update(state));
    }

    [Fact]
    public void ARewindToASampleAlsoUpdatesItsRate()
    {
        var history = new SessionSilverHistory();
        var state = Session();
        history.Update(At(state, 10));
        history.Update(At(state, 20));
        history.Update(At(state, 30));

        var actual = history.Update(At(state, 20) with { Silver = new(200, 200, 1, [], [], false) });

        Assert.Equal(new[] { 10d, 20d }, actual.Select(sample => sample.Elapsed.TotalSeconds));
        Assert.Equal(36_000m, actual[^1].SilverPerHour, precision: 12);
    }

    [Fact]
    public void NewSessionAndDemoChangesResetTheHistory()
    {
        var history = new SessionSilverHistory();
        var state = Session();
        history.Update(At(state, 10));
        history.Update(At(state, 20));
        var nextSession = At(state, 40) with { SessionId = Guid.NewGuid() };

        Assert.Equal(TimeSpan.FromSeconds(40), Assert.Single(history.Update(nextSession)).Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(50), Assert.Single(history.Update(At(nextSession, 50) with { IsDemo = true })).Elapsed);
        Assert.Single(history.Update(At(nextSession, 60)));
        Assert.Empty(history.Update(nextSession with { HasSession = false, IsRunning = false }));
    }

    [Fact]
    public void AFirstObservationDoesNotInventAnEarlierCurve()
    {
        var history = new SessionSilverHistory();
        var state = At(Session(), 3600);

        var actual = history.Update(state);

        Assert.Equal(new SessionSilverSample(state.Elapsed, 100m), Assert.Single(actual));
    }

    [Fact]
    public void UnknownPricesAndOverflowNeverProduceInventedValues()
    {
        var history = new SessionSilverHistory();
        var state = At(Session(), 10);
        var first = history.Update(state);
        var unknown = At(state, 20) with { Silver = new(0, 0, 0, ["Black Stone"], [], false) };

        Assert.Empty(history.Update(unknown));
        var recovered = history.Update(At(state, 30));
        Assert.Equal(new[] { 10d, 30d }, recovered.Select(sample => sample.Elapsed.TotalSeconds));
        Assert.Equal(first[0], recovered[0]);
        Assert.Empty(history.Update(state with
        {
            SessionId = Guid.NewGuid(), Elapsed = TimeSpan.FromTicks(1),
            Silver = new(decimal.MaxValue, decimal.MaxValue, 1, [], [], false),
        }));
    }

    [Fact]
    public void ZeroQuantityUnknownItemsFollowTheSameRuleAsTheLiveDashboard()
    {
        var state = At(Session(), 10) with
        {
            Loot = new(new Dictionary<string, long> { ["Unknown item"] = 0 }, 0, 0),
            Silver = new(0, 0, 0, ["Unknown item"], [], false),
        };

        Assert.Empty(new SessionSilverHistory().Update(state));
        Assert.Equal(0, Assert.Single(new SessionSilverHistory().Update(state with
        {
            Loot = new(new Dictionary<string, long>(), 0, 0),
        })).SilverPerHour);
    }

    [Fact]
    public void PublishedSamplesAreReadOnlyAndIndependentOfFutureChanges()
    {
        var history = new SessionSilverHistory();
        var state = At(Session(), 10);
        var first = history.Update(state);

        Assert.Throws<NotSupportedException>(() => ((IList<SessionSilverSample>)first)[0] = new(TimeSpan.Zero, 0));
        history.Update(state with { Silver = new(200, 200, 1, [], [], false) });
        history.Update(state with { SessionId = Guid.NewGuid() });

        Assert.Equal(new SessionSilverSample(state.Elapsed, Presentation.Hourly(100, state.Elapsed)), Assert.Single(first));
    }

    [Fact]
    public async Task PreviewSessionPublishesHistoryAndUpdatesItAfterALootCorrection()
    {
        await using var session = new PreviewTrackerSession();
        var initial = session.State;
        Assert.Equal(initial.Elapsed, Assert.Single(initial.SilverHistory).Elapsed);
        Assert.Equal(Presentation.Hourly(initial.Silver.AfterTax, initial.Elapsed), initial.SilverHistory[^1].SilverPerHour);
        var quantity = initial.Loot.Totals["Branch of Abundance"];

        await session.UpdateLootQuantityAsync(initial.SessionId, "Branch of Abundance", quantity + 1000, quantity);

        var actual = session.State;
        Assert.Single(actual.SilverHistory);
        Assert.Equal(Presentation.Hourly(actual.Silver.AfterTax, actual.Elapsed), actual.SilverHistory[^1].SilverPerHour);
        Assert.NotEqual(initial.SilverHistory[^1].SilverPerHour, actual.SilverHistory[^1].SilverPerHour);
    }

    private static TrackerState Session() => new()
    {
        SessionId = Guid.NewGuid(), HasSession = true, IsRunning = true,
        Loot = new(new Dictionary<string, long> { ["Black Stone"] = 1 }, 1, 1),
        Silver = new(100, 100, 1, [], [], false),
    };

    private static TrackerState At(TrackerState state, double seconds) => state with { Elapsed = TimeSpan.FromSeconds(seconds) };
}

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task RealSessionPublishesItsHistoryWithoutAnyOverlayAndResetsForANewSession()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        await fixture.ProcessAfter(TimeSpan.FromSeconds(10), ("Black Crystal Fragment", 5));
        var first = fixture.Service.State.SilverHistory;
        await fixture.ProcessAfter(TimeSpan.FromSeconds(10), ("Black Crystal Fragment", 5));
        await fixture.Service.PauseAsync();
        var paused = fixture.Service.State;

        Assert.Equal(new[] { 10d, 20d }, paused.SilverHistory.Select(sample => sample.Elapsed.TotalSeconds));
        Assert.Equal(Presentation.Hourly(paused.Silver.AfterTax, paused.Elapsed), paused.SilverHistory[^1].SilverPerHour);
        IReadOnlyList<SessionSilverSample>? published = null;
        fixture.Service.Changed += () => published = fixture.Service.State.SilverHistory;
        await fixture.Service.UpdateLootQuantityAsync(paused.SessionId, "Black Crystal Fragment", 15, 10);

        var corrected = fixture.Service.State;
        Assert.Equal(2, corrected.SilverHistory.Count);
        Assert.Equal(first[0], corrected.SilverHistory[0]);
        Assert.Equal(Presentation.Hourly(corrected.Silver.AfterTax, corrected.Elapsed), corrected.SilverHistory[^1].SilverPerHour);
        Assert.Same(corrected.SilverHistory, published);
        await fixture.Service.NewSessionAsync();
        Assert.Empty(fixture.Service.State.SilverHistory);
    }
}
