using System.Text.Json;
using BdoGrindTracker.App.Overlay;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayContentLayoutTests
{
    public static TheoryData<string> Kinds => new(OverlayCatalog.Widgets.Select(item => item.Kind));

    [Theory]
    [MemberData(nameof(Kinds))]
    public void DirectResizeScalesTheEntireVirtualContentWithoutChangingPreferences(string kind)
    {
        var original = OverlayCatalog.CreateWidget(kind);
        var before = OverlayContentLayout.Create(original, OverlaySnapshot.Demo);
        foreach (var factor in new[] { .25, .5, 1.5, 2 })
        {
            var resized = OverlayLayout.ResizeWidget(original, original.Width * factor, original.Height * factor);
            var after = OverlayContentLayout.Create(resized, OverlaySnapshot.Demo);
            Assert.Equal(before.Scale * factor, after.Scale, 10);
            Assert.Equal(before.LayoutWidget.Width, after.LayoutWidget.Width, 10);
            Assert.Equal(before.LayoutWidget.Height, after.LayoutWidget.Height, 10);
            Assert.Equal(original.FontScale, resized.FontScale);
            Assert.Equal(original.ItemSize, resized.ItemSize);
            Assert.Equal(0, after.LayoutWidget.X);
            Assert.Equal(0, after.LayoutWidget.Y);
            Assert.Equal(resized.Width, after.LayoutWidget.Width * after.Scale, 10);
            Assert.Equal(resized.Height, after.LayoutWidget.Height * after.Scale, 10);
        }
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("dashboard")]
    [InlineData("loot")]
    [InlineData("loot-strip")]
    public void CanvasResizeMatchesResizingEachIndividualModule(string preset)
    {
        var original = OverlayCatalog.Preset(preset);
        var resized = OverlayLayout.ResizeCanvas(original, original.Width * 1.25, original.Height * .75);
        for (var i = 0; i < original.Widgets.Count; i++)
        {
            var widget = original.Widgets[i];
            var direct = OverlayLayout.ResizeWidget(widget, widget.Width * 1.25, widget.Height * .75);
            Assert.Equal(OverlayContentLayout.Create(direct, OverlaySnapshot.Demo),
                OverlayContentLayout.Create(resized.Widgets[i], OverlaySnapshot.Demo));
        }
    }

    [Fact]
    public void UnequalAxesPreserveAspectRatioAndUseRemainingSpaceForLayout()
    {
        var original = OverlayCatalog.CreateWidget("duration");
        var wide = OverlayContentLayout.Create(OverlayLayout.ResizeWidget(original, original.Width * 2, original.Height), OverlaySnapshot.Demo);
        Assert.Equal(1, wide.Scale);
        Assert.Equal(original.Width * 2, wide.LayoutWidget.Width);
        Assert.Equal(original.Height, wide.LayoutWidget.Height);
        var narrow = OverlayContentLayout.Create(OverlayLayout.ResizeWidget(original, original.Width * .5, original.Height * 2), OverlaySnapshot.Demo);
        Assert.Equal(.5, narrow.Scale);
        Assert.Equal(original.Width, narrow.LayoutWidget.Width);
        Assert.Equal(original.Height * 4, narrow.LayoutWidget.Height);
    }

    [Fact]
    public void RepeatedResizingAndTemplateSerializationKeepTheInitialReference()
    {
        var original = OverlayCatalog.Preset("loot");
        var changed = OverlayLayout.ResizeCanvas(original, 160, 64);
        changed = OverlayLayout.ResizeCanvas(changed, 1100, 900);
        var template = OverlayTemplate.Create("Skaliert", changed);
        var saved = JsonSerializer.Serialize(template);
        var loaded = JsonSerializer.Deserialize<OverlayTemplate>(saved)!.ApplyTo(new());
        var restored = OverlayLayout.ResizeCanvas(loaded, original.Width, original.Height);
        for (var i = 0; i < original.Widgets.Count; i++)
        {
            Assert.Equal(original.Widgets[i].Width, restored.Widgets[i].ContentWidth);
            Assert.Equal(original.Widgets[i].Height, restored.Widgets[i].ContentHeight);
            var first = OverlayContentLayout.Create(original.Widgets[i], OverlaySnapshot.Demo);
            var last = OverlayContentLayout.Create(restored.Widgets[i], OverlaySnapshot.Demo);
            Assert.Equal(first.Scale, last.Scale, 10);
            Assert.Equal(first.LayoutWidget.Width, last.LayoutWidget.Width, 10);
            Assert.Equal(first.LayoutWidget.Height, last.LayoutWidget.Height, 10);
        }
    }

    [Fact]
    public void LegacyTemplatesUseTheirActualSavedSizeAsReference()
    {
        var settings = OverlayLayout.Normalize(JsonSerializer.Deserialize<OverlaySettings>(
            """{"Width":500,"Height":400,"Widgets":[{"Kind":"spot","Width":460,"Height":90}]}"""));
        var old = Assert.Single(settings.Widgets);
        Assert.Null(old.ContentWidth);
        Assert.Null(old.ContentHeight);
        var resized = OverlayLayout.ResizeWidget(old, 230, 45);
        Assert.Equal(460, resized.ContentWidth);
        Assert.Equal(90, resized.ContentHeight);
        Assert.Equal(.5, OverlayContentLayout.Create(resized, OverlaySnapshot.Demo).Scale);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void TinyAndLargeFontModulesKeepAllRequestedContentLinesInPositiveVirtualBounds(string kind)
    {
        var widget = OverlayLayout.ResizeWidget(OverlayCatalog.CreateWidget(kind) with { FontScale = 2 }, 1, 1);
        var content = OverlayContentLayout.Create(widget, OverlaySnapshot.Demo);
        Assert.True(double.IsFinite(content.Scale) && content.Scale > 0);
        Assert.True(content.LayoutWidget.Width >= content.MinimumWidth - .001);
        Assert.True(content.LayoutWidget.Height >= content.MinimumHeight - .001);
        Assert.Equal(1, content.Scale * content.LayoutWidget.Width, 10);
        Assert.Equal(1, content.Scale * content.LayoutWidget.Height, 10);
    }
}
