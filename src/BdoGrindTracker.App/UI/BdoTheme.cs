using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Shared visual language for the compact, game-facing desktop UI.
/// </summary>
internal static class BdoTheme
{
    public static readonly Color Background = Color.FromArgb(12, 15, 19);
    public static readonly Color Surface = Color.FromArgb(19, 24, 30);
    public static readonly Color SurfaceRaised = Color.FromArgb(26, 32, 40);
    public static readonly Color SurfaceHover = Color.FromArgb(33, 41, 50);
    public static readonly Color SurfacePressed = Color.FromArgb(39, 47, 57);
    public static readonly Color Border = Color.FromArgb(47, 56, 67);
    public static readonly Color BorderSoft = Color.FromArgb(36, 44, 53);
    public static readonly Color Gold = Color.FromArgb(218, 170, 77);
    public static readonly Color GoldBright = Color.FromArgb(250, 204, 111);
    public static readonly Color Text = Color.FromArgb(245, 247, 249);
    public static readonly Color TextMuted = Color.FromArgb(151, 161, 174);
    public static readonly Color Positive = Color.FromArgb(84, 210, 142);
    public static readonly Color Warning = Color.FromArgb(241, 184, 75);
    public static readonly Color Error = Color.FromArgb(242, 104, 116);

    public static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.BackColor = SurfaceRaised;
        comboBox.ForeColor = Text;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.DrawMode = DrawMode.OwnerDrawFixed;
        comboBox.ItemHeight = 28;
        comboBox.IntegralHeight = false;
        comboBox.DropDownHeight = 220;
        comboBox.DrawItem += DrawComboBoxItem;
    }

    internal static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Min(Math.Min(radius * 2, bounds.Width), bounds.Height);
        var path = new GraphicsPath();
        if (diameter <= 1)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawComboBoxItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox comboBox || e.Index < 0)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? SurfaceHover : SurfaceRaised);
        e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(
            e.Graphics,
            comboBox.Items[e.Index]?.ToString() ?? string.Empty,
            comboBox.Font,
            Rectangle.Inflate(e.Bounds, -10, 0),
            selected ? GoldBright : Text,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }
}

internal enum BdoButtonStyle
{
    Primary,
    Secondary,
    Navigation
}

/// <summary>
/// Shared owner-drawn button for primary actions, secondary actions and navigation.
/// </summary>
internal sealed class BdoButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private BdoButtonStyle _buttonStyle;
    private int _cornerRadius = 10;
    private bool _selected;

    public BdoButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Height = 42;
        Padding = new Padding(14, 0, 14, 0);
        Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
    }

    [DefaultValue(BdoButtonStyle.Primary)]
    public BdoButtonStyle ButtonStyle
    {
        get => _buttonStyle;
        set
        {
            if (_buttonStyle == value)
            {
                return;
            }

            _buttonStyle = value;
            Invalidate();
        }
    }

    [DefaultValue(10)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            var normalized = Math.Max(0, value);
            if (_cornerRadius == normalized)
            {
                return;
            }

            _cornerRadius = normalized;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaintBackground(pevent);
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var radius = Math.Max(1, (int)Math.Round(CornerRadius * DeviceDpi / 96d));

        var background = ResolveBackground();
        var foreground = ResolveForeground();
        var borderColor = ResolveBorder(background);

        using var path = BdoTheme.CreateRoundedRectangle(bounds, radius);
        using var fill = new SolidBrush(background);
        using var border = new Pen(borderColor, Math.Max(1f, DeviceDpi / 96f));
        pevent.Graphics.FillPath(fill, path);
        pevent.Graphics.DrawPath(border, path);

        TextRenderer.DrawText(
            pevent.Graphics,
            Text,
            Font,
            bounds,
            foreground,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            var focusBounds = Rectangle.Inflate(bounds, -3, -3);
            using var focusPath = BdoTheme.CreateRoundedRectangle(
                focusBounds, Math.Max(1, radius - 3));
            using var focusPen = new Pen(Color.FromArgb(225, BdoTheme.GoldBright),
                Math.Max(1f, DeviceDpi / 96f))
            {
                DashStyle = DashStyle.Dot
            };
            pevent.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Font.Dispose();
        }

        base.Dispose(disposing);
    }

    private Color ResolveBackground()
    {
        if (!Enabled)
        {
            return BdoTheme.SurfaceRaised;
        }

        if (ButtonStyle == BdoButtonStyle.Primary)
        {
            return _pressed
                ? BdoTheme.Gold
                : _hovered ? BdoTheme.GoldBright : BdoTheme.Gold;
        }

        if (ButtonStyle == BdoButtonStyle.Navigation)
        {
            if (Selected)
                return _pressed ? Color.FromArgb(117, 88, 46) :
                    _hovered ? Color.FromArgb(105, 82, 48) : Color.FromArgb(84, 67, 44);
            return _pressed ? BdoTheme.SurfacePressed :
                _hovered ? BdoTheme.SurfaceHover : BdoTheme.Surface;
        }

        return _pressed
            ? BdoTheme.SurfacePressed
            : _hovered ? BdoTheme.SurfaceHover : BdoTheme.SurfaceRaised;
    }

    private Color ResolveForeground()
    {
        if (!Enabled)
            return BdoTheme.TextMuted;
        if (ButtonStyle == BdoButtonStyle.Primary)
            return BdoTheme.Background;
        if (ButtonStyle == BdoButtonStyle.Navigation)
            return Selected ? BdoTheme.GoldBright : BdoTheme.TextMuted;
        return BdoTheme.Text;
    }

    private Color ResolveBorder(Color background)
    {
        if (!Enabled)
            return Color.FromArgb(110, BdoTheme.BorderSoft);
        if (ButtonStyle == BdoButtonStyle.Primary)
            return background;
        if (ButtonStyle == BdoButtonStyle.Navigation)
            return Selected ? Color.FromArgb(195, BdoTheme.Gold) :
                _hovered && Enabled ? BdoTheme.Border : BdoTheme.BorderSoft;
        return _hovered && Enabled ? BdoTheme.Gold : BdoTheme.Border;
    }
}

