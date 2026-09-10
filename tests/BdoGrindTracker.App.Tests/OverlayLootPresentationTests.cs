using System.Text.Json;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayLootPresentationTests
{
    [Fact]
    public void AllDisplaysUseLiveSessionAmountsRegardlessOfElapsedTimePauseAndCorrection()
    {
        var metrics = new OverlayMetrics();
        var state = new TrackerState
        {
            SessionId = Guid.NewGuid(), HasSession = true, IsRunning = true,
            SpotId = LootSpotCatalog.HermesiaId,
            Loot = new(new Dictionary<string, long> { ["Black Crystal Fragment"] = 1735, ["Black Stone"] = 12 }, 1747, 200),
        };
        foreach (var minutes in new[] { 0, 59, 60, 61, 120, 235 })
        {
            state = state with { Elapsed = TimeSpan.FromMinutes(minutes), IsRunning = minutes < 120 };
            var snapshot = metrics.Update(state, new());
            foreach (var kind in new[] { "drop-grid", "drop-strip", "drop-list", "drop-item", "drops" })
            {
                var widget = OverlayCatalog.CreateWidget(kind) with { ItemNames = ["Black Crystal Fragment"] };
                var view = OverlayLootPresentation.Create(widget, snapshot);
                var trash = Assert.Single(view.Items, item => item.IsTrash);
                Assert.Equal(state.Loot.Totals[trash.CanonicalName], trash.Quantity);
                Assert.Equal(Presentation.Number(1735), trash.QuantityText);
            }
        }
        var corrected = state with { Loot = new(new Dictionary<string, long> { ["Black Crystal Fragment"] = 1720, ["Black Stone"] = 19 }, 1739, 201) };
        var updated = metrics.Update(corrected, new());
        Assert.Equal(1720, updated.Drops.Single(item => item.IsTrash).Quantity);
        Assert.Equal(19, updated.Drops.Single(item => item.CanonicalName == "Black Stone").Quantity);
        Assert.Empty(metrics.Update(new(), new()).Drops);
    }

    [Fact]
    public void ExplicitSelectionKeepsCustomOrderAndZeroSlotsWithoutInventingDrops()
    {
        var snapshot = OverlaySnapshot.Demo;
        var widget = OverlayCatalog.CreateWidget("drop-strip") with
        {
            ItemFilter = "selected", ItemNames = ["Pure Black Stone", "Black Stone", "pure black stone"],
        };
        var selected = OverlayLootPresentation.Create(widget, snapshot).Items;
        Assert.Equal(2, selected.Count);
        Assert.Equal("Pure Black Stone", selected[0].CanonicalName);
        Assert.Equal("0", selected[0].QuantityText);
        Assert.Equal(snapshot.Drops.Single(item => item.CanonicalName == "Black Stone"), selected[1]);
        Assert.DoesNotContain(snapshot.Drops, item => item.CanonicalName == "Pure Black Stone");
    }

    [Fact]
    public void NumericSortFiltersAndSingleItemSelectionUseCanonicalIdentity()
    {
        var snapshot = OverlaySnapshot.Demo;
        var widget = OverlayCatalog.CreateWidget("drop-grid") with { ItemSort = "quantity" };
        var view = OverlayLootPresentation.Create(widget, snapshot);
        Assert.Equal(snapshot.Drops.Select(item => item.Quantity).OrderDescending(), view.Items.Select(item => item.Quantity));
        Assert.All(OverlayLootPresentation.Create(widget with { ItemFilter = "rare" }, snapshot).Items, item => Assert.True(item.IsRare));
        Assert.Single(OverlayLootPresentation.Create(widget with { ItemFilter = "trash" }, snapshot).Items);
        var itemWidget = OverlayCatalog.CreateWidget("drop-item") with { ItemNames = ["Caphras Stone", "Black Stone"] };
        Assert.Equal("Caphras Stone", Assert.Single(OverlayLootPresentation.Create(itemWidget, snapshot).Items).CanonicalName);
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("strip")]
    [InlineData("list")]
    [InlineData("card")]
    public void VisibleItemsAndOverflowAccountForEverySelectedDropAndFitTheModule(string mode)
    {
        var items = Enumerable.Range(0, 17).Select(index => new OverlayLootItem(index.ToString(), "Item " + index, "1", Quantity: 1)).ToArray();
        var snapshot = new OverlaySnapshot { Drops = items };
        foreach (var width in new[] { 80d, 168, 344, 520 })
        foreach (var height in new[] { 40d, 96, 168, 224 })
        foreach (var font in new[] { .7, 1, 2 })
        {
            var widget = OverlayCatalog.CreateWidget("drop-grid") with { Width = width, Height = height, ItemView = mode, ItemLimit = 24, FontScale = font };
            var view = OverlayLootPresentation.Create(widget, snapshot);
            Assert.Equal(mode == "card" ? 1 : 17, view.VisibleItems.Count);
            Assert.Equal(mode == "card" ? 16 : 0, view.HiddenCount);
            Assert.Equal(17, view.VisibleItems.Count + view.HiddenCount);
            AssertFitted(widget, view);
        }
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("strip")]
    [InlineData("list")]
    [InlineData("card")]
    public void SmallModuleKeepsEveryItemUpToTheConfiguredLimitAndReportsOnlyItsExcess(string mode)
    {
        var snapshot = Items(31);
        var widget = OverlayCatalog.CreateWidget("drop-grid") with
        {
            Width = 80, Height = 40, ItemView = mode, ItemLimit = 6,
            ItemSize = 112, FontScale = 2, ItemSort = "quantity",
        };

        var view = OverlayLootPresentation.Create(widget, snapshot);

        var count = mode == "card" ? 1 : 6;
        Assert.Equal(count, view.VisibleItems.Count);
        Assert.Equal(31 - count, view.HiddenCount);
        Assert.Equal(snapshot.Drops.OrderByDescending(item => item.Quantity).Take(count), view.VisibleItems);
        Assert.True(view.ItemSize < widget.ItemSize);
        Assert.True(view.FontScale < widget.FontScale);
        Assert.Equal(18 * view.FontScale, view.HeaderHeight, 10);
        Assert.Equal(18 * view.FontScale, view.FooterHeight, 10);
        AssertFitted(widget, view);
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("strip")]
    [InlineData("list")]
    [InlineData("card")]
    public void SufficientSpacePreservesRequestedItemAndFontSizes(string mode)
    {
        var widget = OverlayCatalog.CreateWidget("drop-grid") with
        {
            Width = 1600, Height = 1200, ItemView = mode, ItemSize = 80, FontScale = 1.25,
        };

        var view = OverlayLootPresentation.Create(widget, Items(3));

        Assert.Equal(widget.ItemSize, view.ItemSize);
        Assert.Equal(widget.FontScale, view.FontScale);
        Assert.Equal(OverlayLootPresentation.Gap, view.Gap);
        AssertFitted(widget, view);
    }

    [Fact]
    public void GridReflowsToLargerCellsWhenThePreferredColumnsCannotFitAllItems()
    {
        var widget = OverlayCatalog.CreateWidget("drop-grid") with
        {
            Width = 200, Height = 200, ItemLimit = 12,
        };

        var view = OverlayLootPresentation.Create(widget, Items(12));

        // Four columns use the available square more efficiently than shrinking
        // the original three-column grid enough to fit its four rows.
        Assert.Equal(4, view.Columns);
        Assert.Equal(56 * 180d / 236, view.CellWidth, 10);
        Assert.Equal(12, view.VisibleItems.Count);
        Assert.Equal(0, view.HiddenCount);
        AssertFitted(widget, view);
    }

    [Fact]
    public void StripMayShrinkBelowUsualEditingMinimumsToKeepAllTwentyFourItems()
    {
        var widget = OverlayCatalog.CreateWidget("drop-strip") with
        {
            Width = 80, Height = 40, ItemLimit = 24, ItemSize = 112, FontScale = 2,
        };

        var view = OverlayLootPresentation.Create(widget, Items(24));

        Assert.Equal(24, view.VisibleItems.Count);
        Assert.Equal(24, view.Columns);
        Assert.Equal(0, view.HiddenCount);
        Assert.InRange(view.ItemSize, double.Epsilon, 3);
        Assert.InRange(view.FontScale, double.Epsilon, .05);
        AssertFitted(widget, view);
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("strip")]
    [InlineData("list")]
    [InlineData("card")]
    public void UniformPhysicalResizeKeepsVirtualLootLayoutAndSelectionStable(string mode)
    {
        var snapshot = Items(24);
        var original = OverlayCatalog.CreateWidget("drop-grid") with
        {
            Width = 520, Height = 224, ItemView = mode, ItemLimit = 24,
            ContentWidth = 520, ContentHeight = 224,
        };
        var baseline = OverlayLootPresentation.Create(OverlayContentLayout.Create(original, snapshot).LayoutWidget, snapshot);
        foreach (var factor in new[] { .01, .1, .5, 1, 2 })
        {
            var physical = original with { Width = original.Width * factor, Height = original.Height * factor };
            var content = OverlayContentLayout.Create(physical, snapshot);
            var resized = OverlayLootPresentation.Create(content.LayoutWidget, snapshot);

            Assert.Equal(factor, content.Scale, 10);
            Assert.Equal(baseline.VisibleItems, resized.VisibleItems);
            Assert.Equal(baseline.HiddenCount, resized.HiddenCount);
            Assert.Equal(baseline.Columns, resized.Columns);
            Assert.Equal(baseline.CellWidth, resized.CellWidth, 10);
            Assert.Equal(baseline.CellHeight, resized.CellHeight, 10);
            Assert.Equal(baseline.ItemSize, resized.ItemSize, 10);
            Assert.Equal(baseline.FontScale, resized.FontScale, 10);
            Assert.Equal(baseline.Gap, resized.Gap, 10);
            AssertFitted(content.LayoutWidget, resized);
        }
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("strip")]
    [InlineData("list")]
    [InlineData("card")]
    public void ExtremePhysicalAspectRatiosStillFitTheCompleteLogicalSelection(string mode)
    {
        var snapshot = Items(31);
        foreach (var (width, height) in new[] { (1d, 1d), (1d, 1200d), (1600d, 1d), (1d, 64d) })
        {
            var physical = OverlayCatalog.CreateWidget("drop-grid") with
            {
                Width = width, Height = height, ItemView = mode, ItemLimit = 24,
                ItemSize = 112, FontScale = 2,
            };
            var content = OverlayContentLayout.Create(physical, snapshot);
            var view = OverlayLootPresentation.Create(content.LayoutWidget, snapshot);

            Assert.Equal(mode == "card" ? 1 : 24, view.VisibleItems.Count);
            Assert.Equal(mode == "card" ? 30 : 7, view.HiddenCount);
            AssertFitted(content.LayoutWidget, view);
        }
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("strip")]
    [InlineData("list")]
    [InlineData("card")]
    public void EmptyLootAndDisabledLabelsDoNotReserveAnOverflowFooter(string mode)
    {
        var widget = OverlayCatalog.CreateWidget("drop-grid") with { ItemView = mode, ShowLabel = false };

        var view = OverlayLootPresentation.Create(widget, new());

        Assert.Empty(view.Items);
        Assert.Empty(view.VisibleItems);
        Assert.Equal(0, view.HiddenCount);
        Assert.Equal(0, view.HeaderHeight);
        Assert.Equal(0, view.FooterHeight);
        AssertFitted(widget, view);
    }

    private static OverlaySnapshot Items(int count) => new()
    {
        Drops = Enumerable.Range(1, count)
            .Select(index => new OverlayLootItem(index.ToString(), "Item " + index, index.ToString(), Quantity: index))
            .ToArray(),
    };

    private static void AssertFitted(OverlayWidget widget, OverlayLootView view)
    {
        var width = widget.Width - OverlayLootPresentation.PaddingX * 2;
        var height = widget.Height - OverlayLootPresentation.PaddingY * 2;
        Assert.InRange(view.HeaderHeight + view.FooterHeight, 0, height + .001);
        Assert.InRange(view.ItemSize, double.Epsilon, widget.ItemSize);
        Assert.InRange(view.FontScale, double.Epsilon, widget.FontScale);
        Assert.InRange(view.Gap, double.Epsilon, OverlayLootPresentation.Gap);
        Assert.Equal(view.ItemSize / widget.ItemSize, view.FontScale / widget.FontScale, 10);
        Assert.Equal(view.ItemSize / widget.ItemSize, view.Gap / OverlayLootPresentation.Gap, 10);
        Assert.True(view.Columns >= 1);
        var rows = Math.Max(1, (view.VisibleItems.Count + view.Columns - 1) / view.Columns);
        Assert.True(rows * view.CellHeight + (rows - 1) * view.Gap <= height - view.HeaderHeight - view.FooterHeight + .001);
        Assert.True(view.Columns * view.CellWidth + (view.Columns - 1) * view.Gap <= width + .001);
    }

    [Fact]
    public void SavedSelectionOwnsItsNamesAndRoundTripsAllPresentationOptions()
    {
        var names = new[] { "Black Stone", "Black Stone", " Caphras Stone ", "" };
        var settings = OverlayLayout.Normalize(new() { Widgets = [OverlayCatalog.CreateWidget("drop-strip") with
        { ItemFilter = "selected", ItemNames = names, ItemSize = 80, ItemSort = "name", ItemLimit = 13 }] });
        names[0] = "Changed by caller";
        var restored = OverlayLayout.Normalize(JsonSerializer.Deserialize<OverlaySettings>(JsonSerializer.Serialize(settings)));
        var widget = Assert.Single(restored.Widgets);
        Assert.Equal(new[] { "Black Stone", "Caphras Stone" }, widget.ItemNames);
        Assert.Equal("selected", widget.ItemFilter);
        Assert.Equal("strip", widget.ItemView);
        Assert.Equal("name", widget.ItemSort);
        Assert.Equal(80, widget.ItemSize);
        Assert.Equal(13, widget.ItemLimit);
    }
}
