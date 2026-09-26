using System.Globalization;
using System.Drawing.Drawing2D;
using BdoGrindTracker.App.Localization;
using System.Drawing.Imaging;
using System.Drawing.Text;
using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Overlay.Native;

internal sealed class NativeOverlayRenderer : IDisposable
{
    // A renderer belongs to one overlay window and is used on that window's UI thread.
    // Reset the palette on every frame so a live theme switch never retains old styling.
    private bool _blackDesert, _light, _cats;
    private NativeOverlayPalette? _palette;
    private int _backgroundAlpha;
    private string _language = "de";
    private string T(string value) => AppText.Translate(value, _language);
    private Color Gold => _palette?.Accent ?? (_light ? Color.FromArgb(54, 95, 145) : _cats ? Color.FromArgb(224, 185, 127) :
        _blackDesert ? Color.FromArgb(211, 182, 117) : Color.FromArgb(242, 199, 108));
    private Color Text => _palette?.Text ?? (_light ? Color.FromArgb(36, 50, 68) : _cats ? Color.FromArgb(245, 237, 223) :
        _blackDesert ? Color.FromArgb(230, 223, 205) : Color.FromArgb(237, 241, 245));
    private Color Muted => _palette?.Muted ?? (_light ? Color.FromArgb(82, 100, 120) : _cats ? Color.FromArgb(188, 175, 153) :
        _blackDesert ? Color.FromArgb(172, 166, 149) : Color.FromArgb(154, 175, 190));
    private Color Positive => _palette?.Positive ?? (_light ? Color.FromArgb(35, 117, 87) : _cats ? Color.FromArgb(170, 203, 170) :
        _blackDesert ? Color.FromArgb(164, 191, 131) : Color.FromArgb(125, 211, 181));
    private Color Warning => _palette?.Warning ?? (_light ? Color.FromArgb(133, 87, 33) :
        _blackDesert ? Color.FromArgb(228, 206, 145) : Gold);
    private Color SlotSurface => _palette?.Slot ?? (_light ? Color.White : _cats ? Color.FromArgb(29, 27, 25) : Color.FromArgb(32, 26, 39));
    private Color SlotEdge => _palette?.Edge ?? (_light ? Color.FromArgb(157, 172, 190) : _cats ? Color.FromArgb(133, 115, 92) : Color.FromArgb(119, 96, 121));
    private Color RareEdge => _palette?.Rare ?? (_light ? Color.FromArgb(148, 108, 39) : Gold);
    private Color Heading => _blackDesert ? Gold : Muted;
    private static readonly Color Brass = Color.FromArgb(96, 90, 73);
    private readonly Dictionary<string, Image?> _icons = new(StringComparer.OrdinalIgnoreCase);
    // Phase colours are a handful of fixed hex values; they are parsed once, not in every frame.
    private readonly Dictionary<string, Color> _htmlColors = new(StringComparer.OrdinalIgnoreCase);
    private Color Html(string value) => _htmlColors.TryGetValue(value, out var color) ? color
        : _htmlColors[value] = ColorTranslator.FromHtml(value);

