using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BdoGrindTracker.App.UI;

/// <summary>Flow list with a thin owner-drawn vertical scrollbar instead of WinForms chrome.</summary>
internal sealed class BdoScrollableFlowLayoutPanel : FlowLayoutPanel
{
    private const int SbVert = 1;
    private bool _dragging;
    private bool _hovered;
    private int _dragStartY;
    private int _dragStartValue;

    public BdoScrollableFlowLayoutPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool HasCustomScrollBar => MaximumScroll > 0;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Rectangle ScrollThumbBounds => CalculateThumbBounds();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HideNativeScrollBar();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        HideNativeScrollBar();
        Invalidate(ScrollGutterBounds());
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        HideNativeScrollBar();
        Invalidate(ScrollGutterBounds());
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!HasCustomScrollBar)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = TrackBounds();
        using (var path = BdoTheme.CreateRoundedRectangle(track, track.Width / 2))
        using (var fill = new SolidBrush(Color.FromArgb(225, BdoTheme.SurfaceRaised)))
        using (var border = new Pen(BdoTheme.BorderSoft))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        var thumb = CalculateThumbBounds();
        using (var path = BdoTheme.CreateRoundedRectangle(thumb, thumb.Width / 2))
        using (var fill = new LinearGradientBrush(thumb,
                   _dragging || _hovered ? BdoTheme.GoldBright : BdoTheme.Gold,
                   Color.FromArgb(176, 121, 58), LinearGradientMode.Horizontal))
        using (var border = new Pen(Color.FromArgb(205, 229, 190, 112)))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HasCustomScrollBar && ScrollGutterBounds().Contains(e.Location))
        {
            var thumb = CalculateThumbBounds();
            if (ThumbHitBounds(thumb).Contains(e.Location))
            {
                _dragging = true;
                _dragStartY = e.Y;
                _dragStartValue = CurrentScroll;
                Capture = true;
            }
            else
            {
                SetScroll(CurrentScroll + (e.Y < thumb.Top ? -ClientSize.Height : ClientSize.Height));
            }
            Invalidate(ScrollGutterBounds());
            return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && MaximumScroll > 0)
        {
            var track = TrackBounds();
            var travel = Math.Max(1, track.Height - CalculateThumbBounds().Height);
            var delta = (int)Math.Round((e.Y - _dragStartY) * (double)MaximumScroll / travel);
            SetScroll(_dragStartValue + delta);
            Update();
            return;
        }

        var hovered = HasCustomScrollBar && ThumbHitBounds(CalculateThumbBounds()).Contains(e.Location);
        if (_hovered != hovered)
        {
            _hovered = hovered;
            Invalidate(ScrollGutterBounds());
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _dragging)
        {
            _dragging = false;
            Capture = false;
            Invalidate(ScrollGutterBounds());
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragging)
            return;
        _hovered = false;
        Invalidate(ScrollGutterBounds());
    }

    private int MaximumScroll => Math.Max(0, DisplayRectangle.Height - ClientSize.Height);
    private int CurrentScroll => Math.Clamp(-AutoScrollPosition.Y, 0, MaximumScroll);

    private void SetScroll(int value)
    {
        var normalized = Math.Clamp(value, 0, MaximumScroll);
        AutoScrollPosition = new Point(0, normalized);
        HideNativeScrollBar();
        Invalidate();
    }

    private Rectangle ScrollGutterBounds()
    {
        var width = ScaleLogical(16);
        return new Rectangle(Math.Max(0, ClientSize.Width - width), 0,
            Math.Min(width, ClientSize.Width), ClientSize.Height);
    }

    private Rectangle TrackBounds()
    {
        var width = ScaleLogical(6);
        var inset = ScaleLogical(6);
        return new Rectangle(Math.Max(0, ClientSize.Width - ScaleLogical(11)), inset,
            width, Math.Max(1, ClientSize.Height - inset * 2));
    }

    private Rectangle CalculateThumbBounds()
    {
        var track = TrackBounds();
        var maximum = MaximumScroll;
        if (maximum <= 0)
            return track;
        var contentHeight = (long)ClientSize.Height + maximum;
        var proportional = (int)Math.Round(track.Height * (ClientSize.Height / (double)contentHeight));
        var height = Math.Clamp(proportional, Math.Min(track.Height, ScaleLogical(34)), track.Height);
        var travel = Math.Max(0, track.Height - height);
        var y = track.Y + (int)Math.Round(travel * (CurrentScroll / (double)maximum));
        return new Rectangle(track.X, y, track.Width, height);
    }

    private Rectangle ThumbHitBounds(Rectangle thumb) =>
        new(ScrollGutterBounds().X, thumb.Y, ScrollGutterBounds().Width, thumb.Height);

    private void HideNativeScrollBar()
    {
        if (OperatingSystem.IsWindows() && IsHandleCreated)
            _ = ShowScrollBar(Handle, SbVert, false);
    }

    private int ScaleLogical(int pixels) => Math.Max(1, (int)Math.Round(pixels * DeviceDpi / 96d));

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr windowHandle, int bar, bool show);
}
