using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.Tests;

public sealed class HermesiaBufferedSearchTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;
    private const string Dragon = "Markthanan's patrol descends.";
    private static DateTimeOffset[] Times(double start, int count) =>
        Enumerable.Range(0, count).Select(i => Epoch.AddSeconds(start + i * .5)).ToArray();

    [Fact]
    public void MonitorBuffersBetweenThreeSecondProbesWithoutInterruptingRotation()
    {
        var reads = 0;
        using var frame = new System.Drawing.Bitmap(320, 200);
        using var monitor = new HermesiaRotationMonitor(recognize: _ => { Interlocked.Increment(ref reads); return Dragon; });
        monitor.Observe(frame, Epoch);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref reads) == 1, TimeSpan.FromSeconds(5)));
        for (var i = 1; i < 6; i++) monitor.Observe(frame, Epoch.AddSeconds(i * .5));
        Assert.Equal(1, Volatile.Read(ref reads));
        // Wait for the asynchronous probe to commit, then retry at the same
        // capture cadence until the first event has been confirmed.
        for (var i = 6; i <= 12; i++)
        {
            monitor.Observe(frame, Epoch.AddSeconds(i * .5));
            if (SpinWait.SpinUntil(() => monitor.Snapshot(Epoch.AddSeconds(i * .5)).Synchronized,
                    TimeSpan.FromMilliseconds(100))) break;
        }
        Assert.True(monitor.Snapshot(Epoch.AddSeconds(6)).Synchronized);
    }

    [Fact]
    public void EmptyProbeOnlyReadsNewestSample()
    {
        var reads = new List<int>();
        Assert.Empty(new HermesiaBufferedSearch().Read(Times(0, 21), i => { reads.Add(i); return ""; }));
        Assert.Equal(new[] { 20 }, reads);
    }

    [Fact]
    public void BackdatesAndToleratesOneFailedReadWithoutRepeatingSearchForVisibleBanner()
    {
        var search = new HermesiaBufferedSearch();
        var found = Assert.Single(search.Read(Times(0, 7), i => i >= 2 && i != 4 ? Dragon : ""));
        Assert.Equal(Epoch.AddSeconds(1), found.At);
        var reads = 0;
        Assert.Empty(search.Read(Times(0, 13), i => { reads++; return Dragon; }));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void SinglePositiveImageIsNotEnough()
    {
        Assert.Empty(new HermesiaBufferedSearch().Read(Times(0, 7), i => i == 6 ? Dragon : ""));
    }

    [Fact]
    public void RepeatedOccurrenceIsDetectedAfterAbsence()
    {
        var search = new HermesiaBufferedSearch();
        Assert.Single(search.Read(Times(0, 7), _ => Dragon));
        Assert.Empty(search.Read(Times(3, 7), _ => Dragon));
        Assert.Empty(search.Read(Times(6, 7), _ => ""));
        var next = Assert.Single(search.Read(Times(9, 7), i => i >= 2 ? Dragon : ""));
        Assert.Equal(Epoch.AddSeconds(10), next.At);
    }

    [Fact]
    public void MultipleMessagesAreReturnedInTheirActualStartOrder()
    {
        var search = new HermesiaBufferedSearch();
        var result = search.Read(Times(0, 7), i =>
            (i >= 4 ? Dragon : "") + (i >= 2 ? " begins absorbing nearby black crystals" : ""));
        Assert.Equal(new[] { "afk", "dragon" }, result.Select(e => e.Kind));
        Assert.Equal(Epoch.AddSeconds(1), result[0].At);
        Assert.Equal(Epoch.AddSeconds(2), result[1].At);
    }

    [Fact]
    public void CaptureGapCannotConfirmAnEvent()
    {
        Assert.Empty(new HermesiaBufferedSearch().Read([Epoch, Epoch.AddSeconds(3)], _ => Dragon));
    }
}
