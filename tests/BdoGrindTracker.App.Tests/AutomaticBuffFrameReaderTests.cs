using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class AutomaticBuffFrameReaderTests
{
    [Fact]
    public void ExactImageCacheReusesOnlyIdenticalPixelsAndScaleAndPublishesReadOnlyResults()
    {
        var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog);
        using var bitmap = fixture.Frame(("first", new Rectangle(100, 80, 32, 32)));
        using var frame = CompanionFrameDecoder.Decode(bitmap);
        var first = reader.FindIcons(frame, CancellationToken.None, 32);
        Assert.NotEmpty(first);
        using var samePixels = frame.Clone();
        Assert.Same(first, reader.FindIcons(samePixels, CancellationToken.None, 32));
        var published = Assert.IsAssignableFrom<IList<AutomaticBuffFrameReader.Detection>>(first);
        Assert.Throws<NotSupportedException>(() => published.Clear());

        // Even a single changed byte below the icon, where its countdown is read,
        // must invalidate matching; no image-difference tolerance is permitted.
        var pixel = frame.At<Vec3b>(114, 100);
        pixel.Item0 ^= 1;
        frame.Set(114, 100, pixel);
        var changedPixel = reader.FindIcons(frame, CancellationToken.None, 32);
        Assert.NotSame(first, changedPixel);
        Assert.Same(changedPixel, reader.FindIcons(frame, CancellationToken.None, 32));
        var changedScale = reader.FindIcons(frame, CancellationToken.None, 32.01);
        Assert.NotSame(changedPixel, changedScale);
        using var smaller = new Mat(frame, new Rect(0, 0, frame.Width - 1, frame.Height));
        Assert.NotSame(changedScale, reader.FindIcons(smaller, CancellationToken.None, 32.01));
    }

    [Fact]
    public void ExactImageCacheOwnsInputAndHonorsCancellationAndDisposal()
    {
        var fixture = new Fixture();
        var reader = new AutomaticBuffFrameReader(fixture.Catalog);
        using var bitmap = fixture.Frame(("first", new Rectangle(100, 80, 32, 32)));
        using var pixels = CompanionFrameDecoder.Decode(bitmap);
        IReadOnlyList<AutomaticBuffFrameReader.Detection> first;
        using (var temporaryInput = pixels.Clone())
        {
            first = reader.FindIcons(temporaryInput, CancellationToken.None, 32);
            temporaryInput.SetTo(Scalar.All(0));
        }
        Assert.NotEmpty(first);
        Assert.Same(first, reader.FindIcons(pixels, CancellationToken.None, 32));
        Assert.Throws<OperationCanceledException>(() => reader.FindIcons(pixels, new CancellationToken(true), 32));
        pixels.SetTo(Scalar.All(0));
        Assert.Empty(reader.FindIcons(pixels, CancellationToken.None, 32));
        reader.Dispose();
        reader.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reader.FindIcons(pixels, CancellationToken.None, 32));
    }

    [Fact]
    public void ExactImageCacheDoesNotRetainOversizedInput()
    {
        var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog);
        using var bitmap = fixture.Frame(("first", new Rectangle(100, 80, 32, 32)));
        using var pixels = CompanionFrameDecoder.Decode(bitmap);
        using var oversized = new Mat(1100, 1300, MatType.CV_8UC3, Scalar.All(0));
        using (var destination = new Mat(oversized, new Rect(0, 0, pixels.Width, pixels.Height)))
            pixels.CopyTo(destination);
        var first = reader.FindIcons(oversized, CancellationToken.None, 32);
        var second = reader.FindIcons(oversized, CancellationToken.None, 32);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void ReusedIconMatchesStillReadTimersOnEveryFrame()
    {
        var fixture = new Fixture();
        var calls = 0;
        var remaining = "19m";
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog, (_, _) =>
        {
            calls++;
            return new(remaining, default);
        });
        using var frame = fixture.Frame(("first", new Rectangle(100, 80, 32, 32)));
        var first = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));
        Assert.Equal(TimeSpan.FromMinutes(19), Assert.Single(first.Observations).Remaining);
        var previousCalls = calls;
        remaining = "18m";
        var second = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));
        Assert.Equal(TimeSpan.FromMinutes(18), Assert.Single(second.Observations).Remaining);
        Assert.True(calls > previousCalls);
    }

    [Fact]
    public void TemplateCacheReusesPixelsAcrossScansAndTracksChangedHudScale()
    {
        var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog);
        foreach (var width in new[] { 32, 64, 48, 32 })
        {
            using var bitmap = fixture.Frame(("first", new Rectangle(100, 80, width, width)));
            using var frame = CompanionFrameDecoder.Decode(bitmap);
            using var coldReader = new AutomaticBuffFrameReader(fixture.Catalog);
            var expected = coldReader.FindIcons(frame, CancellationToken.None, width);
            var actual = reader.FindIcons(frame, CancellationToken.None, width);
            Assert.NotEmpty(actual);
            Assert.Equal(expected.Select(match => (match.Template.Id, match.Bounds)),
                actual.Select(match => (match.Template.Id, match.Bounds)));
            var resizeCount = reader.TemplateResizeCount;
            var repeated = reader.FindIcons(frame, CancellationToken.None, width);
            Assert.Equal(actual.Select(match => (match.Template.Id, match.Bounds)),
                repeated.Select(match => (match.Template.Id, match.Bounds)));
            Assert.Equal(resizeCount, reader.TemplateResizeCount);
        }
    }

    [Fact]
    public void NearThresholdCompetitorPreventsAFalseVariantAssignment()
    {
        var fixture = new Fixture(second: true);
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog);
        AutomaticBuffFrameReader.Detection[] matches =
        [
            new(fixture.Catalog.Templates[0], new Rectangle(40, 40, 32, 32), .905),
            new(fixture.Catalog.Templates[1], new Rectangle(40, 40, 32, 32), .899),
        ];

        var (accepted, unknown) = reader.ClassifyMatches(matches);

        Assert.Empty(accepted);
        Assert.Equal("harmony-draught-demihuman", Assert.Single(unknown));
    }

    [Fact]
    public void RejectedLookalikeElsewhereDoesNotInvalidateTheRealBuff()
    {
        var fixture = new Fixture(second: true);
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog);
        AutomaticBuffFrameReader.Detection[] matches =
        [
            new(fixture.Catalog.Templates[0], new Rectangle(40, 40, 32, 32), .98),
            new(fixture.Catalog.Templates[1], new Rectangle(100, 40, 32, 32), .99),
            new(fixture.Catalog.Templates[0], new Rectangle(100, 40, 32, 32), .93),
        ];

        var (accepted, unknown) = reader.ClassifyMatches(matches);

        Assert.Empty(unknown);
        Assert.Equal(matches.Take(2), accepted);
    }

    [Fact]
    public void IndependentPublisherHudLocatesBodyEnhancementHarmonyAndCronWithoutTimerOcr()
    {
        using var screenshot = new Bitmap(Path.Combine(AppContext.BaseDirectory, "fixtures", "buffs",
            "bar-official-harmony-20240725.png"));
        using var frame = CompanionFrameDecoder.Decode(screenshot);
        using var reader = new AutomaticBuffFrameReader(recognize: (_, _) =>
            throw new InvalidOperationException("This fixture contains Korean countdowns; verify symbols only."));

        var detections = reader.FindIcons(frame, CancellationToken.None);

        Assert.Contains(detections, detection => detection.Template.GroupId.Contains("body-enhancement", StringComparison.Ordinal));
        Assert.Contains(detections, detection => detection.Template.GroupId.Contains("harmony", StringComparison.Ordinal));
        Assert.Contains(detections, detection => detection.Template.GroupId.Contains("cron", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(32, 18, 24)]
    [InlineData(48, 187, 92)]
    [InlineData(64, 306, 137)]
    public void FindsMovedAndScaledIconsWithoutAProfile(int width, int x, int y)
    {
        var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog, (_, _) => new("19m", default));
        using var frame = fixture.Frame(("first", new Rectangle(x, y, width, width)));

        var result = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));

        var observation = Assert.Single(result.Observations);
        Assert.Equal("harmony-draught-demihuman", observation.BuffId);
        Assert.Equal(TimeSpan.FromMinutes(19), observation.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(1), observation.TimerPrecision);
        Assert.True(observation.ConsumptionAttributionConfirmed);
        Assert.Empty(result.UnknownBuffIds);
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("99m")]
    public void UnreadableOrImpossibleTimerDoesNotInventAnObservation(string timer)
    {
        var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog, (_, _) => new(timer, default));
        using var frame = fixture.Frame(("first", new Rectangle(100, 80, 32, 32)));

        var result = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));

        Assert.Empty(result.Observations);
        Assert.Equal("harmony-draught-demihuman", Assert.Single(result.UnknownBuffIds));
        Assert.Contains("noch unklar", reader.LastDiagnostic);
    }

    [Fact]
    public void DuplicateIconsAreUnknownAndNeverCountedTwice()
    {
        var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog,
            (_, _) => throw new InvalidOperationException("Ambiguous icons must not be OCRed."));
        using var frame = fixture.Frame(("first", new Rectangle(30, 30, 32, 32)),
            ("first", new Rectangle(170, 110, 32, 32)));

        var result = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));

        Assert.Empty(result.Observations);
        Assert.Equal("harmony-draught-demihuman", Assert.Single(result.UnknownBuffIds));
    }

    [Fact]
    public void CompetingTemplatesAtTheSamePositionInvalidateBothCandidates()
    {
        var fixture = new Fixture(competing: true);
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog, (_, _) => new("19m", default));
        using var frame = fixture.Frame(("first", new Rectangle(100, 80, 32, 32)));

        var result = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));

        Assert.Empty(result.Observations);
        Assert.Contains("harmony-draught-demihuman", result.UnknownBuffIds);
        Assert.Contains("perfume-of-courage", result.UnknownBuffIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneUnreadableTimerPreservesOtherRecognizedBuffs(bool throws)
    {
        var fixture = new Fixture(second: true);
        var calls = 0;
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog, (_, _) =>
        {
            calls++;
            if (throws && calls == 1) throw new IOException("One timer could not be read.");
            return new(calls <= (throws ? 1 : 4) ? "unreadable" : "19m", default);
        });
        using var frame = fixture.Frame(("first", new Rectangle(30, 30, 32, 32)),
            ("second", new Rectangle(170, 110, 32, 32)));

        var result = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));

        var known = Assert.Single(result.Observations);
        var unknown = Assert.Single(result.UnknownBuffIds);
        Assert.NotEqual(known.BuffId, unknown);
        Assert.Equal(TimeSpan.FromMinutes(19), known.Remaining);
    }

    [Fact]
    public void MissingSymbolsRemainUnknownInsteadOfBecomingAConfirmedEmptyBar()
    {
        var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog,
            (_, _) => throw new InvalidOperationException("No icon means no timer OCR."));
        using var frame = fixture.Frame();

        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Contains("Noch kein unterstütztes Symbol", reader.LastDiagnostic);
    }

    [Fact]
    public void IndistinguishableVariantsProduceAnUnpricedGroupInsteadOfGuessingAnItem()
    {
        var fixture = new Fixture(group: true);
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog, (_, _) => new("19m", default));
        using var frame = fixture.Frame(("first", new Rectangle(100, 80, 32, 32)));

        var result = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));
        var group = Assert.Single(fixture.Catalog.GroupDefinitions);

        Assert.Equal(group.Id, Assert.Single(result.Observations).BuffId);
        Assert.StartsWith("automatic-", group.Id);
        Assert.Contains("Variante unbekannt", group.DisplayName);
        Assert.Null(group.MarketItemId);
        Assert.Null(group.FixedUnitPrice);
        Assert.Null(BuffPriceCatalog.GetPrice(group, new("eu", [])));
    }

    [Fact]
    public void CanceledRecognitionCannotPublishAPartialScan()
    {
        var fixture = new Fixture();
        using var reader = new AutomaticBuffFrameReader(fixture.Catalog,
            (_, _) => throw new OperationCanceledException());
        using var frame = fixture.Frame(("first", new Rectangle(100, 80, 32, 32)));

        Assert.Throws<OperationCanceledException>(() => reader.Read(frame, CancellationToken.None));
    }

    [Fact]
    public void RouterUsesAutomaticDetectionByDefaultAndNeverFallsBackFromAnInvalidProfile()
    {
        var useProfile = false;
        var automatic = new ReaderStub(new([new("harmony-draught", TimeSpan.FromMinutes(19), TimeSpan.FromMinutes(1))]), "automatic");
        var profile = new ReaderStub(null, "invalid selected profile");
        using var router = new BuffRecognitionRouter(() => useProfile, automatic, profile);
        using var frame = new Bitmap(20, 20);

        Assert.NotNull(router.Read(frame, CancellationToken.None));
        Assert.Equal("automatic", router.LastDiagnostic);
        Assert.Equal(1, automatic.Calls);
        Assert.Equal(0, profile.Calls);

        useProfile = true;
        Assert.Null(router.Read(frame, CancellationToken.None));
        Assert.Equal("invalid selected profile", router.LastDiagnostic);
        Assert.Equal(1, automatic.Calls);
        Assert.Equal(1, profile.Calls);

        useProfile = false;
        Assert.NotNull(router.Read(frame, CancellationToken.None));
        Assert.Equal(2, automatic.Calls);
    }

    private sealed class ReaderStub(BuffFrameReading? reading, string diagnostic) : IBuffFrameReader
    {
        public string LastDiagnostic => diagnostic;
        internal int Calls { get; private set; }
        public BuffFrameReading? Read(Bitmap frame, CancellationToken cancellationToken) { Calls++; return reading; }
        public void Dispose() { }
    }

    private sealed class Fixture
    {
        private readonly Dictionary<string, byte[]> _icons = new(StringComparer.Ordinal);
        internal AutomaticBuffCatalog Catalog { get; }

        internal Fixture(bool second = false, bool competing = false, bool group = false)
        {
            _icons["first"] = Icon(34);
            _icons["second"] = competing ? _icons["first"] : Icon(79);
            List<AutomaticBuffCatalog.Template> templates = [new("first", "harmony",
                group ? ["harmony-draught-demihuman", "harmony-draught-kamasylvia"] : ["harmony-draught-demihuman"],
                "first", new Rectangle(0, 33, 40, 16), .9)];
            if (second || competing) templates.Add(new("second", "perfume", ["perfume-of-courage"],
                "second", new Rectangle(0, 33, 40, 16), .9));
            Catalog = new(templates, path => new MemoryStream(_icons[path], writable: false));
        }

        internal Bitmap Frame(params (string Icon, Rectangle Bounds)[] placements)
        {
            using var frame = new Mat(300, 460, MatType.CV_8UC3, new Scalar(8, 12, 18));
            foreach (var placement in placements)
            {
                using var icon = Cv2.ImDecode(_icons[placement.Icon], ImreadModes.Color);
                using var scaled = new Mat();
                Cv2.Resize(icon, scaled, new(placement.Bounds.Width, placement.Bounds.Height), interpolation: InterpolationFlags.Area);
                using var destination = new Mat(frame, new Rect(placement.Bounds.X, placement.Bounds.Y,
                    placement.Bounds.Width, placement.Bounds.Height));
                scaled.CopyTo(destination);
            }
            using var stream = new MemoryStream(frame.ToBytes(".png"));
            using var bitmap = new Bitmap(stream);
            return new Bitmap(bitmap);
        }

        private static byte[] Icon(int seed)
        {
            using var icon = new Mat(32, 32, MatType.CV_8UC3);
            var random = new Random(seed);
            for (var y = 0; y < 32; y += 4)
            for (var x = 0; x < 32; x += 4)
                Cv2.Rectangle(icon, new Rect(x, y, 4, 4), new Scalar(random.Next(256), random.Next(256), random.Next(256)), -1);
            return icon.ToBytes(".png");
        }
    }
}
