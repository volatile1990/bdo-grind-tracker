using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Bounded resize results for one immutable template. Acquired Mats are read-only views owned by the caller.</summary>
internal sealed class ResizedTemplateCache(Mat source, int maximumEntries = 96, long maximumBytes = 512 * 1024) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _recency = [];
    private long _bytes;
    private long _resizeCount;
    private bool _disposed;

    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal long Bytes { get { lock (_gate) return _bytes; } }
    internal long ResizeCount { get { lock (_gate) return _resizeCount; } }

    public Mat Acquire(int width, int height, InterpolationFlags interpolation)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = new Key(width, height, interpolation);
            if (_entries.TryGetValue(key, out var cached))
            {
                _recency.Remove(cached);
                _recency.AddLast(cached);
                return View(cached.Value.Image);
            }

            var image = new Mat();
            try { Cv2.Resize(source, image, new OpenCvSharp.Size(width, height), interpolation: interpolation); }
            catch { image.Dispose(); throw; }
            _resizeCount++;
            var bytes = image.Total() * image.ElemSize();
            // Oversized variants remain usable but cannot exceed the retained memory budget.
            if (maximumEntries <= 0 || bytes > maximumBytes)
                return image;
            while (_entries.Count >= maximumEntries || _bytes + bytes > maximumBytes)
            {
                var oldest = _recency.First!;
                _recency.RemoveFirst();
                _entries.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Bytes;
                oldest.Value.Image.Dispose();
            }
            var node = _recency.AddLast(new Entry(key, image, bytes));
            _entries.Add(key, node);
            _bytes += bytes;
            // A ref-counted header keeps an in-flight caller safe even after eviction/disposal.
            return View(image);
        }
    }

    private static Mat View(Mat image) => new(image, new Rect(0, 0, image.Width, image.Height));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _recency) entry.Image.Dispose();
            _entries.Clear();
            _recency.Clear();
            _bytes = 0;
        }
    }

    private readonly record struct Key(int Width, int Height, InterpolationFlags Interpolation);
    private sealed record Entry(Key Key, Mat Image, long Bytes);
}
