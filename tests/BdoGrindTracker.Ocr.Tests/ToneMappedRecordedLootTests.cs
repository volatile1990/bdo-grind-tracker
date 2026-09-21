using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class ToneMappedRecordedLootTests
{
    [WindowsOcrTheory("en-US")]
    [Trait("Category", "WindowsOcr")]
    [InlineData("helmet-four-hdr.png", "Elion Follower's Helmet x 4")]
    [InlineData("helmet-fifty-six-hdr.png", "Elion Follower's Helmet x 56")]
    public void RecordedHdrLootRemainsReadableByThePrimaryEngine(string fileName, string expected)
    {
        using var source = LoadFixture(fileName);
        using var prepared = ToneMappedNormalRowProcessor.Process(source, 250);

        Assert.False(prepared.IsBlank);
        Assert.NotNull(prepared.NameImage);
        var recognizer = CompanionWindowsOcrRecognizer.TryCreate("en-US", requirePreferredLanguage: true);
        Assert.NotNull(recognizer);
        var reading = recognizer.Recognize(prepared.NameImage);

        Assert.Equal(expected, reading.Text);
        Assert.True(CompanionWindowsOcrRecognizer.PassesNormalGeometryGate(reading.FirstWord, 1f));

        // Replay identical prepared pixels, including a strided ROI, through the
        // public adapter. Cached reads retain the native text and every coordinate.
        using var padded = new Mat(prepared.NameImage.Height + 2, prepared.NameImage.Width + 7,
            prepared.NameImage.Type(), Scalar.All(255));
        using var region = new Mat(padded, new Rect(3, 1, prepared.NameImage.Width, prepared.NameImage.Height));
        prepared.NameImage.CopyTo(region);
        var cached = recognizer.Recognize(region);
        Assert.Equal(reading.Text, cached.Text);
        Assert.Equal(reading.FirstWord, cached.FirstWord);
        Assert.Equal(reading.Words, cached.Words);
        Assert.Same(cached, recognizer.Recognize(prepared.NameImage));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => recognizer.Recognize(region, cancelled.Token));
    }

    [Fact]
    public void RecordedPaleSceneryDoesNotCreateAnOcrRow()
    {
        using var source = LoadFixture("empty-scenery-hdr.png");
        using var prepared = ToneMappedNormalRowProcessor.Process(source, 250);

        Assert.True(prepared.IsBlank);
        Assert.Null(prepared.NameImage);
    }

    private static Mat LoadFixture(string fileName)
    {
        var image = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "tone-mapped-loot", fileName),
            ImreadModes.Color);
        Assert.False(image.Empty());
        return image;
    }
}
