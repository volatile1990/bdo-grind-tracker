using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeGrindRatingSpectrumTests
{
    [Fact]
    public void MovingWithinOneTierRepaintsTheNativeMarker()
    {
        var widget = OverlayCatalog.CreateWidget("grind-rating", 0, 0) with { ShowIcon = false };
        var settings = new OverlaySettings
        {
            Width = widget.Width, Height = widget.Height, Widgets = [widget],
            ShowBorder = false, BackgroundOpacity = 0, Interaction = "passthrough",
        };
        var before = Snapshot(30);
        var after = Snapshot(45);
        var size = new Size((int)widget.Width, (int)widget.Height);
        var cache = new NativeOverlayRenderState();
        cache.Remember(settings, before, size);
        Assert.False(cache.Matches(settings, after, size));

        using var renderer = new NativeOverlayRenderer();
        using var first = renderer.Render(size, settings, before, out _);
        using var second = renderer.Render(size, settings, after, out _);
        // Text and benchmark stops are identical; only the continuous marker moves.
        Assert.Contains(Enumerable.Range(0, size.Height), y =>
            Enumerable.Range(0, size.Width).Any(x => first.GetPixel(x, y) != second.GetPixel(x, y)));
    }

    [Theory]
    [InlineData(false, .7)]
    [InlineData(false, 2)]
    [InlineData(true, .7)]
    [InlineData(true, 2)]
    public void SmallWidgetsReserveTheScaleAndStatusEvenWithTheirLabelHidden(bool showLabel, double fontScale)
    {
        var widget = OverlayLayout.ResizeWidget(OverlayCatalog.CreateWidget("grind-rating") with
            { ShowLabel = showLabel, FontScale = fontScale }, 50, 25);
        var content = OverlayContentLayout.Create(widget, Snapshot(35));

        Assert.Equal(16 + (showLabel ? 18 * fontScale : 0) + 72 * fontScale, content.MinimumHeight, 6);
        Assert.True(content.LayoutWidget.Height >= content.MinimumHeight - .000001);
        Assert.Equal(25, content.LayoutWidget.Height * content.Scale, 6);
    }

    private static OverlaySnapshot Snapshot(double position)
    {
        var spectrum = new GrindRatingSpectrum(position,
            [new("Average", 14000, 25, "14.000", OverlayMetricTone.Default),
             new("High", 18000, 50, "18.000", OverlayMetricTone.Positive),
             new("Top", 22000, 75, "22.000", OverlayMetricTone.Accent)],
            "Average → High · 40 %", "Noch 2.400 Trash / h bis High", "Positionsvergleich", "15.600 Trash / h");
        return new() { Metrics = new Dictionary<string, OverlayMetric>
        {
            ["grind-rating"] = new("Grind-Bewertung", "Average Tier", "Vorläufig") { Spectrum = spectrum },
        } };
    }
}
