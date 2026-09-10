using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Ocr;
using Xunit.Abstractions;

namespace BdoGrindTracker.App.Tests;

public sealed class LootScrollTimerReaderTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("6h 29m 38s", 23378, 1)]
    [InlineData("6h29m38s", 23378, 1)]
    [InlineData("6h 24m", 23040, 60)]
    [InlineData("29m 38s", 1778, 1)]
    [InlineData("6 Std. 29 Min. 38 Sek.", 23378, 1)]
    [InlineData("0s", 0, 1)]
    [InlineData("10h", 36000, 3600)]
    public void ParsesRemainingTimeAndItsActualPrecision(string text, int seconds, int resolution)
    {
        Assert.Equal(new LootScrollTimerReading(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(resolution)),
            LootScrollTimerReader.TryParse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("6h 99m 38s")]
    [InlineData("25h")]
    [InlineData("38")]
    [InlineData("Loot Scroll Level 2")]
    [InlineData("6h 29m 3Os")]
    [InlineData("-1s")]
    [InlineData("6h 29m 38s cooldown")]
    [InlineData("6h 29m 38s 7h 14m")]
    public void RejectsAmbiguousOrInvalidTimerText(string text) => Assert.Null(LootScrollTimerReader.TryParse(text));

    [Fact]
    public void SearchesOnlyAdjacentLabelAndBoundsOcrWork()
    {
        using var frame = new Bitmap(3840, 2160);
        var calls = 0;
        using var reader = new LootScrollTimerReader((image, _) =>
        {
            calls++;
            Assert.InRange(image.Width, 20, 800);
            Assert.InRange(image.Height, 20, 140);
            return calls == 1 ? "unreadable" : "6h29m38s";
        });
        Assert.Equal(TimeSpan.FromSeconds(23378), reader.Read(frame, new(3100, 1700, 83, 83), CancellationToken.None)?.RemainingTime);
        Assert.Equal(2, calls);
        Assert.Null(reader.Read(frame, new(0, 0, 83, 83), CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("5h 40m 16s", "5h 40m 15s")]
    [InlineData("5h 40m 16s", "5h 4m 16s")]
    [InlineData("5h 4m 16s", "5h 40m 16s")]
    [InlineData("5h 40m 16s", "5h 40m")]
    [InlineData("5h 40m 0s", "5h 40m")]
    [InlineData("5h 40m", "5h 40m 0s")]
    public void ConflictingValidPreparationsDoNotInventATimerChange(string first, string second)
    {
        using var frame = new Bitmap(400, 150);
        var calls = 0;
        using var reader = new LootScrollTimerReader((_, _) => ++calls == 1 ? first : second);
        Assert.Null(reader.Read(frame, new(300, 30, 83, 83), CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("5h 40m 16s", "5h40m16s")]
    [InlineData("5h 40m 16s", "unreadable")]
    [InlineData("unreadable", "5h 40m 16s")]
    public void CompatibleOrSingleReadablePreparationPreservesTheTimer(string first, string second)
    {
        using var frame = new Bitmap(400, 150);
        var calls = 0;
        using var reader = new LootScrollTimerReader((_, _) => ++calls == 1 ? first : second);
        Assert.Equal(new LootScrollTimerReading(TimeSpan.FromSeconds(20416), TimeSpan.FromSeconds(1)),
            reader.Read(frame, new(300, 30, 83, 83), CancellationToken.None));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("inactive-user-20260910.png", 23378, 1, 0f)]
    [InlineData("inactive-expanded-user-20260910.png", 23378, 1, 0f)]
    [InlineData("active-2-user-20260910.png", 23040, 60, 0f)]
    [InlineData("inactive-user-20260910.png", 23378, 1, 2.5f)]
    [InlineData("inactive-expanded-user-20260910.png", 23378, 1, 5f)]
    [InlineData("active-2-user-20260910.png", 23040, 60, 2.5f)]
    [InlineData("inactive-zero-user-20260910.png", 20416, 1, 0f)]
    [InlineData("inactive-zero-user-20260910.png", 20416, 1, 2.5f)]
    [InlineData("inactive-zero-user-20260910.png", 20416, 1, 5f)]
    public void ReadsTimerNextToTheActualUserHud(string name, int expectedSeconds, int expectedResolution, float whiteLevel)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) { output.WriteLine("Native OCR unavailable; parser and bounded reader have independent tests."); return; }
        using var original = Load(name);
        using var frame = whiteLevel == 0 ? (Bitmap)original.Clone() : LootScrollGaugeDetectorTests.ToneMapHdr(original, whiteLevel);
        using var detector = new LootScrollGaugeDetector();
        var gauge = Assert.IsType<LootScrollGaugeMatch>(detector.FindGauge(frame, CancellationToken.None));
        using var reader = new LootScrollTimerReader((image, token) =>
        {
            var recognized = engine.Recognize(image, token).Text;
            output.WriteLine($"{engine.LanguageTag}: {recognized}");
            return recognized;
        });
        Assert.Equal(new LootScrollTimerReading(TimeSpan.FromSeconds(expectedSeconds), TimeSpan.FromSeconds(expectedResolution)),
            reader.Read(frame, gauge.Bounds, CancellationToken.None));
    }

    [Fact]
    public void TimerOcrFailurePreservesTheVisibleSymbol()
    {
        using var detector = new LootScrollFrameDetector(new BrokenTimer());
        using var frame = Load("inactive-user-20260910.png");
        Assert.Equal(new LootScrollReading(LootScrollStatus.Inactive), detector.Analyze(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData(0.99, 0f)]
    [InlineData(1.01, 0f)]
    [InlineData(0.99, 2.5f)]
    [InlineData(1.01, 2.5f)]
    [InlineData(0.99, 5f)]
    [InlineData(1.01, 5f)]
    public void StationaryZeroGaugeKeepsTheSameTimerAcrossRepeatedReads(double scale, float whiteLevel)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) { output.WriteLine("Native OCR unavailable; parser and bounded reader have independent tests."); return; }
        using var original = Load("inactive-zero-user-20260910.png");
        using var scaled = new Bitmap((int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale),
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(original, new Rectangle(0, 0, scaled.Width, scaled.Height));
        }
        using var frame = whiteLevel == 0 ? (Bitmap)scaled.Clone() : LootScrollGaugeDetectorTests.ToneMapHdr(scaled, whiteLevel);
        using var gaugeDetector = new LootScrollGaugeDetector();
        var gauge = Assert.IsType<LootScrollGaugeMatch>(gaugeDetector.FindGauge(frame, CancellationToken.None));
        output.WriteLine($"Scale {scale}; HDR {whiteLevel}; gauge {gauge.Reading.Status}; bounds {gauge.Bounds}");
        using var detector = new LootScrollFrameDetector(new LootScrollTimerReader((image, token) =>
        {
            var recognized = engine.Recognize(image, token).Text;
            output.WriteLine($"{engine.LanguageTag}: {recognized}");
            return recognized;
        }));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var reading = detector.Analyze(frame, CancellationToken.None);
            output.WriteLine($"Read {attempt}: {reading}");
            Assert.Equal(LootScrollStatus.Inactive, reading.Status);
            Assert.Equal(TimeSpan.FromSeconds(20416), reading.RemainingTime);
            Assert.Equal(TimeSpan.FromSeconds(1), reading.TimerResolution);
        }
    }

    [Theory]
    [InlineData("inactive-user-20260910.png", 23378, 1, 0)]
    [InlineData("inactive-user-20260910.png", 23378, 1, 255)]
    [InlineData("inactive-expanded-user-20260910.png", 23378, 1, 0)]
    [InlineData("inactive-expanded-user-20260910.png", 23378, 1, 255)]
    [InlineData("inactive-zero-user-20260910.png", 20416, 1, 0)]
    [InlineData("inactive-zero-user-20260910.png", 20416, 1, 255)]
    [InlineData("active-2-user-20260910.png", 23040, 60, 0)]
    [InlineData("active-2-user-20260910.png", 23040, 60, 255)]
    public void UnrelatedPixelsLeftOfTheHudDoNotProduceAnotherTimer(string name, int expectedSeconds,
        int expectedResolution, int background)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) { output.WriteLine("Native OCR unavailable; parser and bounded reader have independent tests."); return; }
        using var original = Load(name);
        using var frame = new Bitmap(original.Width + 320, original.Height + 80,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(frame))
        using (var unrelated = new SolidBrush(Color.FromArgb(255 - background, 255 - background, 255 - background)))
        {
            graphics.Clear(Color.FromArgb(background, background, background));
            graphics.FillPolygon(unrelated, [new Point(130, 0), new Point(295, 0),
                new Point(220, frame.Height), new Point(180, frame.Height)]);
            graphics.DrawImageUnscaled(original, 320, 40);
        }
        using var detector = new LootScrollGaugeDetector();
        var gauge = Assert.IsType<LootScrollGaugeMatch>(detector.FindGauge(frame, CancellationToken.None));
        output.WriteLine($"Background {background}; bounds {gauge.Bounds}; timer crop {LootScrollTimerReader.TimerRegion(frame.Size, gauge.Bounds)}");
        var calls = 0;
        using var reader = new LootScrollTimerReader((image, token) =>
        {
            var recognized = engine.Recognize(image, token).Text;
            output.WriteLine($"Preparation {++calls}, {engine.LanguageTag}: {recognized}");
            return recognized;
        });
        var reading = reader.Read(frame, gauge.Bounds, CancellationToken.None);
        output.WriteLine($"Accepted timer: {reading?.ToString() ?? "Unknown"}");
        Assert.Equal(2, calls);
        if (reading is not null)
            Assert.Equal(new LootScrollTimerReading(TimeSpan.FromSeconds(expectedSeconds), TimeSpan.FromSeconds(expectedResolution)), reading);
    }

    private static Bitmap Load(string name) => new(Path.Combine(AppContext.BaseDirectory, "fixtures", "loot-scroll", name));
    private sealed class BrokenTimer : ILootScrollTimerReader
    {
        public LootScrollTimerReading? Read(Bitmap frame, Rectangle gauge, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic optional OCR failure");
        public void Dispose() { }
    }
}
