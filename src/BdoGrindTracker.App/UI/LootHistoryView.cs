using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.UI;

internal sealed class LootHistoryView : UserControl
{
    private readonly FlowLayoutPanel _spotList = CreateSpotList();
    private readonly FlowLayoutPanel _spotDetailList = CreateList("Grindspot-Details im Loot-Verlauf");
    private readonly FlowLayoutPanel _chronologicalList = CreateList("Chronologischer Loot-Verlauf");
    private readonly Button _chronologicalButton = CreateModeButton("Chronologisch");
    private readonly Button _spotsButton = CreateModeButton("Nach Spots");
    private readonly ImageAssetRepository _backgrounds = new(
        Path.Combine(AppContext.BaseDirectory, "data", "spot-backgrounds"));
    private readonly ImageAssetRepository _spotIcons = new(
        Path.Combine(AppContext.BaseDirectory, "data", "spot-icons"));
    private readonly LootIconRepository _icons = new(
        Path.Combine(AppContext.BaseDirectory, "data", "icons"));
    private readonly Font _headingFont = new(
        "Segoe UI Semibold", 16f, FontStyle.Bold, GraphicsUnit.Point);
    private IReadOnlyList<LootHistoryEntry> _entries = [];
    private string? _selectedSpotId;
    private bool _showSpots = true;
    private bool _disposed;

    public LootHistoryView()
    {
        BackColor = BdoTheme.Background;
        ForeColor = BdoTheme.Text;
        AccessibleName = "Loot Verlauf";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
            BackColor = BdoTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(BuildHeader(), 0, 0);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = BdoTheme.Background
        };
        content.Controls.Add(_chronologicalList);
        content.Controls.Add(_spotDetailList);
        content.Controls.Add(_spotList);
        root.Controls.Add(content, 0, 1);
        Controls.Add(root);

