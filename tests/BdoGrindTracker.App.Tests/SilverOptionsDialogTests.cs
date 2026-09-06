using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class SilverOptionsDialogTests
{
    [Fact]
    public void FreshDialogClearlyUsesEuAndNoTaxBenefits()
    {
        RunInSta(() =>
        {
            using var dialog = new SilverOptionsDialog();

            Assert.Equal("eu", dialog.SelectedRegion);
            Assert.Equal(new SilverTaxOptions(), dialog.SelectedTaxOptions);
            Assert.False(Get<CheckBox>(dialog, "_valuePackCheckBox").Checked);
            Assert.False(Get<CheckBox>(dialog, "_merchantRingCheckBox").Checked);
            Assert.Equal(0, Get<NumericUpDown>(dialog, "_familyFameInput").Value);
            Assert.Equal(2, Get<ComboBox>(dialog, "_regionComboBox").Items.Count);
            Assert.Contains(0.65m.ToString("P3", CultureInfo.CurrentCulture),
                Get<Label>(dialog, "_marketReturnLabel").Text);
            Assert.Equal(DialogResult.None, dialog.DialogResult);
            Assert.False(dialog.Visible);
        });
    }

    [Fact]
    public void EditingControlsDoesNotPublishSelectionsBeforeApply()
    {
        RunInSta(() =>
        {
            var original = new SilverTaxOptions(false, false, 1000);
            using var dialog = new SilverOptionsDialog("eu", original);
            EditAllValues(dialog);

            Assert.Equal("eu", dialog.SelectedRegion);
            Assert.Equal(original, dialog.SelectedTaxOptions);
            Assert.False(original.ValuePack);
            Assert.False(original.MerchantRing);
            Assert.Equal(1000, original.FamilyFame);
            Assert.Equal(DialogResult.None, dialog.DialogResult);
        });
    }

    [Fact]
    public void ApplyPublishesValidatedRegionAndBenefits()
    {
        RunInSta(() =>
        {
            using var dialog = new SilverOptionsDialog();
            EditAllValues(dialog);

            Invoke(dialog, "ApplyChanges");

            Assert.Equal(DialogResult.OK, dialog.DialogResult);
            Assert.Equal("na", dialog.SelectedRegion);
            Assert.Equal(new SilverTaxOptions(true, true, 7000), dialog.SelectedTaxOptions);
        });
    }

    [Fact]
    public void CancelLeavesOriginalPreferencesUntouched()
    {
        RunInSta(() =>
        {
            var original = new SilverTaxOptions(false, true, 1200);
            using var dialog = new SilverOptionsDialog("eu", original);
            EditAllValues(dialog);

            Invoke(dialog, "CancelChanges");

            Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
            Assert.Equal("eu", dialog.SelectedRegion);
            Assert.Equal(original, dialog.SelectedTaxOptions);
        });
    }

    [Theory]
    [InlineData(0, "0.65000")]
    [InlineData(999, "0.65000")]
    [InlineData(1000, "0.65325")]
    [InlineData(3999, "0.65325")]
    [InlineData(4000, "0.65650")]
    [InlineData(6999, "0.65650")]
    [InlineData(7000, "0.65975")]
    public void LivePreviewUsesTheEngineFamilyFameThresholds(int fame, string expectedRate)
    {
        RunInSta(() =>
        {
            using var dialog = new SilverOptionsDialog();
            Get<NumericUpDown>(dialog, "_familyFameInput").Value = fame;

            var rate = decimal.Parse(expectedRate, CultureInfo.InvariantCulture);
            Assert.Contains(rate.ToString("P3", CultureInfo.CurrentCulture),
                Get<Label>(dialog, "_marketReturnLabel").Text);
            Assert.Equal(new SilverTaxOptions(), dialog.SelectedTaxOptions);
        });
    }

    [Fact]
    public void PreviewReflectsValuePackAndRingTogetherWithoutApplyingThem()
    {
        RunInSta(() =>
        {
            using var dialog = new SilverOptionsDialog();
            EditAllValues(dialog);

            Assert.Contains(0.88725m.ToString("P3", CultureInfo.CurrentCulture),
                Get<Label>(dialog, "_marketReturnLabel").Text);
            Assert.Equal(new SilverTaxOptions(), dialog.SelectedTaxOptions);
        });
    }

    [Theory]
    [InlineData(600, 580)]
    [InlineData(620, 580)]
    public void CompactDialogRendersOffscreenWithAllImportantControlsInsideClientBounds(int width, int height)
    {
        RunInSta(() =>
        {
            using var dialog = new SilverOptionsDialog();
            dialog.Size = new Size(width, height);
            LayoutHandles(dialog);

            foreach (var field in new[]
                     { "_regionComboBox", "_familyFameInput", "_marketReturnLabel", "_applyButton", "_cancelButton" })
            {
                var control = (Control)dialog.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Assert.True(control.Width > 0 && control.Height > 0, $"{field} is not laid out.");
                Assert.True(dialog.ClientRectangle.Contains(BoundsInForm(control, dialog)),
                    $"{field} leaves the client area: {BoundsInForm(control, dialog)} within {dialog.ClientRectangle}.");
            }
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var directory = Environment.GetEnvironmentVariable("BDO_UI_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                bitmap.Save(Path.Combine(directory, $"silver-options-{width}x{height}.png"), ImageFormat.Png);
            }
            Assert.False(dialog.Visible);
        });
    }

    private static void EditAllValues(SilverOptionsDialog dialog)
    {
        var region = Get<ComboBox>(dialog, "_regionComboBox");
        region.SelectedItem = region.Items.Cast<object>().Single(item => item.ToString()!.StartsWith("NA", StringComparison.Ordinal));
        Get<CheckBox>(dialog, "_valuePackCheckBox").Checked = true;
        Get<CheckBox>(dialog, "_merchantRingCheckBox").Checked = true;
        Get<NumericUpDown>(dialog, "_familyFameInput").Value = 7000;
    }

    private static T Get<T>(object target, string field) =>
        Assert.IsType<T>(target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target));

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);

    private static void LayoutHandles(Control control)
    {
        _ = control.Handle;
        control.PerformLayout();
        foreach (Control child in control.Controls) LayoutHandles(child);
        control.PerformLayout();
    }

    private static Rectangle BoundsInForm(Control control, Form form)
    {
        var location = Point.Empty;
        for (Control? current = control; current is not null && current != form; current = current.Parent)
            location.Offset(current.Location);
        return new Rectangle(location, control.Size);
    }

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Silver options dialog test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
