using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class AutomaticBuffTimerContinuityTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 22, 13, 0, 0, TimeSpan.Zero);
    private const string Boon = "automatic-tent-adventures-boon";

    [Fact]
    public void LiveBoonCountdownDoesNotChargeADurationBoundaryOrRecoveredLowOcrRead()
    {
        var catalog = CreateCatalog();
        var timer = "2h";
        using var reader = new AutomaticBuffFrameReader(catalog, (_, _) => new(timer, default));
        using var frame = CreateFrame(catalog);
        var ledger = new BuffLedger(BuffPriceCatalog.HistoryDefinitions.Concat(catalog.HistoricalGroupDefinitions));
        var index = 0;

        Apply("2h");
        Apply("2h");
        Apply("119m");
        Assert.Equal("tent-adventures-boon-300", Assert.Single(ledger.Snapshot.Active).BuffId);
        var rejected = Apply("19m");
        Assert.Empty(rejected.Observations);
        Assert.Contains(Boon, rejected.UnknownBuffIds);
        Apply("119m");
        Apply("118m");
        Assert.Empty(ledger.Snapshot.Consumptions);

        Apply("4h");
        var renewed = Assert.Single(ledger.Snapshot.Consumptions);
        Assert.Equal("tent-adventures-boon-300", renewed.BuffId);
        Assert.Equal(12_000_000m, renewed.Cost);

        BuffFrameReading Apply(string label)
        {
            timer = label;
            var at = Start.AddSeconds(10 * index++);
            var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, at, CancellationToken.None));
            ledger.Apply(reading.Observations, at,
                definition => new(definition.FixedUnitPrice!.Value, "eu", at, false), reading.UnknownBuffIds);
            return reading;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetOrGloballyUnreadableFrameClearsThePreviousTimerReference(bool explicitReset)
    {
        var catalog = CreateCatalog();
        var timer = "99m";
        using var reader = new AutomaticBuffFrameReader(catalog, (_, _) => new(timer, default));
        using var frame = CreateFrame(catalog);
        Assert.Single(reader.Read(frame, Start, CancellationToken.None)!.Observations);
        if (explicitReset) reader.Reset();
        else
        {
            using var empty = new Bitmap(frame.Width, frame.Height);
            Assert.Null(reader.Read(empty, Start.AddSeconds(5), CancellationToken.None));
        }
        timer = "19m";
        var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, Start.AddSeconds(10), CancellationToken.None));
        Assert.Equal(TimeSpan.FromMinutes(19), Assert.Single(reading.Observations).Remaining);
    }

    private static AutomaticBuffCatalog CreateCatalog()
    {
        var bundled = AutomaticBuffCatalog.Default;
        var template = Assert.Single(bundled.Templates, item => item.GroupId == "tent-adventures-boon");
        return new([template], _ => bundled.OpenIcon(template));
    }

    private static Bitmap CreateFrame(AutomaticBuffCatalog catalog)
    {
        using var stream = catalog.OpenIcon(Assert.Single(catalog.Templates))!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        using var icon = AutomaticBuffFrameReader.DecodeIcon(bytes.ToArray());
        using var pixels = new Mat(100, 160, MatType.CV_8UC3, Scalar.All(32));
        using (var slot = new Mat(pixels, new Rect(20, 10, icon.Width, icon.Height))) icon.CopyTo(slot);
        using var encoded = new MemoryStream(pixels.ToBytes(".png"));
        using var image = new Bitmap(encoded);
        return new Bitmap(image);
    }
}
