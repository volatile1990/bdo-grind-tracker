using System.Drawing.Imaging;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Shares only the same capture's native banner reads; each profile keeps its own search history.</summary>
internal sealed class RotationFrameSamples(Bitmap frame,
    Func<Mat, CancellationToken, string>? recognize = null) : IDisposable
{
    private readonly Dictionary<(Rectangle Region, string Language), RotationBannerSample> _samples = [];

    internal RotationBannerSample Acquire(Rectangle region, string language)
    {
        var key = (region, language);
        if (!_samples.TryGetValue(key, out var sample))
        {
            sample = new RotationBannerSample(frame.Clone(region, PixelFormat.Format24bppRgb), recognize, shareDecoded: true);
            _samples.Add(key, sample);
        }
        sample.Retain();
        return sample;
    }

    public void Dispose()
    {
        foreach (var sample in _samples.Values) sample.Release();
        _samples.Clear();
    }
}

internal sealed class RotationBannerSample(Bitmap pixels,
    Func<Mat, CancellationToken, string>? recognize = null, bool shareDecoded = false)
{
    private readonly SemaphoreSlim _pixelsGate = new(1, 1);
    private readonly object _textSync = new();
    private int _references = 1;
    private Mat? _decoded;
    private int _decodedReaders;
    private string? _text;

    internal void Retain() => Interlocked.Increment(ref _references);

    internal void Release()
    {
        if (Interlocked.Decrement(ref _references) != 0) return;
        _decoded?.Dispose();
        pixels.Dispose();
        _pixelsGate.Dispose();
    }

    internal string Read(Func<Mat, CancellationToken, string> read,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_textSync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_text is not null) return _text;
        }
        using var decoded = Decode(cancellationToken);
        try
        {
            lock (_textSync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_text is not null) return _text;
            }
            // A healthy profile must not wait for another engine's native timeout.
            // Concurrent misses retain their independent engines; completed text
            // and the in-flight immutable decoded pixels are shared.
            var text = (recognize ?? read)(decoded, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_textSync) _text ??= text;
            return text;
        }
        finally { if (shareDecoded) ReleaseDecoded(); }
    }

    internal string ReadBitmap(Func<Bitmap, string> read)
    {
        _pixelsGate.Wait();
        try { return read(pixels); }
        finally { _pixelsGate.Release(); }
    }

    private Mat Decode(CancellationToken cancellationToken)
    {
        _pixelsGate.Wait(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!shareDecoded) return CompanionFrameDecoder.Decode(pixels);
            _decoded ??= CompanionFrameDecoder.Decode(pixels);
            var view = new Mat(_decoded, new Rect(0, 0, _decoded.Width, _decoded.Height));
            _decodedReaders++;
            return view;
        }
        finally { _pixelsGate.Release(); }
    }

    private void ReleaseDecoded()
    {
        _pixelsGate.Wait();
        try
        {
            if (--_decodedReaders != 0) return;
            _decoded?.Dispose();
            _decoded = null;
        }
        finally { _pixelsGate.Release(); }
    }

    internal void WithPixels(Action<Bitmap> inspect)
    {
        _pixelsGate.Wait();
        try { inspect(pixels); }
        finally { _pixelsGate.Release(); }
    }
}