        _chronologicalButton.Click += (_, _) => SetMode(showSpots: false);
        _spotsButton.Click += (_, _) => SetMode(showSpots: true);
        _spotList.Resize += (_, _) => FitSpotCards();
        _spotDetailList.Resize += (_, _) => FitChildren(_spotDetailList);
        _chronologicalList.Resize += (_, _) => FitChildren(_chronologicalList);
        SetMode(showSpots: true);
        SetEntries([]);
    }

    internal bool ShowsSpots => _showSpots;

    internal bool ShowsSpotDetails => _showSpots && _selectedSpotId is not null;

    internal string? SelectedSpotId => _selectedSpotId;

    internal int SpotCardCount => _spotList.Controls.OfType<SpotHistoryCard>().Count();

    internal int ChronologicalEntryCount =>
        _chronologicalList.Controls.OfType<ChronologicalHistoryCard>().Count();

    public void SetEntries(IEnumerable<LootHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries
            .OrderByDescending(static entry => entry.UpdatedAt)
            .ToArray();
        RebuildSpotList();
        RebuildSpotDetails();
        RebuildChronologicalList();
    }

    internal void ShowChronological() => SetMode(showSpots: false);

    internal void ShowSpots() => SetMode(showSpots: true);

    internal void ShowSpotDetails(string spotId)
    {
        var profile = LootSpotPresentationCatalog.GetRequired(spotId);
        _selectedSpotId = profile.SpotId;
        RebuildSpotDetails();
        SetMode(showSpots: true);
    }

    internal void ShowSpotOverview()
    {
        _selectedSpotId = null;
        DisposeChildren(_spotDetailList);
        SetMode(showSpots: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _headingFont.Dispose();
            _backgrounds.Dispose();
            _spotIcons.Dispose();
            _icons.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 12)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var text = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Left
        };
        text.Controls.Add(new Label
        {
            Text = "Loot Verlauf",
            AutoSize = true,
            Font = _headingFont,
            ForeColor = BdoTheme.Text,
            Margin = Padding.Empty
        }, 0, 0);
        text.Controls.Add(new Label
        {
            Text = "Deine lokal gespeicherten Grind-Stunden",
            AutoSize = true,
            ForeColor = BdoTheme.TextMuted,
            Margin = new Padding(1, 3, 0, 0)
        }, 0, 1);

        var modes = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
            Padding = new Padding(3),
            BackColor = BdoTheme.Surface
        };
        _chronologicalButton.AccessibleName = "Loot chronologisch anzeigen";
        _spotsButton.AccessibleName = "Loot nach Grindspots anzeigen";
        modes.Controls.Add(_chronologicalButton);
        modes.Controls.Add(_spotsButton);

        header.Controls.Add(text, 0, 0);
        header.Controls.Add(modes, 1, 0);
        return header;
    }

    private void SetMode(bool showSpots)
    {
        _showSpots = showSpots;
        var showDetails = showSpots && _selectedSpotId is not null;
        _spotList.Visible = showSpots && !showDetails;
        _spotDetailList.Visible = showDetails;
        _chronologicalList.Visible = !showSpots;
        StyleModeButton(_spotsButton, showSpots);
        StyleModeButton(_chronologicalButton, !showSpots);
        if (showDetails)
            FitChildren(_spotDetailList);
        else if (showSpots)
            FitSpotCards();
        else
            FitChildren(_chronologicalList);
    }

    private void RebuildSpotList()
    {
        DisposeChildren(_spotList);
        foreach (var profile in LootSpotPresentationCatalog.Profiles)
        {
            var sessions = _entries
                .Where(entry => string.Equals(entry.SpotId, profile.SpotId, StringComparison.Ordinal))
                .ToArray();
            var card = new SpotHistoryCard(
                profile,
                sessions,
                _backgrounds.Get(profile.BackgroundFileName),
                _spotIcons.Get(profile.IconFileName),
                _icons)
            {
                Margin = new Padding(0, 0, 11, 11)
            };
            card.Selected += (_, _) => ShowSpotDetails(profile.SpotId);
            _spotList.Controls.Add(card);
        }
        FitSpotCards();
    }

    private void RebuildSpotDetails()
    {
        DisposeChildren(_spotDetailList);
        if (_selectedSpotId is null)
            return;

        var profile = LootSpotPresentationCatalog.GetRequired(_selectedSpotId);
        var sessions = _entries
            .Where(entry => string.Equals(entry.SpotId, profile.SpotId, StringComparison.Ordinal))
            .OrderByDescending(static entry => entry.UpdatedAt)
            .ToArray();
        var details = new SpotHistoryDetailView(
            profile,
            sessions,
            _backgrounds.Get(profile.BackgroundFileName),
            _spotIcons.Get(profile.IconFileName),
            _icons)
        {
            Margin = new Padding(0, 0, 0, 11)
        };
        details.BackRequested += (_, _) => BeginInvoke(new Action(ShowSpotOverview));
        _spotDetailList.Controls.Add(details);
        FitChildren(_spotDetailList);
    }

    private void RebuildChronologicalList()
    {
        DisposeChildren(_chronologicalList);
        if (_entries.Count == 0)
        {
            _chronologicalList.Controls.Add(new Label
            {
                Text = "Noch keine Grind-Stunden gespeichert.\nSobald du eine Session pausierst oder beendest, erscheint sie hier.",
                AutoSize = false,
                Height = 92,
                ForeColor = BdoTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 24, 0, 0),
                AccessibleName = "Noch kein Loot-Verlauf"
            });
        }
        else
        {
            foreach (var entry in _entries)
            {
                var profile = LootSpotPresentationCatalog.GetRequired(entry.SpotId);
                _chronologicalList.Controls.Add(new ChronologicalHistoryCard(
                    entry,
                    profile,
                    _spotIcons.Get(profile.IconFileName),
                    _icons)
                {
                    Margin = new Padding(0, 0, 0, 9)
                });
            }
        }
        FitChildren(_chronologicalList);
    }

    private static FlowLayoutPanel CreateList(string accessibleName) => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        BackColor = BdoTheme.Background,
        Margin = Padding.Empty,
        Padding = new Padding(0, 0, 7, 0),
        AccessibleName = accessibleName
    };

    private static FlowLayoutPanel CreateSpotList() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        AutoScroll = true,
        BackColor = BdoTheme.Background,
        Margin = Padding.Empty,
        Padding = new Padding(0, 0, 7, 0),
        AccessibleName = "Kompakte Grindspot-Auswahl im Loot-Verlauf"
    };

    private static Button CreateModeButton(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Size = new Size(126, 34),
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
        UseVisualStyleBackColor = false,
        Cursor = Cursors.Hand,
        Margin = Padding.Empty
    };

    private static void StyleModeButton(Button button, bool selected)
    {
        button.BackColor = selected ? Color.FromArgb(91, 73, 48) : BdoTheme.Surface;
        button.ForeColor = selected ? BdoTheme.GoldBright : BdoTheme.TextMuted;
        button.AccessibleDescription = selected ? "Ausgewählt" : "Nicht ausgewählt";
    }

    private static void FitChildren(FlowLayoutPanel list)
    {
        if (list.ClientSize.Width <= 0)
            return;
        var width = Math.Max(260,
            list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
        foreach (Control control in list.Controls)
            control.Width = width;
    }

    private void FitSpotCards()
    {
        if (_spotList.ClientSize.Width <= 0)
            return;
        var available = Math.Max(260, _spotList.ClientSize.Width - _spotList.Padding.Horizontal -
            SystemInformation.VerticalScrollBarWidth - 2);
        var columns = available >= 720 ? 2 : 1;
        var gap = 11;
        var width = Math.Max(260, (available - columns * gap) / columns);
        foreach (Control control in _spotList.Controls)
            control.Width = width;
    }

    private static void DisposeChildren(Control parent)
    {
        var children = parent.Controls.Cast<Control>().ToArray();
        parent.Controls.Clear();
        foreach (var child in children)
            child.Dispose();
    }
}

