using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.UI;

/// <summary>Keeps the main window reachable while tracking continues in the background.</summary>
internal sealed class WindowTrayIcon : IDisposable
{
    private readonly Form _window;
    private readonly Func<TrackerPreferences> _preferences;
    private readonly Action _savePlacement;
    private readonly Func<bool> _canHide;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _open = new();
    private readonly ToolStripMenuItem _exit = new();
    private FormWindowState _restoreState;
    private bool _exitRequested;
    private bool _choosingCloseBehavior;
    private bool _restoring;
    private bool _disposed;

    public WindowTrayIcon(Form window, Func<TrackerPreferences> preferences,
        Action savePlacement, Func<bool> canHide)
    {
        _window = window;
        _preferences = preferences;
        _savePlacement = savePlacement;
        _canHide = canHide;
        _restoreState = window.WindowState == FormWindowState.Maximized
            ? FormWindowState.Maximized : FormWindowState.Normal;
        _open.Click += (_, _) => RestoreWindow();
        _exit.Click += (_, _) => RequestExit();
        _menu.Items.AddRange([_open, new ToolStripSeparator(), _exit]);
        _menu.Opening += (_, _) => UpdateMenu();
        UpdateMenu();
        _icon = new NotifyIcon
        {
            Icon = window.Icon,
            Text = AppBranding.Name,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) RestoreWindow();
        };
        _window.Resize += WindowResized;
    }

    private void UpdateMenu()
    {
        var language = _preferences().UiLanguage;
        _open.Text = AppText.Translate("Grindcrest öffnen", language);
        _exit.Text = AppText.Translate("Beenden", language);
        _exit.Enabled = _canHide() && !_choosingCloseBehavior;
    }

    private void WindowResized(object? sender, EventArgs e)
    {
        if (_restoring || _disposed) return;
        if (_window.WindowState != FormWindowState.Minimized)
            _restoreState = _window.WindowState;
        else if (!_choosingCloseBehavior && _preferences().MinimizeToTray && _canHide())
            HideToTray();
    }

    public async Task<bool> HandleCloseAsync(CloseReason reason, Func<Task<bool>> configureCloseBehavior)
    {
        // Explicit Exit and system shutdown never ask how the X should behave.
        if (_disposed || _exitRequested || reason != CloseReason.UserClosing || !_canHide()) return false;
        if (_choosingCloseBehavior) return true;
        if (!_preferences().CloseBehaviorConfigured)
        {
            _choosingCloseBehavior = true;
            try
            {
                // Cancel or a failed save leaves the app open and the choice pending.
                if (!await configureCloseBehavior()) return true;
            }
            finally { _choosingCloseBehavior = false; }

            // A system close or explicit exit may have begun while saving the choice.
            if (_disposed || _window.IsDisposed || _exitRequested || !_canHide()) return true;
            if (!_preferences().CloseBehaviorConfigured) return true;
        }
        return TryHideOnClose(reason);
    }

    public bool TryHideOnClose(CloseReason reason)
    {
        // Windows shutdown, Store updates and explicit Exit must still terminate.
        if (_disposed || _exitRequested || reason != CloseReason.UserClosing ||
            !_preferences().CloseBehaviorConfigured || !_preferences().CloseToTray || !_canHide()) return false;
        HideToTray();
        return true;
    }

    private void HideToTray()
    {
        _savePlacement();
        // Hiding also removes the taskbar/Alt-Tab entry without recreating the HWND
        // (changing ShowInTaskbar would disrupt the WebView and native owners).
        _window.Hide();
    }

    public void RestoreWindow()
    {
        if (_disposed || _window.IsDisposed) return;
        _restoring = true;
        try
        {
            _window.Show();
            if (_window.WindowState == FormWindowState.Minimized)
                _window.WindowState = _restoreState;
            _window.Activate();
        }
        finally { _restoring = false; }
    }

    public void RequestExit()
    {
        if (_disposed || _window.IsDisposed) return;
        _exitRequested = true;
        _window.Close();
    }

    public void CancelExit()
    {
        _exitRequested = false;
        RestoreWindow();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Resize -= WindowResized;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
