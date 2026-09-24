using System.Globalization;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BdoGrindTracker.App.Components;

public partial class SessionTimeline
{
    /// <param name="Count">Drops of this item the icon stands for.</param>
    private sealed record TimelineMarkerItem(string Item, int Count, string Color, string Title);

    /// <param name="At">Session time of the group's first drop; its stem stands there.</param>
    /// <param name="Icon">Height of the icons, just above the layers of every moment they cover.</param>
    /// <param name="Items">Different items side by side, each item once with the number of its drops.</param>
    private sealed record TimelineMarker(TimeSpan At, double Icon, IReadOnlyList<TimelineMarkerItem> Items);

    /// <param name="Left">Place across the track in percent; the label is centred above its bar.</param>
    private sealed record TimelineBarLabel(double Left, double Top, string Text, string Color, string Title, bool Inside);

    /// <summary>A rotation with its phases, drawn in the band the way the rotation monitor draws them.</summary>
    private sealed record TimelineRotation(SessionRotationSpan Span, IReadOnlyList<RotationPhase> Phases,
        double Left, double Width);

    // Lanes of the picture, in viewBox units: nothing shares a lane with anything else.
    private const double Height = 240;
    private const double PlotTop = 20, Baseline = 150, PlotHeight = Baseline - PlotTop;
    private const double BandTop = 172, GroupBar = 4, PhaseTop = 178, PhaseHeight = 26;
    private const double RailTop = 206, BandBottom = 212;
    // Drop icons in pixels: the stacked frame of a numbered icon reaches 3 pixels into the gap.
    private const double DropSize = 26, DropGap = 5;

    private static readonly IReadOnlyDictionary<string, object> OpenAttribute =
        new Dictionary<string, object> { ["open"] = "open" };
    private static readonly IReadOnlyDictionary<string, object> NoAttributes = new Dictionary<string, object>();

    private readonly HashSet<string> _layers = [.. SessionTimelineLayers.Default];
    private readonly HashSet<string> _items = new(StringComparer.Ordinal);
    private bool _rotationList, _lootList;
    private double _zoom = 1, _center = .5;
    private int? _selected;
    private double? _dragFrom;
    private double _trackWidth = 1000;
    private TimeSpan _from, _to;
    private IReadOnlyList<SessionRotationSpan> _spans = [];
    private IReadOnlyList<TimelineRotation> _rotations = [];
    private IReadOnlyList<SessionTimelineSeries> _series = [];
    private SessionTimelineSeries? _silver;
    private IReadOnlyList<TimelineMarker> _markers = [];
    private string[] _chosen = [];
    private ElementReference _track;
    private bool _attached;
    // Set by an event that changed nothing on the picture: a pointer moving without a drag, a wheel without Shift.
    private bool _unchanged;
    // Worth and mark of every drop of the history, kept while history, prices and preferences stay the same objects.
    private IReadOnlyList<SessionDropSample>? _valuedDrops;
    private object? _valuedPrices, _valuedPreferences;
    private decimal[] _worth = [];
    private bool[] _valuable = [];

    /// <summary>Keeps the section open across state updates once the user opened it.</summary>
    private IReadOnlyDictionary<string, object> Expanded => _open ? OpenAttribute : NoAttributes;
    private bool _open;

