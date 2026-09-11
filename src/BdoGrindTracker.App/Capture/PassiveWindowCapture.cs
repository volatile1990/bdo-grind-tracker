using System.Diagnostics;
using System.Runtime.InteropServices;
using BdoGrindTracker.App.Overlay.Native;

namespace BdoGrindTracker.App.Capture;

/// <summary>Captures the bound Black Desert window's client pixels, independently of its desktop position.</summary>
internal sealed class PassiveWindowCapture : IDisposable
{
    private readonly object _sync = new();
    private readonly Func<WindowCaptureTarget> _locate;
    private readonly Func<WindowCaptureTarget, WindowCaptureGeometry> _readGeometry;
    private readonly Func<WindowCaptureTarget, WindowCaptureGeometry, IWindowFrameSource> _createSource;
    private WindowCaptureTarget? _target;
    private IWindowFrameSource? _source;
    private bool _disposed;

    public PassiveWindowCapture() : this(NativeWindowCapture.LocateGame,
        NativeWindowCapture.ReadGeometry, static (target, geometry) => new GraphicsWindowFrameSource(target, geometry)) { }

    internal PassiveWindowCapture(Func<WindowCaptureTarget> locate,
        Func<WindowCaptureTarget, WindowCaptureGeometry> readGeometry,
        Func<WindowCaptureTarget, WindowCaptureGeometry, IWindowFrameSource> createSource)
    {
        _locate = locate;
        _readGeometry = readGeometry;
        _createSource = createSource;
    }

    internal Rectangle PrepareCapture()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCaptureCore();
            _target = null;
            var target = _locate();
            var geometry = _readGeometry(target);
            if (geometry.ClientBounds.Width <= 0 || geometry.ClientBounds.Height <= 0)
                throw new InvalidOperationException("Das Black-Desert-Spielfenster besitzt keinen sichtbaren Spielbereich.");
            _target = target with { ClientSize = geometry.ClientBounds.Size };
            return new Rectangle(Point.Empty, geometry.ClientBounds.Size);
        }
    }

    public CapturedDesktopBitmap Capture(Rectangle clientRegion, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var target = _target ?? throw new InvalidOperationException("Die Spielfensteraufnahme wurde nicht vorbereitet.");
            if (clientRegion != new Rectangle(Point.Empty, target.ClientSize))
                throw new InvalidOperationException("Der Aufnahmebereich stimmt nicht mit dem gebundenen Spielfenster überein.");
            var geometry = ReadValidatedGeometry(target);
            _source ??= _createSource(target, geometry);
            // The frame source checks the HWND while waiting and immediately before
            // returning pixels; minimizing or closing cannot replay its last image.
            return _source.Capture(() => ReadValidatedGeometry(target), cancellationToken);
        }
    }

    private WindowCaptureGeometry ReadValidatedGeometry(WindowCaptureTarget target)
    {
        var geometry = _readGeometry(target);
        if (geometry.ClientBounds.Size != target.ClientSize)
            throw new InvalidOperationException("Die Größe des Black-Desert-Spielfensters hat sich geändert. Tracking bitte erneut starten und die Kalibrierung prüfen.");
        return geometry;
    }

    internal void StopCapture()
    {
        lock (_sync) StopCaptureCore();
    }

    private void StopCaptureCore()
    {
        var source = _source;
        _source = null;
        source?.Dispose();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            StopCaptureCore();
            _target = null;
            _disposed = true;
        }
    }
}

internal readonly record struct WindowCaptureTarget(nint Handle, uint ProcessId, Size ClientSize);

