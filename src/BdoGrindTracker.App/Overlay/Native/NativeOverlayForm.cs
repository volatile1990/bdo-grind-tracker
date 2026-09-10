using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>A separate, non-activating desktop window. It never attaches to the game.</summary>
internal sealed class NativeOverlayForm : Form
{
    private const int WmMouseActivate = 0x21, MaNoActivate = 3, WmHotkey = 0x312;
    private string _interaction = "move";
    private Point _dragStart;
    private Rectangle _dragBounds;
    private bool _dragging, _resizing;
    private string? _pressedAction;
    private IReadOnlyDictionary<string, RectangleF> _actions = new Dictionary<string, RectangleF>();
    private readonly NativeOverlayHotkeyRegistration _hotkeys;

    internal event Action<Rectangle, bool>? GeometryCommitted;
    internal event Action<string>? ActionClicked;
    internal event Action<int>? HotkeyPressed;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Rectangle MonitorBounds { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Size MinimumInteractionSize { get; set; } = new(180, 130);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Size MaximumInteractionSize { get; set; } = new(720, 520);
    internal bool IsManipulating => _dragging || _resizing;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<Size, Bitmap>? RenderBitmap { get; set; }

    internal NativeOverlayForm()
    {
        _hotkeys = new NativeOverlayHotkeyRegistration(
            (id, modifiers, key) => NativeOverlayApi.RegisterHotKey(Handle, id, modifiers, key),
            id => { if (IsHandleCreated) NativeOverlayApi.UnregisterHotKey(Handle, id); });
        Text = "Grindcrest Overlay";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        MinimumSize = new Size(1, 1);
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= NativeOverlayApi.ExLayered | NativeOverlayApi.ExToolWindow | NativeOverlayApi.ExNoActivate;
            if (_interaction == "passthrough") parameters.ExStyle |= NativeOverlayApi.ExTransparent;
            return parameters;
        }
    }

    internal void SetInteraction(string interaction)
    {
        if (_interaction == interaction) return;
        _interaction = interaction;
        CancelManipulation();
        if (!IsHandleCreated) return;
        var style = (long)NativeOverlayApi.GetWindowLongPtr(Handle, NativeOverlayApi.GwlExStyle);
        style = interaction == "passthrough" ? style | NativeOverlayApi.ExTransparent : style & ~NativeOverlayApi.ExTransparent;
        NativeOverlayApi.SetWindowLongPtr(Handle, NativeOverlayApi.GwlExStyle, new nint(style));
    }

    internal string? SetCaptureExcluded(bool exclude)
    {
        return NativeOverlayApi.SetWindowDisplayAffinity(Handle, exclude ? 0x11u : 0u) ? null :
            $"Das Overlay konnte nicht aus Bildschirmaufnahmen ausgeschlossen werden (Windows-Fehler {Marshal.GetLastWin32Error()}).";
    }

    internal void SetActions(IReadOnlyDictionary<string, RectangleF> actions) => _actions = actions;

    internal void Present(Rectangle bounds)
    {
        if (IsDisposed) return;
        if (!IsManipulating && Bounds != bounds) Bounds = bounds;
        Render();
        if (!Visible) Show();
        NativeOverlayApi.SetWindowPos(Handle, NativeOverlayApi.TopMost, 0, 0, 0, 0,
            NativeOverlayApi.SwpNoActivate | NativeOverlayApi.SwpNoMove | NativeOverlayApi.SwpNoSize);
    }

