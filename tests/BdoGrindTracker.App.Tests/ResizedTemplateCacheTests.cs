using BdoGrindTracker.App.Analysis;
using OpenCvSharp;

namespace BdoGrindTracker.App.Tests;

public sealed class ResizedTemplateCacheTests
{
    [Fact]
    public void EverySizeAndInterpolationRetainsTheOriginalResizePixels()
    {
        using var source = Pattern();
        using var cache = new ResizedTemplateCache(source);
        foreach (var (width, height) in new[] { (13, 17), (32, 32), (71, 53) })
        foreach (var interpolation in new[] { InterpolationFlags.Area, InterpolationFlags.Nearest })
        {
            using var expected = new Mat();
            Cv2.Resize(source, expected, new OpenCvSharp.Size(width, height), interpolation: interpolation);
            using var actual = cache.Acquire(width, height, interpolation);
            Assert.Equal(0, Cv2.Norm(expected, actual, NormTypes.INF));
            using var reused = cache.Acquire(width, height, interpolation);
            Assert.Equal(actual.Data, reused.Data);
            Assert.Equal(0, Cv2.Norm(expected, reused, NormTypes.INF));
        }
        Assert.Equal(6, cache.ResizeCount);
    }

    [Fact]
    public void ChangingSizeOrInterpolationCannotReturnAnEarlierVariant()
    {
        using var source = Pattern();
        using var cache = new ResizedTemplateCache(source);
        using var area = cache.Acquire(19, 23, InterpolationFlags.Area);
        using var nearest = cache.Acquire(19, 23, InterpolationFlags.Nearest);
        using var differentSize = cache.Acquire(20, 23, InterpolationFlags.Area);
        using var areaAgain = cache.Acquire(19, 23, InterpolationFlags.Area);
        Assert.Equal(3, cache.ResizeCount);
        Assert.True(Cv2.Norm(area, nearest, NormTypes.INF) > 0);
        Assert.Equal(20, differentSize.Width);
        Assert.Equal(area.Data, areaAgain.Data);
    }

    [Fact]
    public void EvictionBoundsMemoryAndDoesNotInvalidateAcquiredViews()
    {
        using var source = Pattern();
        var cache = new ResizedTemplateCache(source, maximumEntries: 2, maximumBytes: 3000);
        using var first = cache.Acquire(16, 16, InterpolationFlags.Area);
        using var expected = first.Clone();
        using (cache.Acquire(24, 24, InterpolationFlags.Area)) { }
        using (cache.Acquire(20, 20, InterpolationFlags.Area)) { }
        Assert.InRange(cache.Count, 1, 2);
        Assert.InRange(cache.Bytes, 1, 3000);
        Assert.Equal(0, Cv2.Norm(expected, first, NormTypes.INF));
        using (cache.Acquire(16, 16, InterpolationFlags.Area)) { }
        Assert.Equal(4, cache.ResizeCount);
        cache.Dispose();
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, Cv2.Norm(expected, first, NormTypes.INF));
        Assert.Throws<ObjectDisposedException>(() => cache.Acquire(16, 16, InterpolationFlags.Area));
    }

    [Fact]
    public void OversizedVariantsAreReturnedWithoutBeingRetained()
    {
        using var source = Pattern();
        using var cache = new ResizedTemplateCache(source, maximumEntries: 2, maximumBytes: 1000);
        using var first = cache.Acquire(32, 32, InterpolationFlags.Area);
        using var second = cache.Acquire(32, 32, InterpolationFlags.Area);
        Assert.Equal(2, cache.ResizeCount);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        Assert.Equal(0, Cv2.Norm(first, second, NormTypes.INF));
    }

    private static Mat Pattern()
    {
        var source = new Mat(32, 32, MatType.CV_8UC3);
        var height = source.Height;
        var width = source.Width;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            source.Set(y, x, new Vec3b((byte)(x * 17 + y * 3), (byte)(y * 19), (byte)(x * y)));
        return source;
    }
}
