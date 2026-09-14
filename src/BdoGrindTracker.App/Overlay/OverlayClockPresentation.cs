using System.Globalization;

namespace BdoGrindTracker.App.Overlay;

public sealed record OverlayClockRow(string Kind, string Label, string Value);
public sealed record OverlayClockView(string Label, bool IsDaytime, IReadOnlyList<OverlayClockRow> Rows)
{
    public string Tooltip => "Lokale Systemzeit · BDO-Weltzeit nach regulärem Zyklus · Countdown in Echtzeit";
}

public static class OverlayClockPresentation
{
    public static int RowCount(OverlayWidget widget) => Math.Max(1,
        (widget.ShowRealTime ? 1 : 0) + (widget.ShowGameTime ? 1 : 0) + (widget.ShowDayNightCountdown ? 1 : 0));

    public static OverlayClockView Create(OverlayWidget widget, DateTimeOffset now, TimeZoneInfo? localTimeZone = null)
    {
        var state = BdoClock.At(now, widget.ClockOffsetMinutes);
        var rows = new List<OverlayClockRow>(3);
        if (widget.ShowRealTime || (!widget.ShowGameTime && !widget.ShowDayNightCountdown))
        {
            var local = TimeZoneInfo.ConvertTime(now, localTimeZone ?? TimeZoneInfo.Local);
            rows.Add(new("real", "Lokal", local.ToString("HH:mm", CultureInfo.InvariantCulture)));
        }
        if (widget.ShowGameTime)
            rows.Add(new("game", "BDO", state.GameTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture)));
        if (widget.ShowDayNightCountdown)
        {
            // Round remaining duration up, so the display never reports zero
            // before the actual transition or loses almost a minute.
            const long unitTicks = TimeSpan.TicksPerMinute;
            var rounded = TimeSpan.FromTicks((state.UntilNextChange.Ticks + unitTicks - 1) / unitTicks * unitTicks);
            rows.Add(new("countdown", state.IsDaytime ? "Nacht in" : "Tag in",
                rounded.ToString(@"hh\:mm", CultureInfo.InvariantCulture)));
        }
        return new(state.IsDaytime ? "Uhrzeit · Tag" : "Uhrzeit · Nacht", state.IsDaytime, rows.AsReadOnly());
    }
}