    internal void Render()
    {
        if (RenderBitmap is null || Width <= 0 || Height <= 0) return;
        using var bitmap = RenderBitmap(Size);
        if (bitmap.PixelFormat != PixelFormat.Format32bppPArgb)
            throw new InvalidOperationException("Das Overlay benötigt ein Bitmap mit vormultipliziertem Alphakanal.");
        var screenDc = NativeOverlayApi.GetDC(0);
        var memoryDc = NativeOverlayApi.CreateCompatibleDC(screenDc);
        nint handle = 0, previous = 0;
        try
        {
            handle = bitmap.GetHbitmap(Color.FromArgb(0));
            previous = NativeOverlayApi.SelectObject(memoryDc, handle);
            var position = new NativeOverlayApi.NativePoint(Left, Top);
            var size = new NativeOverlayApi.NativeSize(Width, Height);
            var source = new NativeOverlayApi.NativePoint(0, 0);
            var blend = new NativeOverlayApi.BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (!NativeOverlayApi.UpdateLayeredWindow(Handle, screenDc, ref position, ref size, memoryDc,
                    ref source, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Das Overlay konnte nicht gezeichnet werden.");
        }
        finally
        {
            if (previous != 0) NativeOverlayApi.SelectObject(memoryDc, previous);
            if (handle != 0) NativeOverlayApi.DeleteObject(handle);
            if (memoryDc != 0) NativeOverlayApi.DeleteDC(memoryDc);
            if (screenDc != 0) NativeOverlayApi.ReleaseDC(0, screenDc);
        }
    }

    internal string? SetHotkeys(bool enabled, OverlayHotkey? toggleOverlay = null, OverlayHotkey? toggleInteraction = null) =>
        _hotkeys.Apply(enabled,
            OverlayHotkey.Normalize(toggleOverlay, OverlayHotkey.DefaultToggleOverlay),
            OverlayHotkey.Normalize(toggleInteraction, OverlayHotkey.DefaultToggleInteraction));

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmMouseActivate)
        {
            message.Result = MaNoActivate;
            return;
        }
        if (message.Msg == WmHotkey)
        {
            var packed = unchecked((uint)message.LParam.ToInt64());
            if (_hotkeys.Matches((int)message.WParam, packed & 0xffff, packed >> 16))
                HotkeyPressed?.Invoke((int)message.WParam);
            return;
        }
        base.WndProc(ref message);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _interaction == "passthrough") return;
        _pressedAction = ActionAt(e.Location);
        if (_pressedAction is not null) { Capture = true; return; }
        if (_interaction != "move") return;
        _dragStart = Cursor.Position;
        _dragBounds = Bounds;
        _resizing = e.X >= Width - ResizeGrip && e.Y >= Height - ResizeGrip;
        _dragging = !_resizing;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsManipulating)
        {
            var delta = Size.Subtract(new Size(Cursor.Position), new Size(_dragStart));
            var proposed = _resizing ? new Rectangle(_dragBounds.Location, ResizedSize(delta)) :
                new Rectangle(Point.Add(_dragBounds.Location, delta), _dragBounds.Size);
            Bounds = NativeOverlayGeometry.Clamp(proposed, MonitorBounds);
            Render();
        }
        Cursor = _interaction == "passthrough" ? Cursors.Default : ActionAt(e.Location) is not null ? Cursors.Hand :
            _interaction == "move" ? e.X >= Width - ResizeGrip && e.Y >= Height - ResizeGrip ? Cursors.SizeNWSE : Cursors.SizeAll : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var moved = IsManipulating;
        var resized = _resizing;
        var action = _pressedAction is not null && ActionAt(e.Location) == _pressedAction ? _pressedAction : null;
        base.OnMouseUp(e);
        CancelManipulation();
        if (moved) GeometryCommitted?.Invoke(Bounds, resized);
        if (action is not null) ActionClicked?.Invoke(action);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) { _dragging = false; _resizing = false; _pressedAction = null; }
    }

    private int ResizeGrip => Math.Max(14, DeviceDpi / 6);
    private Size ResizedSize(Size delta)
    {
        var horizontal = (double)delta.Width / _dragBounds.Width;
        var vertical = (double)delta.Height / _dragBounds.Height;
        var desired = 1 + (Math.Abs(horizontal) > Math.Abs(vertical) ? horizontal : vertical);
        var minimum = Math.Max((double)MinimumInteractionSize.Width / _dragBounds.Width,
            (double)MinimumInteractionSize.Height / _dragBounds.Height);
        var maximum = Math.Min((double)MaximumInteractionSize.Width / _dragBounds.Width,
            (double)MaximumInteractionSize.Height / _dragBounds.Height);
        var factor = Math.Clamp(desired, Math.Min(minimum, maximum), maximum);
        return new Size(Math.Max(1, (int)Math.Round(_dragBounds.Width * factor)),
            Math.Max(1, (int)Math.Round(_dragBounds.Height * factor)));
    }
    private string? ActionAt(Point point)
    {
        // Match drawing order: a later module covers controls beneath it.
        foreach (var pair in _actions.Reverse())
            if (pair.Value.Contains(point)) return pair.Key.StartsWith("toggle-tracking:", StringComparison.Ordinal) ? pair.Key : null;
        return null;
    }
    private void CancelManipulation()
    {
        _dragging = _resizing = false;
        _pressedAction = null;
        Capture = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hotkeys.Clear();
        base.Dispose(disposing);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _hotkeys.Clear();
        base.OnHandleDestroyed(e);
    }
}

/// <summary>Owns only this window's registrations and replaces both bindings as one update.</summary>
internal sealed class NativeOverlayHotkeyRegistration(Func<int, uint, uint, bool> register, Action<int> unregister)
{
    private const uint NoRepeat = 0x4000;
    private readonly HashSet<int> _registered = [];
    private Bindings? _bindings;
    private string? _error;

    internal string? Apply(bool enabled, OverlayHotkey toggleOverlay, OverlayHotkey toggleInteraction)
    {
        var requested = new Bindings(enabled, toggleOverlay, toggleInteraction);
        if (requested == _bindings) return _error;
        // Release both old combinations first, including when the user swaps them.
        Clear();
        _bindings = requested;
        if (!enabled) return null;

        var failures = new List<string>();
        var combinations = new HashSet<OverlayHotkey>();
        foreach (var (id, shortcut) in new[] { (1, toggleOverlay), (2, toggleInteraction) })
        {
            if (!shortcut.IsValid || !combinations.Add(shortcut) ||
                !register(id, (uint)shortcut.Modifiers | NoRepeat, shortcut.VirtualKey))
                failures.Add(shortcut.DisplayText);
            else _registered.Add(id);
        }
        _error = failures.Count == 0 ? null : "Tastenkürzel bereits belegt: " + string.Join(", ", failures) + ".";
        return _error;
    }

    internal bool Matches(int id, uint modifiers, uint key)
    {
        if (!_registered.Contains(id)) return false;
        var shortcut = id switch { 1 => _bindings?.ToggleOverlay, 2 => _bindings?.ToggleInteraction, _ => null };
        return shortcut is not null && (uint)shortcut.Modifiers == modifiers && shortcut.VirtualKey == key;
    }

    internal void Clear()
    {
        foreach (var id in _registered) unregister(id);
        _registered.Clear();
        _bindings = null;
        _error = null;
    }

    private sealed record Bindings(bool Enabled, OverlayHotkey ToggleOverlay, OverlayHotkey ToggleInteraction);
}