    /// <summary>
    /// What the timeline draws for the visible window, recomputed once per render and only while it is open. The worth
    /// of the drops does not depend on the window and is only valued again when the history, prices or preferences
    /// change.
    /// </summary>
    private void Refresh()
    {
        UpdateWindow();
        if (!_open)
        {
            _spans = []; _rotations = []; _series = []; _silver = null; _markers = [];
            return;
        }
        Revalue();
        _spans = SessionRotationStats.Spans(State.Rotation, State.Elapsed, State.ObservedAt);
        _rotations = !IsOn("rotations") ? [] : [.. _spans
            .Where(span => span.End >= _from && span.Start <= _to)
            .Select(span =>
            {
                var (left, width) = Clip(span.Start, span.End);
                return new TimelineRotation(span,
                    RotationPhases.Cached(State.SpotId, span.Events, span.Duration), left, width);
            })];
        _chosen = [.. _items.Order(StringComparer.Ordinal)];
        _series = BuildSeries();
        _silver = !IsOn("silver") ? null : SessionTimelineChart.Series("silver", T("Silber je Abschnitt"),
            SessionTimelineLayers.ColorOf("silver"), Enumerable.Range(0, Drops.Count), index => Drops[index].Elapsed,
            index => _worth[index], _from, _to, isSilver: true, flatten: true);
        if (_silver is { Peak: <= 0 }) _silver = null;
        _markers = BuildMarkers();
    }

    /// <summary>The quantities drawn as bars. Items chosen in the loot selector are marked with their icon instead.</summary>
    private IReadOnlyList<SessionTimelineSeries> BuildSeries()
    {
        List<SessionTimelineSeries> series = [];
        if (IsOn("trash") && Presentation.Profile(State.SpotId)?.TrashItemName is { } trash)
            series.Add(SessionTimelineChart.Series("trash", ItemLabel(trash), SessionTimelineLayers.ColorOf("trash"),
                Drops.Where(drop => drop.ItemName == trash), _from, _to));
        return [.. series.Where(entry => entry.Peak > 0)];
    }

    private void Revalue()
    {
        if (ReferenceEquals(_valuedDrops, Drops) && ReferenceEquals(_valuedPrices, Tracker.Prices) &&
            ReferenceEquals(_valuedPreferences, Tracker.Preferences)) return;
        (_valuedDrops, _valuedPrices, _valuedPreferences) = (Drops, Tracker.Prices, Tracker.Preferences);
        // An item is a favorite or valuable once for all of its drops.
        var marked = new Dictionary<string, bool>(StringComparer.Ordinal);
        _worth = [.. Drops.Select(SilverOf)];
        _valuable = [.. Drops.Select(drop => marked.TryGetValue(drop.ItemName, out var known) ? known
            : marked[drop.ItemName] = SessionLootMarkers.IsMarked(drop.ItemName, Tracker.Preferences, Tracker.Prices))];
    }

    /// <summary>What a drop is worth after tax, the same valuation the session's silver uses.</summary>
    private decimal SilverOf(SessionDropSample drop)
    {
        if (Tracker.Prices is not { } prices || !prices.TryGetQuote(drop.ItemName, out var quote)) return 0;
        try { return checked(Pricing.SilverValuation.UnitAfterTax(quote, Tracker.Preferences.Tax) * drop.Quantity); }
        catch (OverflowException) { return 0; }
    }

    /// <summary>Every layer that draws a value, bars and silver alike.</summary>
    private IEnumerable<SessionTimelineSeries> Layers => _silver is { } silver ? [.. _series, silver] : _series;

