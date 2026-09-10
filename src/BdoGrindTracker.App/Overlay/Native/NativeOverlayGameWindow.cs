using System.Diagnostics;

namespace BdoGrindTracker.App.Overlay.Native;

internal sealed class NativeOverlayGameWindow
{
    private nint _lastWindow;
    private long _nextSearch;

    internal (Screen? Screen, bool Foreground) Locate()
    {
        var foreground = NativeOverlayApi.GetForegroundWindow();
        if (foreground != 0 && IsGameWindow(foreground)) _lastWindow = foreground;
        if (_lastWindow != 0 && (!NativeOverlayApi.IsWindow(_lastWindow) || !NativeOverlayApi.IsWindowVisible(_lastWindow)))
            _lastWindow = 0;
        if (_lastWindow == 0 && Environment.TickCount64 >= _nextSearch)
        {
            _nextSearch = Environment.TickCount64 + 2_000;
            NativeOverlayApi.EnumWindows((window, _) =>
            {
                if (!IsGameWindow(window)) return true;
                _lastWindow = window;
                return false;
            }, 0);
        }
        return _lastWindow == 0 ? (null, false) :
            (Screen.FromHandle(_lastWindow), foreground == _lastWindow && !NativeOverlayApi.IsIconic(_lastWindow));
    }

    internal static double Dpi(Screen screen)
    {
        var point = new NativeOverlayApi.NativePoint(screen.Bounds.Left + screen.Bounds.Width / 2,
            screen.Bounds.Top + screen.Bounds.Height / 2);
        var monitor = NativeOverlayApi.MonitorFromPoint(point, 2);
        return NativeOverlayApi.GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? x : 96;
    }

    internal static bool IsGameProcessName(string name) =>
        name.Equals("BlackDesert64", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("BlackDesert64.bin", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("BlackDesert32", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("BlackDesert", StringComparison.OrdinalIgnoreCase);

    private static bool IsGameWindow(nint window)
    {
        if (!NativeOverlayApi.IsWindowVisible(window)) return false;
        try
        {
            NativeOverlayApi.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == Environment.ProcessId) return false;
            // Only ask Windows for the process name. No access to game memory or modules.
            using var process = Process.GetProcessById((int)processId);
            return IsGameProcessName(process.ProcessName);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
