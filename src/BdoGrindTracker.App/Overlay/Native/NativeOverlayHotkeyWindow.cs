namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>A hidden message-only owner keeps shortcuts alive when any overlay is hidden or deleted.</summary>
internal sealed class NativeOverlayHotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x312;
    private readonly NativeOverlayHotkeyRegistration _hotkeys;
    private bool _disposed;
    internal event Action<int>? HotkeyPressed;

    internal NativeOverlayHotkeyWindow(Func<nint, int, uint, uint, bool>? register = null,
        Action<nint, int>? unregister = null)
    {
        _hotkeys = new NativeOverlayHotkeyRegistration(
            (id, modifiers, key) => register?.Invoke(Handle, id, modifiers, key) ??
                NativeOverlayApi.RegisterHotKey(Handle, id, modifiers, key),
            id =>
            {
                if (Handle == 0) return;
                if (unregister is not null) unregister(Handle, id);
                else NativeOverlayApi.UnregisterHotKey(Handle, id);
            });
    }

    internal string? Apply(OverlayHotkeySettings settings)
    {
        if (_disposed) return null;
        if (settings.Enabled && Handle == 0)
            CreateHandle(new CreateParams { Caption = "Grindcrest Overlay Hotkeys", Parent = new nint(-3) });
        return _hotkeys.Apply(settings.Enabled, settings.ToggleOverlay, settings.ToggleInteraction);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey)
        {
            var packed = unchecked((uint)message.LParam.ToInt64());
            if (_hotkeys.Matches((int)message.WParam, packed & 0xffff, packed >> 16))
                HotkeyPressed?.Invoke((int)message.WParam);
            return;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hotkeys.Clear();
        if (Handle != 0) DestroyHandle();
        HotkeyPressed = null;
    }
}
