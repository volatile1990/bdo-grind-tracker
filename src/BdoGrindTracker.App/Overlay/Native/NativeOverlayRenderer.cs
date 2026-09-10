using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace BdoGrindTracker.App.Overlay.Native;

internal sealed class NativeOverlayRenderer : IDisposable
{
    private static readonly Color Gold = Color.FromArgb(242, 199, 108);
    private static readonly Color Text = Color.FromArgb(237, 241, 245);
    private static readonly Color Muted = Color.FromArgb(154, 175, 190);
    private static readonly Color Positive = Color.FromArgb(125, 211, 181);
    private readonly Dictionary<string, Image?> _icons = new(StringComparer.OrdinalIgnoreCase);

    internal Bitmap Render(Size size, OverlaySettings settings, OverlaySnapshot snapshot,
        out IReadOnlyDictionary<string, RectangleF> actions)
    {
        var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var scale = (float)Math.Min(size.Width / settings.Width, size.Height / settings.Height);
        graphics.ScaleTransform(scale, scale);
        var canvas = new RectangleF(0, 0, size.Width / scale, size.Height / scale);
        // Keep even a fully transparent background draggable. Alpha zero is a
        // hole in a layered window; alpha one remains visually transparent.
        var alpha = Math.Max(settings.Interaction == "move" ? 1 : 0, (int)Math.Round(settings.BackgroundOpacity * 255));
        FillRound(graphics, Color.FromArgb(alpha, 23, 29, 34), canvas, 10);
        if (settings.ShowBorder)
        {
            using var border = new Pen(Color.FromArgb(135, Gold), 1);
            using var path = Round(new RectangleF(.5f, .5f, canvas.Width - 1, canvas.Height - 1), 10);
            graphics.DrawPath(border, path);
        }
        var controls = new Dictionary<string, RectangleF>();
        foreach (var placedWidget in settings.Widgets)
        {
            var rectangle = new RectangleF((float)placedWidget.X, (float)placedWidget.Y, (float)placedWidget.Width, (float)placedWidget.Height);
            controls["widget:" + placedWidget.Id] = new RectangleF(rectangle.X * scale, rectangle.Y * scale,
                rectangle.Width * scale, rectangle.Height * scale);
            var state = graphics.Save();
            graphics.SetClip(rectangle);
            FillRound(graphics, Color.FromArgb((int)(alpha * .11), 182, 201, 213), rectangle, 6);
            if (snapshot.Metrics.TryGetValue(placedWidget.Kind, out var widgetMetric) && widgetMetric.IsWarning)
            {
                FillRound(graphics, Color.FromArgb(13, Gold), rectangle, 6);
                using var warningBorder = new Pen(Color.FromArgb(112, Gold), 1);
                using var warningPath = Round(RectangleF.Inflate(rectangle, -.5f, -.5f), 6);
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
                    Draw(graphics, "Tracking", new RectangleF(inner.X + labelInset, inner.Y, inner.Width - labelInset, 14 * fontScale), 10 * fontScale, Muted);
                    inner.Y += 18 * fontScale; inner.Height -= 18 * fontScale;
                }
                if (inner.Height <= 0)
                {
                    graphics.Restore(state);
                    continue;
                }
                var enabled = snapshot.CanToggleTracking && settings.Interaction != "passthrough";
                var buttonHeight = Math.Min(inner.Height, 28 * (float)widget.FontScale);
                inner.Y += (inner.Height - buttonHeight) / 2;
                inner.Height = buttonHeight;
                FillRound(graphics, Color.FromArgb(enabled ? 24 : 12, Gold), inner, 5);
                using (var outline = new Pen(Color.FromArgb(enabled ? 100 : 50, Gold), 1))
                using (var path = Round(inner, 5)) graphics.DrawPath(outline, path);
                var textBounds = inner;
                var buttonColor = enabled ? Gold : Color.FromArgb(130, Gold);
                var buttonTextBounds = new RectangleF(0, 0, Math.Max(0, inner.Width - 8), Math.Max(0, inner.Height - 4));
                var buttonFontSize = FitTextSize(graphics, snapshot.TrackingButtonLabel, buttonTextBounds,
                    12 * (float)widget.FontScale, true, widget.ShowIcon ? 20 * (float)widget.FontScale : 0);
                if (widget.ShowIcon)
                {
                    using var labelFont = new Font("Segoe UI", buttonFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var measureFormat = TextFormat();
                    var textWidth = graphics.MeasureString(snapshot.TrackingButtonLabel, labelFont, 100000, measureFormat).Width + 2;
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
                Draw(graphics, snapshot.TrackingButtonLabel, textBounds, buttonFontSize,
                    buttonColor, true, StringAlignment.Center, StringAlignment.Center);
                if (enabled)
                {
                    PointF[] corners = [inner.Location, new(inner.Right, inner.Bottom)];
                    using var transform = graphics.Transform;
                    transform.TransformPoints(corners);
                    controls["toggle-tracking:" + widget.Id] = RectangleF.FromLTRB(corners[0].X, corners[0].Y, corners[1].X, corners[1].Y);
                }
            }
            else if (OverlayCatalog.IsLootWidget(widget.Kind))
                DrawLoot(graphics, widget, inner, snapshot);
            else if (widget.Kind == "chart") DrawChart(graphics, widget, inner, snapshot);
            else DrawMetric(graphics, widget, inner, snapshot);
            graphics.Restore(state);
        }
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

    private static void DrawMetric(Graphics graphics, OverlayWidget widget, RectangleF inner, OverlaySnapshot snapshot)
    {
        if (!snapshot.Metrics.TryGetValue(widget.Kind, out var metric)) return;
        var fontScale = (float)widget.FontScale;
        if (widget.ShowLabel)
        {
            var iconWidth = widget.ShowIcon ? 17 * fontScale : 0;
            if (widget.ShowIcon) DrawGlyph(graphics, widget.Kind, new RectangleF(inner.X, inner.Y + fontScale, 12 * fontScale, 12 * fontScale));
            Draw(graphics, metric.Label, new RectangleF(inner.X + iconWidth, inner.Y, inner.Width - iconWidth, 15 * fontScale),
                10 * fontScale, Muted);
            inner.Y += 18 * fontScale;
            inner.Height -= 18 * fontScale;
        }
        var font = widget.Kind is "spot" or "status" or "loot-scroll" or "grind-rating" ? 16 : 23;
        var color = metric.IsWarning || widget.Kind is "silver" or "silver-hour" ? Gold : metric.Tone switch
        {
            OverlayMetricTone.Muted => Muted,
            OverlayMetricTone.Positive => Positive,
            OverlayMetricTone.Accent => Gold,
            _ => Text,
        };
        if (widget.ShowLabel && widget.Kind != "status" && metric.Detail is { Length: > 0 })
        {
            var height = 12 * fontScale;
            Draw(graphics, metric.Detail, new RectangleF(inner.X, inner.Bottom - height, inner.Width, height), 9 * fontScale, Muted);
            inner.Height -= height;
        }
        if (!widget.ShowLabel && widget.ShowIcon)
        {
            var width = 21 * fontScale;
            DrawGlyph(graphics, widget.Kind, new RectangleF(inner.X, inner.Y + Math.Max(0, (inner.Height - 15 * fontScale) / 2),
                15 * fontScale, 15 * fontScale));
            inner.X += width; inner.Width -= width;
        }
        Draw(graphics, metric.Value, inner, Math.Min(font * fontScale, Math.Max(10, inner.Height - 2)), color, true,
            vertical: StringAlignment.Center);
    }

    private void DrawLoot(Graphics graphics, OverlayWidget widget, RectangleF inner, OverlaySnapshot snapshot)
    {
        var view = OverlayLootPresentation.Create(widget, snapshot);
        var fontScale = (float)view.FontScale;
        var presentation = widget.Kind == "drop-item" ? "card" : widget.ItemView;
        if (widget.ShowLabel)
        {
            var inset = widget.ShowIcon ? 17 * fontScale : 0;
            var labelHeight = Math.Min((float)view.HeaderHeight, Math.Max(0, inner.Height - (float)view.FooterHeight));
            if (labelHeight >= 12 * fontScale)
            {
                if (widget.ShowIcon) DrawGlyph(graphics, "drops", new RectangleF(inner.X, inner.Y + fontScale, 12 * fontScale, 12 * fontScale));
                Draw(graphics, view.Label, new RectangleF(inner.X + inset, inner.Y, inner.Width - inset, labelHeight),
                    10 * fontScale, Muted);
            }
            inner.Y += (float)view.HeaderHeight;
            inner.Height -= (float)view.HeaderHeight;
        }
        if (view.HiddenCount > 0)
        {
            Draw(graphics, $"+{view.HiddenCount} weitere", new RectangleF(inner.X, inner.Bottom - (float)view.FooterHeight, inner.Width, (float)view.FooterHeight),
                9 * fontScale, Muted, horizontal: StringAlignment.Far, vertical: StringAlignment.Center);
            inner.Height -= (float)view.FooterHeight;
        }
        if (view.Items.Count == 0)
        {
            Draw(graphics, (widget.ItemFilter == "selected" || widget.Kind == "drop-item") && widget.ItemNames.Count == 0
                    ? "Items im Editor auswählen" : "Noch keine Drops", inner,
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
    }

    private void DrawLootTile(Graphics graphics, OverlayLootItem item, RectangleF cell, bool showIcon, float fontScale, float fit)
    {
        FillRound(graphics, Color.FromArgb(170, 16, 21, 25), cell, 2 * fit);
        if (showIcon)
            DrawIcon(graphics, item, RectangleF.Inflate(cell, -2 * fit, -2 * fit));
        else
            Draw(graphics, item.Name, new RectangleF(cell.X + 4 * fit, cell.Y + 3 * fit, cell.Width - 8 * fit, cell.Height * .55f),
                10 * fontScale, Text, vertical: StringAlignment.Center);
        var countHeight = Math.Min(cell.Height, Math.Max(17 * fit, 18 * fontScale));
        var countBounds = new RectangleF(cell.X, cell.Bottom - countHeight, cell.Width, countHeight);
        using (var shade = new LinearGradientBrush(countBounds, Color.FromArgb(0, 12, 16, 20),
                   Color.FromArgb(230, 12, 16, 20), LinearGradientMode.Vertical))
            graphics.FillRectangle(shade, countBounds);
        var quantity = new RectangleF(cell.X + 3 * fit, countBounds.Y, cell.Width - 6 * fit, countHeight - fit);
        var shadow = quantity; shadow.Offset(fit, fit);
        DrawQuantity(graphics, item.QuantityText, shadow, 12 * fontScale, Color.Black, StringAlignment.Far, StringAlignment.Far);
        DrawQuantity(graphics, item.QuantityText, quantity, 12 * fontScale, Text, StringAlignment.Far, StringAlignment.Far);
        using var border = new Pen(Color.FromArgb(item.IsRare ? 190 : 100, item.IsRare ? Gold : Muted), fit);
        graphics.DrawRectangle(border, cell.X + .5f * fit, cell.Y + .5f * fit, cell.Width - fit, cell.Height - fit);
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
            12 * fontScale, item.IsRare ? Gold : Text, StringAlignment.Far, StringAlignment.Center);
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

    private static void DrawQuantity(Graphics graphics, string text, RectangleF bounds, float requestedSize, Color color,
        StringAlignment horizontal, StringAlignment vertical)
    {
        Draw(graphics, text, bounds, requestedSize, color, true, horizontal, vertical);
    }

    private static void DrawChart(Graphics graphics, OverlayWidget widget, RectangleF inner, OverlaySnapshot snapshot)
    {
        var fontScale = (float)widget.FontScale;
        if (widget.ShowLabel)
        {
            var inset = widget.ShowIcon ? 17 * fontScale : 0;
            if (widget.ShowIcon) DrawGlyph(graphics, "chart", new RectangleF(inner.X, inner.Y + fontScale, 12 * fontScale, 12 * fontScale));
            Draw(graphics, "Silber / Stunde · Verlauf", new RectangleF(inner.X + inset, inner.Y, inner.Width - inset, 16 * fontScale), 10 * fontScale, Muted);
            inner.Y += 20 * fontScale; inner.Height -= 20 * fontScale;
        }
        if (snapshot.Metrics.TryGetValue("chart", out var metric))
        {
            var inset = !widget.ShowLabel && widget.ShowIcon ? 21 * fontScale : 0;
            if (inset > 0) DrawGlyph(graphics, "chart", new RectangleF(inner.X, inner.Y + 5 * fontScale, 15 * fontScale, 15 * fontScale));
            Draw(graphics, metric.Value, new RectangleF(inner.X + inset, inner.Y, inner.Width - inset, 27 * fontScale), 21 * fontScale, Gold, true);
            inner.Y += 33 * fontScale; inner.Height -= 33 * fontScale;
        }
        if (snapshot.Metrics.TryGetValue("chart", out var chart) && chart.Detail is { Length: > 0 })
        {
            Draw(graphics, chart.Detail, new RectangleF(inner.X, inner.Bottom - 13 * fontScale, inner.Width, 13 * fontScale), 9 * fontScale, Muted);
            inner.Height -= 16 * fontScale;
        }
        if (snapshot.SilverHistory.Count < 2 || inner.Height < 8)
        {
            Draw(graphics, "Verlauf entsteht während der Session", inner, 10, Muted, vertical: StringAlignment.Center);
            return;
        }
        var highest = Math.Max(1m, snapshot.SilverHistory.Max(point => point.SilverPerHour));
        var first = snapshot.SilverHistory[0].Elapsed.Ticks;
        var span = Math.Max(1, snapshot.SilverHistory[^1].Elapsed.Ticks - first);
        var points = snapshot.SilverHistory.Select(point => new PointF(
            inner.X + (float)((decimal)(point.Elapsed.Ticks - first) / span) * inner.Width,
            inner.Bottom - 2 - (float)(Math.Max(0m, point.SilverPerHour) / highest) * Math.Max(1, inner.Height - 5))).ToArray();
        using var fill = new SolidBrush(Color.FromArgb(40, Gold));
        graphics.FillPolygon(fill, [new PointF(inner.Left, inner.Bottom), .. points, new PointF(inner.Right, inner.Bottom)]);
        using var line = new Pen(Gold, 1.6f);
        graphics.DrawLines(line, points);
    }

    private void DrawIcon(Graphics graphics, OverlayLootItem item, RectangleF rectangle)
    {
        var icon = LoadIcon(item.IconPath);
        if (icon is not null) graphics.DrawImage(icon, rectangle);
        else Draw(graphics, item.Name.Length > 0 ? item.Name[..1] : "·", rectangle, 18, item.IsRare ? Gold : Muted,
            true, StringAlignment.Center, StringAlignment.Center);
    }

    private Image? LoadIcon(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        if (_icons.TryGetValue(relative, out var cached)) return cached;
        // Metric icon paths refer only to packaged catalog assets, never URLs.
        Image? image = null;
        try
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));
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

    private static void Draw(Graphics graphics, string text, RectangleF bounds, float size, Color color,
        bool bold = false, StringAlignment horizontal = StringAlignment.Near, StringAlignment vertical = StringAlignment.Near)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || string.IsNullOrEmpty(text)) return;
        using var font = new Font("Segoe UI", FitTextSize(graphics, text, bounds, size, bold),
            bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = TextFormat(horizontal, vertical);
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static StringFormat TextFormat(StringAlignment horizontal = StringAlignment.Near,
        StringAlignment vertical = StringAlignment.Near) => new(StringFormat.GenericTypographic)
    {
        Trimming = StringTrimming.None, FormatFlags = StringFormatFlags.NoWrap,
        Alignment = horizontal, LineAlignment = vertical
    };

    private static float FitTextSize(Graphics graphics, string text, RectangleF bounds, float requestedSize,
        bool bold, float additionalWidth = 0)
    {
        var size = Math.Max(.1f, requestedSize);
        using var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = TextFormat();
        var measured = graphics.MeasureString(text, font, 100000, format);
        var fit = Math.Min(1, Math.Min(Math.Max(.1f, bounds.Width - 2) / Math.Max(.1f, measured.Width + additionalWidth),
            Math.Max(.1f, bounds.Height - 1) / Math.Max(.1f, measured.Height)));
        return Math.Max(.1f, size * fit);
    }

    private static void DrawGlyph(Graphics graphics, string kind, RectangleF bounds)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 16, bounds.Height / 16);
        using var pen = new Pen(Gold, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (kind is "silver-hour" or "trash-hour" or "chart" or "grind-rating")
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
        else if (kind == "status")
            graphics.DrawLines(pen, [new PointF(1, 8), new(4, 8), new(6, 2), new(9, 14), new(11, 8), new(15, 8)]);
        else
        {
            graphics.DrawEllipse(pen, 1, 1, 14, 14);
            if (kind == "duration") graphics.DrawLines(pen, [new PointF(8, 4), new(8, 8), new(11, 9)]);
            else graphics.DrawEllipse(pen, 5, 3, 6, 10);
        }
        graphics.Restore(state);
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
