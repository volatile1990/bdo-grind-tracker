namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>Compare the values drawn by this window, independent of new tracker snapshot instances.</summary>
internal sealed class NativeOverlayRenderState
{
    private OverlaySettings? _settings;
    private OverlaySnapshot? _snapshot;
    private Size _size;

    internal void Invalidate() => _settings = null;

    internal bool Matches(OverlaySettings settings, OverlaySnapshot snapshot, Size size)
    {
        if (_settings is null || _snapshot is null || size != _size ||
            _settings with { Widgets = settings.Widgets } != settings ||
            !_settings.Widgets.SequenceEqual(settings.Widgets)) return false;
        foreach (var widget in settings.Widgets)
        {
            if (_snapshot.Metrics.GetValueOrDefault(widget.Kind) != snapshot.Metrics.GetValueOrDefault(widget.Kind)) return false;
            if (widget.Kind == "controls" && (_snapshot.IsRunning != snapshot.IsRunning ||
                _snapshot.CanToggleTracking != snapshot.CanToggleTracking ||
                _snapshot.CanNewSession != snapshot.CanNewSession ||
                _snapshot.TrackingButtonLabel != snapshot.TrackingButtonLabel)) return false;
            if (widget.Kind == "chart" && !_snapshot.SilverHistory.SequenceEqual(snapshot.SilverHistory)) return false;
            if (OverlayCatalog.IsLootWidget(widget.Kind) &&
                (!_snapshot.Drops.SequenceEqual(snapshot.Drops) || !_snapshot.RareDrops.SequenceEqual(snapshot.RareDrops) ||
                 !_snapshot.ItemCatalog.SequenceEqual(snapshot.ItemCatalog))) return false;
            if (widget.Kind == "clock")
            {
                var before = OverlayClockPresentation.Create(widget, _snapshot.ClockUtcNow);
                var after = OverlayClockPresentation.Create(widget, snapshot.ClockUtcNow);
                if (before.Label != after.Label || before.IsDaytime != after.IsDaytime || !before.Rows.SequenceEqual(after.Rows)) return false;
            }
        }
        return true;
    }

    internal void Remember(OverlaySettings settings, OverlaySnapshot snapshot, Size size) =>
        (_settings, _snapshot, _size) = (settings, snapshot, size);
}