/// <summary>
/// Rounded dark surface used for the summary and loot areas.
/// </summary>
internal sealed class BdoSurfacePanel : Panel
{
    private int _cornerRadius = 16;

    public BdoSurfacePanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = BdoTheme.Surface;
    }

    [DefaultValue(16)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(0, value);
            UpdateShape();
            Invalidate();
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateShape();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width <= 1 || ClientSize.Height <= 1)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        var radius = Math.Max(1, (int)Math.Round(CornerRadius * DeviceDpi / 96d));
        using var path = BdoTheme.CreateRoundedRectangle(bounds, radius);
        using var border = new Pen(BdoTheme.BorderSoft, Math.Max(1f, DeviceDpi / 96f));
        e.Graphics.DrawPath(border, path);
    }

    private void UpdateShape()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var radius = Math.Max(1, (int)Math.Round(CornerRadius * DeviceDpi / 96d));
        using var path = BdoTheme.CreateRoundedRectangle(ClientRectangle, radius);
        var nextRegion = new Region(path);
        var previousRegion = Region;
        Region = nextRegion;
        previousRegion?.Dispose();
    }
}

internal static class BdoWindowChrome
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int RoundCornerPreference = 2;
    private const uint WdaNone = 0;

    public static void Apply(Form form)
    {
        if (!OperatingSystem.IsWindows() || !form.IsHandleCreated)
        {
            return;
        }

        try
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(
                form.Handle,
                DwmUseImmersiveDarkMode,
                ref enabled,
                sizeof(int));
            var cornerPreference = RoundCornerPreference;
            _ = DwmSetWindowAttribute(
                form.Handle,
                DwmWindowCornerPreference,
                ref cornerPreference,
                sizeof(int));

            // Keep ordinary screenshot behavior. WDA_EXCLUDEFROMCAPTURE also removes
            // this window from Snipping Tool's frozen desktop, making it look hidden.
            // Do not apply global capture exclusion just to style the window chrome.
            _ = SetWindowDisplayAffinity(form.Handle, WdaNone);
        }
        catch (DllNotFoundException)
        {
            // Older Windows versions keep the ordinary title bar.
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows versions keep the ordinary title bar.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(
        IntPtr windowHandle,
        uint affinity);
}
