using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffMonitorLedgerRecoveryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private const string HarmonyId = "harmony-draught-edania";
    private const string TenacityId = "perfume-of-tenacity";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IndividualTenacityGapCountsTheFirstHigherTimerImmediatelyWithoutChargingTheGap(
        bool explicitUnknown, bool recoveredBeforeDelivery)
    {
        var reader = new Reader();
        using var monitor = new BuffMonitor(reader);
        using var frame = new Bitmap(10, 10);
        var ledger = new BuffLedger(BuffPriceCatalog.Definitions);
        var perfumePrice = 600_000m;
        BuffPrice Price(BuffDefinition definition) => new(
            definition.Id == TenacityId ? perfumePrice : 1_200_000m, "eu", Start, false);

        await Scan(0, Both(8));
        Deliver(0);
        await Scan(10, Both(8));
        var initial = Deliver(10);
        Assert.Equal(2, initial.Consumptions.Count);
        Assert.All(initial.Consumptions, item => Assert.True(item.IsSessionStart));
        Assert.Equal(TimeSpan.FromSeconds(10), Usage(initial, TenacityId).ObservedDuration);
        var initialHarmony = Assert.Single(initial.Consumptions, item => item.BuffId == HarmonyId);
        monitor.Snapshot(Start.AddSeconds(10), out var initialGeneration);

        await Scan(20, new([Observation(HarmonyId, 19)])
        { UnknownBuffIds = explicitUnknown ? [TenacityId] : [] });
        if (!recoveredBeforeDelivery)
        {
            var gap = Deliver(20, expectTenacityGap: true);
            Assert.Equal(HarmonyId, Assert.Single(gap.Active).BuffId);
            Assert.Equal(2, gap.Consumptions.Count);
            Assert.Equal(TimeSpan.FromSeconds(10), Usage(gap, TenacityId).ObservedDuration);
        }

        // A new price belongs to the newly observed cycle; the old startup
        // purchase and usage remain unchanged across the unreadable frame.
        perfumePrice = 2_400_000m;
        await Scan(30, Both(19));
        var renewed = Deliver(30, expectTenacityGap: recoveredBeforeDelivery);
        Assert.Equal(3, renewed.Consumptions.Count);
        Assert.Equal(2, renewed.Active.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), Usage(renewed, TenacityId).ObservedDuration);
        var renewal = Assert.Single(renewed.Consumptions, item => !item.IsSessionStart);
        Assert.Equal(TenacityId, renewal.BuffId);
        Assert.Equal(Start.AddSeconds(30), renewal.ConsumedAt);
        Assert.Equal(2_400_000m, renewal.Cost);

        await Scan(40, Both(19));
        var confirmed = Deliver(40);
        Assert.Equal(renewed.Consumptions, confirmed.Consumptions);
        Assert.Equal(2, confirmed.Active.Count);
        Assert.Equal(TimeSpan.FromSeconds(20), Usage(confirmed, TenacityId).ObservedDuration);

        await Scan(50, Both(19));
        var result = Deliver(50);
        Assert.Equal(confirmed.Consumptions, result.Consumptions);
        Assert.Equal(initialHarmony, Assert.Single(result.Consumptions, item => item.BuffId == HarmonyId));
        Assert.Equal(TimeSpan.FromSeconds(50), Usage(result, HarmonyId).ObservedDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), Usage(result, TenacityId).ObservedDuration);
        Assert.Equal(45_000m, Usage(result, TenacityId).KnownProratedCost);
        Assert.Equal(4_200_000m, result.ConsumedCost);
        monitor.Snapshot(Start.AddSeconds(50), out var finalGeneration);
        Assert.Equal(initialGeneration, finalGeneration);

        async Task Scan(int seconds, BuffFrameReading reading)
        {
            reader.Next = reading;
            monitor.Observe(frame, Start.AddSeconds(seconds));
            await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        }

        BuffLedgerSnapshot Deliver(int seconds, bool expectTenacityGap = false)
        {
            var snapshot = monitor.Snapshot(Start.AddSeconds(seconds));
            Assert.True(snapshot.IsKnown);
            Assert.Equal(Start.AddSeconds(seconds), snapshot.ObservedAt);
            if (expectTenacityGap) Assert.Equal(TenacityId, Assert.Single(snapshot.UnknownBuffIds));
            else Assert.Empty(snapshot.UnknownBuffIds);
            return ledger.Apply(snapshot.Observations, snapshot.ObservedAt!.Value, Price, snapshot.UnknownBuffIds);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllBuffsDisappearingForTwoMinutesKeepTimerComparisonButNeverAccrueGapUsage(bool emptyReading)
    {
        var reader = new Reader();
        using var monitor = new BuffMonitor(reader);
        using var frame = new Bitmap(10, 10);
        var ledger = new BuffLedger(BuffPriceCatalog.Definitions);
        long observedGeneration = -1;
        BuffPrice Price(BuffDefinition definition) => new(
            definition.Id == TenacityId ? 600_000m : 1_200_000m, "eu", Start, false);
        BuffFrameReading Timers(int minutes) => new([Observation(HarmonyId, minutes), Observation(TenacityId, minutes)]);

        await Scan(0, Timers(8));
        Deliver(0);
        await Scan(10, Timers(8));
        var initial = Deliver(10);
        Assert.Equal(2, initial.Consumptions.Count);
        Assert.All(initial.Consumptions, item => Assert.True(item.IsSessionStart));
        var initialGeneration = observedGeneration;

        await Scan(20, emptyReading ? new BuffFrameReading([]) : null);
        var missing = Deliver(20);
        Assert.True(observedGeneration > initialGeneration);
        Assert.Empty(missing.Active);
        Assert.Equal(initial.Consumptions, missing.Consumptions);
        Assert.All(missing.Usage, item => Assert.Equal(TimeSpan.FromSeconds(10), item.ObservedDuration));

        // The player can die and rebuff long after every symbol disappeared.
        // A monitor generation change interrupts usage, not the last timer value.
        await Scan(140, Timers(19));
        var renewed = Deliver(140);
        Assert.Equal(4, renewed.Consumptions.Count);
        Assert.Equal(2, renewed.Active.Count);
        var renewals = renewed.Consumptions.Where(item => !item.IsSessionStart).ToArray();
        Assert.Equal(2, renewals.Length);
        Assert.Equal(new[] { HarmonyId, TenacityId }, renewals.Select(item => item.BuffId));
        Assert.All(renewals, item => Assert.Equal(Start.AddSeconds(140), item.ConsumedAt));
        Assert.All(renewed.Usage, item => Assert.Equal(TimeSpan.FromSeconds(10), item.ObservedDuration));
        Assert.Equal(3_600_000m, renewed.ConsumedCost);
        Assert.Equal(renewed.Consumptions, Deliver(140).Consumptions);

        await Scan(150, Timers(19));
        var unchanged = Deliver(150);
        Assert.Equal(renewed.Consumptions, unchanged.Consumptions);
        Assert.All(unchanged.Usage, item => Assert.Equal(TimeSpan.FromSeconds(20), item.ObservedDuration));
        Assert.Equal(30_000m, unchanged.ProratedCost);

        await Scan(160, Timers(18));
        var falling = Deliver(160);
        Assert.Equal(renewed.Consumptions, falling.Consumptions);
        Assert.All(falling.Usage, item => Assert.Equal(TimeSpan.FromSeconds(30), item.ObservedDuration));

        async Task Scan(int seconds, BuffFrameReading? reading)
        {
            reader.Next = reading;
            monitor.Observe(frame, Start.AddSeconds(seconds));
            await monitor.CurrentAnalysis.WaitAsync(TimeSpan.FromSeconds(5));
        }

        BuffLedgerSnapshot Deliver(int seconds)
        {
            var snapshot = monitor.Snapshot(Start.AddSeconds(seconds), out var generation);
            // Mirror the service's handling of a global reader failure and the
            // generation that prevents usage across an overwritten unknown scan.
            if (generation != observedGeneration)
            {
                ledger.BreakContinuity();
                observedGeneration = generation;
            }
            if (!snapshot.IsKnown)
            {
                ledger.BreakContinuity();
                return ledger.Snapshot;
            }
            return ledger.Apply(snapshot.Observations, snapshot.ObservedAt!.Value, Price, snapshot.UnknownBuffIds);
        }
    }

    private static BuffUsage Usage(BuffLedgerSnapshot snapshot, string id) =>
        Assert.Single(snapshot.Usage, item => item.BuffId == id);

    private static BuffFrameReading Both(int tenacityMinutes) =>
        new([Observation(HarmonyId, 19), Observation(TenacityId, tenacityMinutes)]);

    private static BuffObservation Observation(string id, int minutes) =>
        new(id, TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(1)) { ConsumptionAttributionConfirmed = true };

    private sealed class Reader : IBuffFrameReader
    {
        internal BuffFrameReading? Next { get; set; }
        public BuffFrameReading? Read(Bitmap frame, CancellationToken cancellationToken) => Next;
        public void Dispose() { }
    }
}
