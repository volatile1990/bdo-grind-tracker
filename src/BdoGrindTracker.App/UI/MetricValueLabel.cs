namespace BdoGrindTracker.App.UI;

/// <summary>
/// Keeps dashboard values on one line without making a metric column wider.
/// The row reserves the preferred font's height; only unusually long values shrink.
/// </summary>
internal sealed class MetricValueLabel : Label
{
    private const TextFormatFlags MeasurementFlags =
        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
    private Font? _fittedFont;
    private bool _fitValid;
    private float _fitDpiX;
    private float _fitDpiY;

    public MetricValueLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        AutoEllipsis = false;
        AccessibleRole = AccessibleRole.StaticText;
    }

    internal float RenderedFontSizeInPoints => (_fittedFont ?? Font).SizeInPoints;

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Padding.Horizontal + 1,
            (int)Math.Ceiling(Font.GetHeight(DeviceDpi)) + Padding.Vertical +
            (int)Math.Ceiling(4f * DeviceDpi / 96f));

    internal Size MeasureRenderedText(Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (Text.Length == 0 || ContentBounds.Width <= 0)
        {
            return Size.Empty;
        }

        return Measure(graphics, GetRenderedFont(graphics));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = ContentBounds;
        if (Text.Length == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var alignment = TextAlign switch
        {
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter =>
                TextFormatFlags.HorizontalCenter,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight =>
                TextFormatFlags.Right,
            _ => TextFormatFlags.Left
        };
        TextRenderer.DrawText(e.Graphics, Text, GetRenderedFont(e.Graphics), bounds,
            ForeColor, MeasurementFlags | TextFormatFlags.VerticalCenter | alignment);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        ResetFit();
        base.OnTextChanged(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        ResetFit();
        base.OnSizeChanged(e);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        ResetFit();
        base.OnFontChanged(e);
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        ResetFit();
        base.OnPaddingChanged(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        ResetFit();
        base.OnDpiChangedAfterParent(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ResetFit();
        }

        base.Dispose(disposing);
    }

    private Rectangle ContentBounds => new(Padding.Left, Padding.Top,
        Math.Max(0, ClientSize.Width - Padding.Horizontal),
        Math.Max(0, ClientSize.Height - Padding.Vertical));

    private Font GetRenderedFont(Graphics graphics)
    {
        if (_fitValid && _fitDpiX == graphics.DpiX && _fitDpiY == graphics.DpiY)
        {
            return _fittedFont ?? Font;
        }

        ResetFit();
        var availableWidth = ContentBounds.Width;
        if (availableWidth > 0 && Measure(graphics, Font).Width > availableWidth)
        {
            var lowerSize = 0.1f;
            var upperSize = Font.SizeInPoints;
            _fittedFont = CreateFittedFont(lowerSize);
            // Keep the last fitting font, including for GDI's integer-pixel rounding.
            for (var iteration = 0; iteration < 8; iteration++)
            {
                var candidateSize = (lowerSize + upperSize) / 2f;
                var candidate = CreateFittedFont(candidateSize);
                if (Measure(graphics, candidate).Width <= availableWidth)
                {
                    lowerSize = candidateSize;
                    _fittedFont.Dispose();
                    _fittedFont = candidate;
                }
                else
                {
                    upperSize = candidateSize;
                    candidate.Dispose();
                }
            }
        }

        _fitDpiX = graphics.DpiX;
        _fitDpiY = graphics.DpiY;
        _fitValid = true;
        return _fittedFont ?? Font;
    }

    private Font CreateFittedFont(float sizeInPoints) =>
        new(Font.FontFamily, sizeInPoints, Font.Style, GraphicsUnit.Point,
            Font.GdiCharSet, Font.GdiVerticalFont);

    private Size Measure(Graphics graphics, Font font) =>
        TextRenderer.MeasureText(graphics, Text, font,
            new Size(int.MaxValue, int.MaxValue), MeasurementFlags);

    private void ResetFit()
    {
        _fittedFont?.Dispose();
        _fittedFont = null;
        _fitValid = false;
        Invalidate();
    }
}
