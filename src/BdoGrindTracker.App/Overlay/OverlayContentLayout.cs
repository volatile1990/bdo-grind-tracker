namespace BdoGrindTracker.App.Overlay;

/// <summary>A uniform content transform shared by the editor and native renderer.</summary>
public sealed record OverlayContentLayout(OverlayWidget LayoutWidget, double Scale,
    double ReferenceWidth, double ReferenceHeight, double MinimumWidth, double MinimumHeight)
{
    public static OverlayContentLayout Create(OverlayWidget widget, OverlaySnapshot snapshot)
    {
        var width = Valid(widget.Width, 160);
        var height = Valid(widget.Height, 72);
        var referenceWidth = Valid(widget.ContentWidth ?? width, width);
        var referenceHeight = Valid(widget.ContentHeight ?? height, height);
        var fontScale = double.IsFinite(widget.FontScale) ? Math.Clamp(widget.FontScale, .7, 2) : 1;
        var metric = snapshot.Metrics.GetValueOrDefault(widget.Kind);
        // Reserve the requested lines before fitting the complete content. Tiny
        // modules still show them, at a smaller size, instead of dropping lines.
        var minimumHeight = widget.Kind switch
        {
            // The timeline SVG needs 40px plus the widget's 16px vertical inset.
            "rotation-monitor" => 56 + (widget.RotationComparison == "sectors" ? 18 * fontScale : 0),
            "daily-goal" => 88 + (widget.ShowLabel ? 18 * fontScale : 0),
            "controls" => 16 + (widget.ShowLabel ? 18 * fontScale : 0) + (widget.ShowNewSession ? 60 : 28) * fontScale,
            "chart" => 16 + (widget.ShowLabel ? 20 * fontScale : 0) + 33 * fontScale + 24 + 16 * fontScale,
            "clock" => 16 + (widget.ShowLabel ? 18 * fontScale : 0) + OverlayClockPresentation.RowCount(widget) * 26 * fontScale,
            "consumables" => 48 + (widget.ShowLabel ? 18 * fontScale : 0) + 24 * fontScale,
            "grind-rating" when metric?.Spectrum is not null => 16 +
                (widget.ShowLabel ? 18 * fontScale : 0) + 60 * fontScale +
                (!string.IsNullOrEmpty(metric.Detail) ? 12 * fontScale : 0),
            _ when OverlayCatalog.IsLootWidget(widget.Kind) => 48,
            _ => 16 + (widget.ShowLabel ? 18 * fontScale : 0) +
                (widget.Kind is "spot" or "status" or "loot-scroll" or "grind-rating" ? 20 : 26) * fontScale +
                ((widget.ShowLabel || widget.Kind == "experience") && widget.Kind != "status" && !string.IsNullOrEmpty(metric?.Detail) ? 12 * fontScale : 0),
        };
        const double minimumWidth = 80;
        var scale = Math.Min(Math.Min(width / referenceWidth, height / referenceHeight),
            Math.Min(width / minimumWidth, height / minimumHeight));
        return new(widget with
        {
            X = 0, Y = 0, Width = width / scale, Height = height / scale, FontScale = fontScale,
        }, scale, referenceWidth, referenceHeight, minimumWidth, minimumHeight);
    }

    private static double Valid(double value, double fallback) => double.IsFinite(value) && value > 0
        ? Math.Clamp(value, 1, 1600) : fallback;
}
