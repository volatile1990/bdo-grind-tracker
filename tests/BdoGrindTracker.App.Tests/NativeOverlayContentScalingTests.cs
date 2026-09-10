using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeOverlayContentScalingTests
{
    [Theory]
    [InlineData("duration")]
    [InlineData("spot")]
    [InlineData("status")]
    public void DirectWidgetResizeScalesTheVisibleTextWithItsModule(string kind)
    {
        var widget = OverlayCatalog.CreateWidget(kind, 10, 10) with
        {
            Width = 200, Height = 80, ShowLabel = false, ShowIcon = false
        };
        var snapshot = new OverlaySnapshot { Metrics = new Dictionary<string, OverlayMetric>
            { [kind] = new("Label", "12:34:56") } };
        using var renderer = new NativeOverlayRenderer();
        using var before = renderer.Render(new Size(500, 200), Settings(500, 200, widget), snapshot, out _);
        var enlarged = OverlayLayout.ResizeWidget(widget, 400, 160);
        using var after = renderer.Render(new Size(500, 200), Settings(500, 200, enlarged), snapshot, out _);

        var originalText = OpaqueBounds(before);
        var enlargedText = OpaqueBounds(after);
        Assert.InRange(enlargedText.Width, originalText.Width * 2 - 4, originalText.Width * 2 + 4);
        Assert.InRange(enlargedText.Height, originalText.Height * 2 - 4, originalText.Height * 2 + 4);
    }

    [Theory]
    [InlineData(.5)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void CanvasResizeAndEquivalentWidgetResizeProduceTheSameContent(double factor)
    {
        var widget = OverlayCatalog.CreateWidget("duration", 24, 16) with
        {
            Width = 200, Height = 80, ShowIcon = false
        };
        var original = Settings(480, 256, widget);
        var resizedCanvas = OverlayLayout.ResizeCanvas(original, original.Width * factor, original.Height * factor);
        var resizedWidget = OverlayLayout.ResizeWidget(widget, widget.Width * factor, widget.Height * factor) with
        {
            X = widget.X * factor, Y = widget.Y * factor
        };
        var direct = Settings(original.Width * factor, original.Height * factor, resizedWidget);
        var size = new Size((int)direct.Width, (int)direct.Height);
        using var renderer = new NativeOverlayRenderer();
        using var canvasImage = renderer.Render(size, resizedCanvas, OverlaySnapshot.Demo, out _);
        using var widgetImage = renderer.Render(size, direct, OverlaySnapshot.Demo, out _);

        Assert.Equal(Pixels(canvasImage), Pixels(widgetImage));
    }

    [Theory]
    [InlineData(.75)]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void TrackingButtonHitboxIncludesWidgetContentScaleOffsetAndDpi(double dpi)
    {
        var widget = OverlayLayout.ResizeWidget(OverlayCatalog.CreateWidget("controls", 37, 29) with
        {
            Width = 200, Height = 80, ShowLabel = true, FontScale = 1
        }, 300, 120);
        var settings = Settings(500, 300, widget) with { Interaction = "locked" };
        using var renderer = new NativeOverlayRenderer();
        using var image = renderer.Render(new Size((int)(500 * dpi), (int)(300 * dpi)), settings,
            OverlaySnapshot.Demo with { CanToggleTracking = true }, out var actions);

        var button = actions["toggle-tracking:" + widget.Id];
        // The 200x80 reference places the 180x28 button at local (10,35).
        AssertClose((37 + 10 * 1.5) * dpi, button.X);
        AssertClose((29 + 35 * 1.5) * dpi, button.Y);
        AssertClose(180 * 1.5 * dpi, button.Width);
        AssertClose(28 * 1.5 * dpi, button.Height);
        Assert.True(actions["widget:" + widget.Id].Contains(button));
        Assert.True(image.GetPixel((int)(button.Left + button.Width / 2), (int)(button.Top + button.Height / 2)).A > 0);
    }

    [Theory]
    [InlineData("spot")]
    [InlineData("status")]
    [InlineData("loot-scroll")]
    public void LongMetricValuesKeepTheirDifferentEndingsVisibleAtHighFontScale(string kind)
    {
        var widget = OverlayCatalog.CreateWidget(kind, 0, 0) with
        {
            Width = 200, Height = 80, ShowLabel = false, ShowIcon = false, FontScale = 2
        };
        var first = new OverlaySnapshot { Metrics = new Dictionary<string, OverlayMetric>
            { [kind] = new("Label", "Eine lange Statusmeldung mit wichtigem Ende AAAA") } };
        var second = first with { Metrics = new Dictionary<string, OverlayMetric>
            { [kind] = new("Label", "Eine lange Statusmeldung mit wichtigem Ende ZZZZ") } };
        using var renderer = new NativeOverlayRenderer();
        using var firstImage = renderer.Render(new Size(200, 80), Settings(200, 80, widget), first, out _);
        using var secondImage = renderer.Render(new Size(200, 80), Settings(200, 80, widget), second, out _);

        Assert.NotEqual(Pixels(firstImage), Pixels(secondImage));
        Assert.InRange(OpaqueBounds(firstImage).Right, 170, 192);
    }

    [Fact]
    public void SmallHighFontScaleMetricStillRendersItsDetailLine()
    {
        var widget = OverlayCatalog.CreateWidget("loot-scroll", 0, 0) with
        {
            Width = 160, Height = 40, FontScale = 2, ShowIcon = false
        };
        var first = new OverlaySnapshot { Metrics = new Dictionary<string, OverlayMetric>
            { [widget.Kind] = new("Loot-Scroll", "Aktiv", "Detail AAAA") } };
        var second = first with { Metrics = new Dictionary<string, OverlayMetric>
            { [widget.Kind] = new("Loot-Scroll", "Aktiv", "Detail ZZZZ") } };
        using var renderer = new NativeOverlayRenderer();
        using var firstImage = renderer.Render(new Size(160, 64), Settings(160, 64, widget), first, out _);
        using var secondImage = renderer.Render(new Size(160, 64), Settings(160, 64, widget), second, out _);

        Assert.NotEqual(Pixels(firstImage), Pixels(secondImage));
        Assert.True(OpaqueBounds(firstImage).Bottom <= 40);
    }

    [Fact]
    public void LongTrackingButtonLabelsKeepTheirEndingsVisible()
    {
        var widget = OverlayCatalog.CreateWidget("controls", 0, 0) with
        {
            Width = 200, Height = 80, ShowLabel = false, ShowIcon = true, FontScale = 2
        };
        var settings = Settings(200, 80, widget) with { Interaction = "locked" };
        var first = OverlaySnapshot.Demo with
        {
            CanToggleTracking = true, TrackingButtonLabel = "Die aktuelle Session fortsetzen AAAA"
        };
        using var renderer = new NativeOverlayRenderer();
        using var firstImage = renderer.Render(new Size(200, 80), settings, first, out var firstActions);
        using var secondImage = renderer.Render(new Size(200, 80), settings,
            first with { TrackingButtonLabel = "Die aktuelle Session fortsetzen ZZZZ" }, out var secondActions);

        Assert.NotEqual(Pixels(firstImage), Pixels(secondImage));
        Assert.Equal(firstActions["toggle-tracking:" + widget.Id], secondActions["toggle-tracking:" + widget.Id]);
    }

    private static OverlaySettings Settings(double width, double height, OverlayWidget widget) => new()
    {
        Width = width, Height = height, Widgets = [widget], BackgroundOpacity = 0,
        ShowBorder = false, Interaction = "passthrough"
    };

    private static void AssertClose(double expected, float actual) => Assert.InRange(actual, expected - .002, expected + .002);

    private static int[] Pixels(Bitmap image) => Enumerable.Range(0, image.Height)
        .SelectMany(y => Enumerable.Range(0, image.Width).Select(x => image.GetPixel(x, y).ToArgb())).ToArray();

    private static Rectangle OpaqueBounds(Bitmap image)
    {
        var points = Enumerable.Range(0, image.Height)
            .SelectMany(y => Enumerable.Range(0, image.Width).Where(x => image.GetPixel(x, y).A > 128).Select(x => new Point(x, y)))
            .ToArray();
        Assert.NotEmpty(points);
        return Rectangle.FromLTRB(points.Min(point => point.X), points.Min(point => point.Y),
            points.Max(point => point.X) + 1, points.Max(point => point.Y) + 1);
    }
}
