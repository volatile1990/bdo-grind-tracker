using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.Tests;

public sealed class HermesiaRotationOrderingTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    [Fact]
    public void ConfirmationFromLaterProbeKeepsOriginalEventTimeAndValidRun()
    {
        var tracker = new HermesiaRotationTracker();
        Observe(tracker, "porter", 0);
        Observe(tracker, "drakania", 10);
        Observe(tracker, "transfer", 21);
        Observe(tracker, "drakania-kill", 20); // Confirmed by the next probe.
        Observe(tracker, "mine-enter", 25);
        Observe(tracker, "dragon", 80);
        Observe(tracker, "afk", 90);
        Observe(tracker, "mine-cleared", 150);

        var completed = Assert.Single(tracker.DrainCompleted());
        Assert.Equal(150, completed.Run.Duration);
        Assert.Equal(new[] { "drakania-kill", "transfer" },
            completed.Run.Events.Where(e => e.Seconds is 20 or 21).Select(e => e.Kind));
        Assert.Equal(20, completed.Run.Events.Single(e => e.Kind == "drakania-kill").Seconds);
    }

    [Fact]
    public void BufferedProbeCanDiscoverTheFirstEventAfterASecondEventWasPublished()
    {
        const string offer = "overseer orders the black crystals";
        const string porter = "porters gather to offer";
        var samples = Enumerable.Range(0, 13).Select(i => Epoch.AddSeconds(i * .5)).ToArray();
        // The offer starts first, but one failed read at the first probe leaves
        // only the later porter visible to that probe. Earlier cached reads are
        // recovered when the next probe sees the offer again.
        string Text(int i) => (i >= 4 && i != 6 ? offer : "") + " " + (i >= 5 ? porter : "");
        var search = new HermesiaBufferedSearch();
        var tracker = new HermesiaRotationTracker();
        foreach (var e in search.Read(samples.Take(7).ToArray(), Text))
            tracker.Observe(e.Kind, e.Label, e.At);
        Assert.Equal("porter", tracker.Snapshot(Epoch.AddSeconds(3)).Events[1].Kind);
        foreach (var e in search.Read(samples, Text)) tracker.Observe(e.Kind, e.Label, e.At);

        var state = tracker.Snapshot(Epoch.AddSeconds(6));
        Assert.True(state.Synchronized);
        Assert.Equal(4, state.Elapsed);
        Assert.Equal(new[] { "start", "offer", "porter" }, state.Events.Select(e => e.Kind));
        Assert.Equal(new[] { 0d, 0, .5 }, state.Events.Select(e => e.Seconds));
    }

    [Fact]
    public void DelayedRepeatedMessagesKeepTheirChronologicalOccurrenceKeys()
    {
        var tracker = new HermesiaRotationTracker();
        Observe(tracker, "porter", 1);
        Observe(tracker, "porter", 20);
        Observe(tracker, "porter", 10);
        var events = tracker.Snapshot(Epoch.AddSeconds(21)).Events.Where(e => e.Kind == "porter").ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, events.Select(e => e.Occurrence));
        Assert.Equal(new[] { 0d, 9, 19 }, events.Select(e => e.Seconds));
    }

    [Fact]
    public void DelayedMineCompletionBeforeAfkCannotCloseTheRotation()
    {
        var tracker = new HermesiaRotationTracker();
        Observe(tracker, "porter", 0);
        Observe(tracker, "afk", 100);
        Observe(tracker, "mine-cleared", 98);
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(103)).Synchronized);
        Assert.True(tracker.Snapshot(Epoch.AddSeconds(103)).IsAfk);
        Observe(tracker, "mine-cleared", 160);
        Assert.False(tracker.Snapshot(Epoch.AddSeconds(160)).Synchronized);
        Assert.Equal(160, tracker.Snapshot(Epoch.AddSeconds(160)).Elapsed);
    }

    [Fact]
    public void DelayedAfkEndKeepsAlreadyObservedNextRotationEvents()
    {
        var tracker = new HermesiaRotationTracker();
        foreach (var e in HermesiaRotationDemo.Reference.Events.Where(e => e.Kind is not "start" and not "end"))
            tracker.Observe(e.Kind, e.Label, Epoch.AddSeconds(e.Seconds));
        var end = HermesiaRotationDemo.Reference.Duration;
        Observe(tracker, "porter", end + 2);
        Observe(tracker, "mine-cleared", end);

        Assert.Equal(end, Assert.Single(tracker.DrainCompleted()).Run.Duration);
        var next = tracker.Snapshot(Epoch.AddSeconds(end + 4));
        Assert.True(next.Synchronized);
        Assert.False(next.IsAfk);
        Assert.Equal(2, next.Elapsed);
        Assert.Equal(new[] { "start", "porter" }, next.Events.Select(e => e.Kind));
        Observe(tracker, "offer", end - 1); // A stale previous-run confirmation.
        Assert.Equal(new[] { "start", "porter" }, tracker.Snapshot(Epoch.AddSeconds(end + 4)).Events.Select(e => e.Kind));
    }

    private static void Observe(HermesiaRotationTracker tracker, string kind, double seconds) =>
        tracker.Observe(kind, kind, Epoch.AddSeconds(seconds));
}
