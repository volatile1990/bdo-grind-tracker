using BdoGrindTracker.Core;

namespace BdoGrindTracker.Core.Tests;

public sealed class CompanionItemMatchCacheTests
{
    [Fact]
    public void NegativeResultIsAHitAndRequiresTheCompleteKey()
    {
        var cache = new CompanionItemMatchCache();
        cache.Remember("Unreadable row", 1, false, null);

        Assert.True(cache.TryGet("Unreadable row", 1, false, out var negative));
        Assert.Null(negative);
        Assert.False(cache.TryGet("unreadable row", 1, false, out _));
        Assert.False(cache.TryGet("Unreadable row ", 1, false, out _));
        Assert.False(cache.TryGet("Unreadable row", 2, false, out _));
        Assert.False(cache.TryGet("Unreadable row", 1, true, out _));
    }

    [Fact]
    public void EntryLimitEvictsTheLeastRecentlyUsedResult()
    {
        var cache = new CompanionItemMatchCache();
        for (var index = 0; index < CompanionItemMatchCache.MaximumEntries; index++)
            cache.Remember("row" + index, 1, false, null);
        Assert.True(cache.TryGet("row0", 1, false, out _));

        cache.Remember("new row", 1, false, null);

        Assert.Equal(CompanionItemMatchCache.MaximumEntries, cache.Count);
        Assert.True(cache.TryGet("row0", 1, false, out _));
        Assert.False(cache.TryGet("row1", 1, false, out _));
        Assert.True(cache.TryGet("new row", 1, false, out _));
    }

    [Fact]
    public void ByteLimitBoundsLongNamesAndOversizedResultsAreNotRetained()
    {
        var cache = new CompanionItemMatchCache();
        for (var index = 0; index < CompanionItemMatchCache.MaximumEntries; index++)
        {
            var text = new string('x', 3000) + index;
            cache.Remember(text, 1, false, new(text, "Caphras Stone", .1, false));
            Assert.InRange(cache.RetainedBytes, 0, CompanionItemMatchCache.MaximumBytes);
        }
        Assert.InRange(cache.Count, 1, CompanionItemMatchCache.MaximumEntries - 1);
        var previousBytes = cache.RetainedBytes;
        var oversized = new string('x', (int)(CompanionItemMatchCache.MaximumBytes / 2));

        cache.Remember(oversized, 1, false, null);

        Assert.False(cache.TryGet(oversized, 1, false, out _));
        Assert.Equal(previousBytes, cache.RetainedBytes);
    }

    [Fact]
    public void ConcurrentRememberAndLookupPreserveResultsAndBounds()
    {
        var cache = new CompanionItemMatchCache();
        Parallel.For(0, 2000, index =>
        {
            var text = "row" + index % 700;
            var quantity = index % 3;
            var rare = index % 2 == 0;
            var expected = new CompanionItemMatch(text, "Caphras Stone", .1, false);
            cache.Remember(text, quantity, rare, expected);
            if (cache.TryGet(text, quantity, rare, out var actual))
                Assert.Equal(expected, actual);
        });

        Assert.InRange(cache.Count, 1, CompanionItemMatchCache.MaximumEntries);
        Assert.InRange(cache.RetainedBytes, 0, CompanionItemMatchCache.MaximumBytes);
    }
}
