using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class GarmothOptionsDialogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticUploadChangesAreOnlyPublishedOnSave(bool initialValue)
    {
        RunInSta(() =>
        {
            using var dialog = new GarmothOptionsDialog("original-test-key", initialValue);
            var checkbox = Get<CheckBox>(dialog, "_autoUploadCheckBox");
            Assert.True(checkbox.Enabled);
            Assert.Equal(initialValue, checkbox.Checked);

            checkbox.Checked = !initialValue;

            Assert.Equal(initialValue, dialog.AutoUploadEnabled);
            Assert.Equal(DialogResult.None, dialog.DialogResult);
            Invoke(dialog, "SaveChanges");
            Assert.Equal(DialogResult.OK, dialog.DialogResult);
            Assert.Equal(!initialValue, dialog.AutoUploadEnabled);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelDiscardsAutomaticUploadChanges(bool initialValue)
    {
        RunInSta(() =>
        {
            using var dialog = new GarmothOptionsDialog("original-test-key", initialValue);
            Get<CheckBox>(dialog, "_autoUploadCheckBox").Checked = !initialValue;

            Invoke(dialog, "CancelChanges");

            Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
            Assert.Equal(initialValue, dialog.AutoUploadEnabled);
        });
    }

    [Fact]
    public void AutomaticUploadsNeedAValidKeyAndRemainOffAfterReplacingRemovedKey()
    {
        RunInSta(() =>
        {
            using var dialog = new GarmothOptionsDialog();
            var checkbox = Get<CheckBox>(dialog, "_autoUploadCheckBox");
            var input = Get<TextBox>(dialog, "_apiKeyTextBox");
            Assert.False(checkbox.Enabled);
            Assert.False(checkbox.Checked);

            input.Text = "valid-test-key";
            Assert.True(checkbox.Enabled);
            checkbox.Checked = true;
            input.Clear();
            Assert.False(checkbox.Enabled);
            Assert.False(checkbox.Checked);

            input.Text = "replacement-test-key";
            Assert.True(checkbox.Enabled);
            Assert.False(checkbox.Checked);
            Invoke(dialog, "SaveChanges");
            Assert.Equal(DialogResult.OK, dialog.DialogResult);
            Assert.False(dialog.AutoUploadEnabled);
        });
    }

    [Fact]
    public void KeyIsMaskedAndEditingDoesNotPublishIt()
    {
        RunInSta(() =>
        {
            using var dialog = new GarmothOptionsDialog("original-test-key");
            var input = Get<TextBox>(dialog, "_apiKeyTextBox");

            Assert.True(input.UseSystemPasswordChar);
            Assert.Equal(4096, input.MaxLength);
            input.Text = "replacement-test-key";

            Assert.Equal("original-test-key", dialog.ApiKey);
            Assert.Equal(DialogResult.None, dialog.DialogResult);
            Assert.DoesNotContain("replacement-test-key", Get<Label>(dialog, "_statusLabel").Text);
            Assert.False(dialog.Visible);
        });
    }

    [Fact]
    public void SavePublishesNormalizedKeyOnlyOnOk()
    {
        RunInSta(() =>
        {
            using var dialog = new GarmothOptionsDialog("original-test-key");
            Get<TextBox>(dialog, "_apiKeyTextBox").Text = "  replacement-test-key  ";

            Invoke(dialog, "SaveChanges");

            Assert.Equal(DialogResult.OK, dialog.DialogResult);
            Assert.Equal("replacement-test-key", dialog.ApiKey);
        });
    }

    [Fact]
    public void CancelDoesNotReplaceThePreviousKey()
    {
        RunInSta(() =>
        {
            using var dialog = new GarmothOptionsDialog("original-test-key");
            Get<TextBox>(dialog, "_apiKeyTextBox").Text = "replacement-test-key";

            Invoke(dialog, "CancelChanges");

            Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
            Assert.Equal("original-test-key", dialog.ApiKey);
        });
    }

    [Fact]
    public void ForgetOnlyStagesRemovalUntilExplicitSave()
    {
        RunInSta(() =>
        {
            using var dialog = new GarmothOptionsDialog("original-test-key", autoUploadEnabled: true);

            Invoke(dialog, "StageRemoval");

            Assert.Empty(Get<TextBox>(dialog, "_apiKeyTextBox").Text);
            Assert.Equal("original-test-key", dialog.ApiKey);
            Assert.True(dialog.AutoUploadEnabled);
            Assert.False(Get<CheckBox>(dialog, "_autoUploadCheckBox").Checked);
            Assert.False(Get<CheckBox>(dialog, "_autoUploadCheckBox").Enabled);
            Assert.Contains("entfernt", Get<Label>(dialog, "_statusLabel").Text);
            Invoke(dialog, "SaveChanges");
            Assert.Equal(DialogResult.OK, dialog.DialogResult);
            Assert.Empty(dialog.ApiKey);
            Assert.False(dialog.AutoUploadEnabled);
        });
    }

    [Fact]
    public void CancelAfterForgetKeepsThePreviousKey()
    {
        RunInSta(() =>
        {
            using var dialog = new GarmothOptionsDialog("original-test-key", autoUploadEnabled: true);
            Invoke(dialog, "StageRemoval");

            Invoke(dialog, "CancelChanges");

            Assert.Equal(DialogResult.Cancel, dialog.DialogResult);
            Assert.Equal("original-test-key", dialog.ApiKey);
            Assert.True(dialog.AutoUploadEnabled);
        });
    }

    [Theory]
    [InlineData("bad key")]
    [InlineData("schlüssel")]
    public void InvalidKeyCannotBeAppliedOrEchoedIntoStatus(string value)
    {
        RunInSta(() =>
        {
            using var dialog = new GarmothOptionsDialog("original-test-key", autoUploadEnabled: true);
            Get<TextBox>(dialog, "_apiKeyTextBox").Text = value;

            Invoke(dialog, "SaveChanges");

            Assert.False(Get<BdoButton>(dialog, "_saveButton").Enabled);
            Assert.False(Get<CheckBox>(dialog, "_autoUploadCheckBox").Enabled);
            Assert.False(Get<CheckBox>(dialog, "_autoUploadCheckBox").Checked);
            Assert.Equal(DialogResult.None, dialog.DialogResult);
            Assert.Equal("original-test-key", dialog.ApiKey);
            Assert.True(dialog.AutoUploadEnabled);
            Assert.DoesNotContain(value, Get<Label>(dialog, "_statusLabel").Text);
        });
    }

    [Fact]
    public void KeyDialogRendersOffscreenAndKeepsActionsInsideClientBounds()
    {
        RunInSta(() =>
        {
            using var dialog = new GarmothOptionsDialog();
            dialog.Size = dialog.MinimumSize;
            LayoutHandles(dialog);
            foreach (var name in new[] { "_apiKeyTextBox", "_autoUploadCheckBox", "_saveButton", "_cancelButton", "_forgetButton" })
            {
                var control = (Control)dialog.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Assert.True(dialog.ClientRectangle.Contains(BoundsInForm(control, dialog)), name);
            }
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var directory = Environment.GetEnvironmentVariable("BDO_UI_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                bitmap.Save(Path.Combine(directory, "garmoth-options-dialog.png"), ImageFormat.Png);
            }
            Assert.False(dialog.Visible);
        });
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Garmoth options test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
