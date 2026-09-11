namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>One corner drag, with the same canvas/content layout used by the editor.</summary>
internal sealed class NativeOverlayResize
{
    private readonly OverlaySettings _original;
    private readonly Rectangle _start, _monitor;
    private readonly double _dpi, _factor;

    internal NativeOverlayResize(OverlaySettings settings, Rectangle bounds, Rectangle monitor, double dpi)
    {
        _original = OverlayLayout.Normalize(settings);
        _start = bounds;
        _monitor = monitor;
        _dpi = dpi;
        _factor = NativeOverlayGeometry.EffectiveScale(_original.Scale, dpi);
        Current = new(_original, bounds);
    }

    internal NativeOverlayResizeFrame Current { get; private set; }

    internal NativeOverlayResizeFrame Update(Size delta)
    {
        if (delta == Size.Empty) return Current = new(_original, _start);
        // Monitor fitting can display a very narrow/tall layout below the
        // configured zoom's minimum physical size. Such a target cannot be
        // persisted with the editor's size limits. Keep that fitted starting
        // layout until the pointer reaches a representable size, rather than
        // abruptly enlarging an untouched axis to its logical minimum.
        if (BelowFittedMinimum(_start.Width, delta.Width, 160) ||
            BelowFittedMinimum(_start.Height, delta.Height, 64))
            return Current = new(_original, _start);
        // Use the configured scale, including DPI, even if Place previously had
        // to fit a large layout to the monitor. Reusing that temporary fit factor
        // would enlarge the window again after saving a smaller canvas.
        var width = Dimension(_original.Width, _start.Width, delta.Width, _monitor.Right - _start.Left, 160, 1600);
        var height = Dimension(_original.Height, _start.Height, delta.Height, _monitor.Bottom - _start.Top, 64, 1200);
        var layout = OverlayLayout.ResizeCanvas(_original, width, height);
        var size = NativeOverlayGeometry.Place(_monitor, layout.Width, layout.Height, 0, 0, layout.Scale, _dpi).Size;
        var bounds = NativeOverlayGeometry.Clamp(new Rectangle(_start.Location, size), _monitor);
        var position = NativeOverlayGeometry.RelativePosition(bounds, _monitor);
        return Current = new(layout with { PositionX = position.X, PositionY = position.Y }, bounds);
    }

    private bool BelowFittedMinimum(int pixels, int delta, double minimum) =>
        pixels < Math.Round(minimum * _factor) && pixels + (double)delta < Math.Round(minimum * _factor);

    private double Dimension(double original, int pixels, int delta, int available, double minimum, double maximum)
    {
        var value = delta == 0 && (int)Math.Round(original * _factor) == pixels
            ? original : (pixels + (double)delta) / _factor;
        if (delta != 0 && _original.SnapToGrid) value = Math.Floor(value / 8 + .5) * 8;
        // Keep the opposite corner fixed at the monitor edge wherever the
        // editor's minimum size permits it. Each axis has its own limit.
        return Math.Clamp(value, minimum, Math.Max(minimum, Math.Min(maximum, available / _factor)));
    }
}

internal sealed record NativeOverlayResizeFrame(OverlaySettings Settings, Rectangle Bounds);
