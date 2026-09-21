using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffFrameReaderTests
{
    [Theory]
    [InlineData("19m", 1140, 60)]
    [InlineData("19 Min.", 1140, 60)]
    [InlineData("2h", 7200, 3600)]
    [InlineData("1 Std. 30 Min.", 5400, 60)]
    [InlineData("1m 25s", 85, 1)]
    [InlineData("1:25", 85, 1)]
    [InlineData("1:00:25", 3625, 1)]
    [InlineData("35 Sek.", 35, 1)]
    [InlineData("119:59", 7199, 1)]
    public void TimerPreservesDisplayedPrecision(string text, int seconds, int precision)
    {
        var actual = BuffFrameReader.ParseTimer(text);
        Assert.NotNull(actual);
        Assert.Equal(TimeSpan.FromSeconds(seconds), actual.Value.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(precision), actual.Value.Precision);
    }

    [Theory]
    [InlineData("")]
    [InlineData("20")]
    [InlineData("l9m")]
    [InlineData("19m 20m")]
    [InlineData("1:80")]
    [InlineData("1:70:00")]
    [InlineData("1h 80m")]
    [InlineData("1m 80s")]
    [InlineData("0s")]
    [InlineData("25h")]
    [InlineData("19m nearby text")]
    [InlineData("-1m")]
    public void InvalidOrAmbiguousTimersAreUnknown(string text) => Assert.Null(BuffFrameReader.ParseTimer(text));

    [Fact]
    public void SimilarCompetingIconsAndDuplicateLocationsAreRejected()
    {
        var first = Template("harmony-draught");
        var second = Template("perfume-of-courage");
        var bounds = new Rectangle(20, 10, 32, 32);
        Assert.Empty(BuffFrameReader.SelectUnambiguous([
            new(first, bounds, .96), new(second, bounds, .94)]));
        Assert.Empty(BuffFrameReader.SelectUnambiguous([
            new(first, bounds, .96), new(first, new Rectangle(100, 10, 32, 32), .94)]));
        Assert.Equal("harmony-draught", Assert.Single(BuffFrameReader.SelectUnambiguous([
            new(first, bounds, .99), new(second, bounds, .93)])).Template.BuffId);
    }

    [Fact]
    public void CalibratedIconIsFoundAfterMovingWithinBarAndTimerIsRead()
    {
        using var fixture = new ImageFixture();
        using var reader = new BuffFrameReader(() => fixture.Profile,
            (_, _) => new("19m", default));
        using var frame = fixture.Frame(90);

        var reading = reader.Read(frame, CancellationToken.None);

        var observation = Assert.Single(Assert.IsType<BuffFrameReading>(reading).Observations);
        Assert.Equal("harmony-draught", observation.BuffId);
        Assert.Equal(TimeSpan.FromMinutes(19), observation.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(1), observation.TimerPrecision);
        Assert.Contains("1 Buff(s)", reader.LastDiagnostic);
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("99m")]
    public void VisibleIconWithUnusableTimerOnlyInvalidatesThatBuff(string timer)
    {
        using var fixture = new ImageFixture();
        using var reader = new BuffFrameReader(() => fixture.Profile, (_, _) => new(timer, default));
        using var frame = fixture.Frame(50);
        var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));
        Assert.Empty(reading.Observations);
        Assert.Equal("harmony-draught", Assert.Single(reading.UnknownBuffIds));
        Assert.Contains("Restzeit", reader.LastDiagnostic);
    }

    [Fact]
    public void ConflictingOcrVariantsAreUnknown()
    {
        using var fixture = new ImageFixture();
        var count = 0;
        using var reader = new BuffFrameReader(() => fixture.Profile, (_, _) => new(++count == 1 ? "19m" : "10m", default));
        using var frame = fixture.Frame(50);
        var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));
        Assert.Empty(reading.Observations);
        Assert.Equal("harmony-draught", Assert.Single(reading.UnknownBuffIds));
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData("2h", "2h", true)]
    [InlineData("2h", "", false)]
    [InlineData("2h", "2m", false)]
    public void SmallerTimerFallbackRequiresTwoAgreeingReadings(string first, string second, bool expected)
    {
        var results = new Queue<string>(["", "", first, second]);
        using var reader = new BuffFrameReader(() => null, (_, _) => new(results.Dequeue(), default));
        using var bar = new OpenCvSharp.Mat(30, 60, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.All(32));

        var timer = reader.ReadTimer(bar, new Rectangle(0, 0, 54, 21), CancellationToken.None);

        Assert.Equal(expected, timer.HasValue);
        if (expected)
        {
            Assert.Equal(TimeSpan.FromHours(2), timer!.Value.Remaining);
            Assert.Equal(TimeSpan.FromHours(1), timer.Value.Precision);
        }
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneFailedTimerDoesNotDiscardAnotherReadableBuff(bool throws)
    {
        using var fixture = new ImageFixture();
        var calls = 0;
        using var reader = new BuffFrameReader(() => fixture.ProfileWithSecond, (_, _) =>
        {
            calls++;
            if (throws && calls == 1) throw new IOException("OCR failed for one timer");
            return new(calls <= (throws ? 1 : 4) ? "unreadable" : "19m", default);
        });
        using var frame = fixture.Frame(10);
        fixture.AddIcon(frame, 110, second: true);
        var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));
        var known = Assert.Single(reading.Observations);
        var unknown = Assert.Single(reading.UnknownBuffIds);
        Assert.NotEqual(known.BuffId, unknown);
        Assert.Equal(TimeSpan.FromMinutes(19), known.Remaining);
        Assert.Contains("Restzeit", reader.LastDiagnostic);
    }

    [Fact]
    public void DuplicateBuffIconDoesNotDiscardAnUnrelatedUnambiguousBuff()
    {
        using var fixture = new ImageFixture();
        using var reader = new BuffFrameReader(() => fixture.ProfileWithSecond, (_, _) => new("19m", default));
        using var frame = fixture.Frame(10);
        fixture.AddIcon(frame, 65);
        fixture.AddIcon(frame, 120, second: true);
        var reading = Assert.IsType<BuffFrameReading>(reader.Read(frame, CancellationToken.None));
        Assert.Equal("perfume-of-courage", Assert.Single(reading.Observations).BuffId);
        Assert.Equal("harmony-draught", Assert.Single(reading.UnknownBuffIds));
        Assert.Contains("mehrdeutig", reader.LastDiagnostic);
    }

    [Fact]
    public void CancellationDuringTimerOcrStillInterruptsTheEntireScan()
    {
        using var fixture = new ImageFixture();
        using var reader = new BuffFrameReader(() => fixture.ProfileWithSecond,
            (_, _) => throw new OperationCanceledException());
        using var frame = fixture.Frame(10);
        fixture.AddIcon(frame, 110, second: true);
        Assert.Throws<OperationCanceledException>(() => reader.Read(frame, CancellationToken.None));
    }

    [Fact]
    public void WrongFrameSizeDoesNotAttemptRecognition()
    {
        using var fixture = new ImageFixture();
        using var reader = new BuffFrameReader(() => fixture.Profile, (_, _) => throw new InvalidOperationException());
        using var frame = new Bitmap(100, 100);
        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Contains("Auflösung", reader.LastDiagnostic);
    }

    [Fact]
    public void HiddenBarCannotBecomeAnObservedEmptyBuffList()
    {
        using var fixture = new ImageFixture();
        using var reader = new BuffFrameReader(() => fixture.Profile, (_, _) => throw new InvalidOperationException());
        using var frame = new Bitmap(200, 100);
        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Contains("Keine ausgewählte", reader.LastDiagnostic);
    }

    [Fact]
    public void TemplatesAreReusedAcrossEquivalentProfilesAndReloadedAfterFileOrProfileChanges()
    {
        using var fixture = new ImageFixture();
        var profile = fixture.Profile;
        // Loading JSON produces new lists and records each time; these must still reuse cached pixels.
        using var reader = new BuffFrameReader(() => profile with
        { Templates = profile.Templates.Select(template => template with { }).ToArray() }, (_, _) => new("19m", default));
        using var frame = fixture.Frame(50);
        Assert.NotNull(reader.Read(frame, CancellationToken.None));
        Assert.NotNull(reader.Read(frame, CancellationToken.None));
        Assert.Equal(1, reader.TemplateLoadCount);

        var icon = profile.Templates[0].IconPath;
        File.SetLastWriteTimeUtc(icon, File.GetLastWriteTimeUtc(icon).AddMinutes(1));
        Assert.NotNull(reader.Read(frame, CancellationToken.None));
        Assert.Equal(2, reader.TemplateLoadCount);

        profile = profile with { Templates = [profile.Templates[0] with { MinimumSimilarity = .95 }] };
        Assert.NotNull(reader.Read(frame, CancellationToken.None));
        Assert.Equal(3, reader.TemplateLoadCount);

        File.Delete(icon);
        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Contains("fehlt", reader.LastDiagnostic);
    }

    [Fact]
    public void ChangedFilePixelsCannotContinueMatchingTheCachedImage()
    {
        using var fixture = new ImageFixture();
        using var reader = new BuffFrameReader(() => fixture.Profile, (_, _) => new("19m", default));
        using var frame = fixture.Frame(50);
        Assert.NotNull(reader.Read(frame, CancellationToken.None));
        var icon = fixture.Profile.Templates[0].IconPath;
        using (var replacement = new Bitmap(32, 32)) replacement.Save(icon, System.Drawing.Imaging.ImageFormat.Png);
        File.SetLastWriteTimeUtc(icon, DateTime.UtcNow.AddMinutes(2));

        Assert.Null(reader.Read(frame, CancellationToken.None));
        Assert.Contains("zu wenig Bildinhalt", reader.LastDiagnostic);
        Assert.Equal(2, reader.TemplateLoadCount);
    }

    [Theory]
    [InlineData("harmony-draught-demihuman")]
    [InlineData("immortal-harmony-draught")]
    [InlineData("immortal-harmony-draught-human")]
    [InlineData("immortal-harmony-draught-demihuman")]
    [InlineData("immortal-harmony-draught-kamasylvia")]
    [InlineData("immortal-harmony-draught-edania")]
    public void HarmonyFramePreservesVariantWithoutRequiringOwnConsumptionAttribution(string id)
    {
        using var fixture = new ImageFixture();
        var profile = fixture.Profile with
        { Templates = [fixture.Profile.Templates[0] with { BuffId = id }] };
        using var reader = new BuffFrameReader(() => profile, (_, _) => new("19m", default));
        using var frame = fixture.Frame(50);
        var observation = Assert.Single(reader.Read(frame, CancellationToken.None)!.Observations);
        Assert.Equal(id, observation.BuffId);
        Assert.False(observation.ConsumptionAttributionConfirmed);
    }

    [Fact]
    public void DisposedReaderCannotReuseItsNativeTemplates()
    {
        using var fixture = new ImageFixture();
        var reader = new BuffFrameReader(() => fixture.Profile, (_, _) => new("19m", default));
        using var frame = fixture.Frame(50);
        Assert.NotNull(reader.Read(frame, CancellationToken.None));
        reader.Dispose();
        reader.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reader.Read(frame, CancellationToken.None));
    }

    private static BuffIconTemplate Template(string id) => new(id, "unused.png", new(0, 32, 40, 16));

    private sealed class ImageFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "grindcrest-buff-image-" + Guid.NewGuid().ToString("N"));
        private readonly Bitmap _icon = new(32, 32);
        private readonly Bitmap _secondIcon = new(32, 32);
        internal BuffRecognitionProfile Profile { get; }
        internal BuffRecognitionProfile ProfileWithSecond => Profile with
        {
            Templates = [Profile.Templates[0], new("perfume-of-courage", Path.Combine(_directory, "second.png"), new(0, 32, 40, 16))],
        };

        internal ImageFixture()
        {
            Directory.CreateDirectory(_directory);
            var random = new Random(34);
            for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                _icon.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
                _secondIcon.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
            }
            var path = Path.Combine(_directory, "icon.png");
            _icon.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            _secondIcon.Save(Path.Combine(_directory, "second.png"), System.Drawing.Imaging.ImageFormat.Png);
            Profile = new()
            {
                ScreenWidth = 200, ScreenHeight = 100, Region = new(0, 0, 200, 100),
                Templates = [new("harmony-draught", path, new(0, 32, 40, 16))],
            };
        }

        internal Bitmap Frame(int x)
        {
            var frame = new Bitmap(200, 100);
            using var graphics = Graphics.FromImage(frame);
            graphics.Clear(Color.Black);
            graphics.DrawImageUnscaled(_icon, x, 10);
            return frame;
        }

        internal void AddIcon(Bitmap frame, int x, bool second = false)
        {
            using var graphics = Graphics.FromImage(frame);
            graphics.DrawImageUnscaled(second ? _secondIcon : _icon, x, 10);
        }

        public void Dispose() { _icon.Dispose(); _secondIcon.Dispose(); Directory.Delete(_directory, true); }
    }
}
