using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// A deliberately low-resolution, in-memory view of the captured monitor. It visualizes
/// the area used for the current analysis separately from the ROI learned for the next
/// frame and never writes captured pixels to disk.
/// </summary>
internal sealed class DetectionRegionPreview : Control
{
    internal static readonly Size MaximumPreviewSize = new(420, 236);

    private Bitmap? _preview;
    private Size _frameSize;
    private IReadOnlyList<Rectangle> _analyzedRegions = [];
    private IReadOnlyList<Rectangle> _activeRegions = [];

    public DetectionRegionPreview()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = BdoTheme.SurfaceRaised;
        ForeColor = BdoTheme.TextMuted;
        AccessibleName = "Diagnose Erkennungsbereich";
        AccessibleRole = AccessibleRole.Graphic;
        MinimumSize = new Size(260, 146);
    }

    /// <summary>
    /// Creates the only pixel copy retained by the debug UI. The result keeps the source
    /// aspect ratio and is capped so debug display cannot materially increase capture
    /// memory pressure.
    /// </summary>
    public static Bitmap CreateThumbnail(Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width <= 0 || source.Height <= 0)
        {
            throw new ArgumentException("Das Quellbild muss eine positive Größe haben.", nameof(source));
        }

        var scale = Math.Min(
            1d,
            Math.Min(
                MaximumPreviewSize.Width / (double)source.Width,
                MaximumPreviewSize.Height / (double)source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var thumbnail = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(thumbnail);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.Bilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
        graphics.DrawImage(
            source,
            new Rectangle(Point.Empty, thumbnail.Size),
            new Rectangle(Point.Empty, source.Size),
            GraphicsUnit.Pixel);
        return thumbnail;
    }

    /// <summary>Takes ownership of <paramref name="preview"/>.</summary>
    public void SetSnapshot(
        Bitmap preview,
        Size frameSize,
        Rectangle? analyzedRegion,
        Rectangle? activeRegion) =>
        SetSnapshot(
            preview,
            frameSize,
            analyzedRegion is { } analyzed ? [analyzed] : [],
            activeRegion is { } active ? [active] : []);

    /// <summary>Takes ownership of <paramref name="preview"/>.</summary>
    public void SetSnapshot(
        Bitmap preview,
        Size frameSize,
        IReadOnlyList<Rectangle> analyzedRegions,
        IReadOnlyList<Rectangle> activeRegions)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(analyzedRegions);
        ArgumentNullException.ThrowIfNull(activeRegions);

        var previous = _preview;
        _preview = preview;
        _frameSize = frameSize;
        _analyzedRegions = analyzedRegions.ToArray();
        _activeRegions = activeRegions.ToArray();
        previous?.Dispose();
        Invalidate();
    }

    public void ClearSnapshot()
    {
        var previous = _preview;
        _preview = null;
        _frameSize = Size.Empty;
        _analyzedRegions = [];
        _activeRegions = [];
        previous?.Dispose();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        if (_preview is null || _frameSize.Width <= 0 || _frameSize.Height <= 0)
        {
            TextRenderer.DrawText(
                e.Graphics,
                "Noch kein analysierter Frame",
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
            return;
        }

        var imageBounds = FitInside(_preview.Size, Rectangle.Inflate(ClientRectangle, -1, -1));
        e.Graphics.DrawImage(_preview, imageBounds);

        if (_analyzedRegions.Count > 0)
        {
            using var outside = new Region(imageBounds);
            foreach (var analyzed in _analyzedRegions)
            {
                outside.Exclude(MapToDisplay(analyzed, imageBounds));
            }

            using var shade = new SolidBrush(Color.FromArgb(150, 4, 7, 10));
            e.Graphics.FillRegion(shade, outside);
            using var analysisPen = new Pen(BdoTheme.Positive, 2f);
            foreach (var analyzed in _analyzedRegions)
            {
                var displayedAnalysis = MapToDisplay(analyzed, imageBounds);
                if (displayedAnalysis.Width > 0 && displayedAnalysis.Height > 0)
                {
                    e.Graphics.DrawRectangle(analysisPen, EnsureDrawable(displayedAnalysis));
                }
            }
        }

        using var lockPen = new Pen(BdoTheme.GoldBright, 2f)
        {
            DashStyle = DashStyle.Dash
        };
        foreach (var active in _activeRegions)
        {
            var displayedLock = MapToDisplay(active, imageBounds);
            if (displayedLock.Width > 0 && displayedLock.Height > 0)
            {
                e.Graphics.DrawRectangle(lockPen, EnsureDrawable(displayedLock));
            }
        }

        using var borderPen = new Pen(BdoTheme.Border, 1f);
        e.Graphics.DrawRectangle(borderPen, EnsureDrawable(imageBounds));
    }

    private Rectangle MapToDisplay(Rectangle source, Rectangle display)
    {
        var clipped = Rectangle.Intersect(source, new Rectangle(Point.Empty, _frameSize));
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var scaleX = display.Width / (double)_frameSize.Width;
        var scaleY = display.Height / (double)_frameSize.Height;
        return Rectangle.FromLTRB(
            display.Left + (int)Math.Round(clipped.Left * scaleX),
            display.Top + (int)Math.Round(clipped.Top * scaleY),
            display.Left + (int)Math.Round(clipped.Right * scaleX),
            display.Top + (int)Math.Round(clipped.Bottom * scaleY));
    }

    private static Rectangle FitInside(Size content, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var scale = Math.Min(
            bounds.Width / (double)content.Width,
            bounds.Height / (double)content.Height);
        var width = Math.Max(1, (int)Math.Round(content.Width * scale));
        var height = Math.Max(1, (int)Math.Round(content.Height * scale));
        return new Rectangle(
            bounds.Left + (bounds.Width - width) / 2,
            bounds.Top + (bounds.Height - height) / 2,
            width,
            height);
    }

    private static Rectangle EnsureDrawable(Rectangle rectangle) =>
        rectangle.Width > 0 && rectangle.Height > 0
            ? new Rectangle(rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1)
            : Rectangle.Empty;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview?.Dispose();
            _preview = null;
        }

        base.Dispose(disposing);
    }
}
