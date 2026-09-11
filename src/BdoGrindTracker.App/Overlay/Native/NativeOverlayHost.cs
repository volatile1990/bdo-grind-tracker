using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>Owns the optional desktop window and samples immutable application state.</summary>
internal sealed class NativeOverlayHost(IOverlayService service, ITrackerSession tracker, Form owner,
    bool validationMode = false) : IDisposable
{
    private readonly NativeOverlayGameWindow _game = new();
    private readonly NativeOverlayRenderer _renderer = new();
    private NativeOverlayForm? _window;
    private bool _disposed, _commandInProgress;
    private bool? _captureExcluded;
    private string? _captureError, _hotkeyError, _commandError;
    private double _dpi = 96;

    internal void Tick()
    {
        if (_disposed || owner.IsDisposed) return;
        var settings = service.Settings;
        var preview = service.State.Previewing;
        if (validationMode)
        {
            service.UpdateRuntime(new OverlayRuntimeState { Status = "In der UI-Testvorschau wird kein Desktop-Fenster geöffnet." });
            return;
        }
        try
        {
            if (!settings.Enabled && !preview && !settings.HotkeysEnabled)
            {
                _window?.Hide();
                if (_window is not null)
                {
                    _window.SetHotkeys(false);
                    _hotkeyError = null;
                }
                Publish(false, "Overlay ausgeschaltet.", null);
                return;
            }
            EnsureWindow();
            // The form compares the full binding pair, so editing either shortcut
            // and recreating its window handle both trigger fresh registration.
            _hotkeyError = _window!.SetHotkeys(settings.HotkeysEnabled,
                settings.ToggleOverlayHotkey, settings.ToggleInteractionHotkey);
            _window!.SetInteraction(settings.Interaction);
            if (_captureExcluded != settings.CaptureExcluded)
            {
                _captureError = _window.SetCaptureExcluded(settings.CaptureExcluded);
                _captureExcluded = settings.CaptureExcluded;
            }
            var (gameScreen, foreground) = _game.Locate();
            var screen = gameScreen ?? Screen.AllScreens.FirstOrDefault(value =>
                value.DeviceName == tracker.Preferences.MonitorDeviceName) ?? Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
            if (screen is null)
            {
                _window.Hide();
                Publish(false, "Kein Bildschirm für das Overlay verfügbar.", null, true);
                return;
            }
            var monitorLabel = tracker.Monitors.FirstOrDefault(value => value.DeviceName == screen.DeviceName)?.Label ?? screen.DeviceName;
            _dpi = NativeOverlayGameWindow.Dpi(screen);
            _window.MonitorBounds = screen.Bounds;
            var visible = NativeOverlayGeometry.ShouldShow(settings.Enabled, preview, settings.Visibility,
                foreground, tracker.State.HasSession);
            if (visible)
            {
                var bounds = NativeOverlayGeometry.Place(screen.Bounds, settings.Width, settings.Height,
                    settings.PositionX, settings.PositionY, settings.Scale, _dpi);
                _window.Present(bounds);
            }
            else _window.Hide();
            var status = preview ? "Desktop-Vorschau aktiv." : !settings.Enabled ? "Overlay ausgeschaltet." :
                visible ? "Overlay aktiv." : settings.Visibility == "session" && !tracker.State.HasSession ?
                "Das Overlay erscheint mit der nächsten Session." : "Das Overlay erscheint, sobald Black Desert im Vordergrund ist.";
            if (gameScreen is null && (settings.Enabled || preview))
                status += $" Black Desert nicht gefunden; als Ersatz wird {monitorLabel} verwendet.";
            Publish(visible, status, monitorLabel);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or
            ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            _window?.Hide();
            Publish(false, "Das Overlay konnte nicht angezeigt werden: " + exception.Message, null, true);
        }
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _window = new NativeOverlayForm();
        _window.HandleCreated += (_, _) => _captureExcluded = null;
        _window.CreateResize = bounds => new NativeOverlayResize(service.Settings, bounds, _window.MonitorBounds, _dpi);
        _window.RenderBitmap = size =>
        {
            var settings = _window.ResizePreview ?? service.Settings;
            var bitmap = _renderer.Render(size, settings, service.Snapshot, out var actions);
            _window!.SetActions(actions);
            return bitmap;
        };
        _window.GeometryCommitted += (bounds, resized) => _ = RunCommandAsync(async () =>
        {
            var position = NativeOverlayGeometry.RelativePosition(bounds, _window.MonitorBounds);
            // Commit exactly the layout already shown during the corner drag.
            var result = resized is not null
                ? await service.SaveAsync(resized)
                : await service.SavePositionAsync(position.X, position.Y);
            if (!result.Succeeded) throw new InvalidOperationException(result.Error);
        });
        _window.ActionClicked += action =>
        {
            if (action.StartsWith("toggle-tracking:", StringComparison.Ordinal) && service.Snapshot.CanToggleTracking)
                _ = RunCommandAsync(service.ToggleTrackingAsync);
        };
        _window.HotkeyPressed += id => _ = RunCommandAsync(async () =>
        {
            var settings = service.Settings;
            var updated = id switch
            {
                1 => settings with { Enabled = !settings.Enabled },
                2 => settings with { Interaction = settings.Interaction == "passthrough" ? "move" : "passthrough" },
                _ => settings,
            };
            var result = await service.SaveAsync(updated);
            if (!result.Succeeded) throw new InvalidOperationException(result.Error);
        });
    }

    private async Task RunCommandAsync(Func<Task> action)
    {
        if (_disposed || _commandInProgress) return;
        _commandInProgress = true;
        try { await action(); _commandError = null; }
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

    private void Publish(bool visible, string status, string? monitor, bool error = false)
    {
        var issue = _commandError ?? _captureError;
        if (issue is not null) status += " " + issue;
        service.UpdateRuntime(new OverlayRuntimeState
        {
            IsVisible = visible, Status = status, IsError = error || issue is not null,
            TargetMonitorLabel = monitor, HotkeyStatus = _hotkeyError,
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window?.Dispose();
        _renderer.Dispose();
    }
}
