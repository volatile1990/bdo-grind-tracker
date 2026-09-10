using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace BdoGrindTracker.App.Overlay.Native;

internal sealed class NativeOverlayRenderer : IDisposable
{
    private static readonly Color Gold = Color.FromArgb(242, 199, 108);
    private static readonly Color Text = Color.FromArgb(237, 241, 245);
    private static readonly Color Muted = Color.FromArgb(154, 175, 190);
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
        foreach (var widget in settings.Widgets)
        {
            var rectangle = new RectangleF((float)widget.X, (float)widget.Y, (float)widget.Width, (float)widget.Height);
            controls["widget:" + widget.Id] = new RectangleF(rectangle.X * scale, rectangle.Y * scale,
                rectangle.Width * scale, rectangle.Height * scale);
            var state = graphics.Save();
            graphics.SetClip(rectangle);
            FillRound(graphics, Color.FromArgb((int)(alpha * .11), 182, 201, 213), rectangle, 6);
            var inner = RectangleF.Inflate(rectangle, -10, -8);
            if (widget.Kind == "controls")
            {
                if (widget.ShowLabel)
                {
                    var labelInset = widget.ShowIcon ? 17 : 0;
                    if (widget.ShowIcon) DrawGlyph(graphics, "status", new RectangleF(inner.X, inner.Y + 1, 12, 12));
                    Draw(graphics, "Tracking", new RectangleF(inner.X + labelInset, inner.Y, inner.Width - labelInset, 14), 10, Muted);
                    inner.Y += 16; inner.Height -= 16;
                }
                var enabled = snapshot.CanToggleTracking && settings.Interaction != "passthrough";
                FillRound(graphics, enabled ? Gold : Color.FromArgb(115, Gold), inner, 5);
                var textBounds = inner;
                if (widget.ShowIcon)
                {
                    using var glyph = new SolidBrush(Color.FromArgb(35, 31, 24));
                    var center = new PointF(inner.X + 14, inner.Y + inner.Height / 2);
                    if (snapshot.IsRunning)
                    {
                        graphics.FillRectangle(glyph, center.X - 4, center.Y - 5, 3, 10);
                        graphics.FillRectangle(glyph, center.X + 1, center.Y - 5, 3, 10);
                    }
                    else graphics.FillPolygon(glyph, [new PointF(center.X - 3, center.Y - 5),
                        new(center.X + 5, center.Y), new(center.X - 3, center.Y + 5)]);
                    textBounds.X += 20; textBounds.Width -= 20;
                }
                Draw(graphics, snapshot.TrackingButtonLabel, textBounds, 12 * (float)widget.FontScale,
                    Color.FromArgb(35, 31, 24), true, StringAlignment.Center, StringAlignment.Center);
                if (enabled) controls["toggle-tracking:" + widget.Id] = new RectangleF(inner.X * scale, inner.Y * scale, inner.Width * scale, inner.Height * scale);
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
        var font = widget.Kind is "spot" or "status" ? 16 : 23;
        var color = widget.Kind is "silver" or "silver-hour" ? Gold : Text;
        if (widget.ShowLabel && widget.Kind != "status" && metric.Detail is { Length: > 0 } && inner.Height >= 31)
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
        var fontScale = (float)widget.FontScale;
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
            Draw(graphics, $"+{view.HiddenCount} weitere", new RectangleF(inner.X, inner.Bottom - 14, inner.Width, 14),
                9, Muted, horizontal: StringAlignment.Far, vertical: StringAlignment.Center);
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
        var gap = (float)OverlayLootPresentation.Gap;
        for (var index = 0; index < view.VisibleItems.Count; index++)
        {
            var cell = new RectangleF(inner.X + index % view.Columns * (cellWidth + gap),
                inner.Y + index / view.Columns * (cellHeight + gap), cellWidth, cellHeight);
            if (cell.Width <= 0 || cell.Height <= 0) continue;
            var item = view.VisibleItems[index];
            if (presentation == "list") DrawLootRow(graphics, item, cell, widget.ShowIcon, fontScale);
            else if (presentation == "card") DrawLootCard(graphics, item, cell, widget.ShowIcon, fontScale, (float)widget.ItemSize);
            else DrawLootTile(graphics, item, cell, widget.ShowIcon, fontScale);
        }
    }

    private void DrawLootTile(Graphics graphics, OverlayLootItem item, RectangleF cell, bool showIcon, float fontScale)
    {
        FillRound(graphics, Color.FromArgb(170, 16, 21, 25), cell, 2);
        if (showIcon)
            DrawIcon(graphics, item, RectangleF.Inflate(cell, -2, -2));
        else
            Draw(graphics, item.Name, new RectangleF(cell.X + 4, cell.Y + 3, cell.Width - 8, cell.Height * .55f),
                10 * fontScale, Text, vertical: StringAlignment.Center);
        var countHeight = Math.Min(cell.Height, Math.Max(17, 18 * fontScale));
        var countBounds = new RectangleF(cell.X, cell.Bottom - countHeight, cell.Width, countHeight);
        using (var shade = new LinearGradientBrush(countBounds, Color.FromArgb(0, 12, 16, 20),
                   Color.FromArgb(230, 12, 16, 20), LinearGradientMode.Vertical))
            graphics.FillRectangle(shade, countBounds);
        var quantity = new RectangleF(cell.X + 3, countBounds.Y, cell.Width - 6, countHeight - 1);
        var shadow = quantity; shadow.Offset(1, 1);
        DrawQuantity(graphics, item.QuantityText, shadow, 12 * fontScale, Color.Black, StringAlignment.Far, StringAlignment.Far);
        DrawQuantity(graphics, item.QuantityText, quantity, 12 * fontScale, Text, StringAlignment.Far, StringAlignment.Far);
        using var border = new Pen(Color.FromArgb(item.IsRare ? 190 : 100, item.IsRare ? Gold : Muted), 1);
        graphics.DrawRectangle(border, cell.X + .5f, cell.Y + .5f, cell.Width - 1, cell.Height - 1);
    }

    private void DrawLootRow(Graphics graphics, OverlayLootItem item, RectangleF cell, bool showIcon, float fontScale)
    {
        var iconSize = Math.Max(0, Math.Min(cell.Height - 4, cell.Width / 3));
        var iconSpace = showIcon ? iconSize + 6 : 0;
        if (showIcon && iconSize > 0)
            DrawIcon(graphics, item, new RectangleF(cell.X, cell.Y + (cell.Height - iconSize) / 2, iconSize, iconSize));
        var quantityWidth = Math.Min(cell.Width - iconSpace, Math.Max(52 * fontScale, cell.Width * .25f));
        Draw(graphics, item.Name, new RectangleF(cell.X + iconSpace, cell.Y, cell.Width - iconSpace - quantityWidth - 4, cell.Height),
            11 * fontScale, Text, vertical: StringAlignment.Center);
        DrawQuantity(graphics, item.QuantityText, new RectangleF(cell.Right - quantityWidth, cell.Y, quantityWidth, cell.Height),
            12 * fontScale, item.IsRare ? Gold : Text, StringAlignment.Far, StringAlignment.Center);
        using var line = new Pen(Color.FromArgb(25, Muted), 1);
        graphics.DrawLine(line, cell.Left, cell.Bottom - .5f, cell.Right, cell.Bottom - .5f);
    }

    private void DrawLootCard(Graphics graphics, OverlayLootItem item, RectangleF cell, bool showIcon, float fontScale, float itemSize)
    {
        if (showIcon)
        {
            var size = Math.Min(itemSize, Math.Min(cell.Height, cell.Width * .35f));
            if (size > 0)
                DrawIcon(graphics, item, new RectangleF(cell.X, cell.Y + (cell.Height - size) / 2, size, size));
            cell.X += size + 12;
            cell.Width -= size + 12;
        }
        var nameHeight = cell.Height >= 45 * fontScale ? 26 * fontScale : 0;
        var quantityHeight = Math.Min(Math.Max(0, cell.Height - nameHeight), 36 * fontScale);
        var top = cell.Y + Math.Max(0, (cell.Height - nameHeight - quantityHeight) / 2);
        DrawQuantity(graphics, item.QuantityText, new RectangleF(cell.X, top, cell.Width, quantityHeight),
            Math.Min(30 * fontScale, quantityHeight), Gold, StringAlignment.Near, StringAlignment.Far);
        using var font = new Font("Segoe UI", Math.Max(7, 11 * fontScale), FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Muted);
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.LineLimit
        };
        if (cell.Width > 0 && nameHeight > 0)
            graphics.DrawString(item.Name, font, brush, new RectangleF(cell.X, top + quantityHeight, cell.Width, nameHeight), format);
    }