    /// <summary>
    /// Rare drops, favorites and the items chosen in the loot selector, each above the tallest layer of its moment.
    /// Drops whose icons would cover each other share a row: different items stand side by side, the same item
    /// appears once with the number of its drops.
    /// </summary>
    private IReadOnlyList<TimelineMarker> BuildMarkers()
    {
        var rare = IsOn("rare");
        var marked = Enumerable.Range(0, Drops.Count).Where(index => Drops[index].Elapsed >= _from && Drops[index].Elapsed <= _to &&
            (_items.Contains(Drops[index].ItemName) || rare && _valuable[index]))
            .Select(index => Drops[index]).OrderBy(drop => drop.Elapsed).ToArray();
        var layers = Layers.ToArray();
        List<TimelineMarker> markers = [];
        for (var index = 0; index < marked.Length;)
        {
            List<SessionDropSample> group = [marked[index]];
            HashSet<string> names = new(StringComparer.Ordinal) { marked[index].ItemName };
            // The row starts at its first drop; a drop joins while its icon would touch the row.
            var left = X(marked[index].Elapsed) / 1000 * _trackWidth - DropSize / 2;
            while (++index < marked.Length &&
                   X(marked[index].Elapsed) / 1000 * _trackWidth - DropSize / 2 < left + RowWidth(names.Count) + DropGap)
            {
                names.Add(marked[index].ItemName);
                group.Add(marked[index]);
            }
            var top = Y(group.Max(drop => SessionTimelineChart.Top(drop.Elapsed, layers, _from, _to)));
            markers.Add(new(group[0].Elapsed, Math.Max(PlotTop - 4, top - 20), [.. group.GroupBy(drop => drop.ItemName)
                .Select(item =>
                {
                    var times = item.Take(7).Select(drop => Presentation.Duration(drop.Elapsed)).ToArray();
                    return new TimelineMarkerItem(item.Key, item.Count(),
                        _items.Contains(item.Key) ? ItemColor(item.Key) : SessionTimelineLayers.ColorOf("rare"),
                        ItemLabel(item.Key) + " × " + Number(item.Sum(drop => drop.Quantity)) + " · " +
                        string.Join(", ", times.Take(6)) + (times.Length > 6 ? ", …" : ""));
                })]));
        }
        return markers;
    }

