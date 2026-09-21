using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BdoGrindTracker.App.Overlay.Native;

namespace BdoGrindTracker.App.Capture;

internal interface IGameForegroundMonitor : IDisposable
{
    bool IsGameForeground { get; }
    event EventHandler? Changed;
}

/// <summary>
/// A WinEvent notification needs a message loop on its registering thread. The
/// dedicated thread sleeps in GetMessage; it neither polls nor captures pixels.
/// </summary>
internal sealed class NativeGameForegroundMonitor : IGameForegroundMonitor
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WmQuit = 0x0012;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly WinEventCallback _callback;
    private Exception? _startupError;
    private uint _threadId;
    private nint _gameWindow;
    private int _disposed;

    public event EventHandler? Changed;

    internal NativeGameForegroundMonitor()
    {
        _callback = OnForegroundChanged;
        _thread = new Thread(Run) { IsBackground = true, Name = "Grind foreground notifications" };
        _thread.Start();
        _ready.Wait();
        if (_startupError is not null)
        {
            _thread.Join();
            _ready.Dispose();
            throw new InvalidOperationException("Die Vordergrunderkennung für automatisches Tracking konnte nicht gestartet werden.", _startupError);
        }
    }

    public bool IsGameForeground
    {
        get
        {
            var window = Volatile.Read(ref _gameWindow);
            // A cheap handle check also covers a pending WinEvent callback. The
            // process name is inspected only on actual foreground notifications.
            return Volatile.Read(ref _disposed) == 0 && window != 0 &&
                NativeOverlayApi.GetForegroundWindow() == window &&
                NativeOverlayApi.IsWindowVisible(window) && !NativeOverlayApi.IsIconic(window);
        }
    }

    private void Run()
    {
        nint hook = 0;
        try
        {
            _threadId = GetCurrentThreadId();
            // Establish the message queue before exposing the thread to Dispose.
            PeekMessage(out _, 0, 0, 0, 0);
            // WINEVENT_OUTOFCONTEXT (0): Windows delivers notifications to this
            // thread without injecting a DLL or opening the game's memory.
            hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, _callback, 0, 0, 0);
            if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            UpdateForeground();
            _ready.Set();
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(in message);
                DispatchMessage(in message);
            }
        }
        catch (Exception error)
        {
            _startupError = error;
            Trace.TraceWarning("Automatic tracking foreground monitor stopped: {0}", error.Message);
        }
        finally
        {
            Volatile.Write(ref _gameWindow, 0);
            // Windows requires unhooking on the same thread that registered it.
            if (hook != 0) UnhookWinEvent(hook);
            _ready.Set();
        }
    }

    private void OnForegroundChanged(nint hook, uint eventType, nint window, int objectId,
        int childId, uint eventThread, uint eventTime)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try
        {
            UpdateForeground();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error)
        {
            // Never unwind managed exceptions through the Windows callback.
            Trace.TraceWarning("Automatic tracking foreground notification failed: {0}", error.Message);
        }
    }

    private void UpdateForeground()
    {
        var window = NativeOverlayApi.GetForegroundWindow();
        Volatile.Write(ref _gameWindow, IsGameWindow(window) ? window : 0);
    }

    private static bool IsGameWindow(nint window)
    {
        if (window == 0 || !NativeOverlayApi.IsWindowVisible(window) || NativeOverlayApi.IsIconic(window)) return false;
        try
        {
            NativeOverlayApi.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == Environment.ProcessId) return false;
            using var process = Process.GetProcessById(checked((int)processId));
            return NativeOverlayGameWindow.IsGameProcessName(process.ProcessName);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception or OverflowException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Write(ref _gameWindow, 0);
        PostThreadMessage(_threadId, WmQuit, 0, 0);
        if (Thread.CurrentThread == _thread) return;
        _thread.Join();
        _ready.Dispose();
    }

    private delegate void WinEventCallback(nint hook, uint eventType, nint window, int objectId,
        int childId, uint eventThread, uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal nint Window;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal NativeOverlayApi.NativePoint Point;
        internal uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, nint window, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(in NativeMessage message);
    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(in NativeMessage message);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