internal readonly record struct WindowCaptureGeometry(
    Rectangle ClientBounds, Rectangle WindowBounds, Rectangle ExtendedFrameBounds, bool IsHdr)
{
    internal Rectangle ClientCrop(Size capturedSize)
    {
        // WGC normally excludes invisible resize borders. Some window styles return
        // the full Win32 bounds or already expose only the client surface.
        var source = capturedSize == ExtendedFrameBounds.Size ? ExtendedFrameBounds
            : capturedSize == WindowBounds.Size ? WindowBounds
            : capturedSize == ClientBounds.Size ? ClientBounds
            : throw new InvalidOperationException("Die Fensteraufnahme und die Spielbereichsgröße stimmen nicht überein. Tracking bitte erneut starten.");
        var crop = new Rectangle(ClientBounds.Left - source.Left, ClientBounds.Top - source.Top,
            ClientBounds.Width, ClientBounds.Height);
        if (!new Rectangle(Point.Empty, capturedSize).Contains(crop))
            throw new InvalidOperationException("Der Spielbereich liegt außerhalb des aufgenommenen Fensters.");
        return crop;
    }
}

internal interface IWindowFrameSource : IDisposable
{
    CapturedDesktopBitmap Capture(Func<WindowCaptureGeometry> readGeometry, CancellationToken cancellationToken);
}

internal static class NativeWindowCapture
{
    internal static WindowCaptureTarget LocateGame()
    {
        var candidates = new List<WindowCaptureTarget>();
        NativeOverlayApi.EnumWindows((window, _) =>
        {
            if (!NativeOverlayApi.IsWindowVisible(window) || NativeOverlayApi.IsIconic(window) || GetWindow(window, 4) != 0)
                return true;
            try
            {
                NativeOverlayApi.GetWindowThreadProcessId(window, out var processId);
                if (processId == 0 || processId == Environment.ProcessId) return true;
                using var process = Process.GetProcessById(checked((int)processId));
                if (!NativeOverlayGameWindow.IsGameProcessName(process.ProcessName)) return true;
                var target = new WindowCaptureTarget(window, processId, Size.Empty);
                var geometry = ReadGeometry(target);
                if (geometry.ClientBounds.Width > 0 && geometry.ClientBounds.Height > 0)
                    candidates.Add(target with { ClientSize = geometry.ClientBounds.Size });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return true;
        }, 0);
        var foreground = NativeOverlayApi.GetForegroundWindow();
        return candidates.OrderByDescending(candidate => candidate.Handle == foreground)
            .ThenByDescending(candidate => (long)candidate.ClientSize.Width * candidate.ClientSize.Height)
            .Cast<WindowCaptureTarget?>().FirstOrDefault()
            ?? throw new InvalidOperationException("Kein sichtbares Black-Desert-Spielfenster gefunden. Spiel öffnen und minimierte Fenster wiederherstellen.");
    }

    internal static WindowCaptureGeometry ReadGeometry(WindowCaptureTarget target)
    {
        NativeOverlayApi.GetWindowThreadProcessId(target.Handle, out var processId);
        if (!NativeOverlayApi.IsWindow(target.Handle) || processId != target.ProcessId)
            throw new InvalidOperationException("Das aufgenommene Black-Desert-Spielfenster wurde geschlossen. Tracking bitte erneut starten.");
        if (!NativeOverlayApi.IsWindowVisible(target.Handle) || NativeOverlayApi.IsIconic(target.Handle) || IsCloaked(target.Handle))
            throw new InvalidOperationException("Das Black-Desert-Spielfenster ist minimiert oder nicht sichtbar. Fenster wiederherstellen und Tracking erneut starten.");
        if (!GetClientRect(target.Handle, out var client) || !GetWindowRect(target.Handle, out var window))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var origin = new NativeOverlayApi.NativePoint(0, 0);
        if (!ClientToScreen(target.Handle, ref origin)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var extended = DwmGetWindowAttribute(target.Handle, 9, out NativeRect bounds, Marshal.SizeOf<NativeRect>()) >= 0
            ? bounds.ToRectangle() : window.ToRectangle();
        return new WindowCaptureGeometry(new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top),
            window.ToRectangle(), extended, PassiveScreenCapture.ReadOutputHdrState(Screen.FromHandle(target.Handle).Bounds));
    }

    private static bool IsCloaked(nint window) =>
        DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) >= 0 && cloaked != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left, Top, Right, Bottom;
        internal readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativeOverlayApi.NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out NativeRect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
}
