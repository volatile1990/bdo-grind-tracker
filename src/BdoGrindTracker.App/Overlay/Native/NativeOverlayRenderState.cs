using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Overlay.Native;

/// <summary>Compare the values drawn by this window, independent of new tracker snapshot instances.</summary>
internal sealed class NativeOverlayRenderState
{
    private OverlaySettings? _settings;
    private OverlaySnapshot? _snapshot;
    private Size _size;
    private string _title = "Grindcrest";

    internal void Invalidate() => _settings = null;

    internal bool Matches(OverlaySettings settings, OverlaySnapshot snapshot, Size size, string title = "Grindcrest")
    {
        if (_settings is null || _snapshot is null || size != _size ||
            _snapshot.UiLanguage != snapshot.UiLanguage ||
            AppThemes.Normalize(_snapshot.ThemeId) != AppThemes.Normalize(snapshot.ThemeId) ||
            _settings with { Widgets = settings.Widgets } != settings ||
            !_settings.Widgets.SequenceEqual(settings.Widgets)) return false;
        if (OverlayWindowChrome.For(snapshot.ThemeId, settings.ShowBorder).HasTitleBar && _title != title) return false;
        foreach (var widget in settings.Widgets)
        {
            if (_snapshot.Metrics.GetValueOrDefault(widget.Kind) != snapshot.Metrics.GetValueOrDefault(widget.Kind)) return false;
            if (widget.Kind == "controls" && (_snapshot.IsRunning != snapshot.IsRunning ||
                _snapshot.CanToggleTracking != snapshot.CanToggleTracking ||
                _snapshot.CanNewSession != snapshot.CanNewSession ||
                _snapshot.TrackingButtonLabel != snapshot.TrackingButtonLabel)) return false;
            if (widget.Kind == "chart" && (!SameItems(_snapshot.SilverHistory, snapshot.SilverHistory) ||
                widget.ChartMode == OverlayChartSections.SectionsMode && (_snapshot.SessionElapsed != snapshot.SessionElapsed ||
                    !SameItems(_snapshot.SilverDrops, snapshot.SilverDrops)) ||
                !SameItems(_snapshot.DropMarkers, snapshot.DropMarkers))) return false;
            if (widget.Kind == "rotation-monitor" && !SameRotation(_snapshot.Rotation, snapshot.Rotation,
                widget.RotationComparison)) return false;
            if (widget.Kind == "daily-goal" && _snapshot.DailyGoal != snapshot.DailyGoal) return false;
            if (widget.Kind == "consumables" &&
                !SameItems(_snapshot.Consumables.Items, snapshot.Consumables.Items)) return false;
            if (OverlayCatalog.IsLootWidget(widget.Kind) &&
                (!SameItems(_snapshot.Drops, snapshot.Drops) || !SameItems(_snapshot.RareDrops, snapshot.RareDrops) ||
                 !SameItems(_snapshot.ItemCatalog, snapshot.ItemCatalog))) return false;
            if (widget.Kind == "clock")
            {
                var before = OverlayClockPresentation.Create(widget, _snapshot.ClockUtcNow);
                var after = OverlayClockPresentation.Create(widget, snapshot.ClockUtcNow);
                if (before.Label != after.Label || before.IsDaytime != after.IsDaytime || !before.Rows.SequenceEqual(after.Rows)) return false;
            }
        }
        return true;
    }

    internal void Remember(OverlaySettings settings, OverlaySnapshot snapshot, Size size, string title = "Grindcrest") =>
        (_settings, _snapshot, _size, _title) = (settings, snapshot, size, title);

    private static bool SameItems<T>(IReadOnlyList<T> before, IReadOnlyList<T> after) =>
        ReferenceEquals(before, after) || before.SequenceEqual(after);

    private static bool SameRotation(RotationMonitorSnapshot before, RotationMonitorSnapshot after, string mode) =>
        before.SpotId == after.SpotId && before.Elapsed == after.Elapsed &&
        before.SmallScarecrows == after.SmallScarecrows &&
        before.Error == after.Error &&
        before.Synchronized == after.Synchronized && before.Events.SequenceEqual(after.Events) &&
        SameRun(RotationTimelinePresentation.Reference(before, mode), RotationTimelinePresentation.Reference(after, mode)) &&
        (mode != "sectors" || RotationTimelinePresentation.Sector(before) == RotationTimelinePresentation.Sector(after));

    private static bool SameRun(RotationRun? before, RotationRun? after) =>
        before is null ? after is null : after is not null && before.Duration == after.Duration &&
            before.Events.SequenceEqual(after.Events);
}
