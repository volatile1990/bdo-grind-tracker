using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeOverlayLootRenderingTests
{
    [Theory]
    [InlineData(AppThemes.Grindcrest, .75)]
    [InlineData(AppThemes.Grindcrest, 1)]
    [InlineData(AppThemes.Grindcrest, 1.25)]
    [InlineData(AppThemes.Grindcrest, 1.5)]
    [InlineData(AppThemes.Grindcrest, 2)]
    [InlineData(AppThemes.Light, 1.25)]
    [InlineData(AppThemes.Cats, 1.25)]
    public void LootQuantityShadeFadesInWithoutHorizontalSeams(string theme, double scale)
    {
        foreach (var offset in new[] { 0, .25, .5, .75 })
        {
            var widget = OverlayCatalog.CreateWidget("drop-grid", 0, offset) with
            {
                Width = 320, Height = 200, ItemSize = 56, ShowLabel = false, ShowIcon = false
            };
            var settings = new OverlaySettings
            {
                Width = 320, Height = 208, Widgets = [widget], ShowBorder = false,
                BackgroundOpacity = .5, Interaction = "passthrough"
            };
            // Empty labels expose the shading without artwork or text affecting
            // the pixels. Its opacity must increase smoothly toward the bottom.
            var snapshot = new OverlaySnapshot
            {
                ThemeId = theme,
                Drops = Enumerable.Range(0, 10).Select(i => new OverlayLootItem($"item-{i}", "", "")).ToArray()
            };
            var view = OverlayLootPresentation.Create(widget, snapshot);
            using var renderer = new NativeOverlayRenderer();
            using var rendered = renderer.Render(new((int)(320 * scale), (int)(208 * scale)), settings, snapshot, out _);

            for (var row = 0; row < 2; row++)
            {
                var top = offset + 8 + row * (view.CellHeight + view.Gap);
                var x = (int)((10 + view.CellWidth / 2) * scale);
                var start = (int)Math.Ceiling((top + 4) * scale);
                var end = (int)Math.Floor((top + view.CellHeight - 4) * scale);
                var previous = rendered.GetPixel(x, start).A;
                for (var y = start + 1; y <= end; y++)
                {
                    var alpha = rendered.GetPixel(x, y).A;
                    Assert.True(alpha >= previous - 1,
                        $"Shade opacity drops from {previous} to {alpha} at ({x}, {y}); theme={theme}, scale={scale}, offset={offset}.");
                    previous = alpha;
                }
            }
        }
    }
}
