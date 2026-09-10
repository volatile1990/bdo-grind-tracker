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
    [InlineData("8h 12m IO s", 29530, 1)]
    [InlineData("8h 12m IOS", 29530, 1)]
    [InlineData("8h12mI0s", 29530, 1)]
    [InlineData("8h12m1Os", 29530, 1)]
    [InlineData("6h 29m 3Os", 23370, 1)]
    [InlineData("I0h 2m", 36120, 60)]
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
    [InlineData("IOs")]
    [InlineData("Ih Om")]
    [InlineData("L 8h 12m IO s")]
    [InlineData("8h 12m l0s")]
    [InlineData("8h 12m iOs")]
    [InlineData("8h 12m S0s")]
    [InlineData("8h 12m OIOs")]
    [InlineData("8h 12m IO seconds")]
    [InlineData("8O 12m 10s")]
    [InlineData("3Oh 12m IOs")]
    [InlineData("8h 6Om IOs")]
    [InlineData("8h 12m 6Os")]
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
    [InlineData("8h 12m IO s", "8h 12m 11s")]
    public void ConflictingValidPreparationsDoNotInventATimerChange(string first, string second)
    {
        using var frame = new Bitmap(400, 150);
        var calls = 0;
        using var reader = new LootScrollTimerReader((_, _) => ++calls switch { 1 => first, 2 => second, _ => "ambiguous" });
        Assert.Null(reader.Read(frame, new(300, 30, 83, 83), CancellationToken.None));
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData("8h 12m 10s", "2m 10s", "8h 12m IO s", 29530)]
    [InlineData("2m 10s", "8h 12m 10s", "8h 12m IO s", 29530)]
    [InlineData("8h 12m 10s", "2m 10s", "2m 10s", 130)]
    public void AThirdSmallPreparationResolvesOnlyAnExactMajority(string first, string second, string third, int seconds)
    {
        using var frame = new Bitmap(400, 150);
        var calls = 0;
        using var reader = new LootScrollTimerReader((image, _) =>
        {
            Assert.InRange(image.Width, 20, 1000);
            Assert.InRange(image.Height, 20, 180);
            return ++calls switch { 1 => first, 2 => second, _ => third };
        });
        Assert.Equal(new LootScrollTimerReading(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1)),
            reader.Read(frame, new(300, 30, 83, 83), CancellationToken.None));
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData("8h 12m 11s")]
    [InlineData("8h 12m")]
    [InlineData("unreadable")]
    public void AThirdUncorroboratedResultDoesNotResolveTheConflict(string third)
    {
        using var frame = new Bitmap(400, 150);
        var calls = 0;
        using var reader = new LootScrollTimerReader((_, _) => ++calls switch
        {
            1 => "8h 12m 10s", 2 => "2m 10s", _ => third
        });
        Assert.Null(reader.Read(frame, new(300, 30, 83, 83), CancellationToken.None));
        Assert.Equal(3, calls);
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
    [InlineData("8h 12m IO s", "8h12m10s")]
    [InlineData("L 8h 12m IO s", "8h 12m IOS")]
    [InlineData("8h 12m IOS", "unreadable")]
    public void NumericOcrCorrectionsAgreeWithTheSecondSmallPreparation(string first, string second)
    {
        using var frame = new Bitmap(400, 150);
        var calls = 0;
        using var reader = new LootScrollTimerReader((_, _) => ++calls == 1 ? first : second);
        Assert.Equal(new LootScrollTimerReading(TimeSpan.FromSeconds(29530), TimeSpan.FromSeconds(1)),
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
    [InlineData("inactive-eight-hours-user-20260910.png", 29530, 1, 0f)]
    [InlineData("inactive-eight-hours-user-20260910.png", 29530, 1, 2.5f)]
    [InlineData("inactive-eight-hours-user-20260910.png", 29530, 1, 5f)]
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

    [Theory]
    [InlineData("inactive-user-20260910.png")]
    [InlineData("active-2-user-20260910.png")]
    public void TimerOcrFailureCannotClassifyFromTheVisibleSymbol(string name)
    {
        using var detector = new LootScrollFrameDetector(new BrokenTimer());
        using var frame = Load(name);
        Assert.Equal(LootScrollReading.Unknown, detector.Analyze(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData("inactive-user-20260910.png")]
    [InlineData("active-2-user-20260910.png")]
    public void AReadableTimerCarriesNoSymbolDerivedStatusOrLevel(string name)
    {
        using var detector = new LootScrollFrameDetector(new LootScrollTimerReader((_, _) => "8h12m10s"));
        using var frame = Load(name);
        Assert.Equal(LootScrollReading.Unknown with
        {
            RemainingTime = TimeSpan.FromSeconds(29530), TimerResolution = TimeSpan.FromSeconds(1)
        }, detector.Analyze(frame, CancellationToken.None));
    }

    [Theory]
    [InlineData("inactive-user-20260910.png")]
    [InlineData("active-2-user-20260910.png")]
    public void UnreadableTimerCannotClassifyFromTheVisibleSymbol(string name)
    {
        using var detector = new LootScrollFrameDetector(new LootScrollTimerReader((_, _) => "ambiguous"));
        using var frame = Load(name);
        Assert.Equal(LootScrollReading.Unknown, detector.Analyze(frame, CancellationToken.None));
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
            Assert.Equal(LootScrollStatus.Unknown, reading.Status);
            Assert.Null(reading.Level);
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
        Assert.InRange(calls, 2, 3);
        if (reading is not null)
            Assert.Equal(new LootScrollTimerReading(TimeSpan.FromSeconds(expectedSeconds), TimeSpan.FromSeconds(expectedResolution)), reading);
    }

    [Theory]
    [InlineData(1920, 1080, .75, 0f)]
    [InlineData(1920, 1080, 1, 0f)]
    [InlineData(1920, 1080, 1.01, 2.5f)]
    [InlineData(2560, 1440, .99, 0f)]
    [InlineData(2560, 1440, 1.25, 0f)]
    [InlineData(2560, 1440, 1, 5f)]
    [InlineData(3840, 2160, .75, 0f)]
    [InlineData(3840, 2160, 1, 0f)]
    [InlineData(3840, 2160, 1.25, 2.5f)]
    public void ReadsNewUserEightHourTimerOnMonitor(int width, int height, double uiScale, float whiteLevel)
    {
        var engine = CompanionWindowsOcrRecognizer.TryCreate();
        if (engine is null) { output.WriteLine("Native OCR unavailable; parser and bounded reader have independent tests."); return; }
        using var original = Load("inactive-eight-hours-user-20260910.png");
        using var source = whiteLevel == 0 ? (Bitmap)original.Clone() : LootScrollGaugeDetectorTests.ToneMapHdr(original, whiteLevel);
        using var frame = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var scaledWidth = (int)Math.Round(source.Width * uiScale);
        var scaledHeight = (int)Math.Round(source.Height * uiScale);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(37, 49, 61));
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(width - scaledWidth - 173, height - scaledHeight - 89, scaledWidth, scaledHeight));
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var gaugeDetector = new LootScrollGaugeDetector();
        var gauge = Assert.IsType<LootScrollGaugeMatch>(gaugeDetector.FindGauge(frame, CancellationToken.None));
        output.WriteLine($"{width}x{height}, UI {uiScale}, HDR {whiteLevel}; gauge {gauge.Bounds}; search {watch.ElapsedMilliseconds}ms");
        var calls = 0;
        using var reader = new LootScrollTimerReader((image, token) =>
        {
            Assert.InRange(image.Width, 20, 1000);
            Assert.InRange(image.Height, 20, 180);
            var text = engine.Recognize(image, token).Text;
            output.WriteLine($"Preparation {++calls}: {text}");
            return text;
        });
        Assert.Equal(new LootScrollTimerReading(TimeSpan.FromSeconds(29530), TimeSpan.FromSeconds(1)),
            reader.Read(frame, gauge.Bounds, CancellationToken.None));
        Assert.InRange(calls, 2, 3);
        output.WriteLine($"Gauge and {calls} small OCR preparations: {watch.ElapsedMilliseconds}ms");
    }

    private static Bitmap Load(string name) => new(Path.Combine(AppContext.BaseDirectory, "fixtures", "loot-scroll", name));
    private sealed class BrokenTimer : ILootScrollTimerReader
    {
        public LootScrollTimerReading? Read(Bitmap frame, Rectangle gauge, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic optional OCR failure");
        public void Dispose() { }
    }
}