    private static double RowWidth(int items) => items * DropSize + Math.Max(0, items - 1) * DropGap;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        // A Shift wheel zooms the timeline; every other wheel keeps scrolling the page, which Blazor alone
        // cannot express: its preventDefault is static per handler.
        if (!_open || _attached) return;
        _attached = true;
        try
        {
            await JS.InvokeVoidAsync("sessionTimeline.attach", _track);
            if (await Measure()) StateHasChanged();
        }
        catch (Exception) { _attached = false; }
    }

    /// <summary>The rendered width decides how much text fits and how far a drag moves the window.</summary>
    private async Task<bool> Measure()
    {
        try
        {
            var width = await JS.InvokeAsync<double>("sessionTimeline.width", [_track]);
            if (!double.IsFinite(width) || width < 1 || Math.Abs(width - _trackWidth) < 1) return false;
            _trackWidth = width;
            return true;
        }
        catch (Exception) { return false; }
    }

    private IReadOnlyList<SessionDropSample> Drops => State.DropHistory;
    private int RotationCount => SessionRotationStats.Count(State.Rotation);
    private double? Average => SessionRotationStats.Average(State.Rotation);
    private double? Fastest => SessionRotationStats.Fastest(State.Rotation);
    private TimeSpan Span => State.Elapsed > TimeSpan.Zero ? State.Elapsed : TimeSpan.FromMinutes(1);

    /// <summary>Every item the session has dropped, the ones already shown first.</summary>
    private IReadOnlyList<string> LootItems =>
        [.. State.Loot.Totals.Keys.OrderByDescending(_items.Contains).ThenBy(ItemLabel, StringComparer.CurrentCulture)];
    private string ItemColor(string item) => Array.IndexOf(_chosen, item) is var index && index >= 0
        ? SessionTimelineLayers.ItemColorOf(index) : SessionTimelineLayers.Neutral;

    private bool IsOn(string id) => _layers.Contains(id);
    private void Toggle(string id, bool on)
    {
        if (on) _layers.Add(id); else _layers.Remove(id);
    }
    private void ToggleItem(string item, bool on)
    {
        if (on) _items.Add(item); else _items.Remove(item);
    }

    private void UpdateWindow()
    {
        var visible = Span / Math.Clamp(_zoom, 1, 120);
        var half = visible / 2;
        var center = TimeSpan.FromSeconds(Math.Clamp(_center, 0, 1) * Span.TotalSeconds);
        var from = center - half;
        if (from < TimeSpan.Zero) from = TimeSpan.Zero;
        if (from + visible > Span) from = Span - visible;
        _from = from < TimeSpan.Zero ? TimeSpan.Zero : from;
        _to = _from + visible;
    }

    /// <summary>SVG coordinates never depend on the user's culture: a decimal comma would break every path.</summary>
    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private double X(TimeSpan at)
    {
        var window = (_to - _from).TotalSeconds;
        return window <= 0 ? 0 : Math.Clamp((at - _from).TotalSeconds / window, -.1, 1.1) * 1000;
    }
    private double Width(TimeSpan length)
    {
        var window = (_to - _from).TotalSeconds;
        return window <= 0 ? 0 : Math.Max(1, length.TotalSeconds / window * 1000);
    }
    /// <summary>A span cut to the visible picture: a rotation reaching past an edge keeps its true other edge.</summary>
    private (double Left, double Width) Clip(TimeSpan from, TimeSpan to)
    {
        var left = Math.Clamp(X(from), 0, 1000);
        return (left, Math.Max(1, Math.Clamp(X(to), 0, 1000) - left));
    }
    private static double Y(double height) => Baseline - Math.Clamp(height, 0, 1) * PlotHeight;
    private static string Percent(double units) => Num(units / Height * 100);

    /// <summary>A phase's place on the session axis: its rotation's start plus its own seconds.</summary>
    private double PhaseX(SessionRotationSpan span, double seconds) => X(span.Start + TimeSpan.FromSeconds(seconds));
    private double PhaseWidth(SessionRotationSpan span, double from, double to) =>
        Math.Max(0, PhaseX(span, to) - PhaseX(span, from));
    private static string GroupLabel(RotationPhase phase) =>
        phase.Group.StartsWith("cycle-", StringComparison.Ordinal) ? "Zyklus" : "Abschnitt";

    /// <summary>Phase times only where they add something: on a shared axis a width already tells the duration.</summary>
    private bool ShowsPhaseTime(SessionRotationSpan span, double width) => _selected == span.Number || width >= 48;

    /// <summary>Readable time labels for the visible window.</summary>
    private IReadOnlyList<TimeSpan> Ticks
    {
        get
        {
            var window = (_to - _from).TotalSeconds;
            if (window <= 0) return [];
            var step = new[] { 10d, 30, 60, 120, 300, 600, 1800, 3600, 7200 }.FirstOrDefault(value => window / value <= 8, 7200);
            var result = new List<TimeSpan>();
            for (var second = Math.Ceiling(_from.TotalSeconds / step) * step; second <= _to.TotalSeconds; second += step)
                result.Add(TimeSpan.FromSeconds(second));
            return result;
        }
    }

    private string BarTitle(SessionTimelineSeries series, SessionTimelineBar bar) =>
        series.Label + " · " + (series.IsSilver ? Silver(bar.Value) : Number(bar.Value)) +
        " · " + Presentation.Duration(bar.At);

    /// <summary>What the top of the plot stands for: the peak of every layer that draws a value.</summary>
    private string? ScaleCaption => Layers.ToArray() is not { Length: > 0 } layers ? null
        : F("max {0} / {1}", string.Join(" · ", layers.Select(Peak)), IntervalLabel);
    private string Peak(SessionTimelineSeries series) => series.IsSilver ? Silver(series.Peak) : Number(series.Peak);
    private string IntervalLabel
    {
        get
        {
            var seconds = SessionTimelineChart.IntervalFor(_to - _from);
            return seconds < 60 ? F("{0} s", Number((long)seconds))
                : seconds < 3600 ? F("{0} min", Number((long)(seconds / 60))) : F("{0} h", Number((long)(seconds / 3600)));
        }
    }

    /// <summary>Every bar says how much it stands for.</summary>
    private IEnumerable<TimelineBarLabel> BarLabels
    {
        get
        {
            for (var index = 0; index < _series.Count; index++)
            {
                var series = _series[index];
                foreach (var bar in series.Bars)
                {
                    if (bar.Value <= 0) continue;
                    var width = (bar.End - bar.Start) * 1000 / _series.Count;
                    var inside = bar.Filled > .86;
                    yield return new((bar.Start * 1000 + (index + .5) * width) / 10,
                        (inside ? Y(bar.Filled) + 13 : Y(bar.Filled) - 3) / Height * 100,
                        series.IsSilver ? Silver(bar.Value) : Number(bar.Value),
                        series.Color, BarTitle(series, bar), inside);
                }
            }
        }
    }

    /// <summary>The silver of each interval as a curve through the middle of every interval.</summary>
    private string SilverCurve => _silver is not { Bars.Count: > 0 } silver ? ""
        : string.Join(" ", silver.Bars.Select(bar => Num((bar.Start + bar.End) / 2 * 1000) + "," + Num(Y(bar.Filled))));
    private string SilverArea => _silver is not { Bars.Count: > 0 } silver ? ""
        : Num((silver.Bars[0].Start + silver.Bars[0].End) / 2 * 1000) + "," + Num(Baseline) + " " + SilverCurve + " " +
          Num((silver.Bars[^1].Start + silver.Bars[^1].End) / 2 * 1000) + "," + Num(Baseline);

    private IEnumerable<TimeSpan> SpecialEvents => !IsOn("special") ? []
        : _spans.SelectMany(span => span.SpecialEvents).Where(at => at >= _from && at <= _to);

    private void ToggleOpen()
    {
        _open = !_open;
        _attached = false;
        StateHasChanged();
    }

    /// <summary>An event that changed nothing leaves the picture as it is instead of rebuilding it.</summary>
    protected override bool ShouldRender()
    {
        if (!_unchanged) return true;
        _unchanged = false;
        return false;
    }

    private void Wheel(WheelEventArgs e)
    {
        if (!e.ShiftKey)
        {
            _unchanged = true;
            return;
        }
        _zoom = Math.Clamp(_zoom * (e.DeltaY < 0 ? 1.25 : .8), 1, 120);
        _selected = null;
        UpdateWindow();
    }

    private async Task PointerDown(PointerEventArgs e)
    {
        _dragFrom = e.ClientX;
        // The pointer belongs to the timeline until it is released, so a drag that leaves the element keeps working.
        try { await JS.InvokeVoidAsync("sessionTimeline.capture", _track, e.PointerId); }
        catch (Exception) { /* Without the capture a drag simply ends at the edge. */ }
        // Dragging moves the window by the share of the track the pointer crossed, so it needs its rendered width.
        await Measure();
    }
    private async Task PointerUp(PointerEventArgs e)
    {
        _dragFrom = null;
        try { await JS.InvokeVoidAsync("sessionTimeline.release", _track, e.PointerId); }
        catch (Exception) { /* Nothing was captured. */ }
    }
    private void PointerMove(PointerEventArgs e)
    {
        if (_dragFrom is not { } start)
        {
            _unchanged = true;
            return;
        }
        _dragFrom = e.ClientX;
        // Crossing the whole track moves the view by exactly one visible window, which is 1/zoom of the session.
        _center = Math.Clamp(_center + (start - e.ClientX) / _trackWidth / Math.Clamp(_zoom, 1, 120), 0, 1);
        UpdateWindow();
    }

    private void Focus(SessionRotationSpan span)
    {
        _selected = span.Number;
        var length = (span.End - span.Start).TotalSeconds;
        // A little room on both sides keeps the neighbouring drops visible.
        _zoom = Math.Clamp(Span.TotalSeconds / Math.Max(1, length * 1.3), 1, 120);
        _center = Math.Clamp((span.Start + (span.End - span.Start) / 2).TotalSeconds / Math.Max(1, Span.TotalSeconds), 0, 1);
        UpdateWindow();
        StateHasChanged();
    }

    private void ResetView()
    {
        _zoom = 1;
        _center = .5;
        _selected = null;
        UpdateWindow();
        StateHasChanged();
    }
}
