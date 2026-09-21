using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>
/// A small per-engine cache of completed OCR results. Pixels are compared exactly;
/// the caller still processes every frame through its current tracking rules.
/// </summary>
internal sealed class ExactOcrResultCache
{
    private const int DefaultMaximumEntries = 32;
    private const long DefaultMaximumBytes = 4 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly LinkedList<Entry> _entries = new();
    private readonly int _maximumEntries;
    private readonly long _maximumBytes;
    private long _retainedBytes;

    internal ExactOcrResultCache(int maximumEntries = DefaultMaximumEntries,
        long maximumBytes = DefaultMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _maximumEntries = maximumEntries;
        _maximumBytes = maximumBytes;
    }

    internal bool TryGet(Mat gray, MatType sourceType, out CompanionOcrResult result)
    {
        if (gray.Type() != MatType.CV_8UC1)
            throw new ArgumentException("The OCR cache requires Gray8 pixels.", nameof(gray));
        var width = gray.Width;
        var height = gray.Height;

        lock (_gate)
        {
            for (var node = _entries.First; node is not null; node = node.Next)
            {
                var entry = node.Value;
                if (entry.Width != width || entry.Height != height ||
                    entry.SourceType != sourceType || !PixelsEqual(gray, entry.Pixels)) continue;

                _entries.Remove(node);
                _entries.AddFirst(node);
                result = entry.Result;
                return true;
            }
        }

        result = null!;
        return false;
    }

    /// <summary>
    /// Takes exclusive ownership of the copied pixels used for this completed OCR
    /// operation. The caller must not modify them afterwards. No native Mat/WinRT
    /// object is retained; eviction and engine replacement require no native disposal.
    /// </summary>
    internal void RememberOwned(int width, int height, MatType sourceType, byte[] pixels,
        CompanionOcrResult result)
    {
        if (width <= 0 || height <= 0 || pixels.LongLength != (long)width * height)
            throw new ArgumentException("The copied pixels must match the Gray8 image dimensions.", nameof(pixels));

        // Include result payloads as well as pixels. Entry count separately bounds
        // container overhead; large images/results continue through OCR without caching.
        var retainedBytes = pixels.LongLength + 128L + 2L * result.Text.Length;
        foreach (var word in result.Words)
            retainedBytes += 64L + 2L * word.Text.Length;
        if (retainedBytes > _maximumBytes) return;

        // Callers may supply mutable lists. Never expose one through a cache hit.
        var ownedResult = result with { Words = Array.AsReadOnly(result.Words.ToArray()) };
        var entry = new Entry(width, height, sourceType, pixels, ownedResult, retainedBytes);
        lock (_gate)
        {
            for (var node = _entries.First; node is not null; node = node.Next)
            {
                var previous = node.Value;
                if (previous.Width != width || previous.Height != height ||
                    previous.SourceType != sourceType || !pixels.AsSpan().SequenceEqual(previous.Pixels)) continue;
                _retainedBytes -= previous.RetainedBytes;
                _entries.Remove(node);
                break;
            }

            while (_entries.Count >= _maximumEntries || _retainedBytes + retainedBytes > _maximumBytes)
            {
                _retainedBytes -= _entries.Last!.Value.RetainedBytes;
                _entries.RemoveLast();
            }

            _entries.AddFirst(entry);
            _retainedBytes += retainedBytes;
        }
    }

    private static unsafe bool PixelsEqual(Mat gray, byte[] pixels)
    {
        var width = gray.Width;
        var height = gray.Height;
        try
        {
            if (gray.IsContinuous() && gray.Step() == width)
                return new ReadOnlySpan<byte>(gray.DataPointer, pixels.Length).SequenceEqual(pixels);

            // A ROI can have a larger row stride. Padding and neighboring pixels
            // are not OCR input, so compare only the visible bytes in each row.
            for (var row = 0; row < height; row++)
                if (!new ReadOnlySpan<byte>(gray.Ptr(row).ToPointer(), width)
                    .SequenceEqual(pixels.AsSpan(row * width, width))) return false;
            return true;
        }
        finally
        {
            GC.KeepAlive(gray);
        }
    }

    private sealed record Entry(int Width, int Height, MatType SourceType, byte[] Pixels,
        CompanionOcrResult Result, long RetainedBytes);
}
