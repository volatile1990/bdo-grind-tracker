using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace BdoGrindTracker.App.UI;

/// <summary>Selections always use original screenshot pixels, independent of zoom and display DPI.</summary>
internal sealed class BuffCalibrationCanvas : ScrollableControl
{
    private Bitmap? _image;
    private float _zoom = 1;
    private Point? _dragStart;
    private Rectangle _selection;

    internal event Action<Rectangle>? SelectionCompleted;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Rectangle BarRegion { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Rectangle IconRegion { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Rectangle TimerRegion { get; set; }

    internal BuffCalibrationCanvas()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(30, 32, 37);
        Cursor = Cursors.Cross;
        Dock = DockStyle.Fill;
        AccessibleName = "Spiel-Screenshot: Rechteck mit gedrückter Maustaste auswählen";
    }

    internal void SetImage(Bitmap image)
    {
        _image = image;
        _selection = Rectangle.Empty;
        _dragStart = null;
        FitImage();
    }

    internal void FitImage()
    {
        if (_image is null) return;
        SetZoom(Math.Min(1, Math.Min((ClientSize.Width - 24f) / _image.Width,
            (ClientSize.Height - 24f) / _image.Height)));
        AutoScrollPosition = Point.Empty;
    }

    internal void SetZoom(float value)
    {
        _zoom = Math.Clamp(value, .05f, 8);
        AutoScrollMinSize = _image is null ? Size.Empty : new Size(
            (int)Math.Ceiling(_image.Width * _zoom), (int)Math.Ceiling(_image.Height * _zoom));
        Invalidate();
    }

    internal void ShowBar()
    {
        if (BarRegion.IsEmpty) return;
        SetZoom(Math.Min(4, Math.Min((ClientSize.Width - 40f) / BarRegion.Width,
            (ClientSize.Height - 40f) / BarRegion.Height)));
        AutoScrollPosition = new Point(Math.Max(0, (int)(BarRegion.X * _zoom) - 20),
            Math.Max(0, (int)(BarRegion.Y * _zoom) - 20));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image is null)
        {
            TextRenderer.DrawText(e.Graphics, "Vollständigen Spiel-Screenshot laden.\nDanach die Buffleiste markieren.",
                Font, ClientRectangle, Color.WhiteSmoke, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        e.Graphics.ScaleTransform(_zoom, _zoom);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_image, new Rectangle(Point.Empty, _image.Size));
        DrawRegion(e.Graphics, BarRegion, Color.DeepSkyBlue);
        DrawRegion(e.Graphics, IconRegion, Color.LimeGreen);
        DrawRegion(e.Graphics, TimerRegion, Color.Gold);
        DrawRegion(e.Graphics, _selection, Color.White);
    }

    private void DrawRegion(Graphics graphics, Rectangle region, Color color)
    {
        if (region.IsEmpty) return;
        using var pen = new Pen(color, 2 / _zoom);
        graphics.DrawRectangle(pen, region);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _image is null) return;
        Focus();
        Capture = true;
        _dragStart = ImagePoint(e.Location);
        _selection = Rectangle.Empty;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start) return;
        _selection = Between(start, ImagePoint(e.Location));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _dragStart is not { } start) return;
        var rectangle = Between(start, ImagePoint(e.Location));
        _dragStart = null;
        _selection = Rectangle.Empty;
        Capture = false;
        Invalidate();
        if (rectangle.Width > 0 && rectangle.Height > 0) SelectionCompleted?.Invoke(rectangle);
    }

    private Point ImagePoint(Point point) => new(
        Math.Clamp((int)Math.Floor((point.X - AutoScrollPosition.X) / _zoom), 0, _image!.Width),
        Math.Clamp((int)Math.Floor((point.Y - AutoScrollPosition.Y) / _zoom), 0, _image!.Height));

    internal static Rectangle Between(Point first, Point second) => Rectangle.FromLTRB(
        Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
        Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
}
