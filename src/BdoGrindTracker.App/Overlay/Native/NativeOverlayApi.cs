using System.Runtime.InteropServices;
using System.Text;

namespace BdoGrindTracker.App.Overlay.Native;

internal static class NativeOverlayApi
{
    internal const int ExLayered = 0x80000, ExTransparent = 0x20, ExToolWindow = 0x80, ExNoActivate = 0x8000000;
    internal const int GwlExStyle = -20;
    internal const uint SwpNoActivate = 0x10, SwpNoMove = 0x2, SwpNoSize = 0x1, SwpShowWindow = 0x40;
    internal static readonly nint TopMost = new(-1);
    internal delegate bool EnumWindowsCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)] internal struct NativePoint(int x, int y) { internal int X = x; internal int Y = y; }
    [StructLayout(LayoutKind.Sequential)] internal struct NativeSize(int width, int height) { internal int Width = width; internal int Height = height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct BlendFunction
    {
        internal byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(nint window, nint destinationDc, ref NativePoint position,
        ref NativeSize size, nint sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint window);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(nint value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor, int kind, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);
}
