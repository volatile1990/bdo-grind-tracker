using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Tests;

public sealed class BdoWindowChromeTests
{
    internal static void AssertScreenshotCaptureEnabled(Form form)
    {
        _ = form.Handle;
        Assert.True(GetWindowDisplayAffinity(form.Handle, out var affinity));
        Assert.Equal(0u, affinity);
    }

    [Fact]
    public void ChromeKeepsWindowsScreenshotCaptureEnabled()
    {
        RunInSta(() =>
        {
            using var form = new Form();
            _ = form.Handle;
            BdoWindowChrome.Apply(form);
            Assert.True(GetWindowDisplayAffinity(form.Handle, out var affinity));
            Assert.Equal(0u, affinity);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void ChromeClearsLegacyCaptureExclusionOnTheOwnedWindow()
    {
        RunInSta(() =>
        {
            // Only this test-owned, never-shown HWND is touched. No screen capture,
            // Snipping Tool automation or game input is needed for this regression.
            using var form = new Form();
            _ = form.Handle;
            Assert.True(SetWindowDisplayAffinity(form.Handle, 0x11));
            Assert.True(GetWindowDisplayAffinity(form.Handle, out var excluded));
            Assert.Equal(0x11u, excluded);
            BdoWindowChrome.Apply(form);
            Assert.True(GetWindowDisplayAffinity(form.Handle, out var affinity));
            Assert.Equal(0u, affinity);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void ApplyingChromeBeforeHandleCreationDoesNotShowAWindow()
    {
        RunInSta(() =>
        {
            using var form = new Form();
            Assert.False(form.IsHandleCreated);
            BdoWindowChrome.Apply(form);
            Assert.False(form.IsHandleCreated);
            Assert.False(form.Visible);
        });
    }

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
}
