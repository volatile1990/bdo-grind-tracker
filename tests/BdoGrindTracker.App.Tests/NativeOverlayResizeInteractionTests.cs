using System.Reflection;
using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;

namespace BdoGrindTracker.App.Tests;

public sealed class NativeOverlayResizeInteractionTests
{
    [Fact]
    public void CornerDragPreviewsIndependentAxesAndCommitsTheVisibleLayoutOnce()
    {
        RunInSta(() =>
        {
            using var form = CreateForm();
            var commits = new List<(Rectangle Bounds, OverlaySettings? Settings)>();
            form.GeometryCommitted += (bounds, layout) => commits.Add((bounds, layout));
            Mouse(form, "OnMouseDown", new(358, 258));
            Mouse(form, "OnMouseMove", new(478, 198));

            Assert.Equal(new Size(480, 200), form.Size);
            var preview = Assert.IsType<OverlaySettings>(form.ResizePreview);
            Assert.Equal(480, preview.Width);
            Assert.Equal(200, preview.Height);
            Assert.Empty(commits);
            // Regular host refreshes must not restore the persisted old size.
            form.Present(new Rectangle(form.Location, new Size(360, 260)));
            Assert.Equal(new Size(480, 200), form.Size);
            Mouse(form, "OnMouseUp", new(478, 198));

            var committed = Assert.Single(commits);
            Assert.Same(preview, committed.Settings);
            Assert.Equal(form.Bounds, committed.Bounds);
            Assert.False(form.IsManipulating);
            Assert.Null(form.ResizePreview);
        });
    }

    [Fact]
    public void ResizeGripWinsOverAnOverlappingTrackingButton()
    {
        RunInSta(() =>
        {
            using var form = CreateForm();
            form.SetActions(new Dictionary<string, RectangleF> { ["toggle-tracking:test"] = new(330, 230, 30, 30) });
            var actions = new List<string>();
            form.ActionClicked += actions.Add;
            Mouse(form, "OnMouseDown", new(358, 258));
            Assert.NotNull(form.ResizePreview);
            Mouse(form, "OnMouseMove", new(458, 258));
            Mouse(form, "OnMouseUp", new(458, 258));
            Assert.Equal(new Size(460, 260), form.Size);
            Assert.Empty(actions);
        });
    }

    [Theory]
    [InlineData("locked", 1)]
    [InlineData("passthrough", 0)]
    public void OtherInteractionModesKeepTheirButtonAndPassThroughBehavior(string mode, int actionCount)
    {
        RunInSta(() =>
        {
            using var form = CreateForm();
            form.SetInteraction(mode);
            form.SetActions(new Dictionary<string, RectangleF> { ["toggle-tracking:test"] = new(330, 230, 30, 30) });
            var actions = new List<string>();
            form.ActionClicked += actions.Add;
            Mouse(form, "OnMouseDown", new(358, 258));
            Assert.False(form.IsManipulating);
            Assert.Null(form.ResizePreview);
            Mouse(form, "OnMouseUp", new(358, 258));
            Assert.Equal(actionCount, actions.Count);
            Assert.Equal(new Size(360, 260), form.Size);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CaptureLossOrInteractionChangeRestoresTheOriginalWithoutSaving(bool loseCapture)
    {
        RunInSta(() =>
        {
            using var form = CreateForm();
            var original = form.Bounds;
            var commits = 0;
            form.GeometryCommitted += (_, _) => commits++;
            Mouse(form, "OnMouseDown", new(358, 258));
            Mouse(form, "OnMouseMove", new(458, 318));
            Assert.NotEqual(original, form.Bounds);
            if (loseCapture) form.Capture = false;
            else form.SetInteraction("passthrough");

            Assert.Equal(original, form.Bounds);
            Assert.False(form.IsManipulating);
            Assert.Null(form.ResizePreview);
            Mouse(form, "OnMouseUp", new(358, 258));
            Assert.Equal(0, commits);
        });
    }

    [Fact]
    public void ClickingGripWithoutMovingDoesNotRewriteTheLayout()
    {
        RunInSta(() =>
        {
            using var form = CreateForm();
            var commits = 0;
            form.GeometryCommitted += (_, _) => commits++;
            Mouse(form, "OnMouseDown", new(358, 258));
            Mouse(form, "OnMouseUp", new(358, 258));
            Assert.Equal(0, commits);
        });
    }

    private static NativeOverlayForm CreateForm()
    {
        // Test-owned HWND and synthetic local events only: no global mouse input,
        // hotkey registration or interaction with the user's running game.
        var form = new NativeOverlayForm
        {
            Bounds = new Rectangle(-4000, -4000, 360, 260),
            MonitorBounds = new Rectangle(-5000, -5000, 3000, 3000),
        };
        var settings = new OverlaySettings { SnapToGrid = false };
        form.CreateResize = bounds => new(settings, bounds, form.MonitorBounds, 96);
        _ = form.Handle;
        return form;
    }

    private static void Mouse(NativeOverlayForm form, string method, Point point) =>
        typeof(NativeOverlayForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);

    private static void RunInSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
