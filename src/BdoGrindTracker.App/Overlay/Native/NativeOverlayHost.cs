using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>Owns an independent desktop window for each overlay, regardless of editor selection.</summary>
internal sealed class NativeOverlayHost(IOverlayService service, ITrackerSession tracker, Form owner,
    bool validationMode = false, Func<NativeOverlayHotkeyWindow>? hotkeyWindowFactory = null,
    Func<(Screen? Screen, bool Foreground)>? locateGame = null) : IDisposable
{
    private readonly NativeOverlayGameWindow _game = new();
    private readonly Dictionary<string, NativeOverlayWindowHost> _windows = new(StringComparer.Ordinal);
    private NativeOverlayHotkeyWindow? _hotkeyWindow;
    private bool _disposed, _commandInProgress;
    private string? _commandError;

    internal void Tick()
    {
        if (_disposed || owner.IsDisposed) return;
        service.RefreshClock();
        if (!validationMode) UpdateHotkeys();
        var overlays = service.Overlays.ToArray();
        var retained = overlays.Select(overlay => overlay.Id).ToHashSet(StringComparer.Ordinal);
        var gameLocation = !validationMode && overlays.Any(overlay => (service.OverlaysVisible && overlay.Settings.Enabled) || service.GetState(overlay.Id).Previewing)
            ? (locateGame?.Invoke() ?? _game.Locate()) : default;
        foreach (var id in _windows.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            _windows[id].Dispose();
            _windows.Remove(id);
        }
        foreach (var overlay in overlays)
        {
            if (validationMode)
            {
                service.UpdateRuntime(overlay.Id, new OverlayRuntimeState
                {
                    Status = "In der UI-Testvorschau wird kein Desktop-Fenster geöffnet.",
                });
                continue;
            }
            if (!_windows.TryGetValue(overlay.Id, out var window))
                _windows[overlay.Id] = window = new NativeOverlayWindowHost(overlay.Id, service, tracker, Tick);
            window.Tick(overlay, gameLocation);
        }
    }

    private void UpdateHotkeys()
    {
        try
        {
            if (_hotkeyWindow is null)
            {
                _hotkeyWindow = hotkeyWindowFactory?.Invoke() ?? new NativeOverlayHotkeyWindow();
                _hotkeyWindow.HotkeyPressed += hotkey => _ = RunHotkeyAsync(hotkey);
            }
            var registrationError = _hotkeyWindow.Apply(service.Hotkeys);
            service.UpdateHotkeyRuntime(_commandError ?? registrationError);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or
            ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            service.UpdateHotkeyRuntime("Tastenkürzel konnten nicht registriert werden: " + exception.Message);
        }
    }

    private async Task RunHotkeyAsync(int hotkey)
    {
        if (_disposed || _commandInProgress || !service.Hotkeys.Enabled || hotkey is not (1 or 2)) return;
        _commandInProgress = true;
        try
        {
            var result = hotkey == 1 ? await service.ToggleAllOverlaysAsync() : await service.ToggleAllInteractionAsync();
            _commandError = result.Error;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _commandError = exception.Message;
        }
        finally
        {
            _commandInProgress = false;
            if (!_disposed) Tick();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hotkeyWindow?.Dispose();
        foreach (var window in _windows.Values) window.Dispose();
        _windows.Clear();
    }
}

internal sealed class NativeOverlayWindowHost(string id, IOverlayService service, ITrackerSession tracker,
    Action refresh) : IDisposable
{
    private readonly NativeOverlayRenderer _renderer = new();
    private readonly NativeOverlayRenderState _renderState = new();
    private NativeOverlayForm? _window;
    private bool _disposed, _commandInProgress;
    private readonly NativeOverlayCaptureExclusion _captureExclusion = new();
    private string? _captureError, _commandError;
    private double _dpi = 96;
    private OverlaySettings _lastSettings = new();
    private string _lastName = "Grindcrest";
    private OverlaySettings Settings => service.Overlays.FirstOrDefault(overlay => overlay.Id == id)?.Settings ?? _lastSettings;
    private string T(string source) => AppText.Translate(source, service.Snapshot.UiLanguage);

    private string Title => service.Overlays.FirstOrDefault(overlay => overlay.Id == id)?.Name ?? _lastName;

    internal void Tick(OverlayInstance overlay, (Screen? Screen, bool Foreground) gameLocation)
    {
        if (_disposed) return;
        var settings = _lastSettings = overlay.Settings;
        _lastName = overlay.Name;
        var preview = service.GetState(id).Previewing;
        try
        {
            if ((!settings.Enabled || !service.OverlaysVisible) && !preview)
            {
                _window?.Hide();
                Publish(false, T(settings.Enabled ? "Overlays über Tastenkürzel ausgeblendet." : "Overlay ausgeschaltet."), null);
                return;
            }
            EnsureWindow();
            _window!.Text = "Grindcrest – " + overlay.Name;
            _window!.SetInteraction(settings.Interaction);
            _captureError = _captureExclusion.Apply(settings.CaptureExcluded, _window.SetCaptureExcluded);
            var (gameScreen, foreground) = gameLocation;
            var screen = gameScreen ?? Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
            if (screen is null)
            {
                _window.Hide();
                Publish(false, T("Kein Bildschirm für das Overlay verfügbar."), null, true);
                return;
            }
            var monitorLabel = tracker.Monitors.FirstOrDefault(value => value.DeviceName == screen.DeviceName)?.Label ?? screen.DeviceName;
            _dpi = NativeOverlayGameWindow.Dpi(screen);
            _window.MonitorBounds = screen.Bounds;
            var visible = NativeOverlayGeometry.ShouldShow(settings.Enabled, preview, settings.Visibility,
                foreground, tracker.State.HasSession);
            if (visible)
            {
                var snapshot = service.Snapshot;
                var chrome = OverlayWindowChrome.For(snapshot.ThemeId, settings.ShowBorder);
                var bounds = NativeOverlayGeometry.Place(screen.Bounds, chrome.OuterWidth(settings.Width),
                    chrome.OuterHeight(settings.Height), settings.PositionX, settings.PositionY, settings.Scale, _dpi);
                _window.Present(bounds, repaint: !_renderState.Matches(settings, snapshot, bounds.Size, overlay.Name));
                _renderState.Remember(settings, snapshot, bounds.Size, overlay.Name);
            }
            else _window.Hide();
            var status = T(preview ? "Desktop-Vorschau aktiv." : !settings.Enabled ? "Overlay ausgeschaltet." :
                visible ? "Overlay aktiv." : settings.Visibility == "session" && !tracker.State.HasSession ?
                "Das Overlay erscheint mit der nächsten Session." : "Das Overlay erscheint, sobald Black Desert im Vordergrund ist.");
            if (gameScreen is null && (settings.Enabled || preview))
                status += AppText.Format(" Black Desert nicht gefunden; als Ersatz wird {0} verwendet.", service.Snapshot.UiLanguage, monitorLabel);
            Publish(visible, status, monitorLabel);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or
            ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            _window?.Hide();
            Publish(false, AppText.Format("Das Overlay konnte nicht angezeigt werden: {0}", service.Snapshot.UiLanguage, exception.Message), null, true);
        }
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _window = new NativeOverlayForm();
        _window.HandleCreated += (_, _) => { _captureExclusion.Invalidate(); _renderState.Invalidate(); };
        _window.CreateResize = bounds => new NativeOverlayResize(Settings, bounds, _window.MonitorBounds, _dpi,
            OverlayWindowChrome.For(service.Snapshot.ThemeId, Settings.ShowBorder));
        _window.RenderBitmap = size =>
        {
            var settings = _window.ResizePreview ?? Settings;
            var bitmap = _renderer.Render(size, settings, service.Snapshot, out var actions, Title);
            _window!.SetActions(actions);
            return bitmap;
        };
        _window.GeometryCommitted += (bounds, resized) =>
            _ = CommitGeometryAsync(bounds, resized, _window.MonitorBounds);
        _window.ActionClicked += action =>
        {
            if (action.StartsWith("toggle-tracking:", StringComparison.Ordinal) && service.Snapshot.CanToggleTracking)
                _ = RunCommandAsync(service.ToggleTrackingAsync);
            if (action.StartsWith("new-session:", StringComparison.Ordinal) && service.Snapshot.CanNewSession)
                _ = RunCommandAsync(async () =>
                {
                    if (MessageBox.Show(_window, BdoGrindTracker.App.Localization.AppText.Translate("Neue Session beginnen? Die bisherige Session wird abgeschlossen.", service.Snapshot.UiLanguage),
                            BdoGrindTracker.App.Localization.AppText.Translate("Neue Session", service.Snapshot.UiLanguage), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                        await service.NewSessionAsync();
                });
        };
    }

    internal async Task CommitGeometryAsync(Rectangle bounds, OverlaySettings? resized, Rectangle monitorBounds)
    {
        if (_disposed || !service.Overlays.Any(overlay => overlay.Id == id)) return;
        try
        {
            // Settings persistence is independent of an awaiting tracking action.
            // A drag completed while pause is draining must still keep its geometry.
            var position = NativeOverlayGeometry.RelativePosition(bounds, monitorBounds);
            var result = resized is not null
                ? await service.SaveAsync(id, resized with { PositionX = position.X, PositionY = position.Y })
                : await service.SavePositionAsync(id, position.X, position.Y);
            _commandError = result.Error;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _commandError = exception.Message;
        }
        finally { if (!_disposed) refresh(); }
    }

    private async Task RunCommandAsync(Func<Task> action)
    {
        if (_disposed || _commandInProgress || !service.Overlays.Any(overlay => overlay.Id == id)) return;
        _commandInProgress = true;
        try { await action(); _commandError = null; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _commandError = exception.Message;
        }
        finally
        {
            _commandInProgress = false;
            if (!_disposed) refresh();
        }
    }

    private void Publish(bool visible, string status, string? monitor, bool error = false)
    {
        var issue = _commandError ?? _captureError;
        if (issue is not null) status += " " + issue;
        service.UpdateRuntime(id, new OverlayRuntimeState
        {
            IsVisible = visible, Status = status, IsError = error || issue is not null,
            TargetMonitorLabel = monitor,
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_window is not null)
        {
            // Releasing capture during disposal can otherwise render a removed overlay once more.
            _window.RenderBitmap = null;
            _window.CreateResize = null;
            _window.Dispose();
        }
        _renderer.Dispose();
    }
}

/// <summary>Remember only successfully applied affinity and bound retries after a transient Windows failure.</summary>
internal sealed class NativeOverlayCaptureExclusion(Func<long>? timestamp = null)
{
    private bool? _applied, _requested;
    private string? _error;
    private long _retryAt;

    internal string? Apply(bool requested, Func<bool, string?> apply)
    {
        if (_applied == requested) return _error = null;
        var now = timestamp?.Invoke() ?? Environment.TickCount64;
        if (_requested == requested && _error is not null && now < _retryAt) return _error;
        _requested = requested;
        _error = apply(requested);
        if (_error is null) _applied = requested;
        else _retryAt = now + 5_000;
        return _error;
    }

    internal void Invalidate()
    {
        _applied = _requested = null;
        _error = null;
        _retryAt = 0;
    }
}
