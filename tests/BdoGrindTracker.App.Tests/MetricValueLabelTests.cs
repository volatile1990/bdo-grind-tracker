using System.Reflection;
using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class MetricValueLabelTests
{
    [Theory]
    [InlineData(96, 140, "00:00:00")]
    [InlineData(120, 180, "999:59:59")]
    [InlineData(144, 140, "1.234.567.890")]
    [InlineData(192, 140, "9.223.372.036.854.775.807")]
    public void NumericValuesFitInACompletelyVisibleSingleLine(int dpi, int logicalWidth, string text)
    {
        RunInSta(() =>
        {
            using var preferredFont = new Font("Segoe UI Semibold", 25f * dpi / 96f, FontStyle.Bold);
            using var label = new MetricValueLabel
            {
                AutoSize = false,
                Font = preferredFont,
                Text = text,
                Padding = new Padding(4),
                Width = logicalWidth * dpi / 96
            };
            label.Height = label.GetPreferredSize(Size.Empty).Height;
            using var bitmap = new Bitmap(label.Width, label.Height);
            bitmap.SetResolution(dpi, dpi);
            using var graphics = Graphics.FromImage(bitmap);

            var measured = label.MeasureRenderedText(graphics);

            Assert.InRange(measured.Width, 1, label.ClientSize.Width - label.Padding.Horizontal);
            Assert.InRange(measured.Height, 1, label.ClientSize.Height - label.Padding.Vertical);
            Assert.InRange(label.RenderedFontSizeInPoints, 0.1f, preferredFont.SizeInPoints);
            Assert.False(label.AutoEllipsis);
            Assert.Equal(AccessibleRole.StaticText, label.AccessibleRole);
            label.DrawToBitmap(bitmap, label.ClientRectangle);
        });
    }

    [Fact]
    public void NormalValuesKeepThePreferredFontAndLongValuesDoNotGrowColumns()
    {
        RunInSta(() =>
        {
            using var font = new Font("Segoe UI Semibold", 25f, FontStyle.Bold);
            using var label = new MetricValueLabel { AutoSize = false, Font = font, Text = "1.582" };
            var preferred = label.GetPreferredSize(Size.Empty);
            label.Size = new Size(200, preferred.Height);
            using var bitmap = new Bitmap(label.Width, label.Height);
            using var graphics = Graphics.FromImage(bitmap);

            label.MeasureRenderedText(graphics);
            Assert.Equal(25f, label.RenderedFontSizeInPoints);
            Assert.Equal(1, preferred.Width);

            label.Text = "9.223.372.036.854.775.807";
            Assert.Equal(preferred, label.GetPreferredSize(Size.Empty));
            Assert.True(label.MeasureRenderedText(graphics).Width <= label.Width);
            Assert.True(label.RenderedFontSizeInPoints < 25f);
            Assert.Equal(25f, font.SizeInPoints);
        });
    }

    [Fact]
    public void FontFitIsCachedAndInvalidatedWhenTextWidthPaddingOrFontChanges()
    {
        RunInSta(() =>
        {
            using var font = new Font("Segoe UI Semibold", 25f, FontStyle.Bold);
            using var otherFont = new Font("Segoe UI Semibold", 28f, FontStyle.Bold);
            using var label = new MetricValueLabel
            {
                AutoSize = false, Font = font, Text = "9.223.372.036.854.775.807", Size = new Size(160, 60)
            };
            using var bitmap = new Bitmap(1000, 120);
            using var graphics = Graphics.FromImage(bitmap);

            label.MeasureRenderedText(graphics);
            var initialFit = GetFittedFont(label);
            Assert.NotNull(initialFit);
            label.MeasureRenderedText(graphics);
            Assert.Same(initialFit, GetFittedFont(label));

            foreach (var change in new Action[]
                     {
                         () => label.Text = "8.223.372.036.854.775.807",
                         () => label.Width = 140,
                         () => label.Padding = new Padding(10, 2, 10, 2),
                         () => label.Font = otherFont
                     })
            {
                var oldFit = GetFittedFont(label);
                change();
                Assert.Null(GetFittedFont(label));
                Assert.True(label.MeasureRenderedText(graphics).Width <= label.Width - label.Padding.Horizontal);
                Assert.NotSame(oldFit, GetFittedFont(label));
            }

            label.Width = 1000;
            label.MeasureRenderedText(graphics);
            Assert.Null(GetFittedFont(label));
            Assert.Equal(28f, label.RenderedFontSizeInPoints);
        });
    }

    [Fact]
    public void EmptyAndZeroSizedLabelsAreSafeAndDisposalDoesNotOwnThePreferredFont()
    {
        RunInSta(() =>
        {
            using var font = new Font("Segoe UI Semibold", 25f, FontStyle.Bold);
            var label = new MetricValueLabel { AutoSize = false, Font = font, Size = Size.Empty };
            using var bitmap = new Bitmap(160, 60);
            using var graphics = Graphics.FromImage(bitmap);
            Assert.Equal(Size.Empty, label.MeasureRenderedText(graphics));
            label.Text = "1.234.567.890";
            Assert.Equal(Size.Empty, label.MeasureRenderedText(graphics));
            label.Size = bitmap.Size;
            label.MeasureRenderedText(graphics);
            var fittedFont = GetFittedFont(label);
            Assert.NotNull(fittedFont);

            label.Dispose();
            label.Dispose();

            Assert.Null(GetFittedFont(label));
            Assert.Throws<ArgumentException>(() => fittedFont.GetHeight(graphics));
            Assert.True(font.GetHeight(graphics) > 0);
        });
    }

    [Fact]
    public void RenderingOnAnotherDpiContextRecalculatesAndReleasesTheOldFit()
    {
        RunInSta(() =>
        {
            using var font = new Font("Segoe UI Semibold", 25f, FontStyle.Bold);
            using var label = new MetricValueLabel
            {
                AutoSize = false, Font = font, Text = "9.223.372.036.854.775.807", Size = new Size(160, 60)
            };
            using var firstBitmap = new Bitmap(160, 60);
            firstBitmap.SetResolution(96, 96);
            using var firstGraphics = Graphics.FromImage(firstBitmap);
            label.MeasureRenderedText(firstGraphics);
            var oldFit = GetFittedFont(label);
            Assert.NotNull(oldFit);

            using var nextBitmap = new Bitmap(160, 60);
            nextBitmap.SetResolution(192, 192);
            using var nextGraphics = Graphics.FromImage(nextBitmap);
            Assert.True(label.MeasureRenderedText(nextGraphics).Width <= label.Width);

            Assert.NotSame(oldFit, GetFittedFont(label));
            Assert.Throws<ArgumentException>(() => oldFit.GetHeight(firstGraphics));
        });
    }

    private static Font? GetFittedFont(MetricValueLabel label) =>
        (Font?)typeof(MetricValueLabel).GetField("_fittedFont", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(label);

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Metric label test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