    internal Bitmap Render(Size size, OverlaySettings settings, OverlaySnapshot snapshot,
        out IReadOnlyDictionary<string, RectangleF> actions, string title = "Grindcrest")
    {
        _language = snapshot.UiLanguage;
        var theme = AppThemes.Normalize(snapshot.ThemeId);
        _blackDesert = theme == AppThemes.BlackDesert;
        _light = theme is AppThemes.Light or AppThemes.Valencia;
        _cats = theme == AppThemes.Cats;
        _palette = NativeOverlayPalette.For(theme);
        var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var chrome = OverlayWindowChrome.For(snapshot.ThemeId, settings.ShowBorder);
        var scale = (float)Math.Min(size.Width / chrome.OuterWidth(settings.Width),
            size.Height / chrome.OuterHeight(settings.Height));
        graphics.ScaleTransform(scale, scale);
        var canvas = new RectangleF(0, 0, size.Width / scale, size.Height / scale);
        // Keep even a fully transparent background draggable. Alpha zero is a
        // hole in a layered window; alpha one remains visually transparent.
        var alpha = _backgroundAlpha = Math.Max(settings.Interaction == "move" ? 1 : 0,
            (int)Math.Round(settings.BackgroundOpacity * 255));
        if (_blackDesert)
        {
            FillRound(graphics, Color.FromArgb(alpha, 37, 37, 38), canvas, 1);
            if (settings.ShowBorder) DrawBevel(graphics, canvas, Brass, 170);
            if (chrome.HasTitleBar) DrawTitleBar(graphics, canvas, (float)chrome.Top, title, alpha);
        }
        else if (_palette is { } palette)
            FillRound(graphics, Color.FromArgb(alpha, palette.Surface), canvas, palette.Corner);
        else if (_light)
            FillRound(graphics, Color.FromArgb(alpha, 245, 246, 248), canvas, 10);
        else if (_cats)
        {
            FillRound(graphics, Color.FromArgb(alpha, 37, 34, 31), canvas, 10);
            DrawCatBackdrop(graphics, canvas, new RectangleF((float)chrome.Left, (float)chrome.Top,
                (float)(canvas.Width - chrome.Horizontal), (float)(canvas.Height - chrome.Vertical)), alpha);
            if (chrome.HasTitleBar) DrawCatTitleBar(graphics, canvas, (float)chrome.Top, title, alpha);
        }
        else
            FillRound(graphics, Color.FromArgb(alpha, 23, 29, 34), canvas, 10);
        if (!_blackDesert && settings.ShowBorder)
        {
            using var border = new Pen(Color.FromArgb(_cats || _palette is not null ? 135 * alpha / 255 : 135, Gold), 1);
            using var path = Round(new RectangleF(.5f, .5f, canvas.Width - 1, canvas.Height - 1), _palette?.Corner ?? 10);
            graphics.DrawPath(border, path);
        }
        var controls = new Dictionary<string, RectangleF>();
        var contentHitBounds = new RectangleF((float)chrome.Left * scale, (float)chrome.Top * scale,
            (float)(canvas.Width - chrome.Horizontal) * scale, (float)(canvas.Height - chrome.Vertical) * scale);
        var contentState = graphics.Save();
        graphics.TranslateTransform((float)chrome.Left, (float)chrome.Top);
        if (chrome.HasTitleBar)
            graphics.SetClip(new RectangleF(0, 0, (float)(canvas.Width - chrome.Horizontal),
                (float)(canvas.Height - chrome.Vertical)));
        foreach (var placedWidget in settings.Widgets)
        {
            var rectangle = new RectangleF((float)placedWidget.X, (float)placedWidget.Y, (float)placedWidget.Width, (float)placedWidget.Height);
            var widgetHitBounds = new RectangleF((rectangle.X + (float)chrome.Left) * scale,
                (rectangle.Y + (float)chrome.Top) * scale,
                rectangle.Width * scale, rectangle.Height * scale);
            controls["widget:" + placedWidget.Id] = chrome.HasTitleBar
                ? RectangleF.Intersect(widgetHitBounds, contentHitBounds) : widgetHitBounds;
            var state = graphics.Save();
            graphics.SetClip(rectangle, chrome.HasTitleBar ? CombineMode.Intersect : CombineMode.Replace);
            // Faint square edges divide the shared window without competing
            // with its title bar or the brighter inventory slots.
            if (_blackDesert)
            {
                using var fill = new SolidBrush(Color.FromArgb((int)(alpha * .14), 8, 9, 11));
                graphics.FillRectangle(fill, rectangle);
                if (rectangle.Width > 1 && rectangle.Height > 1)
                {
                    using var edge = new Pen(Color.FromArgb((int)(alpha * .2), 155, 146, 126), 1);
                    graphics.DrawRectangle(edge, rectangle.X + .5f, rectangle.Y + .5f, rectangle.Width - 1, rectangle.Height - 1);
                }
            }
            else if (_palette is { } widgetPalette)
            {
                FillRound(graphics, Color.FromArgb((int)(alpha * .65), widgetPalette.Panel), rectangle, widgetPalette.Corner);
                using var edge = new Pen(Color.FromArgb((int)(alpha * .3), SlotEdge), 1);
                using var path = Round(RectangleF.Inflate(rectangle, -.5f, -.5f), widgetPalette.Corner);
                graphics.DrawPath(edge, path);
            }
            else if (_light || _cats || _palette is not null)
            {
                var radius = _cats ? 10 : 8;
                FillRound(graphics, _light ? Color.FromArgb((int)(alpha * .65), Color.White) :
                    Color.FromArgb((int)(alpha * .6), 60, 53, 46), rectangle, radius);
                using var edge = new Pen(Color.FromArgb((int)(alpha * (_cats ? .35 : .18)), SlotEdge), 1);
                if (_cats) edge.DashPattern = [4, 3];
                using var path = Round(RectangleF.Inflate(rectangle, -.5f, -.5f), radius);
                graphics.DrawPath(edge, path);
            }
            else
                FillRound(graphics, Color.FromArgb((int)(alpha * .11), 182, 201, 213), rectangle, 6);
            if (!_blackDesert && snapshot.Metrics.TryGetValue(placedWidget.Kind, out var widgetMetric) && widgetMetric.IsWarning)
            {
                FillRound(graphics, Color.FromArgb(_light || _cats || _palette is not null ? (int)(alpha * .05) : 13, Warning), rectangle, _cats ? 10 : 6);
                using var warningBorder = new Pen(Color.FromArgb(_light || _cats || _palette is not null ? (int)(alpha * .44) : 112, Warning), 1);
                using var warningPath = Round(RectangleF.Inflate(rectangle, -.5f, -.5f), _cats ? 10 : 6);
                graphics.DrawPath(warningBorder, warningPath);
            }
            var content = OverlayContentLayout.Create(placedWidget, snapshot);
            var widget = content.LayoutWidget;
            graphics.TranslateTransform(rectangle.X, rectangle.Y);
            graphics.ScaleTransform((float)content.Scale, (float)content.Scale);
            var inner = RectangleF.Inflate(new RectangleF(0, 0, (float)widget.Width, (float)widget.Height), -10, -8);
            if (inner.Width <= 0 || inner.Height <= 0)
            {
                graphics.Restore(state);
                continue;
            }
            if (widget.Kind == "controls")
            {
                if (widget.ShowLabel)
                {
                    var fontScale = (float)widget.FontScale;
                    var labelInset = widget.ShowIcon ? 17 * fontScale : 0;
                    if (widget.ShowIcon) DrawGlyph(graphics, "status", new RectangleF(inner.X, inner.Y + fontScale, 12 * fontScale, 12 * fontScale));
                    Draw(graphics, "Tracking", new RectangleF(inner.X + labelInset, inner.Y, inner.Width - labelInset, 14 * fontScale), 10 * fontScale, Heading);
                    inner.Y += 18 * fontScale; inner.Height -= 18 * fontScale;
                }
                if (inner.Height <= 0)
                {
                    graphics.Restore(state);
                    continue;
                }
                var enabled = snapshot.CanToggleTracking && settings.Interaction != "passthrough";
                if (widget.ShowNewSession)
                {
                    var half = Math.Max(1, (inner.Height-4)/2);
                    var next = new RectangleF(inner.X, inner.Y+half+4, inner.Width, half);
                    var newEnabled = snapshot.CanNewSession && settings.Interaction != "passthrough";
                    FillRound(graphics, Color.FromArgb(newEnabled ? 24 : 12, Gold), next, 5);
                    Draw(graphics, T("Neue Session"), next, 12*(float)widget.FontScale,
                        newEnabled ? Gold : Color.FromArgb(130, Gold), true, StringAlignment.Center, StringAlignment.Center);
                    if (newEnabled)
                    {
                        PointF[] corners = [next.Location, new(next.Right,next.Bottom)];
                        using var transform = graphics.Transform;
                        transform.TransformPoints(corners);
                        var action = RectangleF.FromLTRB(corners[0].X,corners[0].Y,corners[1].X,corners[1].Y);
                        if (chrome.HasTitleBar) action = RectangleF.Intersect(action, contentHitBounds);
                        if (action.Width > 0 && action.Height > 0) controls["new-session:"+widget.Id] = action;
                    }
                    inner.Height = half;
                }
                var buttonHeight = Math.Min(inner.Height, 28 * (float)widget.FontScale);
                inner.Y += (inner.Height - buttonHeight) / 2;
                inner.Height = buttonHeight;
                if (_blackDesert)
                {
                    FillPanel(graphics, inner, Color.FromArgb(enabled ? 240 : 160, 46, 45, 39),
                        Color.FromArgb(enabled ? 240 : 160, 22, 23, 22));
                    DrawBevel(graphics, inner, Gold, enabled ? 165 : 75);
                }
                else
                {
                    var fillAlpha = _light || _cats || _palette is not null ? (enabled ? 24 : 12) * alpha / 255 : enabled ? 24 : 12;
                    var outlineAlpha = _light || _cats || _palette is not null ? (enabled ? 100 : 50) * alpha / 255 : enabled ? 100 : 50;
                    FillRound(graphics, Color.FromArgb(fillAlpha, Gold), inner, _cats ? 9 : 5);
                    using var outline = new Pen(Color.FromArgb(outlineAlpha, Gold), 1);
                    using var path = Round(inner, _cats ? 9 : 5);
                    graphics.DrawPath(outline, path);
                }
                var textBounds = inner;
                var trackingButtonLabel = T(snapshot.TrackingButtonLabel);
                var buttonColor = enabled ? Gold : Color.FromArgb(130, Gold);
                var buttonTextBounds = new RectangleF(0, 0, Math.Max(0, inner.Width - 8), Math.Max(0, inner.Height - 4));
                var buttonFontSize = FitTextSize(graphics, trackingButtonLabel, buttonTextBounds,
                    12 * (float)widget.FontScale, true, widget.ShowIcon ? 20 * (float)widget.FontScale : 0);
                if (widget.ShowIcon)
                {
                    using var labelFont = new Font("Segoe UI", buttonFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var measureFormat = TextFormat();
                    var textWidth = graphics.MeasureString(trackingButtonLabel, labelFont, 100000, measureFormat).Width + 2;
                    var glyphScale = buttonFontSize / 12;
                    var groupWidth = textWidth + 20 * glyphScale;
                    using var glyph = new SolidBrush(buttonColor);
                    var center = new PointF(inner.X + (inner.Width - groupWidth) / 2 + 5 * glyphScale, inner.Y + inner.Height / 2);
                    if (snapshot.IsRunning)
                    {
                        graphics.FillRectangle(glyph, center.X - 4 * glyphScale, center.Y - 5 * glyphScale, 3 * glyphScale, 10 * glyphScale);
                        graphics.FillRectangle(glyph, center.X + glyphScale, center.Y - 5 * glyphScale, 3 * glyphScale, 10 * glyphScale);
                    }
                    else graphics.FillPolygon(glyph, [new PointF(center.X - 3 * glyphScale, center.Y - 5 * glyphScale),
                        new(center.X + 5 * glyphScale, center.Y), new(center.X - 3 * glyphScale, center.Y + 5 * glyphScale)]);
                    textBounds.X = center.X + 15 * glyphScale; textBounds.Width = textWidth;
                }
                Draw(graphics, trackingButtonLabel, textBounds, buttonFontSize,
                    buttonColor, true, StringAlignment.Center, StringAlignment.Center);
                if (enabled)
                {
                    PointF[] corners = [inner.Location, new(inner.Right, inner.Bottom)];
                    using var transform = graphics.Transform;
                    transform.TransformPoints(corners);
                    var action = RectangleF.FromLTRB(corners[0].X, corners[0].Y, corners[1].X, corners[1].Y);
                    // The visible content ends before the title/border. Cropped
                    // controls must not remain clickable on that window chrome.
                    if (chrome.HasTitleBar) action = RectangleF.Intersect(action, contentHitBounds);
                    if (action.Width > 0 && action.Height > 0) controls["toggle-tracking:" + widget.Id] = action;
                }
            }
            else if (OverlayCatalog.IsLootWidget(widget.Kind) || widget.Kind == "consumables")
                DrawLoot(graphics, widget, inner, snapshot);
            else if (widget.Kind == "chart") DrawTimeline(graphics, widget, inner, snapshot);
            else if (widget.Kind == "clock") DrawClock(graphics, widget, inner, snapshot);
            else if (widget.Kind == "grind-rating" && snapshot.Metrics.GetValueOrDefault(widget.Kind)?.Spectrum is not null)
                DrawGrindRating(graphics, widget, inner, snapshot.Metrics[widget.Kind]);
            else if (widget.Kind == "rotation-monitor") DrawRotation(graphics, widget, inner, snapshot.Rotation);
            else if (widget.Kind == "daily-goal")
            {
                var goal = snapshot.DailyGoal;
                if (widget.ShowLabel)
                {
                    Draw(graphics,"Daily Goal",new RectangleF(inner.X,inner.Y,inner.Width,14),10,Muted);
                    inner.Y += 18; inner.Height -= 18;
                }
                Draw(graphics,goal.Value,new RectangleF(inner.X,inner.Y,inner.Width,Math.Max(1,inner.Height-42)),20*(float)widget.FontScale,Gold,true);
                var bar = new RectangleF(inner.X,inner.Bottom-38,inner.Width,22);
                var goalTrack = _palette?.GoalTrack ?? (_light ? Color.FromArgb(231, 237, 245) : _cats ? Color.FromArgb(69, 60, 48) :
                    _blackDesert ? Color.FromArgb(61, 57, 47) : Color.FromArgb(60, Gold));
                var goalFill = _palette?.GoalFill ?? (_light ? Color.FromArgb(178, 201, 227) : _cats ? Color.FromArgb(120, 96, 57) :
                    _blackDesert ? Color.FromArgb(101, 83, 49) : Color.FromArgb(128, 104, 54));
                FillRound(graphics,goalTrack,bar,3);
                if (goal.Fraction > 0) FillRound(graphics,goalFill,new RectangleF(bar.X,bar.Y,bar.Width*(float)goal.Fraction,bar.Height),3);
                Draw(graphics,goal.Percentage,bar,14*(float)widget.FontScale,Text,true,StringAlignment.Center,StringAlignment.Center);
                Draw(graphics,goal.Detail,new RectangleF(inner.X,inner.Bottom-13,inner.Width,13),10,Muted);
            }
            else DrawMetric(graphics, widget, inner, snapshot);
            graphics.Restore(state);
        }
        graphics.Restore(contentState);
        if (settings.Interaction == "move")
        {
            using var grip = new Pen(Color.FromArgb(130, Gold), 1);
            for (var i = 0; i < 3; i++)
                graphics.DrawLine(grip, canvas.Width - 5 - i * 4, canvas.Height - 5,
                    canvas.Width - 5, canvas.Height - 5 - i * 4);
        }
        actions = controls;
        return bitmap;
    }

    private void DrawClock(Graphics graphics, OverlayWidget widget, RectangleF inner, OverlaySnapshot snapshot)
    {
        var clock = OverlayClockPresentation.Create(widget, snapshot.ClockUtcNow);
        var fontScale = (float)widget.FontScale;
        if (widget.ShowLabel)
        {
            var inset = widget.ShowIcon ? 17 * fontScale : 0;
            if (widget.ShowIcon) DrawGlyph(graphics, "clock", new RectangleF(inner.X, inner.Y + fontScale, 12 * fontScale, 12 * fontScale));
            Draw(graphics, T(clock.Label), new RectangleF(inner.X + inset, inner.Y, inner.Width - inset, 15 * fontScale), 10 * fontScale, Heading);
            inner.Y += 18 * fontScale; inner.Height -= 18 * fontScale;
        }
        var rowHeight = 26 * fontScale;
        var top = inner.Y + Math.Max(0, (inner.Height - rowHeight * clock.Rows.Count) / 2);
        for (var index = 0; index < clock.Rows.Count; index++)
        {
            var row = clock.Rows[index];
            var bounds = new RectangleF(inner.X, top + index * rowHeight, inner.Width, rowHeight);
            if (index == 0 && !widget.ShowLabel && widget.ShowIcon)
            {
                DrawGlyph(graphics, "clock", new RectangleF(bounds.X, bounds.Y + 6 * fontScale, 14 * fontScale, 14 * fontScale));
                bounds.X += 20 * fontScale; bounds.Width -= 20 * fontScale;
            }
            var labelWidth = bounds.Width * .4f;
            Draw(graphics, T(row.Label), new RectangleF(bounds.X, bounds.Y, labelWidth, bounds.Height), 11 * fontScale, Muted,
                vertical: StringAlignment.Center);
            Draw(graphics, row.Value, new RectangleF(bounds.X + labelWidth + 6 * fontScale, bounds.Y,
                    Math.Max(0, bounds.Width - labelWidth - 6 * fontScale), bounds.Height), 18 * fontScale,
                row.Kind == "countdown" ? Gold : Text, true, StringAlignment.Far, StringAlignment.Center);
        }
    }

    private void DrawGrindRating(Graphics graphics, OverlayWidget widget, RectangleF inner, OverlayMetric metric)
    {
        var spectrum = metric.Spectrum!;
        var fontScale = (float)widget.FontScale;
        Color Tone(OverlayMetricTone tone) => tone switch
        {
            OverlayMetricTone.Positive => Positive,
            OverlayMetricTone.Accent => Gold,
            OverlayMetricTone.Muted => Muted,
            _ => Text,
        };
        if (widget.ShowLabel)
        {
            var inset = widget.ShowIcon ? 17 * fontScale : 0;
            if (widget.ShowIcon) DrawGlyph(graphics, "grind-rating", new RectangleF(inner.X, inner.Y + fontScale, 12 * fontScale, 12 * fontScale));
            Draw(graphics, T(metric.Label), new RectangleF(inner.X + inset, inner.Y, inner.Width - inset, 15 * fontScale),
                10 * fontScale, Heading);
            inner.Y += 18 * fontScale; inner.Height -= 18 * fontScale;
        }
        var headline = new RectangleF(inner.X, inner.Y, inner.Width, 20 * fontScale);
        if (!widget.ShowLabel && widget.ShowIcon)
        {
            DrawGlyph(graphics, "grind-rating", new RectangleF(headline.X, headline.Y + 3 * fontScale, 14 * fontScale, 14 * fontScale));
            headline.X += 20 * fontScale; headline.Width -= 20 * fontScale;
        }
        Draw(graphics, spectrum.ProgressLabel, headline, 16 * fontScale, Tone(metric.Tone), true, vertical: StringAlignment.Center);
        inner.Y += 20 * fontScale;

        var track = new RectangleF(inner.X, inner.Y + 6 * fontScale, inner.Width, 6 * fontScale);
        using (var gradient = new LinearGradientBrush(track, Muted, Tone(spectrum.Stops.LastOrDefault()?.Tone ?? metric.Tone), 0f))
        {
            var stops = spectrum.Stops.Where(stop => stop.Position > 0 && stop.Position < 100).ToArray();
            gradient.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Muted }.Concat(stops.Select(stop => Tone(stop.Tone))).Append(Tone(spectrum.Stops.LastOrDefault()?.Tone ?? metric.Tone)).ToArray(),
                Positions = new[] { 0f }.Concat(stops.Select(stop => (float)(stop.Position / 100))).Append(1f).ToArray(),
            };
            using var path = Round(track, 3 * fontScale);
            graphics.FillPath(gradient, path);
        }
        for (var index = 0; index < spectrum.Stops.Count; index++)
        {
            var stop = spectrum.Stops[index];
            var x = track.X + track.Width * (float)(stop.Position / 100);
            using var tick = new Pen(Color.FromArgb(180, Text), fontScale);
            graphics.DrawLine(tick, x, track.Top - 2 * fontScale, x, track.Bottom + 2 * fontScale);
            var halfLabel = track.Width / (spectrum.Stops.Count + 1) * .48f;
            Draw(graphics, stop.Label, new RectangleF(x - halfLabel, inner.Y + 16 * fontScale, halfLabel * 2, 11 * fontScale),
                8 * fontScale, Muted, horizontal: StringAlignment.Center);
        }
        var radius = 4.5f * fontScale;
        var markerX = track.X + Math.Clamp(track.Width * (float)(spectrum.Position / 100), radius, Math.Max(radius, track.Width - radius));
        using (var marker = new SolidBrush(Text))
        using (var edge = new Pen(SlotSurface, 1.5f * fontScale))
        {
            var dot = new RectangleF(markerX - radius, track.Top + track.Height / 2 - radius, radius * 2, radius * 2);
            graphics.FillEllipse(marker, dot);
            graphics.DrawEllipse(edge, dot);
        }
        inner.Y += 28 * fontScale;
        Draw(graphics, spectrum.GapLabel, new RectangleF(inner.X, inner.Y, inner.Width, 12 * fontScale), 9 * fontScale, Muted);
        inner.Y += 12 * fontScale;
        if (metric.Detail is { Length: > 0 })
            Draw(graphics, T(metric.Detail), new RectangleF(inner.X, inner.Y, inner.Width, 12 * fontScale), 9 * fontScale, Muted);
    }

    private void DrawMetric(Graphics graphics, OverlayWidget widget, RectangleF inner, OverlaySnapshot snapshot)
    {
        if (!snapshot.Metrics.TryGetValue(widget.Kind, out var metric)) return;
        var fontScale = (float)widget.FontScale;
        if (widget.ShowLabel)
        {
            var iconWidth = widget.ShowIcon ? 17 * fontScale : 0;
            if (widget.ShowIcon) DrawGlyph(graphics, widget.Kind, new RectangleF(inner.X, inner.Y + fontScale, 12 * fontScale, 12 * fontScale));
            Draw(graphics, T(metric.Label), new RectangleF(inner.X + iconWidth, inner.Y, inner.Width - iconWidth, 15 * fontScale),
                10 * fontScale, Heading);
            inner.Y += 18 * fontScale;
            inner.Height -= 18 * fontScale;
        }
        var font = widget.Kind is "spot" or "status" or "loot-scroll" or "grind-rating" ? 16 : 23;
        var color = metric.IsWarning ? Warning :
            widget.Kind is "silver" or "silver-hour" ? Gold : metric.Tone switch
        {
            OverlayMetricTone.Muted => Muted,
            OverlayMetricTone.Positive => Positive,
            OverlayMetricTone.Accent => Gold,
            _ => Text,
        };
        if ((widget.ShowLabel || widget.Kind == "experience") && widget.Kind != "status" && metric.Detail is { Length: > 0 })
        {
            var height = 12 * fontScale;
            Draw(graphics, T(metric.Detail), new RectangleF(inner.X, inner.Bottom - height, inner.Width, height), 9 * fontScale, Muted);
            inner.Height -= height;
        }
        if (!widget.ShowLabel && widget.ShowIcon)
        {
            var width = 21 * fontScale;
            DrawGlyph(graphics, widget.Kind, new RectangleF(inner.X, inner.Y + Math.Max(0, (inner.Height - 15 * fontScale) / 2),
                15 * fontScale, 15 * fontScale));
            inner.X += width; inner.Width -= width;
        }
        Draw(graphics, T(metric.Value), inner, Math.Min(font * fontScale, Math.Max(10, inner.Height - 2)), color, true,
            vertical: StringAlignment.Center);
    }

    private void DrawLoot(Graphics graphics, OverlayWidget widget, RectangleF inner, OverlaySnapshot snapshot)
    {
        var view = OverlayLootPresentation.Create(widget, snapshot);
        var fontScale = (float)view.FontScale;
        var consumables = widget.Kind == "consumables";
        var presentation = consumables ? "grid" : widget.Kind == "drop-item" ? "card" : widget.ItemView;
        if (widget.ShowLabel)
        {
            var inset = widget.ShowIcon ? 17 * fontScale : 0;
            var labelHeight = Math.Min((float)view.HeaderHeight, Math.Max(0, inner.Height - (float)view.FooterHeight));
            if (labelHeight >= 12 * fontScale)
            {
                if (widget.ShowIcon) DrawGlyph(graphics, "drops", new RectangleF(inner.X, inner.Y + fontScale, 12 * fontScale, 12 * fontScale));
                Draw(graphics, T(view.Label), new RectangleF(inner.X + inset, inner.Y, inner.Width - inset, labelHeight),
                    10 * fontScale, Heading);
                if (_blackDesert)
                {
                    using var separator = new Pen(Color.FromArgb(46, 157, 140, 109), fontScale);
                    var y = inner.Y + (float)view.HeaderHeight - 4 * fontScale;
                    graphics.DrawLine(separator, inner.X, y, inner.Right, y);
                }
            }
            inner.Y += (float)view.HeaderHeight;
            inner.Height -= (float)view.HeaderHeight;
        }
        if (consumables)
        {
            var footer = new RectangleF(inner.X, inner.Bottom - (float)view.FooterHeight, inner.Width, (float)view.FooterHeight);
            var inset = widget.ShowIcon ? 18 * fontScale : 0;
            if (widget.ShowIcon)
                DrawGlyph(graphics, "silver", new RectangleF(footer.X, footer.Y + 5 * fontScale, 13 * fontScale, 13 * fontScale));
            Draw(graphics, snapshot.Consumables.Cost, new RectangleF(footer.X + inset, footer.Y,
                    Math.Max(0, footer.Width - inset), footer.Height), 13 * fontScale,
                snapshot.Consumables.HasMissingPrices ? Warning : Gold, true, vertical: StringAlignment.Center);
            inner.Height -= (float)view.FooterHeight;
        }
        else if (view.HiddenCount > 0)
        {
            Draw(graphics, AppText.Format("+{0} weitere", _language, view.HiddenCount), new RectangleF(inner.X, inner.Bottom - (float)view.FooterHeight, inner.Width, (float)view.FooterHeight),
                9 * fontScale, Muted, horizontal: StringAlignment.Far, vertical: StringAlignment.Center);
            inner.Height -= (float)view.FooterHeight;
        }
        if (view.Items.Count == 0)
        {
            Draw(graphics, consumables ? T("Noch keine verbrauchten Items") : (widget.ItemFilter == "selected" || widget.Kind == "drop-item") && widget.ItemNames.Count == 0
                    ? T("Items im Editor auswählen") : T("Noch keine Drops"), inner,
                11 * fontScale, Muted, vertical: StringAlignment.Center);
            return;
        }
        var cellWidth = (float)view.CellWidth;
        var cellHeight = (float)view.CellHeight;
        var gap = (float)view.Gap;
        var itemFit = (float)Math.Min(1, view.ItemSize / Math.Max(1, widget.ItemSize));
        for (var index = 0; index < view.VisibleItems.Count; index++)
        {
            var cell = new RectangleF(inner.X + index % view.Columns * (cellWidth + gap),
                inner.Y + index / view.Columns * (cellHeight + gap), cellWidth, cellHeight);
            if (cell.Width <= 0 || cell.Height <= 0) continue;
            var item = view.VisibleItems[index];
            if (presentation == "list") DrawLootRow(graphics, item, cell, widget.ShowIcon, fontScale, itemFit);
            else if (presentation == "card") DrawLootCard(graphics, item, cell, widget.ShowIcon, fontScale, (float)view.ItemSize, itemFit);
            else DrawLootTile(graphics, item, cell, widget.ShowIcon, fontScale, itemFit);
        }
        var emptySlots = OverlayLootPresentation.EmptySlotCount(widget, snapshot, view);
        for (var index = view.VisibleItems.Count; index < view.VisibleItems.Count + emptySlots; index++)
        {
            var cell = new RectangleF(inner.X + index % view.Columns * (cellWidth + gap),
                inner.Y + index / view.Columns * (cellHeight + gap), cellWidth, cellHeight);
            FillRound(graphics, Color.FromArgb(184, 11, 12, 15), cell, .5f * itemFit);
            DrawInventoryBorder(graphics, cell, rare: false, thickness: itemFit, alpha: 107, empty: true);
        }
    }

    private void DrawLootTile(Graphics graphics, OverlayLootItem item, RectangleF cell, bool showIcon, float fontScale, float fit)
    {
        if (_blackDesert)
            FillRound(graphics, Color.FromArgb(235, 14, 14, 18), cell, .5f * fit);
        else if (_light || _cats || _palette is not null)
            FillRound(graphics, Color.FromArgb(235 * _backgroundAlpha / 255, SlotSurface), cell, (_cats ? 7 : 3) * fit);
        else
            FillRound(graphics, Color.FromArgb(170, 16, 21, 25), cell, 2 * fit);
        if (showIcon)
            DrawIcon(graphics, item, RectangleF.Inflate(cell, (_blackDesert ? -1 : -2) * fit,
                (_blackDesert ? -1 : -2) * fit), inventorySlot: false);
        else
            Draw(graphics, item.Name, new RectangleF(cell.X + 4 * fit, cell.Y + 3 * fit, cell.Width - 8 * fit, cell.Height * .55f),
                10 * fontScale, Text, vertical: StringAlignment.Center);
        var countHeight = Math.Min(cell.Height, Math.Max(17 * fit, 18 * fontScale));
        var countBounds = new RectangleF(cell.X, cell.Bottom - countHeight, cell.Width, countHeight);
        if (!_blackDesert)
        {
            var shadeColor = _palette?.Slot ?? (_light ? Color.White : _cats ? SlotSurface : Color.FromArgb(12, 16, 20));
            using var shade = new LinearGradientBrush(countBounds, Color.FromArgb(0, shadeColor),
                Color.FromArgb(_light || _cats || _palette is not null ? 230 * _backgroundAlpha / 255 : 230, shadeColor), LinearGradientMode.Vertical);
            // Edge antialiasing samples beyond the gradient. Mirror it so the
            // opaque bottom cannot wrap into the transparent top as a dark line.
            shade.WrapMode = WrapMode.TileFlipXY;
            graphics.FillRectangle(shade, countBounds);
        }
        var quantity = new RectangleF(cell.X + 3 * fit, countBounds.Y, cell.Width - 6 * fit, countHeight - fit);
        if (!_blackDesert && !_light)
        {
            var shadow = quantity; shadow.Offset(fit, fit);
            DrawQuantity(graphics, item.QuantityText, shadow, 12 * fontScale, Color.Black, StringAlignment.Far, StringAlignment.Far);
        }
        DrawQuantity(graphics, item.QuantityText, quantity, 12 * fontScale,
            _blackDesert ? Color.FromArgb(225, 225, 223) : Text, StringAlignment.Far, StringAlignment.Far);
        if (_blackDesert)
            DrawInventoryBorder(graphics, cell, item.IsRare, fit);
        else if (_light || _cats || _palette is not null)
            DrawSoftSlotBorder(graphics, cell, item.IsRare, fit);
        else
        {
            using var border = new Pen(Color.FromArgb(item.IsRare ? 190 : 100, item.IsRare ? Gold : Muted), fit);
            graphics.DrawRectangle(border, cell.X + .5f * fit, cell.Y + .5f * fit, cell.Width - fit, cell.Height - fit);
        }
    }

    private void DrawLootRow(Graphics graphics, OverlayLootItem item, RectangleF cell, bool showIcon, float fontScale, float fit)
    {
        var iconSize = Math.Max(0, Math.Min(cell.Height - 4 * fit, cell.Width / 3));
        var iconSpace = showIcon ? iconSize + 6 * fit : 0;
        if (showIcon && iconSize > 0)
            DrawIcon(graphics, item, new RectangleF(cell.X, cell.Y + (cell.Height - iconSize) / 2, iconSize, iconSize));
        var quantityWidth = Math.Min(Math.Max(0, cell.Width - iconSpace) * .4f, Math.Max(52 * fontScale, cell.Width * .25f));
        Draw(graphics, item.Name, new RectangleF(cell.X + iconSpace, cell.Y, cell.Width - iconSpace - quantityWidth - 4 * fit, cell.Height),
            11 * fontScale, Text, vertical: StringAlignment.Center);
        DrawQuantity(graphics, item.QuantityText, new RectangleF(cell.Right - quantityWidth, cell.Y, quantityWidth, cell.Height),
            12 * fontScale, item.IsRare ? (_light ? RareEdge : Gold) : Text, StringAlignment.Far, StringAlignment.Center);
        using var line = new Pen(Color.FromArgb(25, Muted), fit);
        graphics.DrawLine(line, cell.Left, cell.Bottom - .5f * fit, cell.Right, cell.Bottom - .5f * fit);
    }

    private void DrawLootCard(Graphics graphics, OverlayLootItem item, RectangleF cell, bool showIcon, float fontScale, float itemSize, float fit)
    {
        if (showIcon)
        {
            var size = Math.Min(itemSize, Math.Min(cell.Height, cell.Width * .35f));
            if (size > 0)
                DrawIcon(graphics, item, new RectangleF(cell.X, cell.Y + (cell.Height - size) / 2, size, size));
            cell.X += size + 12 * fit;
            cell.Width -= size + 12 * fit;
        }
        var nameHeight = Math.Min(cell.Height * .45f, 26 * fontScale);
        var quantityHeight = Math.Min(Math.Max(0, cell.Height - nameHeight), 36 * fontScale);
        var top = cell.Y + Math.Max(0, (cell.Height - nameHeight - quantityHeight) / 2);
        DrawQuantity(graphics, item.QuantityText, new RectangleF(cell.X, top, cell.Width, quantityHeight),
            Math.Min(30 * fontScale, quantityHeight), Gold, StringAlignment.Near, StringAlignment.Far);
        Draw(graphics, item.Name, new RectangleF(cell.X, top + quantityHeight, cell.Width, nameHeight),
            11 * fontScale, Muted);
    }

    private void DrawQuantity(Graphics graphics, string text, RectangleF bounds, float requestedSize, Color color,
        StringAlignment horizontal, StringAlignment vertical)
    {
        Draw(graphics, text, bounds, requestedSize, color, !_blackDesert, horizontal, vertical, lightOutline: _light);
    }

    private void DrawTimeline(Graphics graphics, OverlayWidget widget, RectangleF inner, OverlaySnapshot snapshot)
    {
        var fontScale = (float)widget.FontScale;
        var colors = NativeTimelinePalette.For(snapshot.ThemeId);
        var timeline = OverlaySessionTimeline.Create(widget, snapshot, inner.Width);
        if (widget.ShowLabel)
        {
            var inset = widget.ShowIcon ? 17 * fontScale : 0;
            if (widget.ShowIcon) DrawGlyph(graphics, "chart", new RectangleF(inner.X, inner.Y + fontScale, 12 * fontScale, 12 * fontScale));
            var header = new RectangleF(inner.X + inset, inner.Y, inner.Width - inset, 16 * fontScale);
            Draw(graphics, T("Session-Timeline"), header with { Width = header.Width * .6f }, 10 * fontScale, Heading);
            Draw(graphics, timeline.Caption, header with { X = header.X + header.Width * .6f, Width = header.Width * .4f },
                9 * fontScale, Muted, horizontal: StringAlignment.Far);
            inner.Y += (float)OverlaySessionTimeline.HeaderHeight * fontScale;
            inner.Height -= (float)OverlaySessionTimeline.HeaderHeight * fontScale;
        }
        if (snapshot.Metrics.GetValueOrDefault("chart")?.Detail is { Length: > 0 } detail)
        {
            Draw(graphics, T(detail), new RectangleF(inner.X, inner.Bottom - 13 * fontScale, inner.Width, 13 * fontScale), 9 * fontScale, Muted);
            inner.Height -= (float)OverlaySessionTimeline.DetailHeight * fontScale;
        }
        var tickHeight = (float)OverlaySessionTimeline.TickHeight * fontScale;
        var band = (float)timeline.BandHeight * fontScale;
        if (!timeline.HasData || inner.Height < tickHeight + band + 8)
        {
            Draw(graphics, T("Verlauf entsteht während des Grindens"), inner, 10, Muted, vertical: StringAlignment.Center);
            return;
        }
        var plot = RectangleF.FromLTRB(inner.Left, inner.Top + tickHeight, inner.Right, inner.Bottom - band);
        PointF At(double x, double height) => new(plot.X + (float)x * plot.Width,
            plot.Y + (float)OverlaySessionTimeline.Y(height) * plot.Height);

        using var grid = new Pen(Color.FromArgb(70, SlotEdge), 1);
        foreach (var tick in timeline.Ticks)
        {
            var x = plot.X + (float)tick.X * plot.Width;
            graphics.DrawLine(grid, x, inner.Top + 2 * fontScale, x, plot.Bottom);
            // Labels near the right edge sit left of their line, as on the session timeline.
            var right = tick.X > .85;
            Draw(graphics, tick.Label, new RectangleF(right ? x - 63 * fontScale : x + 3 * fontScale, inner.Top,
                60 * fontScale, tickHeight), 9 * fontScale, Muted, horizontal: right ? StringAlignment.Far : StringAlignment.Near);
        }
        if (timeline.Trash is { } trash)
        {
            using var bars = new SolidBrush(Color.FromArgb(158, Html(trash.Color)));
            foreach (var bar in timeline.TrashBars)
            {
                var top = At(bar.Left, bar.Height);
                graphics.FillRectangle(bars, top.X, top.Y, Math.Max(.8f, (float)(bar.Right - bar.Left) * plot.Width),
                    Math.Max(1, plot.Bottom - top.Y));
            }
        }
        if (timeline.SilverCurves.Count > 0)
        {
            using var area = new SolidBrush(Color.FromArgb(51, colors.Silver));
            using var curve = new Pen(colors.Silver, 1.75f) { LineJoin = LineJoin.Round };
            // One stretch between two pauses at a time: no line crosses a gap.
            foreach (var stretch in timeline.SilverCurves)
            {
                var points = stretch.Select(point => At(point.X, point.Height)).ToArray();
                if (points.Length == 0) continue;
                graphics.FillPolygon(area, [new PointF(points[0].X, plot.Bottom), .. points, new PointF(points[^1].X, plot.Bottom)]);
                if (points.Length > 1) graphics.DrawLines(curve, points);
            }
        }
        using (var axis = new Pen(Color.FromArgb(150, SlotEdge), 1))
            foreach (var piece in timeline.Baseline)
                graphics.DrawLine(axis, plot.X + (float)piece.Left * plot.Width, plot.Bottom, plot.X + (float)piece.Right * plot.Width, plot.Bottom);
        DrawTimelineGaps(graphics, timeline, RectangleF.FromLTRB(plot.Left, plot.Top, plot.Right, inner.Bottom), fontScale);
        using (var special = new SolidBrush(colors.Special))
            foreach (var x in timeline.SpecialEvents.Select(value => plot.X + (float)value * plot.Width))
                graphics.FillPolygon(special, [new PointF(x, plot.Bottom - 6 * fontScale), new(x + 3.5f * fontScale, plot.Bottom),
                    new(x, plot.Bottom + 6 * fontScale), new(x - 3.5f * fontScale, plot.Bottom)]);
        DrawTimelineMarkers(graphics, timeline, plot, colors, fontScale);
        if (timeline.ShowsRotations)
            DrawTimelineRotations(graphics, timeline, RectangleF.FromLTRB(plot.Left,
                plot.Bottom + (float)OverlaySessionTimeline.BandGap * fontScale, plot.Right, inner.Bottom), colors, fontScale);
    }

    /// <summary>A pause is a hatched gap of one width with a pause sign in its middle; its length is not drawn.</summary>
    private void DrawTimelineGaps(Graphics graphics, OverlaySessionTimeline timeline, RectangleF area, float fontScale)
    {
        if (timeline.Gaps.Count == 0) return;
        using var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(70, Muted), Color.Transparent);
        using var edge = new Pen(Color.FromArgb(110, SlotEdge), 1) { DashPattern = [2, 2] };
        using var sign = new SolidBrush(Muted);
        foreach (var gap in timeline.Gaps)
        {
            var bounds = RectangleF.FromLTRB(area.X + (float)gap.Left * area.Width, area.Top, area.X + (float)gap.Right * area.Width, area.Bottom);
            if (bounds.Width <= 0) continue;
            graphics.FillRectangle(hatch, bounds);
            graphics.DrawLine(edge, bounds.Left, bounds.Top, bounds.Left, bounds.Bottom);
            graphics.DrawLine(edge, bounds.Right, bounds.Top, bounds.Right, bounds.Bottom);
            var bar = Math.Min(2 * fontScale, bounds.Width / 5);
            var height = Math.Min(10 * fontScale, bounds.Height / 2);
            if (bar < .5f || height < 2) continue;
            var middle = bounds.Y + bounds.Height / 2 - height / 2;
            graphics.FillRectangle(sign, bounds.X + bounds.Width / 2 - bar * 1.75f, middle, bar, height);
            graphics.FillRectangle(sign, bounds.X + bounds.Width / 2 + bar * .75f, middle, bar, height);
        }
    }

    /// <summary>
    /// Each mark is a row of item icons above the tallest layer it covers, starting at its first drop, with a stem from
    /// there down to the baseline. An item dropped more than once carries the number of its drops.
    /// </summary>
    private void DrawTimelineMarkers(Graphics graphics, OverlaySessionTimeline timeline, RectangleF plot,
        NativeTimelinePalette colors, float fontScale)
    {
        var size = Math.Min((float)OverlaySessionTimeline.IconSize * fontScale, plot.Height);
        if (size <= 2) return;
        var gap = (float)OverlaySessionTimeline.IconGap * fontScale;
        using var stem = new Pen(Color.FromArgb(150, colors.Rare), 1.5f);
        using var edge = new Pen(colors.Rare, 1);
        foreach (var marker in timeline.Markers)
        {
            var x = plot.X + (float)marker.X * plot.Width;
            var row = (float)OverlaySessionTimeline.RowWidth(marker.Items.Count, size, gap);
            var left = Math.Clamp(x - size / 2, plot.Left, Math.Max(plot.Left, plot.Right - row));
            var peak = plot.Y + (float)OverlaySessionTimeline.Y(marker.Height) * plot.Height;
            var top = Math.Clamp(peak - size - 2 * fontScale, plot.Top, plot.Bottom - size);
            var stemX = Math.Clamp(x, plot.Left + size / 2, plot.Right - size / 2);
            graphics.DrawLine(stem, stemX, top + size, stemX, plot.Bottom);
            for (var index = 0; index < marker.Items.Count; index++)
            {
                var item = marker.Items[index];
                var bounds = new RectangleF(left + index * (size + gap), top, size, size);
                // Grindcrest paints the icon's slot itself (--timeline-raised); the themes bring their own inventory slot.
                if (!_blackDesert && !_light && !_cats && _palette is null)
                    FillRound(graphics, Color.FromArgb(255, 37, 45, 51), bounds, 3);
                DrawIcon(graphics, item.Item, bounds);
                graphics.DrawRectangle(edge, bounds.X + .5f, bounds.Y + .5f, bounds.Width - 1, bounds.Height - 1);
                if (item.Drops.Count < 2) continue;
                var badge = new RectangleF(bounds.Right - 7 * fontScale, bounds.Bottom - 7 * fontScale, 12 * fontScale, 11 * fontScale);
                FillRound(graphics, Color.FromArgb(235, SlotSurface), badge, 5 * fontScale);
                Draw(graphics, item.Drops.Count.ToString(CultureInfo.InvariantCulture), badge, 8 * fontScale, Text, true,
                    StringAlignment.Center, StringAlignment.Center);
            }
        }
    }

    /// <summary>
    /// Simplified, a rotation is one bar in its state's colour with its AFK phases hatched; otherwise every phase in
    /// its own colour above a rail in the state's colour, as on the session timeline.
    /// </summary>
    private void DrawTimelineRotations(Graphics graphics, OverlaySessionTimeline timeline, RectangleF band,
        NativeTimelinePalette colors, float fontScale)
    {
        if (band.Height <= 1) return;
        float X(double share) => band.X + (float)share * band.Width;
        // One set of brushes per rotation state and a single reused fill, however many rotations are visible.
        var styles = new Dictionary<string, (Color State, SolidBrush Body, HatchBrush Afk)>(StringComparer.Ordinal);
        using var fill = new SolidBrush(Color.Empty);
        using var outline = new Pen(colors.Rare, 1.5f) { DashPattern = [3, 2] };
        try
        {
            foreach (var rotation in timeline.Rotations)
            {
                if (!styles.TryGetValue(rotation.Status, out var style))
                {
                    var color = rotation.Status switch
                    {
                        "active" => colors.Active, "failed" => colors.Failed, "fastest" => colors.Fastest,
                        "complete" => colors.Complete, _ => Muted,
                    };
                    styles[rotation.Status] = style = (color, new SolidBrush(Color.FromArgb(120, color)),
                        new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(120, color), Color.FromArgb(28, color)));
                }
                var bounds = RectangleF.FromLTRB(X(rotation.Left) + 1, band.Top, Math.Max(X(rotation.Left) + 2, X(rotation.Right) - 1), band.Bottom);
                var clip = graphics.Save();
                graphics.SetClip(bounds, CombineMode.Intersect);
                if (timeline.IsSimplified)
                {
                    foreach (var part in rotation.Phases)
                    {
                        var segment = RectangleF.FromLTRB(X(part.Left), band.Top, X(part.Right), band.Bottom);
                        graphics.FillRectangle(part.IsAfk ? style.Afk : style.Body, segment);
                        if (part.IsAfk && segment.Width >= 26 * fontScale)
                            Draw(graphics, "AFK", segment, 8 * fontScale, Text, true, StringAlignment.Center, StringAlignment.Center);
                    }
                    var mechanics = rotation.Phases.FirstOrDefault(part => !part.IsAfk);
                    var label = mechanics is null ? bounds : RectangleF.FromLTRB(X(mechanics.Left), band.Top, X(mechanics.Right), band.Bottom);
                    if (label.Width >= 32 * fontScale)
                        Draw(graphics, RotationPhases.Duration(rotation.Span.Duration), RectangleF.Inflate(label, -3 * fontScale, 0),
                            9 * fontScale, Text, true, vertical: StringAlignment.Center);
                }
                else
                {
                    var rail = Math.Min(3 * fontScale, band.Height / 3);
                    foreach (var part in rotation.Phases)
                    {
                        var segment = RectangleF.FromLTRB(X(part.Left), band.Top, Math.Max(X(part.Left) + .6f, X(part.Right) - .5f), band.Bottom - rail - 1);
                        fill.Color = Color.FromArgb(230, Html(part.Color));
                        graphics.FillRectangle(fill, segment);
                        if (part.IsSpecial)
                            graphics.DrawRectangle(outline, segment.X + .75f, segment.Y + .75f, segment.Width - 1.5f, segment.Height - 1.5f);
                        // The rail runs under each phase, so it breaks at a pause's gap as well.
                        fill.Color = style.State;
                        graphics.FillRectangle(fill, X(part.Left), band.Bottom - rail, X(part.Right) - X(part.Left), rail);
                    }
                }
                graphics.Restore(clip);
            }
        }
        finally
        {
            foreach (var style in styles.Values)
            {
                style.Body.Dispose();
                style.Afk.Dispose();
            }
        }
    }

    private void DrawRotation(Graphics graphics, OverlayWidget widget, RectangleF inner, RotationMonitorSnapshot rotation)
    {
        if (rotation.Error is not null)
        {
            Draw(graphics, T(rotation.Error), new RectangleF(inner.X, inner.Y, inner.Width, 16), 10, Muted);
            inner.Y += 18; inner.Height = Math.Max(1, inner.Height - 18);
        }
        if (RotationTimelinePresentation.ShowSetup(rotation))
        {
            Draw(graphics, RotationTimelinePresentation.SetupCount(rotation),
                new RectangleF(inner.X, inner.Y + inner.Height * .15f, inner.Width, inner.Height * .35f),
                20 * (float)widget.FontScale, Gold, true, StringAlignment.Center, StringAlignment.Center);
            Draw(graphics, T(RotationTimelinePresentation.SetupHint),
                new RectangleF(inner.X, inner.Y + inner.Height * .5f, inner.Width, inner.Height * .4f),
                12 * (float)widget.FontScale, Muted, false, StringAlignment.Center, StringAlignment.Center);
            return;
        }
        var mode = widget.RotationComparison;
        if (mode == "sectors")
        {
            var fontScale = (float)widget.FontScale;
            Draw(graphics, RotationTimelinePresentation.Sector(rotation, _language),
                new RectangleF(inner.X, inner.Bottom - 13 * fontScale, inner.Width, 13 * fontScale),
                10 * fontScale, Muted);
            inner.Height -= 18 * fontScale;
        }
        var reference = RotationTimelinePresentation.Reference(rotation, mode);
        var extent = RotationTimelinePresentation.Extent(rotation, mode);
        var totalWidth = Math.Min(inner.Width * .25f, 56 * (float)widget.FontScale);
        var graph = new RectangleF(inner.X + 2, inner.Y + 4, Math.Max(1, inner.Width - 4 - totalWidth), Math.Max(1, inner.Height - 8));
        float X(double seconds) => graph.Left + (float)Math.Clamp(seconds / extent, 0, 1) * graph.Width;
        using var baseline = new Pen(Color.FromArgb(150, Muted), 1);
        var bandHeight = Math.Max(4, graph.Height * .28f);
        foreach (var group in RotationPhases.Create(rotation.SpotId, reference?.Events ?? rotation.Events,
                     reference?.Duration ?? rotation.Elapsed, widget.RotationColors).GroupBy(p => p.Group))
        {
            using var background = new SolidBrush(Color.FromArgb(45, ColorTranslator.FromHtml(group.First().Color)));
            using var accent = new SolidBrush(ColorTranslator.FromHtml(group.First().GroupColor));
            var width = Math.Max(0, X(group.Last().End)-X(group.First().Start)-2);
            graphics.FillRectangle(background, X(group.First().Start), graph.Top+2, width, graph.Height-2);
            graphics.FillRectangle(accent, X(group.First().Start), graph.Top+2, width, 2);
            // Short phases have no room for their own duration; the group keeps a readable total.
            var headroom = graph.Height * .25f - bandHeight / 2 - 4;
            if (group.Count() > 1 && width >= 40 && headroom >= 8)
                Draw(graphics, RotationPhases.Duration(group.Last().End-group.First().Start),
                    new RectangleF(X(group.First().Start), graph.Top+4, width-2, headroom),
                    Math.Clamp(headroom*.8f, 9, 14), ColorTranslator.FromHtml(group.First().GroupColor), false,
                    StringAlignment.Far, StringAlignment.Center);
        }
        var current = RotationCurrentRow.Create(rotation, reference, widget.RotationColors);
        for (var row = 0; row < 2; row++)
        {
            var events = row == 0 ? reference?.Events ?? [] : current.Events;
            var y = graph.Top + graph.Height * (row == 0 ? .25f : .78f);
            graphics.DrawLine(baseline, graph.Left, y, graph.Right, y);
            var end = row == 0 ? reference?.Duration ?? 0 : current.End;
            Draw(graphics, events.Count > 0 ? RotationPhases.Duration(end) : "–",
                new RectangleF(graph.Right+4, y-bandHeight/2, Math.Max(1,totalWidth-4), bandHeight),
                Math.Clamp(bandHeight*.45f,11,18), Text, false, StringAlignment.Far, StringAlignment.Center);
            if (row == 1 && current.HasTrackingError)
            {
                // Everything before the first certain phase of a rotation picked up mid-way.
                var error = ColorTranslator.FromHtml(RotationCurrentRow.TrackingErrorColor);
                var bounds = new RectangleF(graph.Left, y-bandHeight/2, Math.Max(0, X(current.ErrorEnd)-graph.Left-2), bandHeight);
                using var fill = new SolidBrush(Color.FromArgb(55, error));
                using var outline = new Pen(error, 1.5f) { DashPattern = [3, 2] };
                graphics.FillRectangle(fill, bounds);
                if (bounds.Width >= 2) graphics.DrawRectangle(outline, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                if (bounds.Width >= 90 && bandHeight >= 12)
                    Draw(graphics, T("Tracking-Fehler"), bounds, Math.Clamp(bandHeight*.4f, 10, 16), error, false,
                        StringAlignment.Center, StringAlignment.Center);
            }
            var phases = row == 0 ? RotationPhases.Create(rotation.SpotId, events, end, widget.RotationColors) : current.Phases;
            foreach (var phase in phases)
            {
                using var fill = new SolidBrush(Color.FromArgb(190, ColorTranslator.FromHtml(phase.Color)));
                var width = Math.Max(0, X(phase.End)-X(phase.Start)-2);
                var bounds = new RectangleF(X(phase.Start), y-bandHeight/2, width, bandHeight);
                graphics.FillRectangle(fill, bounds);
                if (phase.Special && width >= 2)
                {
                    using var special = new Pen(ColorTranslator.FromHtml(RotationPhases.SpecialColor), 1.5f) { DashPattern = [3, 2] };
                    graphics.DrawRectangle(special, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                }
                if (width >= 28 && bandHeight >= 12)
                    Draw(graphics, RotationPhases.Duration(phase.End-phase.Start), bounds,
                        Math.Clamp(bandHeight*.45f, 11, 18), Color.White, false, StringAlignment.Center, StringAlignment.Center,
                        darkOutline: _light);
            }
            foreach (var gap in row == 1 ? current.Gaps : [])
            {
                // Required phases that were never seen, drawn over the phase they fell into.
                var error = ColorTranslator.FromHtml(RotationCurrentRow.TrackingErrorColor);
                var bounds = new RectangleF(X(gap.Start), y-bandHeight/2, Math.Max(0, X(gap.End)-X(gap.Start)-2), bandHeight);
                using var fill = new SolidBrush(Color.FromArgb(55, error));
                using var outline = new Pen(error, 1.5f) { DashPattern = [3, 2] };
                graphics.FillRectangle(fill, bounds);
                if (bounds.Width >= 2) graphics.DrawRectangle(outline, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
            foreach (var e in events.Where(e => e.Seconds <= end && RotationTimelinePresentation.IsMarker(e)))
            {
                using var pen = new Pen(ColorTranslator.FromHtml(RotationPhases.MarkerStroke(rotation.SpotId, e, widget.RotationColors, row == 1)), 2);
                graphics.DrawLine(pen, X(e.Seconds), y-bandHeight/2-4, X(e.Seconds), y-bandHeight/2+3);

            }
        }
        if (rotation.Synchronized || rotation.Events.Count > 0)
        {
            using var playhead = new Pen(Text, 2);
            graphics.DrawLine(playhead, X(current.End), graph.Top, X(current.End), graph.Bottom);
        }

    }

    private void DrawIcon(Graphics graphics, OverlayLootItem item, RectangleF rectangle, bool inventorySlot = true)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        var imageBounds = rectangle;
        if (_blackDesert && inventorySlot)
        {
            FillRound(graphics, Color.FromArgb(235, 14, 14, 18), rectangle, .5f);
            imageBounds = RectangleF.Inflate(rectangle, -1, -1);
        }
        else if ((_light || _cats || _palette is not null) && inventorySlot)
        {
            FillRound(graphics, Color.FromArgb(235 * _backgroundAlpha / 255, SlotSurface), rectangle, _cats ? 6 : 3);
            imageBounds = RectangleF.Inflate(rectangle, -2, -2);
        }
        var icon = LoadIcon(item.IconPath);
        if (imageBounds.Width > 0 && imageBounds.Height > 0)
        {
            if (icon is not null) graphics.DrawImage(icon, imageBounds);
            else Draw(graphics, item.Name.Length > 0 ? item.Name[..1] : "·", imageBounds, 18, item.IsRare ? Gold : Muted,
                true, StringAlignment.Center, StringAlignment.Center);
        }
        if (_blackDesert && inventorySlot)
            DrawInventoryBorder(graphics, rectangle, item.IsRare);
        else if ((_light || _cats || _palette is not null) && inventorySlot)
            DrawSoftSlotBorder(graphics, rectangle, item.IsRare);
    }

    private Image? LoadIcon(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        if (_icons.TryGetValue(relative, out var cached)) return cached;
        // Metric icon paths refer only to packaged catalog assets, never URLs.
        Image? image = null;
        try
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets"));
            var suffix = relative.Replace('\\', '/');
            if (suffix.StartsWith("assets/", StringComparison.Ordinal)) suffix = suffix[7..];
            var path = Path.GetFullPath(Path.Combine(root, suffix));
            if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                using var source = Image.FromFile(path);
                image = new Bitmap(source);
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException) { }
        _icons[relative] = image;
        return image;
    }

    private void Draw(Graphics graphics, string text, RectangleF bounds, float size, Color color,
        bool bold = false, StringAlignment horizontal = StringAlignment.Near, StringAlignment vertical = StringAlignment.Near,
        string fontFamily = "Segoe UI", bool lightOutline = false, bool darkOutline = false)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || string.IsNullOrEmpty(text)) return;
        using var font = new Font(fontFamily, FitTextSize(graphics, text, bounds, size, bold, fontFamily: fontFamily),
            bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = TextFormat(horizontal, vertical);
        if (lightOutline || darkOutline)
        {
            // Keep dark Light-theme quantities readable over the item's own
            // artwork even when the user makes the slot background transparent.
            using var halo = new SolidBrush(Color.FromArgb(225 * color.A / 255,
                darkOutline ? Color.FromArgb(24, 33, 43) : Color.White));
            foreach (var offset in new PointF[] { new(-.75f, 0), new(.75f, 0), new(0, -.75f), new(0, .75f) })
            {
                var haloBounds = bounds;
                haloBounds.Offset(offset);
                graphics.DrawString(text, font, halo, haloBounds, format);
            }
        }
        if (_blackDesert)
        {
            // Small dark offset is the game's high-contrast HUD lettering treatment.
            var shadowBounds = bounds;
            shadowBounds.Offset(.75f, .75f);
            using var shadow = new SolidBrush(Color.FromArgb(200 * color.A / 255, 0, 0, 0));
            graphics.DrawString(text, font, shadow, shadowBounds, format);
        }
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static StringFormat TextFormat(StringAlignment horizontal = StringAlignment.Near,
        StringAlignment vertical = StringAlignment.Near) => new(StringFormat.GenericTypographic)
    {
        Trimming = StringTrimming.None, FormatFlags = StringFormatFlags.NoWrap,
        Alignment = horizontal, LineAlignment = vertical
    };

    private static float FitTextSize(Graphics graphics, string text, RectangleF bounds, float requestedSize,
        bool bold, float additionalWidth = 0, string fontFamily = "Segoe UI")
    {
        var size = Math.Max(.1f, requestedSize);
        using var font = new Font(fontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = TextFormat();
        var measured = graphics.MeasureString(text, font, 100000, format);
        var fit = Math.Min(1, Math.Min(Math.Max(.1f, bounds.Width - 2) / Math.Max(.1f, measured.Width + additionalWidth),
            Math.Max(.1f, bounds.Height - 1) / Math.Max(.1f, measured.Height)));
        return Math.Max(.1f, size * fit);
    }

    private void DrawGlyph(Graphics graphics, string kind, RectangleF bounds)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 16, bounds.Height / 16);
        using var pen = new Pen(Gold, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (kind is "silver-hour" or "trash-hour" or "chart" or "grind-rating" or "experience" or "rotations-hour" or "special-events-hour")
        {
            graphics.DrawLines(pen, [new PointF(1, 12), new(6, 7), new(9, 9), new(14, 3)]);
            graphics.DrawLines(pen, [new PointF(10, 3), new(14, 3), new(14, 7)]);
        }
        else if (kind is "trash" or "drops" or "rare-drops" or "loot-scroll")
        {
            graphics.DrawPolygon(pen, [new PointF(8, 1), new(14, 4), new(14, 12), new(8, 15), new(2, 12), new(2, 4)]);
            graphics.DrawLines(pen, [new PointF(2, 4), new(8, 7), new(14, 4)]);
            graphics.DrawLine(pen, 8, 7, 8, 15);
        }
        else if (kind == "spot")
        {
            graphics.DrawEllipse(pen, 4, 1, 8, 8);
            graphics.DrawLines(pen, [new PointF(4, 7), new(8, 15), new(12, 7)]);
            graphics.DrawEllipse(pen, 7, 4, 2, 2);
        }
        else if (kind == "special-events")
            graphics.DrawPolygon(pen, [new PointF(8, 1), new(10, 6), new(15, 6), new(11, 9.5f), new(12.5f, 15), new(8, 11.5f),
                new(3.5f, 15), new(5, 9.5f), new(1, 6), new(6, 6)]);
        else if (kind == "status")
            graphics.DrawLines(pen, [new PointF(1, 8), new(4, 8), new(6, 2), new(9, 14), new(11, 8), new(15, 8)]);
        else
        {
            graphics.DrawEllipse(pen, 1, 1, 14, 14);
            if (kind is "duration" or "clock" or "rotation-count") graphics.DrawLines(pen, [new PointF(8, 4), new(8, 8), new(11, 9)]);
            else graphics.DrawEllipse(pen, 5, 3, 6, 10);
        }
        graphics.Restore(state);
    }

    private void DrawTitleBar(Graphics graphics, RectangleF canvas, float height, string title, int alpha)
    {
        var header = new RectangleF(2, 2, Math.Max(0, canvas.Width - 4), Math.Max(0, height - 2));
        FillPanel(graphics, header, Color.FromArgb(alpha, 128, 102, 66), Color.FromArgb(alpha, 103, 81, 54));
        using var rim = new Pen(Color.FromArgb(184, 202, 181, 142), 1);
        graphics.DrawLine(rim, 2, 2.5f, canvas.Right - 2, 2.5f);
        var state = graphics.Save();
        graphics.SetClip(header);
        using (var motif = new Pen(Color.FromArgb((int)(alpha * .14), 220, 207, 183), 3))
            for (var index = 0; index < 4; index++)
            {
                var right = canvas.Right - 10 - index * 15;
                graphics.DrawLines(motif, [new PointF(right, -2), new(right - 17, height / 2), new(right, height + 2)]);
            }
        graphics.Restore(state);
        using var separator = new Pen(Color.FromArgb(210, 51, 45, 38), 1);
        graphics.DrawLine(separator, 2, height - .5f, canvas.Right - 2, height - .5f);
        DrawTitleText(graphics, title, new RectangleF(12, 3, Math.Max(0, canvas.Width - 24), height - 5));
    }

    private void DrawTitleText(Graphics graphics, string title, RectangleF bounds)
    {
        if (string.IsNullOrWhiteSpace(title) || bounds.Width <= 0 || bounds.Height <= 0) return;
        using var font = new Font("Georgia", FitTextSize(graphics, title, bounds, 14, false, fontFamily: "Georgia"),
            FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = TextFormat(vertical: StringAlignment.Center);
        // Rasterize the actual glyph outlines at the final window scale. Small
        // Georgia DrawString glyphs are either grid-fitted and jagged or softened
        // by text antialiasing; the path keeps their fine contours and counters.
        using var glyphs = new GraphicsPath();
        glyphs.AddString(title, font.FontFamily, (int)font.Style, font.Size, bounds, format);
        var state = graphics.Save();
        graphics.TranslateTransform(.75f, .75f);
        using var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        graphics.FillPath(shadow, glyphs);
        graphics.Restore(state);
        using var brush = new SolidBrush(Text);
        graphics.FillPath(brush, glyphs);
    }

    private void DrawCatBackdrop(Graphics graphics, RectangleF canvas, RectangleF content, int alpha)
    {
        if (alpha <= 0 || content.Width <= 0 || content.Height <= 0) return;
        var state = graphics.Save();
        using var outline = Round(canvas, 10);
        graphics.SetClip(outline);
        graphics.SetClip(content, CombineMode.Intersect);

        // The same packaged illustration and contain sizing are used by the
        // browser overlay. LoadIcon caches the decoded image for this window.
        var kittens = LoadIcon("assets/themes/cats/kitten-lounge.png");
        if (kittens is not null)
        {
            var scale = Math.Min(Math.Min(content.Width * .76f, 440) / kittens.Width,
                content.Height * .82f / kittens.Height);
            var width = kittens.Width * scale;
            var height = kittens.Height * scale;
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = .72f * alpha / 255 });
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(kittens, Rectangle.Round(new RectangleF(content.Right - width, content.Bottom - height,
                width, height)), 0, 0, kittens.Width, kittens.Height, GraphicsUnit.Pixel, attributes);
        }
        DrawCatPaw(graphics, new(content.Left + 16, content.Bottom - 40, 18, 18), -20, (int)(alpha * .13));
        DrawCatPaw(graphics, new(content.Left + 43, content.Bottom - 58, 14, 14), 20, (int)(alpha * .10));
        DrawCatPaw(graphics, new(content.Left + 68, content.Bottom - 78, 12, 12), -20, (int)(alpha * .08));
        graphics.Restore(state);
    }

    private void DrawCatPaw(Graphics graphics, RectangleF bounds, float angle, int alpha)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        graphics.RotateTransform(angle);
        graphics.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        graphics.TranslateTransform(-12, -12);
        using var paw = new SolidBrush(Color.FromArgb(alpha, Gold));
        graphics.FillEllipse(paw, 7, 12, 11, 8);
        graphics.FillEllipse(paw, 4, 7, 4, 5);
        graphics.FillEllipse(paw, 9, 4, 4, 5);
        graphics.FillEllipse(paw, 14, 5, 4, 5);
        graphics.FillEllipse(paw, 18, 8, 4, 5);
        graphics.Restore(state);
    }

    private void DrawCatTitleBar(Graphics graphics, RectangleF canvas, float height, string title, int alpha)
    {
        var header = new RectangleF(2, 2, Math.Max(0, canvas.Width - 4), Math.Max(0, height - 2));
        using (var path = Round(header, 8))
        using (var fill = new LinearGradientBrush(header, Color.FromArgb(alpha, 75, 61, 48),
                   Color.FromArgb(alpha, 52, 46, 39), LinearGradientMode.Vertical))
            graphics.FillPath(fill, path);
        using var separator = new Pen(Color.FromArgb((int)(alpha * .38), Gold), 1);
        graphics.DrawLine(separator, 10, height - .5f, canvas.Right - 10, height - .5f);
        if (canvas.Width > 54)
        {
            using var stitch = new Pen(Color.FromArgb((int)(alpha * .38), Gold), 1) { DashPattern = [4, 3] };
            graphics.DrawLine(stitch, 42, 5.5f, canvas.Right - 12, 5.5f);
            graphics.DrawLine(stitch, 42, height - 4.5f, canvas.Right - 12, height - 4.5f);
        }
        DrawCatFace(graphics, new RectangleF(10, 5, 24, 24));
        Draw(graphics, title, new RectangleF(42, 3, Math.Max(0, canvas.Width - 78), height - 5),
            13, Text, true, vertical: StringAlignment.Center);
        DrawCatPaw(graphics, new RectangleF(canvas.Right - 34, 3, 26, 26), 14, (int)(alpha * .39));
    }

    private void DrawCatFace(Graphics graphics, RectangleF bounds)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        using var outline = new Pen(Color.FromArgb(_backgroundAlpha, Gold), 1.3f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var face = new GraphicsPath();
        face.AddLines([new PointF(4, 9), new(3, 2), new(9, 6), new(15, 6), new(21, 2), new(20, 9)]);
        face.AddBezier(new PointF(20, 9), new PointF(24, 15), new PointF(19, 21), new PointF(12, 21));
        face.AddBezier(new PointF(12, 21), new PointF(5, 21), new PointF(0, 15), new PointF(4, 9));
        face.CloseFigure();
        using var tint = new SolidBrush(Color.FromArgb(22 * _backgroundAlpha / 255, Gold));
        graphics.FillPath(tint, face);
        graphics.DrawPath(outline, face);
        using var detail = new SolidBrush(Color.FromArgb(_backgroundAlpha, Gold));
        graphics.FillEllipse(detail, 7, 11, 2, 3);
        graphics.FillEllipse(detail, 15, 11, 2, 3);
        graphics.FillPolygon(detail, [new PointF(10.5f, 15), new(13.5f, 15), new(12, 17)]);
        graphics.DrawLine(outline, 2, 14, 6, 15);
        graphics.DrawLine(outline, 2, 18, 6, 17);
        graphics.DrawLine(outline, 18, 15, 22, 14);
        graphics.DrawLine(outline, 18, 17, 22, 18);
        graphics.Restore(state);
    }

    private void DrawSoftSlotBorder(Graphics graphics, RectangleF rectangle, bool rare, float thickness = 1)
    {
        if (rectangle.Width <= thickness || rectangle.Height <= thickness) return;
        using var edge = new Pen(Color.FromArgb((rare ? 220 : 145) * _backgroundAlpha / 255,
            rare ? RareEdge : SlotEdge), thickness);
        using var path = Round(RectangleF.Inflate(rectangle, -thickness * .5f, -thickness * .5f),
            (_cats ? 7 : 3) * thickness);
        graphics.DrawPath(edge, path);
    }

    private static void DrawInventoryBorder(Graphics graphics, RectangleF rectangle, bool rare, float thickness = 1,
        int alpha = 210, bool empty = false)
    {
        if (rectangle.Width <= thickness || rectangle.Height <= thickness) return;
        var color = empty ? Color.FromArgb(112, 111, 116) :
            rare ? Color.FromArgb(194, 166, 107) : Color.FromArgb(166, 175, 194);
        using var edge = new Pen(Color.FromArgb(alpha, color), thickness);
        graphics.DrawRectangle(edge, rectangle.X + thickness * .5f, rectangle.Y + thickness * .5f,
            rectangle.Width - thickness, rectangle.Height - thickness);
    }

    private static void FillPanel(Graphics graphics, RectangleF rectangle, Color top, Color bottom)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        using var fill = new LinearGradientBrush(rectangle, top, bottom, LinearGradientMode.Vertical);
        graphics.FillRectangle(fill, rectangle);
    }

    private static void DrawBevel(Graphics graphics, RectangleF rectangle, Color border, int alpha,
        float thickness = 1, bool inset = false)
    {
        if (rectangle.Width <= thickness * 3 || rectangle.Height <= thickness * 3 || alpha <= 0) return;
        var outer = RectangleF.Inflate(rectangle, -thickness * .5f, -thickness * .5f);
        using var outline = new Pen(Color.FromArgb(alpha, border), thickness);
        graphics.DrawRectangle(outline, outer.X, outer.Y, outer.Width, outer.Height);
        var inner = RectangleF.Inflate(outer, -thickness, -thickness);
        using var light = new Pen(Color.FromArgb((int)(alpha * .5), 207, 194, 157), thickness);
        using var dark = new Pen(Color.FromArgb((int)(alpha * .85), 0, 0, 0), thickness);
        var leading = inset ? dark : light;
        var trailing = inset ? light : dark;
        graphics.DrawLine(leading, inner.Left, inner.Bottom, inner.Left, inner.Top);
        graphics.DrawLine(leading, inner.Left, inner.Top, inner.Right, inner.Top);
        graphics.DrawLine(trailing, inner.Right, inner.Top, inner.Right, inner.Bottom);
        graphics.DrawLine(trailing, inner.Right, inner.Bottom, inner.Left, inner.Bottom);
    }

    private static void FillRound(Graphics graphics, Color color, RectangleF rectangle, float radius)
    {
        using var path = Round(rectangle, radius);
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath Round(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return path;
        var diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        foreach (var image in _icons.Values) image?.Dispose();
        _icons.Clear();
    }
}