internal sealed class SpotHistoryCard : Control
{
    private const int CollapsedLogicalHeight = 154;
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private readonly LootSpotPresentation _profile;
    private readonly IReadOnlyList<LootHistoryEntry> _sessions;
    private readonly Image? _background;
    private readonly Image? _spotIcon;
    private readonly LootIconRepository _icons;
    private Font? _titleFont;
    private Font? _captionFont;
    private Font? _valueFont;
    private Font? _traitFont;
    private Font? _bodyFont;

    public SpotHistoryCard(
        LootSpotPresentation profile,
        IReadOnlyList<LootHistoryEntry> sessions,
        Image? background,
        Image? spotIcon,
        LootIconRepository icons)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _background = background;
        _spotIcon = spotIcon;
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);
        BackColor = BdoTheme.Surface;
        ForeColor = BdoTheme.Text;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = $"{LootSpotCatalog.GetRequired(profile.SpotId).DisplayName}: Detailansicht öffnen";
        RecreateFonts();
        UpdateHeight();
        UpdateAccessibility();
    }

    internal LootSpotPresentation Profile => _profile;

    internal int SessionCount => _sessions.Count;

    public event EventHandler? Selected;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var collapsedHeight = ScaleLogical(CollapsedLogicalHeight);
        var headerBounds = new Rectangle(0, 0, Math.Max(1, Width - 1), collapsedHeight);
        using var cardPath = BdoTheme.CreateRoundedRectangle(
            new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)),
            ScaleLogical(10));
        var state = e.Graphics.Save();
        e.Graphics.SetClip(cardPath);
        using (var surface = new SolidBrush(BdoTheme.Surface))
            e.Graphics.FillPath(surface, cardPath);
        if (_background is not null)
            DrawCover(e.Graphics, _background, headerBounds);
        using (var veil = new SolidBrush(Color.FromArgb(44, BdoTheme.Background)))
            e.Graphics.FillRectangle(veil, headerBounds);
        using (var gradient = new LinearGradientBrush(headerBounds,
                   Color.FromArgb(10, BdoTheme.Background),
                   Color.FromArgb(245, BdoTheme.Background),
                   LinearGradientMode.Vertical))
            e.Graphics.FillRectangle(gradient, headerBounds);
        e.Graphics.Restore(state);

        DrawHeader(e.Graphics, headerBounds);
        using var border = new Pen(Focused ? BdoTheme.Gold : Color.FromArgb(101, 94, 78),
            Math.Max(1f, DeviceDpi / 96f));
        e.Graphics.DrawPath(border, cardPath);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
        {
            Focus();
            Selected?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Selected?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RecreateFonts();
        UpdateHeight();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeFonts();
        base.Dispose(disposing);
    }

    private void DrawHeader(Graphics graphics, Rectangle bounds)
    {
        var padding = ScaleLogical(14);
        var titleHeight = ScaleLogical(28);
        var chevronWidth = ScaleLogical(24);
        var countWidth = ScaleLogical(70);
        var spot = LootSpotCatalog.GetRequired(_profile.SpotId);

        var titleBand = new Rectangle(ScaleLogical(7), ScaleLogical(6),
            Math.Max(1, bounds.Width - ScaleLogical(14)), ScaleLogical(51));
        using (var titlePath = BdoTheme.CreateRoundedRectangle(titleBand, ScaleLogical(7)))
        using (var titleFill = new SolidBrush(Color.FromArgb(178, 10, 12, 14)))
        using (var titleBorder = new Pen(Color.FromArgb(86, 229, 194, 124)))
        {
            graphics.FillPath(titleFill, titlePath);
            graphics.DrawPath(titleBorder, titlePath);
        }

        var spotIconSize = ScaleLogical(44);
        var spotIconBounds = new Rectangle(padding, ScaleLogical(9), spotIconSize, spotIconSize);
        if (_spotIcon is not null)
            graphics.DrawImage(_spotIcon, spotIconBounds);
        else
            DrawIconFallback(graphics, spotIconBounds, "?");

        var titleX = spotIconBounds.Right + ScaleLogical(8);
        var titleBounds = new Rectangle(titleX, ScaleLogical(16),
            Math.Max(80, bounds.Width - titleX - padding - countWidth - chevronWidth), titleHeight);
        TextRenderer.DrawText(graphics, spot.DisplayName, _titleFont,
            new Rectangle(titleBounds.X + ScaleLogical(1), titleBounds.Y + ScaleLogical(2),
                titleBounds.Width, titleBounds.Height),
            Color.FromArgb(235, 3, 4, 5),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(graphics, spot.DisplayName, _titleFont, titleBounds,
            Color.FromArgb(255, 232, 185),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var totalHours = _sessions.Sum(static session => session.Duration.TotalHours);
        var countText = $"{totalHours:0.#}h · {_sessions.Count:N0}×";
        TextRenderer.DrawText(graphics, countText, _bodyFont,
            new Rectangle(bounds.Right - padding - countWidth - chevronWidth,
                ScaleLogical(18), countWidth, titleHeight),
            Color.FromArgb(202, 205, 205),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(graphics, "›", _titleFont,
            new Rectangle(bounds.Right - padding - chevronWidth, ScaleLogical(16),
                chevronWidth, titleHeight),
            BdoTheme.GoldBright,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        DrawTrash(graphics, bounds);
        DrawStat(graphics, padding, ScaleLogical(68), ScaleLogical(68),
            "REC. AP", _profile.RecommendedAp.ToString(CultureInfo.InvariantCulture) + "+",
            Color.FromArgb(231, 160, 95));
        DrawStat(graphics, padding + ScaleLogical(72), ScaleLogical(68), ScaleLogical(68),
            "MAX AP", _profile.MaxApLimit.ToString(CultureInfo.InvariantCulture),
            Color.FromArgb(240, 200, 111));
        DrawStat(graphics, padding + ScaleLogical(144), ScaleLogical(68), ScaleLogical(68),
            "REC. DP", _profile.RecommendedDp.ToString(CultureInfo.InvariantCulture) + "+",
            Color.FromArgb(131, 209, 153));
        DrawCompactTraits(graphics, padding, ScaleLogical(126), bounds.Width - padding * 2);
    }

    private void DrawTrash(Graphics graphics, Rectangle bounds)
    {
        var width = ScaleLogical(154);
        var height = ScaleLogical(49);
        var right = ScaleLogical(14);
        var box = new Rectangle(bounds.Right - right - width, ScaleLogical(65), width, height);
        using var path = BdoTheme.CreateRoundedRectangle(box, ScaleLogical(6));
        using var fill = new SolidBrush(Color.FromArgb(224, 18, 21, 22));
        using var border = new Pen(Color.FromArgb(118, 217, 186, 121));
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
        var iconBounds = new Rectangle(box.X + ScaleLogical(7), box.Y + ScaleLogical(7),
            ScaleLogical(35), ScaleLogical(35));
        var trashIcon = _icons.GetIcon(_profile.TrashItemName);
        if (trashIcon is not null)
            graphics.DrawImage(trashIcon, iconBounds);
        else
            DrawIconFallback(graphics, iconBounds, "TL");
        TextRenderer.DrawText(graphics, _profile.TrashItemName, _captionFont,
            new Rectangle(iconBounds.Right + ScaleLogical(7), box.Y + ScaleLogical(4),
                box.Right - iconBounds.Right - ScaleLogical(11), ScaleLogical(18)),
            Color.FromArgb(200, 203, 201),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(graphics,
            $"{_profile.TrashSilver.ToString("N0", GermanCulture)} Silber", _valueFont,
            new Rectangle(iconBounds.Right + ScaleLogical(7), box.Y + ScaleLogical(21),
                box.Right - iconBounds.Right - ScaleLogical(11), ScaleLogical(23)),
            BdoTheme.GoldBright,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void DrawStat(Graphics graphics, int x, int y, int width, string label, string value, Color color)
    {
        TextRenderer.DrawText(graphics, label, _captionFont,
            new Rectangle(x, y, width, ScaleLogical(17)),
            Color.FromArgb(174, 179, 179),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(graphics, value, _valueFont,
            new Rectangle(x, y + ScaleLogical(17), width, ScaleLogical(25)),
            color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawCompactTraits(Graphics graphics, int startX, int startY, int availableWidth)
    {
        var x = startX;
        var height = ScaleLogical(20);
        var hidden = 0;
        for (var index = 0; index < _profile.Traits.Count; index++)
        {
            var trait = _profile.Traits[index];
            var textSize = TextRenderer.MeasureText(graphics, trait, _traitFont,
                Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            var width = textSize.Width + ScaleLogical(12);
            if (x + width > startX + availableWidth - ScaleLogical(30))
            {
                hidden = _profile.Traits.Count - index;
                break;
            }
            var chip = new Rectangle(x, startY, width, height);
            var (foreground, background) = ResolveTraitColors(trait);
            using var path = BdoTheme.CreateRoundedRectangle(chip, height / 2);
            using var fill = new SolidBrush(background);
            graphics.FillPath(fill, path);
            TextRenderer.DrawText(graphics, trait, _traitFont, chip, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            x += width + ScaleLogical(4);
        }
        if (hidden > 0)
        {
            TextRenderer.DrawText(graphics, $"+{hidden}", _traitFont,
                new Rectangle(x, startY, ScaleLogical(28), height), BdoTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void UpdateHeight()
    {
        var collapsed = ScaleLogical(CollapsedLogicalHeight);
        MinimumSize = new Size(ScaleLogical(260), collapsed);
        Height = collapsed;
    }

    private void UpdateAccessibility()
    {
        AccessibleDescription = $"{_sessions.Count:N0} gespeicherte Stunden. Öffnet die maximierte Detailansicht.";
    }

    private void RecreateFonts()
    {
        DisposeFonts();
        var scale = DeviceDpi / 96f;
        _titleFont = new Font("Georgia", 13.5f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _captionFont = new Font("Segoe UI Semibold", 7.5f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _valueFont = new Font("Segoe UI Semibold", 10f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _traitFont = new Font("Segoe UI Semibold", 8f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _bodyFont = new Font("Segoe UI", 9f * scale, FontStyle.Regular, GraphicsUnit.Point);
    }

    private void DisposeFonts()
    {
        _titleFont?.Dispose();
        _captionFont?.Dispose();
        _valueFont?.Dispose();
        _traitFont?.Dispose();
        _bodyFont?.Dispose();
    }

    private int ScaleLogical(int pixels) => Math.Max(1, (int)Math.Round(pixels * DeviceDpi / 96d));

    private static void DrawCover(Graphics graphics, Image image, Rectangle destination)
    {
        var scale = Math.Max((double)destination.Width / image.Width,
            (double)destination.Height / image.Height);
        var sourceWidth = destination.Width / scale;
        var sourceHeight = destination.Height / scale;
        var source = new RectangleF(
            (float)((image.Width - sourceWidth) / 2d),
            (float)((image.Height - sourceHeight) / 2d),
            (float)sourceWidth,
            (float)sourceHeight);
        graphics.DrawImage(image, destination, source.X, source.Y, source.Width, source.Height,
            GraphicsUnit.Pixel);
    }

    private static void DrawIconFallback(Graphics graphics, Rectangle bounds, string text)
    {
        using var fill = new SolidBrush(BdoTheme.SurfaceRaised);
        using var border = new Pen(BdoTheme.Border);
        graphics.FillRectangle(fill, bounds);
        graphics.DrawRectangle(border, bounds);
        TextRenderer.DrawText(graphics, text, SystemFonts.MessageBoxFont, bounds,
            BdoTheme.Gold, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private static (Color Foreground, Color Background) ResolveTraitColors(string trait) => trait switch
    {
        "#CombatEXP" => (Color.FromArgb(131, 205, 208), Color.FromArgb(205, 48, 101, 105)),
        "#MarnisRealmPrivate" => (Color.FromArgb(242, 200, 89), Color.FromArgb(210, 103, 77, 27)),
        "#Knockdown/Bound" or "#Knockback/Floating" or "#Stun/Stiffness/Freezing" =>
            (Color.FromArgb(243, 173, 50), Color.FromArgb(210, 105, 57, 10)),
        "#AllanSerbinsLandscape" or "#PartyOf3" =>
            (Color.FromArgb(143, 227, 162), Color.FromArgb(210, 35, 93, 51)),
        "#HighestTier" => (Color.FromArgb(222, 139, 139), Color.FromArgb(210, 102, 48, 52)),
        "#DivineAuthority" => (Color.FromArgb(178, 154, 230), Color.FromArgb(214, 69, 52, 105)),
        "#FeverPowerfulMobs" => (Color.FromArgb(215, 144, 170), Color.FromArgb(214, 91, 48, 65)),
        _ => (BdoTheme.TextMuted, BdoTheme.SurfaceRaised)
    };

    internal static string FormatDuration(TimeSpan duration) =>
        GrindSessionClock.FormatElapsed(duration);

    internal static string FormatSilver(decimal silver)
    {
        var absolute = Math.Abs(silver);
        if (absolute >= 1_000_000_000m)
            return (silver / 1_000_000_000m).ToString("0.##", GermanCulture) + " Mrd.";
        if (absolute >= 1_000_000m)
            return (silver / 1_000_000m).ToString("0.#", GermanCulture) + " Mio.";
        return decimal.Truncate(silver).ToString("N0", GermanCulture);
    }

    internal static string FormatQuantity(long quantity)
    {
        var absolute = Math.Abs((decimal)quantity);
        if (absolute >= 1_000_000m)
            return (quantity / 1_000_000m).ToString("0.#", GermanCulture) + "M";
        if (absolute >= 100_000m)
            return (quantity / 1_000m).ToString("0.#", GermanCulture) + "K";
        return quantity.ToString("N0", GermanCulture);
    }
}

internal sealed class ChronologicalHistoryCard : Control
{
    private const int HeaderLogicalHeight = 72;
    private const int MaximumVisibleItems = 12;
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private readonly LootHistoryEntry _entry;
    private readonly LootSpotPresentation _profile;
    private readonly Image? _spotIcon;
    private readonly LootIconRepository _icons;
    private Font? _titleFont;
    private Font? _bodyFont;
    private Font? _captionFont;
    private Font? _valueFont;
    private bool _expanded;

    public ChronologicalHistoryCard(
        LootHistoryEntry entry,
        LootSpotPresentation profile,
        Image? spotIcon,
        LootIconRepository icons)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _spotIcon = spotIcon;
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);
        BackColor = BdoTheme.Surface;
        ForeColor = BdoTheme.Text;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = $"Grind-Stunde in {LootSpotCatalog.GetRequired(profile.SpotId).DisplayName} aufklappen";
        RecreateFonts();
        UpdateHeight();
        UpdateAccessibility();
    }

    internal LootHistoryEntry Entry => _entry;

    internal bool IsExpanded => _expanded;

    internal void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
            return;
        _expanded = expanded;
        UpdateHeight();
        UpdateAccessibility();
        Invalidate();
        Parent?.PerformLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = BdoTheme.CreateRoundedRectangle(bounds, ScaleLogical(8));
        using var fill = new SolidBrush(BdoTheme.Surface);
        using var border = new Pen(Focused ? BdoTheme.Gold : BdoTheme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        DrawHeader(e.Graphics);
        if (_expanded)
            DrawDetails(e.Graphics);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
        {
            Focus();
            SetExpanded(!_expanded);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            SetExpanded(!_expanded);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RecreateFonts();
        UpdateHeight();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont?.Dispose();
            _bodyFont?.Dispose();
            _captionFont?.Dispose();
            _valueFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void DrawHeader(Graphics graphics)
    {
        var padding = ScaleLogical(15);
        var dateWidth = ScaleLogical(126);
        var durationWidth = ScaleLogical(100);
        var silverWidth = ScaleLogical(130);
        var chevronWidth = ScaleLogical(24);
        var headerHeight = ScaleLogical(HeaderLogicalHeight);
        var date = _entry.UpdatedAt.ToLocalTime();
        TextRenderer.DrawText(graphics, date.ToString("dd.MM.yy", GermanCulture), _titleFont,
            new Rectangle(padding, ScaleLogical(12), dateWidth, ScaleLogical(25)),
            BdoTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(graphics, date.ToString("HH:mm 'Uhr'", GermanCulture), _captionFont,
            new Rectangle(padding, ScaleLogical(36), dateWidth, ScaleLogical(20)),
            BdoTheme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var spotX = padding + dateWidth;
        var spotWidth = Math.Max(100, Width - spotX - durationWidth - silverWidth - chevronWidth - padding);
        var spotIconSize = ScaleLogical(46);
        var spotIconBounds = new Rectangle(spotX, ScaleLogical(13), spotIconSize, spotIconSize);
        if (_spotIcon is not null)
            graphics.DrawImage(_spotIcon, spotIconBounds);
        var spotTextX = spotIconBounds.Right + ScaleLogical(8);
        var spotTextWidth = Math.Max(50, spotWidth - spotIconSize - ScaleLogical(8));
        var spotName = LootSpotCatalog.GetRequired(_entry.SpotId).DisplayName;
        TextRenderer.DrawText(graphics, spotName, _titleFont,
            new Rectangle(spotTextX, ScaleLogical(10), spotTextWidth, ScaleLogical(28)),
            Color.FromArgb(255, 226, 172), TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var trash = _entry.Totals.GetValueOrDefault(_profile.TrashItemName);
        TextRenderer.DrawText(graphics, $"{trash.ToString("N0", GermanCulture)} Trash", _captionFont,
            new Rectangle(spotTextX, ScaleLogical(38), spotTextWidth, ScaleLogical(19)),
            BdoTheme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var durationX = spotX + spotWidth;
        TextRenderer.DrawText(graphics, SpotHistoryCard.FormatDuration(_entry.Duration), _bodyFont,
            new Rectangle(durationX, 0, durationWidth, headerHeight),
            BdoTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        var silverX = durationX + durationWidth;
        TextRenderer.DrawText(graphics, SpotHistoryCard.FormatSilver(_entry.SilverAfterTax), _valueFont,
            new Rectangle(silverX, 0, silverWidth, headerHeight),
            BdoTheme.GoldBright, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(graphics, _expanded ? "⌃" : "⌄", _titleFont,
            new Rectangle(silverX + silverWidth, 0, chevronWidth, headerHeight),
            BdoTheme.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawDetails(Graphics graphics)
    {
        var headerHeight = ScaleLogical(HeaderLogicalHeight);
        using var separator = new Pen(BdoTheme.BorderSoft);
        graphics.DrawLine(separator, ScaleLogical(15), headerHeight,
            Width - ScaleLogical(15), headerHeight);
        var classText = string.IsNullOrWhiteSpace(_entry.CharacterClass)
            ? "LOOT DER STUNDE"
            : $"LOOT DER STUNDE  ·  {_entry.CharacterClass}";
        TextRenderer.DrawText(graphics, classText, _captionFont,
            new Rectangle(ScaleLogical(15), headerHeight + ScaleLogical(8),
                Width - ScaleLogical(30), ScaleLogical(22)),
            BdoTheme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var items = _entry.Totals
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaximumVisibleItems)
            .ToArray();
        var columnWidth = (Width - ScaleLogical(45)) / 2;
        var startY = headerHeight + ScaleLogical(34);
        for (var index = 0; index < items.Length; index++)
        {
            var column = index % 2;
            var row = index / 2;
            var x = ScaleLogical(15) + column * (columnWidth + ScaleLogical(15));
            var y = startY + row * ScaleLogical(36);
            DrawLootItem(graphics, items[index], new Rectangle(x, y, columnWidth, ScaleLogical(32)));
        }
        if (_entry.Totals.Count > items.Length)
        {
            TextRenderer.DrawText(graphics, $"+ {_entry.Totals.Count - items.Length:N0} weitere Items", _captionFont,
                new Rectangle(ScaleLogical(15), Height - ScaleLogical(29),
                    Width - ScaleLogical(30), ScaleLogical(20)),
                BdoTheme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawLootItem(Graphics graphics, KeyValuePair<string, long> item, Rectangle bounds)
    {
        var iconSize = ScaleLogical(28);
        var iconBounds = new Rectangle(bounds.X, bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
        var icon = _icons.GetIcon(item.Key);
        if (icon is not null)
            graphics.DrawImage(icon, iconBounds);
        else
            SpotHistoryCardDrawFallback(graphics, iconBounds);
        var quantityWidth = ScaleLogical(78);
        TextRenderer.DrawText(graphics, item.Key, _bodyFont,
            new Rectangle(iconBounds.Right + ScaleLogical(7), bounds.Y,
                Math.Max(40, bounds.Width - iconSize - quantityWidth - ScaleLogical(11)), bounds.Height),
            BdoTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(graphics, "×" + item.Value.ToString("N0", GermanCulture), _valueFont,
            new Rectangle(bounds.Right - quantityWidth, bounds.Y, quantityWidth, bounds.Height),
            BdoTheme.GoldBright, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void UpdateHeight()
    {
        var header = ScaleLogical(HeaderLogicalHeight);
        if (!_expanded)
        {
            MinimumSize = new Size(ScaleLogical(500), header);
            Height = header;
            return;
        }
        var displayed = Math.Min(_entry.Totals.Count, MaximumVisibleItems);
        var rows = Math.Max(1, (displayed + 1) / 2);
        var expanded = header + ScaleLogical(40 + rows * 36 + (_entry.Totals.Count > displayed ? 25 : 8));
        MinimumSize = new Size(ScaleLogical(500), expanded);
        Height = expanded;
    }

    private void UpdateAccessibility()
    {
        AccessibleDescription = _expanded
            ? $"Ausgeklappt. {_entry.Totals.Count:N0} Loot-Arten."
            : "Eingeklappt. Für Loot-Details aktivieren.";
    }

    private void RecreateFonts()
    {
        _titleFont?.Dispose();
        _bodyFont?.Dispose();
        _captionFont?.Dispose();
        _valueFont?.Dispose();
        var scale = DeviceDpi / 96f;
        _titleFont = new Font("Segoe UI Semibold", 10f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _bodyFont = new Font("Segoe UI", 9f * scale, FontStyle.Regular, GraphicsUnit.Point);
        _captionFont = new Font("Segoe UI Semibold", 7.5f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _valueFont = new Font("Segoe UI Semibold", 9f * scale, FontStyle.Bold, GraphicsUnit.Point);
    }

    private int ScaleLogical(int pixels) => Math.Max(1, (int)Math.Round(pixels * DeviceDpi / 96d));

    private static void SpotHistoryCardDrawFallback(Graphics graphics, Rectangle bounds)
    {
        using var fill = new SolidBrush(BdoTheme.SurfaceRaised);
        graphics.FillRectangle(fill, bounds);
    }
}

internal sealed class ImageAssetRepository(string assetDirectory) : IDisposable
{
    private readonly string _assetDirectory = assetDirectory ??
        throw new ArgumentNullException(nameof(assetDirectory));
    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public Bitmap? Get(string fileName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (_cache.TryGetValue(fileName, out var cached))
            return cached;

        Bitmap? bitmap = null;
        var path = Path.Combine(_assetDirectory, fileName);
        try
        {
            if (File.Exists(path))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var source = Image.FromStream(stream, useEmbeddedColorManagement: false,
                    validateImageData: true);
                bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.DrawImageUnscaled(source, 0, 0);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or OutOfMemoryException)
        {
            bitmap?.Dispose();
            bitmap = null;
        }
        _cache[fileName] = bitmap;
        return bitmap;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var image in _cache.Values)
            image?.Dispose();
        _cache.Clear();
    }
}
