using System.Globalization;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuffDebugLoggingRecordsAppliedEvidenceAndConsumptionOncePerScanOnlyWhenEnabled(bool enabled)
    {
        const string unknownBuff = "simple-cron-meal";
        BuffFrameReading? reading = BuffReading(60) with { UnknownBuffIds = [unknownBuff] };
        var monitor = new BuffMonitor(new SessionBuffReader(() => reading), TimeSpan.FromSeconds(1));
        await using var fixture = new Fixture(autoUpload: false, buffMonitor: monitor,
            lootScrollVisible: _ => true, initialSettings: new AppSettings { AutomaticDebugLogging = enabled });
        BeginBuffSession(fixture);
        var now = DateTimeOffset.UtcNow;
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-4));
        reading = BuffReading(1200);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-3));
        reading = BuffReading(1199);
        await ProcessBuffFrame(fixture, monitor, now.AddSeconds(-2));
        fixture.Service.RefreshPendingState();
        fixture.Service.RefreshPendingState();

        Assert.Single(fixture.Service.State.Buffs!.Consumptions);
        var entries = ReadDebugEntries(fixture).Where(entry => DebugKind(entry) == "buff-observation").ToArray();
        if (!enabled)
        {
            Assert.Empty(entries);
            Assert.False(Directory.Exists(fixture.Service.DebugLogsDirectory));
            return;
        }

        Assert.Equal(3, entries.Length);
        var data = entries.Select(entry => entry.GetProperty("data")).ToArray();
        Assert.Equal(new[] { now.AddSeconds(-4), now.AddSeconds(-3), now.AddSeconds(-2) },
            data.Select(entry => entry.GetProperty("observedAt").GetDateTimeOffset()).ToArray());
        Assert.All(data, entry =>
        {
            Assert.Equal(fixture.Service.State.SessionId, entry.GetProperty("sessionId").GetGuid());
            Assert.True(entry.GetProperty("generation").GetInt64() >= 0);
            var observation = Assert.Single(entry.GetProperty("observations").EnumerateArray());
            Assert.Equal(SessionBuff.Id, observation.GetProperty("buffId").GetString());
            Assert.Equal(TimeSpan.FromSeconds(1), TimeSpan.Parse(
                observation.GetProperty("timerPrecision").GetString()!, CultureInfo.InvariantCulture));
        });
        Assert.Equal(new[] { 60d, 1200d, 1199d }, data.Select(entry => TimeSpan.Parse(
            entry.GetProperty("observations")[0].GetProperty("remaining").GetString()!,
            CultureInfo.InvariantCulture).TotalSeconds).ToArray());
        Assert.Equal(unknownBuff, Assert.Single(data[0].GetProperty("unknownBuffIds").EnumerateArray()).GetString());
        Assert.Empty(data[1].GetProperty("unknownBuffIds").EnumerateArray());
        Assert.Empty(data[0].GetProperty("newConsumptions").EnumerateArray());
        var purchase = Assert.Single(data.SelectMany(entry => entry.GetProperty("newConsumptions").EnumerateArray()));
        Assert.Equal(SessionBuff.Id, purchase.GetProperty("buffId").GetString());
        Assert.Equal(0, data[0].GetProperty("consumptionCountBefore").GetInt32());
        Assert.Equal(0, data[0].GetProperty("consumptionCountAfter").GetInt32());
        Assert.Equal(1, data[^1].GetProperty("consumptionCountAfter").GetInt32());
        Assert.Null(fixture.Service.State.DebugLogError);
    }
}