    private static void DrawQuantity(Graphics graphics, string text, RectangleF bounds, float requestedSize, Color color,
        StringAlignment horizontal, StringAlignment vertical)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.None,
            Alignment = horizontal, LineAlignment = vertical
        };
        using var measureFont = new Font("Segoe UI", Math.Max(7, requestedSize), FontStyle.Bold, GraphicsUnit.Pixel);
        var measured = graphics.MeasureString(text, measureFont, 10000, format).Width;
        var size = Math.Max(7, Math.Min(requestedSize, requestedSize * Math.Max(1, bounds.Width - 2) / Math.Max(1, measured)));
        using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
        var fittedWidth = graphics.MeasureString(text, font, 10000, format).Width;
        var horizontalScale = Math.Min(1, Math.Max(1, bounds.Width - 2) / Math.Max(1, fittedWidth));
        using var brush = new SolidBrush(color);
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(horizontalScale, 1);
        graphics.DrawString(text, font, brush, new RectangleF(0, 0, bounds.Width / horizontalScale, bounds.Height), format);
        graphics.Restore(state);
    }

    private static void DrawChart(Graphics graphics, OverlayWidget widget, RectangleF inner, OverlaySnapshot snapshot)
    {
        if (widget.ShowLabel)
        {
            var inset = widget.ShowIcon ? 17 : 0;
            if (widget.ShowIcon) DrawGlyph(graphics, "chart", new RectangleF(inner.X, inner.Y + 1, 12, 12));
            Draw(graphics, "Silber / Stunde · Verlauf", new RectangleF(inner.X + inset, inner.Y, inner.Width - inset, 16), 10, Muted);
            inner.Y += 20; inner.Height -= 20;
        }
        if (snapshot.Metrics.TryGetValue("silver-hour", out var metric))
        {
            var inset = !widget.ShowLabel && widget.ShowIcon ? 21 : 0;
            if (inset > 0) DrawGlyph(graphics, "chart", new RectangleF(inner.X, inner.Y + 5, 15, 15));
            Draw(graphics, metric.Value, new RectangleF(inner.X + inset, inner.Y, inner.Width - inset, 27), 21 * (float)widget.FontScale, Gold, true);
            inner.Y += 33; inner.Height -= 33;
        }
        if (snapshot.Metrics.TryGetValue("chart", out var chart) && chart.Detail is { Length: > 0 } && inner.Height > 30)
        {
            Draw(graphics, chart.Detail, new RectangleF(inner.X, inner.Bottom - 13, inner.Width, 13), 9, Muted);
            inner.Height -= 16;
        }
        if (snapshot.SilverHistory.Count < 2 || inner.Height < 8)
        {
            Draw(graphics, "Verlauf entsteht während der Session", inner, 10, Muted, vertical: StringAlignment.Center);
            return;
        }
        var highest = Math.Max(1m, snapshot.SilverHistory.Max());
        var points = snapshot.SilverHistory.Select((value, index) => new PointF(
            inner.X + index * inner.Width / (snapshot.SilverHistory.Count - 1),
            inner.Bottom - 2 - (float)(Math.Max(0m, value) / highest) * Math.Max(1, inner.Height - 5))).ToArray();
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
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var font = new Font("Segoe UI", Math.Max(7, size), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap, Alignment = horizontal, LineAlignment = vertical };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private static void DrawGlyph(Graphics graphics, string kind, RectangleF bounds)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 16, bounds.Height / 16);
        using var pen = new Pen(Gold, 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (kind is "silver-hour" or "trash-hour" or "chart")
        {
            graphics.DrawLines(pen, [new PointF(1, 12), new(6, 7), new(9, 9), new(14, 3)]);
            graphics.DrawLines(pen, [new PointF(10, 3), new(14, 3), new(14, 7)]);
        }
        else if (kind is "trash" or "drops" or "rare-drops")
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
