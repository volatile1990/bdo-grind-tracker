using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffTentRefreshRecordedFrameTests
{
    [WindowsOcrFact]
    [Trait("Category", "WindowsOcr")]
    public void RealFourHourTentTimersCountBothReapplicationsOnce()
    {
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs", "bar-tent-refresh-4h.png"));
        using var reader = new AutomaticBuffFrameReader();
        var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));
        Assert.Empty(reading.UnknownBuffIds);
        Assert.Equal(6, reading.Observations.Count);
        var families = new[] { "automatic-tent-body-enhancement", "automatic-tent-adventures-boon" };
        var tent = reading.Observations.Where(item => families.Contains(item.BuffId)).ToArray();
        Assert.Equal(2, tent.Length);
        Assert.All(tent, item =>
        {
            Assert.Equal(TimeSpan.FromHours(4), item.Remaining);
            Assert.Equal(TimeSpan.FromHours(1), item.TimerPrecision);
        });
        foreach (var id in new[] { "harmony-draught-edania", "perfume-of-tenacity" })
        {
            var item = Assert.Single(reading.Observations, item => item.BuffId == id);
            Assert.Equal(TimeSpan.FromMinutes(4), item.Remaining);
            Assert.Equal(TimeSpan.FromMinutes(1), item.TimerPrecision);
        }

        var ledger = new BuffLedger(BuffPriceCatalog.HistoryDefinitions.Concat(AutomaticBuffCatalog.Default.HistoricalGroupDefinitions));
        var at = new DateTimeOffset(2026, 9, 22, 6, 0, 0, TimeSpan.Zero);
        BuffPrice Price(BuffDefinition definition) => new(definition.FixedUnitPrice!.Value, "eu", at, false)
            { Source = BuffPriceSource.FixedNpc };
        var before = tent.Select(item => item with { Remaining = TimeSpan.FromMinutes(20), TimerPrecision = TimeSpan.FromMinutes(1) }).ToArray();
        ledger.Apply(before, at, Price);
        ledger.Apply(before, at.AddSeconds(10), Price);
        ledger.Apply([], at.AddSeconds(20), Price, families);
        var renewed = ledger.Apply(tent, at.AddSeconds(30), Price);
        Assert.Equal(2, renewed.Consumptions.Count);
        Assert.Equal(22_000_000m, renewed.ConsumedCost);
        var tiles = ConsumablesPresentation.Create(renewed, "en").Items;
        Assert.Equal(2, tiles.Count);
        Assert.All(tiles, tile => Assert.Equal(1, tile.Count));
        var repeated = ledger.Apply(tent, at.AddSeconds(40), Price);
        Assert.Equal(renewed.Consumptions, repeated.Consumptions);
    }
}
