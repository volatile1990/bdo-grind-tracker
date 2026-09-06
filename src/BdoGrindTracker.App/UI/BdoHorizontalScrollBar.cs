using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace BdoGrindTracker.App.UI;

/// <summary>A compact owner-drawn scrollbar that follows the Grindcrest visual language.</summary>
internal sealed class BdoHorizontalScrollBar : Control
{
    private int _maximum;
    private int _viewportSize = 1;
    private int _value;
    private bool _dragging;
    private bool _hovered;
    private int _dragStartX;
    private int _dragStartValue;

    public BdoHorizontalScrollBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);
        BackColor = BdoTheme.Surface;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.ScrollBar;
        AccessibleName = "Horizontal scrollen";
        Height = 16;
    }

    public event EventHandler? ValueChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Maximum
    {
        get => _maximum;
        set
        {
            var normalized = Math.Max(0, value);
            if (_maximum == normalized)
                return;
            _maximum = normalized;
            Value = Math.Min(_value, _maximum);
            Invalidate();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int ViewportSize
    {
        get => _viewportSize;
        set
        {
            var normalized = Math.Max(1, value);
            if (_viewportSize == normalized)
                return;
            _viewportSize = normalized;
            Invalidate();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int SmallChange { get; set; } = 48;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int LargeChange { get; set; } = 240;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Value
    {
        get => _value;
        set
        {
            var normalized = Math.Clamp(value, 0, _maximum);
            if (_value == normalized)
                return;
            _value = normalized;
            Invalidate();
            if (!_dragging)
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            ValueChanged?.Invoke(this, EventArgs.Empty);
            // The table is comparatively expensive to repaint. Keep the thumb in lockstep
            // with the pointer instead of waiting behind the parent's next paint pass.
            if (_dragging && IsHandleCreated)
                Update();
        }
    }

    internal Rectangle ThumbBounds => CalculateThumbBounds();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = TrackBounds();
        using (var path = BdoTheme.CreateRoundedRectangle(track, track.Height / 2))
        using (var fill = new SolidBrush(Color.FromArgb(224, BdoTheme.SurfaceRaised)))
        using (var border = new Pen(BdoTheme.BorderSoft))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        var thumb = CalculateThumbBounds();
        using (var path = BdoTheme.CreateRoundedRectangle(thumb, thumb.Height / 2))
        using (var fill = new LinearGradientBrush(thumb,
                   _dragging || _hovered ? BdoTheme.GoldBright : BdoTheme.Gold,
                   Color.FromArgb(176, 121, 58), LinearGradientMode.Vertical))
        using (var border = new Pen(Color.FromArgb(205, 229, 190, 112)))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1),
                BdoTheme.GoldBright, BackColor);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        Focus();
        var thumb = CalculateThumbBounds();
        if (thumb.Contains(e.Location))
        {
            _dragging = true;
            _dragStartX = e.X;
            _dragStartValue = _value;
            Capture = true;
        }
        else
        {
            Value += e.X < thumb.Left ? -LargeChange : LargeChange;
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hovered = _dragging || CalculateThumbBounds().Contains(e.Location);
        if (!_dragging && _hovered != hovered)
        {
            _hovered = hovered;
            Invalidate();
        }
        if (!_dragging || _maximum <= 0)
            return;
        var track = TrackBounds();
        var travel = Math.Max(1, track.Width - CalculateThumbBounds().Width);
        var delta = (int)Math.Round((e.X - _dragStartX) * (double)_maximum / travel);
        Value = _dragStartValue + delta;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
            return;
        var wasDragging = _dragging;
        _dragging = false;
        Capture = false;
        if (wasDragging)
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragging)
            return;
        _hovered = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += e.Delta > 0 ? -SmallChange : SmallChange;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var handled = true;
        switch (e.KeyCode)
        {
            case Keys.Left: Value -= SmallChange; break;
            case Keys.Right: Value += SmallChange; break;
            case Keys.PageUp: Value -= LargeChange; break;
            case Keys.PageDown: Value += LargeChange; break;
            case Keys.Home: Value = 0; break;
            case Keys.End: Value = Maximum; break;
            default: handled = false; break;
        }
        if (!handled)
            return;
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private Rectangle TrackBounds()
    {
        var inset = ScaleLogical(3);
        var height = Math.Max(4, ScaleLogical(6));
        return new Rectangle(inset, Math.Max(0, (Height - height) / 2),
            Math.Max(1, Width - inset * 2), height);
    }

    private Rectangle CalculateThumbBounds()
    {
        var track = TrackBounds();
        if (_maximum <= 0)
            return track;
        var contentSize = (long)_viewportSize + _maximum;
        var proportional = (int)Math.Round(track.Width * (_viewportSize / (double)contentSize));
        var width = Math.Clamp(proportional, Math.Min(track.Width, ScaleLogical(36)), track.Width);
        var travel = Math.Max(0, track.Width - width);
        var x = track.X + (int)Math.Round(travel * (_value / (double)_maximum));
        return new Rectangle(x, track.Y, width, track.Height);
    }

    private int ScaleLogical(int pixels) => Math.Max(1, (int)Math.Round(pixels * DeviceDpi / 96d));
}
