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
            Assert.Equal(17, view.VisibleItems.Count + view.HiddenCount);
            Assert.InRange(view.HeaderHeight + view.FooterHeight, 0, height - 16);
            if (view.VisibleItems.Count == 0) continue;
            Assert.True(view.Columns >= 1);
            var rows = (view.VisibleItems.Count + view.Columns - 1) / view.Columns;
            Assert.True(rows * view.CellHeight + (rows - 1) * OverlayLootPresentation.Gap <= height - 16 - view.HeaderHeight - view.FooterHeight + .001);
            Assert.True(view.Columns * view.CellWidth + (view.Columns - 1) * OverlayLootPresentation.Gap <= width - 20 + .001);
        }
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
