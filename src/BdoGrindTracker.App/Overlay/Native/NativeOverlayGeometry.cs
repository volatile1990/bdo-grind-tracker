namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>Convert saved logical sizes and relative positions into monitor pixels.</summary>
internal static class NativeOverlayGeometry
{
    internal static Rectangle Place(Rectangle monitor, double width, double height,
        double x, double y, double scale, double dpi)
    {
        width = Math.Clamp(Safe(width, 360), 1, 100_000);
        height = Math.Clamp(Safe(height, 260), 1, 100_000);
        var factor = Math.Min(EffectiveScale(scale, dpi), Math.Min(Math.Max(1, monitor.Width) / width,
            Math.Max(1, monitor.Height) / height));
        var pixels = new Size(
            Math.Clamp((int)Math.Round(Safe(width, 360) * factor), 1, Math.Max(1, monitor.Width)),
            Math.Clamp((int)Math.Round(Safe(height, 260) * factor), 1, Math.Max(1, monitor.Height)));
        return new Rectangle(monitor.Left + (int)Math.Round(Math.Clamp(Safe(x, 0), 0, 1) * (monitor.Width - pixels.Width)),
            monitor.Top + (int)Math.Round(Math.Clamp(Safe(y, 0), 0, 1) * (monitor.Height - pixels.Height)),
            pixels.Width, pixels.Height);
    }

    internal static Rectangle Clamp(Rectangle bounds, Rectangle monitor)
    {
        var width = Math.Clamp(bounds.Width, 1, Math.Max(1, monitor.Width));
        var height = Math.Clamp(bounds.Height, 1, Math.Max(1, monitor.Height));
        return new Rectangle(Math.Clamp(bounds.Left, monitor.Left, monitor.Right - width),
            Math.Clamp(bounds.Top, monitor.Top, monitor.Bottom - height), width, height);
    }

    internal static (double X, double Y) RelativePosition(Rectangle bounds, Rectangle monitor) =>
        (monitor.Width > bounds.Width ? Math.Clamp((double)(bounds.Left - monitor.Left) / (monitor.Width - bounds.Width), 0, 1) : 0,
         monitor.Height > bounds.Height ? Math.Clamp((double)(bounds.Top - monitor.Top) / (monitor.Height - bounds.Height), 0, 1) : 0);

    internal static double EffectiveScale(double scale, double dpi) =>
        Math.Clamp(Safe(scale, 1), .5, 3) * Math.Clamp(Safe(dpi, 96), 48, 768) / 96;

    internal static bool ShouldShow(bool enabled, bool preview, string visibility,
        bool gameForeground, bool hasSession) => preview || enabled && visibility switch
        {
            "always" => true,
            "session" => hasSession,
            _ => gameForeground
        };

    private static double Safe(double number, double fallback) => double.IsFinite(number) ? number : fallback;
}
