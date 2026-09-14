using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;

namespace BdoGrindTracker.App.Tests;

public sealed class BdoClockNativeTests
{
    [Fact]
    public void NativeClockRenderingChangesAtTheDayNightTransition()
    {
        var widget = OverlayCatalog.CreateWidget("clock", 0, 0) with
            { ShowRealTime = false, ShowGameTime = false, ShowIcon = false, ShowLabel = false };
        var transition = new DateTimeOffset(2026, 9, 13, 3, 40, 0, TimeSpan.Zero);
        var before = new OverlaySnapshot { ClockUtcNow = transition.AddSeconds(-1) };
        var after = before with { ClockUtcNow = transition };
        var settings = new OverlaySettings { Width = widget.Width, Height = widget.Height, Widgets = [widget] };
        using var renderer = new NativeOverlayRenderer();
        using var earlier = renderer.Render(new((int)widget.Width, (int)widget.Height), settings, before, out _);
        using var later = renderer.Render(new((int)widget.Width, (int)widget.Height), settings, after, out _);
        Assert.Contains(Enumerable.Range(0, earlier.Height), y =>
            Enumerable.Range(0, earlier.Width).Any(x => earlier.GetPixel(x, y) != later.GetPixel(x, y)));
    }
}
