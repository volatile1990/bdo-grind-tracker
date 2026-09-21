using BdoGrindTracker.App.Analysis;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class AutomaticBuffCatalogDetectionTests
{
    [Theory]
    [InlineData(32, 0)]
    [InlineData(37, 0)]
    [InlineData(48, 0)]
    [InlineData(53, 0)]
    [InlineData(64, 0)]
    [InlineData(32, 3840)]
    [InlineData(48, 3840)]
    public void EveryBundledSymbolIsLocatedAndClassifiedAtDifferentHudScales(int size, int screenWidth)
    {
        var catalog = AutomaticBuffCatalog.Default;
        const int columns = 7;
        var cell = size + 28;
        var rows = (catalog.Templates.Count + columns - 1) / columns;
        using var frame = new Mat(screenWidth > 0 ? screenWidth * 9 / 16 : rows * cell + 30,
            screenWidth > 0 ? screenWidth : columns * cell + 30, MatType.CV_8UC3, new Scalar(17, 23, 31));
        for (var index = 0; index < catalog.Templates.Count; index++)
        {
            using var stream = catalog.OpenIcon(catalog.Templates[index])!;
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            using var icon = AutomaticBuffFrameReader.DecodeIcon(bytes.ToArray());
            using var scaled = new Mat();
            Cv2.Resize(icon, scaled, new(size, size), interpolation: InterpolationFlags.Linear);
            using var destination = new Mat(frame, new Rect(15 + index % columns * cell, 15 + index / columns * cell, size, size));
            scaled.CopyTo(destination);
        }
        using var encoded = new MemoryStream(frame.ToBytes(".png"));
        using var bitmap = new Bitmap(encoded);
        using var reader = new AutomaticBuffFrameReader(recognize: (_, _) => new("1m", default));

        var reading = Assert.IsType<BuffFrameReading>(reader.Read(bitmap, CancellationToken.None));

        Assert.Empty(reading.UnknownBuffIds);
        var expected = catalog.Templates.Select(template => catalog.DefinitionFor(template).Id).Order().ToArray();
        Assert.Equal(expected, reading.Observations.Select(observation => observation.BuffId).Order().ToArray());
    }
}
