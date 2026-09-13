using System.Reflection;
using System.Runtime.ExceptionServices;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Overlay.Native;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class MultiOverlayNativeTests
{
    [Fact]
    public void GameWindowIsLocatedOncePerTickAcrossAllEnabledOverlays()
    {
        RunInSta(() =>
        {
            var tracker = new PreviewTrackerSession(empty: true);
            try
            {
                using var service = new OverlayService(tracker);
                service.SaveHotkeysAsync(service.Hotkeys with { Enabled = false }).GetAwaiter().GetResult();
                service.SaveAsync(service.Settings with { Enabled = true, Visibility = "session" }).GetAwaiter().GetResult();
                service.CreateOverlayAsync("Zweites", service.SelectedOverlayId).GetAwaiter().GetResult();
                service.SaveAsync(service.Settings with { Enabled = true }).GetAwaiter().GetResult();
                using var owner = new Form();
                var searches = 0;
                using var host = new NativeOverlayHost(service, tracker, owner, locateGame: () => { searches++; return (null, false); });
                host.Tick();
                Assert.Equal(1, searches);
                host.Tick();
                Assert.Equal(2, searches);
                service.ToggleAllOverlaysAsync().GetAwaiter().GetResult();
                host.Tick();
                Assert.Equal(2, searches);
            }
            finally { tracker.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
    }

    [Fact]
    public void WindowsHaveSeparateHandlesAndCallbacksKeepTheirOriginalIdAfterEditorSelectionChanges()
    {
        RunInSta(() =>
        {
            var tracker = new PreviewTrackerSession();
            try
            {
                using var service = new OverlayService(tracker);
                using var owner = new Form();
                using var host = new NativeOverlayHost(service, tracker, owner);
                var firstId = service.SelectedOverlayId;
                service.SaveHotkeysAsync(service.Hotkeys with { Enabled = false }).GetAwaiter().GetResult();
                service.CreateOverlayAsync("Zweites").GetAwaiter().GetResult();
                var secondId = service.SelectedOverlayId;
                host.Tick();
                var windows = Windows(host);
                Assert.Equal(2, windows.Count);
                // Build hidden, test-owned HWNDs. No global input or hotkeys are used.
                var first = CreateHiddenWindow(windows[firstId]);
                var second = CreateHiddenWindow(windows[secondId]);
                Assert.NotSame(first, second);
                Assert.NotEqual(first.Handle, second.Handle);
                first.MonitorBounds = new(-5000, -5000, 3000, 3000);
                second.MonitorBounds = first.MonitorBounds;
                var untouched = service.Settings;
                var position = new Rectangle(-3000, -3500, 360, 260);
                var expected = NativeOverlayGeometry.RelativePosition(position, first.MonitorBounds);
                var committed = (Action<Rectangle, OverlaySettings?>)typeof(NativeOverlayForm)
                    .GetField("GeometryCommitted", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(first)!;
                committed(position, null);
                Assert.Equal(secondId, service.SelectedOverlayId);
                Assert.Same(untouched, service.Settings);
                var moved = service.Overlays.Single(overlay => overlay.Id == firstId).Settings;
                Assert.Equal(expected.X, moved.PositionX);
                Assert.Equal(expected.Y, moved.PositionY);
                Assert.False(first.Visible);
                Assert.False(second.Visible);

                service.DeleteOverlayAsync(firstId).GetAwaiter().GetResult();
                host.Tick();
                Assert.True(first.IsDisposed);
                Assert.False(second.IsDisposed);
                Assert.Equal(secondId, Assert.Single(windows).Key);
                host.Dispose();
                Assert.True(second.IsDisposed);
                Assert.Empty(windows);
            }
            finally { tracker.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
    }

    [Fact]
    public void OneHiddenHotkeyOwnerControlsAllWindowsAfterSelectionChangesAndDeletion()
    {
        RunInSta(() =>
        {
            var tracker = new PreviewTrackerSession(empty: true);
            try
            {
                using var service = new OverlayService(tracker);
                using var owner = new Form();
                var registrations = new List<(nint Window, int Id)>();
                var releases = new List<(nint Window, int Id)>();
                var created = 0;
                using var host = new NativeOverlayHost(service, tracker, owner, hotkeyWindowFactory: () =>
                {
                    created++;
                    return new((window, id, modifiers, key) =>
                    {
                        registrations.Add((window, id));
                        return true;
                    }, (window, id) => releases.Add((window, id)));
                });
                var firstId = service.SelectedOverlayId;
                service.SaveAsync(service.Settings with { Visibility = "session", CaptureExcluded = false }).GetAwaiter().GetResult();
                service.CreateOverlayAsync("Zweites", firstId).GetAwaiter().GetResult();
                var secondId = service.SelectedOverlayId;
                host.Tick();
                var hotkeys = HotkeyWindow(host)!;
                var handle = hotkeys.Handle;
                Assert.NotEqual(0, handle);
                Assert.Equal(1, created);
                Assert.Equal(new[] { (handle, 1), (handle, 2) }, registrations);
                var windows = Windows(host);
                var first = CreateHiddenWindow(windows[firstId]);
                var second = CreateHiddenWindow(windows[secondId]);
                Assert.NotEqual(first.Handle, handle);
                Assert.NotEqual(second.Handle, handle);

                DispatchShortcut(hotkeys, 1, service.Hotkeys.ToggleOverlay);
                Assert.All(service.Overlays, overlay => Assert.True(overlay.Settings.Enabled));
                DispatchShortcut(hotkeys, 2, service.Hotkeys.ToggleInteraction);
                Assert.All(service.Overlays, overlay => Assert.Equal("passthrough", overlay.Settings.Interaction));
                service.SelectOverlayAsync(firstId).GetAwaiter().GetResult();
                service.SaveAsync(service.Settings with { Interaction = "move" }).GetAwaiter().GetResult();
                DispatchShortcut(hotkeys, 2, service.Hotkeys.ToggleInteraction);
                Assert.All(service.Overlays, overlay => Assert.Equal("passthrough", overlay.Settings.Interaction));
                DispatchShortcut(hotkeys, 2, service.Hotkeys.ToggleInteraction);
                Assert.All(service.Overlays, overlay => Assert.Equal("move", overlay.Settings.Interaction));
                Assert.False(first.Visible);
                Assert.False(second.Visible);

                service.DeleteOverlayAsync(firstId).GetAwaiter().GetResult();
                host.Tick();
                Assert.True(first.IsDisposed);
                Assert.Equal(handle, HotkeyWindow(host)!.Handle);
                DispatchShortcut(hotkeys, 1, service.Hotkeys.ToggleOverlay);
                Assert.False(service.Settings.Enabled);
                DispatchShortcut(hotkeys, 1, service.Hotkeys.ToggleOverlay);
                Assert.True(service.Settings.Enabled);
                service.CreateOverlayAsync("Drittes", secondId).GetAwaiter().GetResult();
                host.Tick();
                Assert.Equal(1, created);
                Assert.Equal(2, registrations.Count);
                DispatchShortcut(hotkeys, 1, service.Hotkeys.ToggleOverlay);
                Assert.All(service.Overlays, overlay => Assert.False(overlay.Settings.Enabled));

                host.Dispose();
                Assert.Equal(new[] { (handle, 1), (handle, 2) }, releases);
                Assert.Equal(0, hotkeys.Handle);
            }
            finally { tracker.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
    }

    [Fact]
    public void RegistrationErrorRemainsGlobalAcrossSelectionAndDeletionAndDisableClearsIt()
    {
        RunInSta(() =>
        {
            var tracker = new PreviewTrackerSession(empty: true);
            try
            {
                using var service = new OverlayService(tracker);
                using var owner = new Form();
                using var host = new NativeOverlayHost(service, tracker, owner, hotkeyWindowFactory: () =>
                    new((window, id, modifiers, key) => id != 2, (window, id) => { }));
                var firstId = service.SelectedOverlayId;
                service.CreateOverlayAsync("Zweites").GetAwaiter().GetResult();
                host.Tick();
                var error = service.HotkeyStatus;
                Assert.Contains(service.Hotkeys.ToggleInteraction.DisplayText, error);
                Assert.All(service.Overlays, overlay => Assert.Equal(error, service.GetState(overlay.Id).HotkeyStatus));
                service.SelectOverlayAsync(firstId).GetAwaiter().GetResult();
                service.DeleteOverlayAsync(firstId).GetAwaiter().GetResult();
                host.Tick();
                Assert.Equal(error, service.HotkeyStatus);
                service.SaveHotkeysAsync(service.Hotkeys with { Enabled = false }).GetAwaiter().GetResult();
                host.Tick();
                Assert.Null(service.HotkeyStatus);
                DispatchShortcut(HotkeyWindow(host)!, 1, service.Hotkeys.ToggleOverlay);
                Assert.False(service.Settings.Enabled);
            }
            finally { tracker.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
    }

    [Fact]
    public void ValidationModePublishesRuntimeForEveryOverlayWithoutCreatingWindows()
    {
        RunInSta(() =>
        {
            var tracker = new PreviewTrackerSession();
            try
            {
                using var service = new OverlayService(tracker);
                using var owner = new Form();
                using var host = new NativeOverlayHost(service, tracker, owner, validationMode: true);
                var firstId = service.SelectedOverlayId;
                service.CreateOverlayAsync("Zweites").GetAwaiter().GetResult();
                service.SetPreviewAsync(firstId, true).GetAwaiter().GetResult();
                host.Tick();
                Assert.Empty(Windows(host));
                Assert.Null(HotkeyWindow(host));
                Assert.All(service.Overlays, overlay =>
                {
                    Assert.Contains("UI-Testvorschau", service.GetState(overlay.Id).Status);
                    Assert.False(service.GetState(overlay.Id).IsVisible);
                });
                Assert.True(service.GetState(firstId).Previewing);
                Assert.False(service.State.Previewing);
            }
            finally { tracker.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
    }

    private static Dictionary<string, NativeOverlayWindowHost> Windows(NativeOverlayHost host) =>
        (Dictionary<string, NativeOverlayWindowHost>)typeof(NativeOverlayHost)
            .GetField("_windows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;

    private static NativeOverlayHotkeyWindow? HotkeyWindow(NativeOverlayHost host) =>
        (NativeOverlayHotkeyWindow?)typeof(NativeOverlayHost)
            .GetField("_hotkeyWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host);

    private static void DispatchShortcut(NativeOverlayHotkeyWindow window, int id, OverlayHotkey shortcut)
    {
        var packed = (uint)shortcut.Modifiers | shortcut.VirtualKey << 16;
        var message = Message.Create(window.Handle, 0x312, id, unchecked((nint)packed));
        typeof(NativeOverlayHotkeyWindow).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [message]);
    }

    private static NativeOverlayForm CreateHiddenWindow(NativeOverlayWindowHost host)
    {
        typeof(NativeOverlayWindowHost).GetMethod("EnsureWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(host, null);
        var form = (NativeOverlayForm)typeof(NativeOverlayWindowHost)
            .GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
        form.Bounds = new(-4000, -4000, 360, 260);
        _ = form.Handle;
        return form;
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
