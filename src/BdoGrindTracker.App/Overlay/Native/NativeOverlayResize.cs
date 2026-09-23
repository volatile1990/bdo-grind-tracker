namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>
/// One corner drag in the game: it zooms the whole overlay. The module layout keeps its canvas size, so every module
/// and its text grow or shrink together, like the zoom in the editor. The canvas itself is only edited there.
/// </summary>
internal sealed class NativeOverlayResize
{
    internal const double MinimumScale = .5, MaximumScale = 2;
    private readonly OverlaySettings _original;
    private readonly Rectangle _start, _monitor;
    private readonly double _dpi;
    private readonly OverlayWindowChrome _chrome;

    internal NativeOverlayResize(OverlaySettings settings, Rectangle bounds, Rectangle monitor, double dpi,
        OverlayWindowChrome chrome = default)
    {
        _original = OverlayLayout.Normalize(settings);
        _start = bounds;
        _monitor = monitor;
        _dpi = dpi;
        _chrome = chrome;
        Current = new(_original, bounds);
    }

    internal NativeOverlayResizeFrame Current { get; private set; }

    internal NativeOverlayResizeFrame Update(Size delta)
    {
        if (delta == Size.Empty) return Current = new(_original, _start);
        var outerWidth = _chrome.OuterWidth(_original.Width);
        var outerHeight = _chrome.OuterHeight(_original.Height);
        // Both axes drive the same zoom: the pointer's distance along the window's diagonal.
        var dragged = (_start.Width + delta.Width + (double)_start.Height + delta.Height) / (_start.Width + _start.Height);
        var scale = Round(_original.Scale * dragged);
        // Keep the window's own corner fixed: the zoom grows only until the opposite edge reaches the monitor.
        // A saved zoom beyond that would also be fitted on screen and reopen at a different size than the drag showed.
        var fits = Math.Min((_monitor.Right - _start.Left) / outerWidth, (_monitor.Bottom - _start.Top) / outerHeight) * 96 / Dpi;
        var limit = Math.Floor(Math.Min(MaximumScale, fits) * Steps) / Steps;
        scale = Math.Clamp(scale, MinimumScale, Math.Max(MinimumScale, limit));
        var size = NativeOverlayGeometry.Place(_monitor, outerWidth, outerHeight, 0, 0, scale, _dpi).Size;
        var bounds = NativeOverlayGeometry.Clamp(new Rectangle(_start.Location, size), _monitor);
        var position = NativeOverlayGeometry.RelativePosition(bounds, _monitor);
        return Current = new(_original with { Scale = scale, PositionX = position.X, PositionY = position.Y }, bounds);
    }

    private double Dpi => Math.Clamp(double.IsFinite(_dpi) ? _dpi : 96, 48, 768);

    // Five-percent steps while the grid is on, else a hundredth: a pixel of pointer movement must not
    // produce a zoom that no longer round-trips through the saved settings.
    private double Steps => _original.SnapToGrid ? 20 : 100;
    private double Round(double scale) => Math.Round(scale * Steps, MidpointRounding.AwayFromZero) / Steps;
}

internal sealed record NativeOverlayResizeFrame(OverlaySettings Settings, Rectangle Bounds);
