using System.Drawing.Imaging;
using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class LootTotalsViewTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void DesktopGridShowsTwelveNamedItemsWithoutScrolling(int dpi)
    {
        var viewport = new Size(1000 * dpi / 96, 400 * dpi / 96);
        var layout = LootTotalsView.CalculateGridLayout(viewport, 12, dpi, 17 * dpi / 96);

        Assert.Equal(3, layout.ColumnCount);
        Assert.True(layout.GetContentHeight(12) <= viewport.Height);
        Assert.True(layout.TileWidth >= 280 * dpi / 96);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void NarrowerWindowKeepsTwoColumnsAndReservesOneScrollbar(int dpi)
    {
        var viewport = new Size(650 * dpi / 96, 400 * dpi / 96);
        var scrollWidth = 17 * dpi / 96;
        var layout = LootTotalsView.CalculateGridLayout(viewport, 30, dpi, scrollWidth);

        Assert.Equal(2, layout.ColumnCount);
        Assert.True(layout.GetContentHeight(30) > viewport.Height);
        Assert.InRange(
            (layout.OuterPadding * 2) + (layout.TileWidth * 2) + layout.Gap,
            viewport.Width - scrollWidth - 1,
            viewport.Width - scrollWidth);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void CardReservesSeparateAreasForIconWrappedNameAndQuantity(int dpi)
    {
        RunInSta(() =>
        {
            using var surface = new Bitmap(600 * dpi / 96, 200 * dpi / 96);
            surface.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(surface);
            using var nameFont = new Font("Segoe UI", 9f * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var quantityFont = new Font("Segoe UI", 12.5f * dpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
            var card = new Rectangle(10, 20, 280 * dpi / 96, 84 * dpi / 96);
            var layout = LootTotalsView.CalculateCardContent(graphics, card, dpi,
                "Violet Primordial Pigment - Sovereign", "× 1,234,567", nameFont, quantityFont);

            AssertCompactCenteredLayout(card, layout, dpi);
            Assert.True(layout.ItemName.Width >= 190 * dpi / 96);
        });
    }

    public static IEnumerable<object[]> CardMeasurements =>
        from dpi in new[] { 96, 120, 144, 192 }
        from width in new[] { 160, 280, 400 }
        select new object[] { dpi, width };

    [Theory]
    [MemberData(nameof(CardMeasurements))]
    public void OneAndTwoLineNamesUseActualHeightsInACompactCenteredGroup(int dpi, int logicalWidth)
    {
        RunInSta(() =>
        {
            using var surface = new Bitmap(600 * dpi / 96, 200 * dpi / 96);
            surface.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(surface);
            using var nameFont = new Font("Segoe UI", 9f * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var quantityFont = new Font("Segoe UI", 12.5f * dpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
            var card = new Rectangle(10, 20, logicalWidth * dpi / 96, 84 * dpi / 96);
            var single = LootTotalsView.CalculateCardContent(graphics, card, dpi,
                "Black Stone", "× 1", nameFont, quantityFont);
            var wrapped = LootTotalsView.CalculateCardContent(graphics, card, dpi,
                "An exceptionally long item name that wraps into several lines without covering its quantity",
                "× 1,234,567,890", nameFont, quantityFont);
            var lineHeight = TextRenderer.MeasureText(graphics, "Ag", nameFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
            var quantityHeight = TextRenderer.MeasureText(graphics, "× 1,234,567,890", quantityFont,
                new Size(wrapped.Quantity.Width, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;

            Assert.Equal(lineHeight, single.ItemName.Height);
            Assert.Equal(lineHeight * 2, wrapped.ItemName.Height);
            Assert.Equal(quantityHeight, single.Quantity.Height);
            Assert.Equal(quantityHeight, wrapped.Quantity.Height);
            Assert.True(single.ItemName.Top > wrapped.ItemName.Top);
            Assert.True(single.Quantity.Bottom < wrapped.Quantity.Bottom);
            AssertCompactCenteredLayout(card, single, dpi);
            AssertCompactCenteredLayout(card, wrapped, dpi);
        });
    }

    [Fact]
    public void NarrowViewStillHasOneColumnAndNoHorizontalScrollExtent()
    {
        RunInSta(() =>
        {
            using var view = new LootTotalsView { Size = new Size(220, 180) };
            view.SetTotals(CreateTotals(40));

            Assert.Equal(40, view.EntryCount);
            Assert.Equal(0, view.AutoScrollMinSize.Width);
            Assert.True(view.AutoScrollMinSize.Height > view.ClientSize.Height);
            Assert.Equal(1, LootTotalsView.CalculateGridLayout(view.ClientSize, 40, 96, 17).ColumnCount);
        });
    }

    [Fact]
    public void RepeatedSnapshotsDoNotAccumulateDropsOrCreateItemControls()
    {
        RunInSta(() =>
        {
            using var view = new LootTotalsView { Size = new Size(1000, 400) };
            for (var update = 0; update < 100; update++)
            {
                view.SetTotals(
                [
                    new("Black Crystal Fragment", 1000),
                    new("Black Crystal Fragment", 582),
                    new("Ancient Spirit Dust", 20),
                    new("Ancient Spirit Dust", -5),
                    new("Black Stone", 0),
                    new("Caphras Stone", -1),
                    new(" ", 500)
                ]);
            }

            Assert.Equal(2, view.EntryCount);
            Assert.Equal(1597, view.TotalQuantity);
            Assert.Empty(view.Controls);

            view.SetTotals([]);
            Assert.Equal(0, view.EntryCount);
            Assert.Equal(0, view.TotalQuantity);
            Assert.True(view.AutoScrollMinSize.Height <= view.ClientSize.Height);
        });
    }

    [Fact]
    public void LongNamesAndLargeQuantitiesPaintAfterResizeAndFontChange()
    {
        RunInSta(() =>
        {
            using var view = new LootTotalsView();
            using var font = new Font("Segoe UI", 10f);
            view.Font = font;
            view.SetTotals(
            [
                new("An exceptionally long item name that needs to wrap without covering the quantity", 1_234_567_890),
                new("Refined Essence of Devouring", 8),
                new("BON Origin Shard", 1)
            ]);

            foreach (var size in new[] { new Size(1000, 400), new Size(650, 320), new Size(300, 240) })
            {
                view.Size = size;
                using var bitmap = new Bitmap(size.Width, size.Height);
                view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
                Assert.Equal(3, view.EntryCount);
                Assert.Equal(1_234_567_899, view.TotalQuantity);
            }
        });
    }

    [Fact]
    public void CompactSingleAndWrappedTitlesRenderOffscreen()
    {
        RunInSta(() =>
        {
            using var view = new LootTotalsView { Size = new Size(1000, 230) };
            view.SetTotals(
            [
                new("Black Crystal Fragment", 1582),
                new("Ancient Spirit Dust", 48),
                new("Black Stone", 22),
                new("Refined Essence of Devouring", 5),
                new("Violet Primordial Pigment - Sovereign", 2),
                new("An exceptionally long item name that needs more than two lines", 1)
            ]);
            using var bitmap = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var directory = Environment.GetEnvironmentVariable("BDO_UI_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                bitmap.Save(Path.Combine(directory, "loot-cards-centered-text.png"), ImageFormat.Png);
            }
            Assert.Equal(6, view.EntryCount);
        });
    }

    [Fact]
    public void DisposingViewReleasesItsOwnedIconRepository()
    {
        RunInSta(() =>
        {
            var repository = new LootIconRepository(Path.Combine(AppContext.BaseDirectory, "data", "icons"));
            var view = new LootTotalsView(repository);
            view.SetTotals([new("Nev's Fragment", 1)]);

            view.Dispose();
            view.Dispose();

            Assert.Throws<ObjectDisposedException>(() => repository.GetIcon("Nev's Fragment"));
        });
    }

    private static IEnumerable<KeyValuePair<string, long>> CreateTotals(int count) =>
        Enumerable.Range(1, count).Select(index => new KeyValuePair<string, long>($"Test loot item {index}", index));

    private static void AssertCompactCenteredLayout(Rectangle card, LootTotalsView.CardContentLayout layout, int dpi)
    {
        Assert.True(card.Contains(layout.Icon));
        Assert.True(card.Contains(layout.ItemName));
        Assert.True(card.Contains(layout.Quantity));
        Assert.False(layout.Icon.IntersectsWith(layout.ItemName));
        Assert.False(layout.Icon.IntersectsWith(layout.Quantity));
        Assert.False(layout.ItemName.IntersectsWith(layout.Quantity));
        Assert.Equal((int)Math.Round(4 * dpi / 96d), layout.Quantity.Top - layout.ItemName.Bottom);
        var groupCenterTwice = layout.ItemName.Top + layout.Quantity.Bottom;
        var cardCenterTwice = (card.Top * 2) + card.Height;
        Assert.InRange(Math.Abs(groupCenterTwice - cardCenterTwice), 0, 1);
        Assert.Equal(layout.ItemName.Left, layout.Quantity.Left);
        Assert.Equal(layout.ItemName.Width, layout.Quantity.Width);
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Loot view test did not finish.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
