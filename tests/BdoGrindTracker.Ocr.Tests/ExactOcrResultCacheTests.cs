using System.Runtime.InteropServices;
using OpenCvSharp;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class ExactOcrResultCacheTests
{
    [Fact]
    public void IdenticalPixelsReturnOwnedTextAndAllWordGeometryAfterSourceDisposal()
    {
        var cache = new ExactOcrResultCache();
        var geometry = new CompanionOcrWordGeometry(CompanionOcrGeometryStatus.Success, 2, 3, 4, 5);
        var words = new List<CompanionOcrWord> { new("Black", geometry), new("Stone", geometry with { X = 9 }) };
        var result = new CompanionOcrResult("Black Stone", geometry) { Words = words };
        using (var source = Image(7)) Remember(cache, source, result);
        words.Clear();

        using var identical = Image(7);
        Assert.True(cache.TryGet(identical, MatType.CV_8UC1, out var cached));
        Assert.Equal(result.Text, cached.Text);
        Assert.Equal(geometry, cached.FirstWord);
        Assert.Equal(new[] { "Black", "Stone" }, cached.Words.Select(word => word.Text));
        Assert.Equal(9f, cached.Words[1].Geometry.X);
        var list = Assert.IsAssignableFrom<IList<CompanionOcrWord>>(cached.Words);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = new("changed", geometry));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void EvenOneChangedByteDoesNotReuseRecognition(int row, int column)
    {
        var cache = new ExactOcrResultCache();
        using var source = Image(7);
        Remember(cache, source, Result("old"));
        using var changed = source.Clone();
        changed.Set<byte>(row, column, 8);

        Assert.False(cache.TryGet(changed, MatType.CV_8UC1, out _));
        Assert.True(cache.TryGet(source, MatType.CV_8UC1, out _));
    }

    [Fact]
    public void EqualByteCountsWithDifferentDimensionsOrSourceTypesDoNotMatch()
    {
        var cache = new ExactOcrResultCache();
        using var source = Image(7);
        Remember(cache, source, Result("old"));
        using var reshaped = new Mat(1, 4, MatType.CV_8UC1, Scalar.All(7));

        Assert.False(cache.TryGet(reshaped, MatType.CV_8UC1, out _));
        Assert.False(cache.TryGet(source, MatType.CV_8UC3, out _));
        Assert.False(cache.TryGet(source, MatType.CV_8UC4, out _));
    }

    [Fact]
    public void NonContiguousRegionMatchesVisiblePixelsWithoutMatchingPadding()
    {
        var cache = new ExactOcrResultCache();
        using var source = Image(7);
        Remember(cache, source, Result("same"));
        using var padded = new Mat(4, 8, MatType.CV_8UC1, Scalar.All(255));
        using var region = new Mat(padded, new Rect(3, 1, 2, 2));
        region.SetTo(Scalar.All(7));
        Assert.False(region.IsContinuous());

        Assert.True(cache.TryGet(region, MatType.CV_8UC1, out var cached));
        Assert.Equal("same", cached.Text);
        region.Set<byte>(1, 1, 8);
        Assert.False(cache.TryGet(region, MatType.CV_8UC1, out _));
    }

    [Fact]
    public void EntryLimitEvictsLeastRecentlyUsedResult()
    {
        var cache = new ExactOcrResultCache(maximumEntries: 2);
        using var first = Image(1);
        using var second = Image(2);
        using var third = Image(3);
        Remember(cache, first, Result("one"));
        Remember(cache, second, Result("two"));
        Assert.True(cache.TryGet(first, MatType.CV_8UC1, out _));
        Remember(cache, third, Result("three"));

        Assert.True(cache.TryGet(first, MatType.CV_8UC1, out _));
        Assert.False(cache.TryGet(second, MatType.CV_8UC1, out _));
        Assert.True(cache.TryGet(third, MatType.CV_8UC1, out _));
    }

    [Fact]
    public void MemoryLimitIncludesTextAndDoesNotEvictForUncacheableImages()
    {
        var cache = new ExactOcrResultCache(maximumEntries: 10, maximumBytes: 300);
        using var first = Image(1);
        using var second = Image(2);
        using var third = Image(3);
        Remember(cache, first, Result("a"));
        Remember(cache, second, Result("b"));
        Remember(cache, third, Result("c"));
        Assert.False(cache.TryGet(first, MatType.CV_8UC1, out _));
        Assert.True(cache.TryGet(second, MatType.CV_8UC1, out _));
        Assert.True(cache.TryGet(third, MatType.CV_8UC1, out _));

        Remember(cache, first, Result(new string('x', 200)));
        Assert.False(cache.TryGet(first, MatType.CV_8UC1, out _));
        Assert.True(cache.TryGet(second, MatType.CV_8UC1, out _));
        Assert.True(cache.TryGet(third, MatType.CV_8UC1, out _));
    }

    [Fact]
    public void RememberingTheSameImageReplacesItsResultWithoutConsumingAnotherEntry()
    {
        var cache = new ExactOcrResultCache(maximumEntries: 2);
        using var first = Image(1);
        using var second = Image(2);
        Remember(cache, first, Result("original"));
        Remember(cache, second, Result("second"));
        Remember(cache, first, Result("latest"));

        Assert.True(cache.TryGet(first, MatType.CV_8UC1, out var cached));
        Assert.Equal("latest", cached.Text);
        Assert.True(cache.TryGet(second, MatType.CV_8UC1, out _));
    }

    [Fact]
    public void EngineInstancesDoNotShareLanguageDependentResults()
    {
        var englishEngineCache = new ExactOcrResultCache();
        var germanEngineCache = new ExactOcrResultCache();
        using var source = Image(1);
        Remember(englishEngineCache, source, Result("English"));
        Assert.False(germanEngineCache.TryGet(source, MatType.CV_8UC1, out _));
        Remember(germanEngineCache, source, Result("Deutsch"));

        Assert.True(englishEngineCache.TryGet(source, MatType.CV_8UC1, out var english));
        Assert.True(germanEngineCache.TryGet(source, MatType.CV_8UC1, out var german));
        Assert.Equal("English", english.Text);
        Assert.Equal("Deutsch", german.Text);
    }

    [Fact]
    public void GrayCacheHitsDoNotAllocatePixelSizedBuffers()
    {
        var cache = new ExactOcrResultCache();
        using var source = new Mat(1024, 1024, MatType.CV_8UC1, Scalar.All(7));
        Remember(cache, source, Result("unchanged"));
        for (var warmup = 0; warmup < 10; warmup++)
            Assert.True(cache.TryGet(source, MatType.CV_8UC1, out _));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var allFound = true;
        for (var repeat = 0; repeat < 32; repeat++)
            allFound &= cache.TryGet(source, MatType.CV_8UC1, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allFound);
        Assert.True(allocated < 64 * 1024, $"Repeated cache hits allocated {allocated} bytes.");
    }

    [Fact]
    public void ConcurrentLookupAndEvictionNeverMixPixelsAndResults()
    {
        var cache = new ExactOcrResultCache(maximumEntries: 2);
        Parallel.For(0, 8, worker =>
        {
            using var source = Image(worker);
            for (var attempt = 0; attempt < 25; attempt++)
            {
                Remember(cache, source, Result(worker.ToString()));
                if (cache.TryGet(source, MatType.CV_8UC1, out var cached))
                    Assert.Equal(worker.ToString(), cached.Text);
            }
        });
    }

    private static Mat Image(int value) => new(2, 2, MatType.CV_8UC1, Scalar.All(value));

    private static CompanionOcrResult Result(string text) => new(text, default);

    private static void Remember(ExactOcrResultCache cache, Mat image, CompanionOcrResult result)
    {
        var width = image.Width;
        var height = image.Height;
        var pixels = new byte[checked(width * height)];
        for (var row = 0; row < height; row++)
            Marshal.Copy(image.Ptr(row), pixels, row * width, width);
        cache.RememberOwned(width, height, MatType.CV_8UC1, pixels, result);
    }
}
