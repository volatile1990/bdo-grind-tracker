using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Tests;

public sealed class OverlayConsumablesTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void MetricsUseTheSameRecordedCountsCostsAndMissingPricesAsLive(string language)
    {
        var at = DateTimeOffset.UnixEpoch;
        var buffs = new BuffLedgerSnapshot(
        [
            new("simple-cron-meal", "Simple Cron Meal", 9692, at, new(1_000_000, "EU", at, false)) { IsSessionStart = true },
            new("simple-cron-meal", "Simple Cron Meal", 9692, at.AddHours(2), new(2_000_000, "EU", at, true)),
            new("perfume-of-courage", "Perfume of Courage", 734, at, null),
        ], [], [new("active-only", "Active only", null, TimeSpan.FromMinutes(5), at, null, true)]);
        var expected = ConsumablesPresentation.Create(buffs, language);

        var actual = new OverlayMetrics().Update(new TrackerState { Buffs = buffs }, new() { UiLanguage = language });

        Assert.Equal(expected.Items, actual.Consumables.Items);
        Assert.Equal(expected.Cost, actual.Consumables.Cost);
        Assert.Equal(expected.CostDescription, actual.Consumables.CostDescription);
        Assert.Equal(expected.Cost, actual.Metrics["consumables"].Value);
        Assert.True(actual.Metrics["consumables"].IsWarning);
        Assert.Equal(2, actual.Consumables.Items.Single(item => item.Id == "simple-cron-meal").Count);
        Assert.Equal(3_000_000m, actual.Consumables.Items.Sum(item => item.KnownCost));
        Assert.DoesNotContain(actual.Consumables.Items, item => item.Id == "active-only");
        Assert.Empty(actual.Drops);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void EveryConsumedItemFitsAndCostIsReservedEvenWithOldLootFilters(bool label, bool icon)
    {
        var snapshot = Snapshot(37);
        foreach (var width in new[] { 80d, 168, 344 })
        foreach (var height in new[] { 40d, 96, 168 })
        {
            var saved = OverlayCatalog.CreateWidget("consumables") with
            {
                Width = width, Height = height, ShowLabel = label, ShowIcon = icon,
                ItemLimit = 1, ItemView = "card", ItemFilter = "rare", ItemNames = ["unrelated"],
            };
            var widget = OverlayContentLayout.Create(saved, snapshot).LayoutWidget;
            var view = OverlayLootPresentation.Create(widget, snapshot);
            Assert.Equal(37, view.VisibleItems.Count);
            Assert.Equal(0, view.HiddenCount);
            Assert.Equal(snapshot.Consumables.Items.Select(item => item.Id), view.VisibleItems.Select(item => item.CanonicalName));
            Assert.True(view.FooterHeight > 0);
            Assert.Equal(24 * view.FontScale, view.FooterHeight, 8);
            var rows = (view.VisibleItems.Count + view.Columns - 1) / view.Columns;
            Assert.True(view.Columns * view.CellWidth + (view.Columns - 1) * view.Gap <= widget.Width - 20 + .00001);
            Assert.True(view.HeaderHeight + rows * view.CellHeight + (rows - 1) * view.Gap + view.FooterHeight <= widget.Height - 16 + .00001);
        }
    }

    [Fact]
    public void ConsumablesSurvivePersistenceAndCannotInheritDropSelectionOrCardTruncation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Grindcrest.Consumables.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new OverlaySettingsStore(directory);
            var widget = OverlayCatalog.CreateWidget("consumables") with
            {
                ShowLabel = false, ShowIcon = false, ItemSize = 80, FontScale = 1.25,
                ItemView = "card", ItemFilter = "rare", ItemNames = ["Black Stone"],
            };
            store.Save(new() { Widgets = [widget] });

            var restored = Assert.Single(new OverlaySettingsStore(directory).Load().Widgets);

            Assert.Equal("consumables", restored.Kind);
            Assert.Equal(widget.Id, restored.Id);
            Assert.Equal("grid", restored.ItemView);
            Assert.Equal("all", restored.ItemFilter);
            Assert.Empty(restored.ItemNames);
            Assert.Equal(80, restored.ItemSize);
            Assert.Equal(1.25, restored.FontScale);
            Assert.False(restored.ShowLabel);
            Assert.False(restored.ShowIcon);
            Assert.False(OverlayCatalog.IsLootWidget(restored.Kind));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void DemoHasRealCatalogIconsRepeatedConsumptionAndKnownCosts(string language)
    {
        var demo = OverlayMetrics.DemoFor(language).Consumables;
        Assert.Equal(4, demo.Items.Count);
        Assert.Contains(demo.Items, item => item.Count > 1);
        Assert.All(demo.Items, item => Assert.StartsWith("assets/buffs/client-", item.IconPath));
        Assert.All(demo.Items, item => Assert.True(item.KnownCost > 0));
        Assert.False(demo.HasMissingPrices);
    }

    [Theory]
    [InlineData(.5)]
    [InlineData(1)]
    [InlineData(2)]
    public void NativeRendererKeepsTheCostWithoutHeadingOrIconsAndDrawsTheLastItem(double scale)
    {
        var widget = OverlayCatalog.CreateWidget("consumables", 0, 0) with { ShowLabel = false, ShowIcon = false };
        var settings = new OverlaySettings
        {
            Width = widget.Width, Height = widget.Height, Widgets = [widget], ShowBorder = false,
            BackgroundOpacity = 0, Interaction = "passthrough",
        };
        var snapshot = Snapshot(17);
        var size = new Size((int)(widget.Width * scale), (int)(widget.Height * scale));
        using var renderer = new NativeOverlayRenderer();
        using var withCost = renderer.Render(size, settings, snapshot, out _);
        using var withoutCost = renderer.Render(size, settings,
            snapshot with { Consumables = snapshot.Consumables with { Cost = "" } }, out _);
        var costDifference = Differences(withCost, withoutCost);
        Assert.NotEmpty(costDifference);
        var view = OverlayLootPresentation.Create(widget, snapshot);
        Assert.All(costDifference, point => Assert.True(point.Y >= (widget.Height - 8 - view.FooterHeight) * scale - 1));

        var changedItems = snapshot.Consumables.Items.ToArray();
        changedItems[^1] = changedItems[^1] with { Count = 9999 };
        using var changedLast = renderer.Render(size, settings,
            snapshot with { Consumables = snapshot.Consumables with { Items = changedItems } }, out _);
        Assert.NotEmpty(Differences(withCost, changedLast));
    }

    private static OverlaySnapshot Snapshot(int count) => new()
    {
        UiLanguage = "de",
        Consumables = new(Enumerable.Range(1, count).Select(index =>
            new ConsumableItem("buff-" + index, "Buff " + index, null, index, index * 1000, 0,
                "Recorded buff " + index)).ToArray(), "51,25 Mio. Silber", "Gesamtkosten der Session", false),
    };

    private static IReadOnlyList<Point> Differences(Bitmap left, Bitmap right)
    {
        var points = new List<Point>();
        for (var y = 0; y < left.Height; y++)
        for (var x = 0; x < left.Width; x++)
            if (left.GetPixel(x, y) != right.GetPixel(x, y)) points.Add(new(x, y));
        return points;
    }
}
