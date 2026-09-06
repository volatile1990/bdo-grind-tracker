using System.Drawing.Drawing2D;
using System.Globalization;
using System.ComponentModel;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.UI;

internal sealed record SpotHistoryMetrics(
    decimal TotalSilver,
    decimal AverageSilverPerHour,
    decimal TrashPerHour,
    decimal RecentFiveHourTrashPerHour,
    decimal BestFiveHourTrashPerHour,
    decimal TotalHours);

internal sealed class SpotHistoryDetailView : Control
{
    private const int HeaderLogicalHeight = 248;
    private const int TableHeaderLogicalHeight = 62;
    private const int RowLogicalHeight = 54;
    private const int LootColumnLogicalWidth = 54;
    private const int ScrollBarLogicalHeight = 22;
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Color[] ClassColors =
    [
        Color.FromArgb(126, 78, 168),
        Color.FromArgb(56, 131, 154),
        Color.FromArgb(155, 74, 82),
        Color.FromArgb(66, 139, 95),
        Color.FromArgb(166, 112, 49),
        Color.FromArgb(87, 100, 163)
    ];

    private readonly LootSpotPresentation _profile;
    private readonly IReadOnlyList<LootHistoryEntry> _sessions;
    private readonly Image? _background;
    private readonly Image? _spotIcon;
    private readonly Image? _crystalIcon;
    private readonly ImageAssetRepository _classIcons;
    private readonly LootIconRepository _icons;
    private readonly SpotHistoryMetrics _metrics;
    private readonly IReadOnlyList<KeyValuePair<string, long>> _lootItems;
    private readonly BdoHorizontalScrollBar _lootScroll = new()
    {
        TabStop = true,
        AccessibleName = "Loot-Tabelle horizontal scrollen"
    };
    private Font? _titleFont;
    private Font? _subtitleFont;
    private Font? _captionFont;
    private Font? _metricFont;
    private Font? _valueFont;
    private Font? _bodyFont;
    private Font? _smallFont;
    private Rectangle _backBounds;

    public SpotHistoryDetailView(
        LootSpotPresentation profile,
        IReadOnlyList<LootHistoryEntry> sessions,
        Image? background,
        Image? spotIcon,
        Image? crystalIcon,
        LootPriceSnapshot prices,
        SilverTaxOptions tax,
        ImageAssetRepository classIcons,
        LootIconRepository icons)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _background = background;
        _spotIcon = spotIcon;
        _crystalIcon = crystalIcon;
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(tax);
        _classIcons = classIcons ?? throw new ArgumentNullException(nameof(classIcons));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _metrics = CalculateMetrics(profile, sessions);
        _lootItems = BuildLootItems(profile, sessions, prices, tax);
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);
        BackColor = BdoTheme.Surface;
        ForeColor = BdoTheme.Text;
        TabStop = true;
        AccessibleRole = AccessibleRole.Pane;
        AccessibleName = $"Maximierte Grindspot-Details für {LootSpotCatalog.GetRequired(profile.SpotId).DisplayName}";
        AccessibleDescription = $"{sessions.Count:N0} Grindstunden, Kennzahlen und Loot-Tabelle. Escape führt zur Übersicht zurück.";
        _lootScroll.ValueChanged += (_, _) => Invalidate();
        Controls.Add(_lootScroll);
        RecreateFonts();
        UpdateHeight();
        UpdateLootScrollBar();
    }

    public event EventHandler? BackRequested;

    internal LootSpotPresentation Profile => _profile;

    internal IReadOnlyList<LootHistoryEntry> Sessions => _sessions;

    internal SpotHistoryMetrics Metrics => _metrics;

    internal string DisplayedCrystalLabel => _profile.RecommendedCrystalName;

    internal IReadOnlyList<string> DisplayedTraitLabels => _profile.Traits;

    internal IReadOnlyList<string> LootItemNames => _lootItems.Select(static item => item.Key).ToArray();

    internal bool HasScrollableLootOverflow =>
        _lootScroll.Maximum > 0;

    internal Rectangle LootScrollBounds => _lootScroll.Bounds;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int LootScrollValue
    {
        get => _lootScroll.Value;
        set => _lootScroll.Value = value;
    }

    internal int LootScrollMaximum => _lootScroll.Maximum;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var cardPath = BdoTheme.CreateRoundedRectangle(bounds, ScaleLogical(10));
        using (var fill = new SolidBrush(BdoTheme.Surface))
            e.Graphics.FillPath(fill, cardPath);

        var state = e.Graphics.Save();
        e.Graphics.SetClip(cardPath);
        var headerBounds = new Rectangle(0, 0, bounds.Width, ScaleLogical(HeaderLogicalHeight));
        if (_background is not null)
            DrawCover(e.Graphics, _background, headerBounds);
        using (var veil = new SolidBrush(Color.FromArgb(118, 7, 9, 11)))
            e.Graphics.FillRectangle(veil, headerBounds);
        using (var gradient = new LinearGradientBrush(headerBounds,
                   Color.FromArgb(24, BdoTheme.Background),
                   Color.FromArgb(247, BdoTheme.Background),
                   LinearGradientMode.Vertical))
            e.Graphics.FillRectangle(gradient, headerBounds);
        e.Graphics.Restore(state);

        DrawHeader(e.Graphics, headerBounds);
        DrawTable(e.Graphics, headerBounds.Bottom);
        using var border = new Pen(Focused ? BdoTheme.Gold : Color.FromArgb(105, 94, 75),
            Math.Max(1f, DeviceDpi / 96f));
        e.Graphics.DrawPath(border, cardPath);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && _backBounds.Contains(e.Location))
        {
            Focus();
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Escape or Keys.Back)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
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
        UpdateLootScrollBar();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateLootScrollBar();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeFonts();
        base.Dispose(disposing);
    }

    private void DrawHeader(Graphics graphics, Rectangle bounds)
    {
        var padding = ScaleLogical(15);
        _backBounds = new Rectangle(padding, ScaleLogical(15), ScaleLogical(92), ScaleLogical(32));
        using (var path = BdoTheme.CreateRoundedRectangle(_backBounds, ScaleLogical(6)))
        using (var fill = new SolidBrush(Color.FromArgb(212, 18, 21, 24)))
        using (var border = new Pen(Color.FromArgb(105, 210, 184, 126)))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }
        TextRenderer.DrawText(graphics, "‹  Übersicht", _bodyFont, _backBounds,
            BdoTheme.GoldBright, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix);

        var iconSize = ScaleLogical(58);
        var iconBounds = new Rectangle(_backBounds.Right + ScaleLogical(12), ScaleLogical(7), iconSize, iconSize);
        if (_spotIcon is not null)
            graphics.DrawImage(_spotIcon, iconBounds);

        var name = LootSpotCatalog.GetRequired(_profile.SpotId).DisplayName;
        var titleX = iconBounds.Right + ScaleLogical(10);
        TextRenderer.DrawText(graphics, name, _titleFont,
            new Rectangle(titleX + ScaleLogical(1), ScaleLogical(13),
                Math.Max(100, bounds.Width - titleX - padding), ScaleLogical(32)),
            Color.FromArgb(235, 3, 4, 5), TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(graphics, name, _titleFont,
            new Rectangle(titleX, ScaleLogical(11),
                Math.Max(100, bounds.Width - titleX - padding), ScaleLogical(32)),
            Color.FromArgb(255, 232, 185), TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var summary = $"{_metrics.TotalHours:0.#} Std.   ·   {_sessions.Count:N0} Sessions   ·   " +
                      $"{_profile.RecommendedAp}+ AP   ·   {_profile.MaxApLimit} Max AP   ·   {_profile.RecommendedDp}+ DP";
        TextRenderer.DrawText(graphics, summary, _subtitleFont,
            new Rectangle(titleX, ScaleLogical(43), Math.Max(100, bounds.Width - titleX - padding), ScaleLogical(25)),
            Color.FromArgb(211, 215, 213), TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        DrawTraitLine(graphics, padding, ScaleLogical(78), bounds.Width - padding * 2);
        DrawMetrics(graphics, new Rectangle(padding, ScaleLogical(112),
            Math.Max(1, bounds.Width - padding * 2), ScaleLogical(112)));
    }

    private void DrawTraitLine(Graphics graphics, int startX, int y, int availableWidth)
    {
        var x = startX;
        var height = ScaleLogical(22);
        var crystalTextSize = TextRenderer.MeasureText(graphics, DisplayedCrystalLabel, _captionFont,
            Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        var crystalWidth = crystalTextSize.Width + height + ScaleLogical(13);
        if (x + crystalWidth <= startX + availableWidth)
        {
            DrawCrystalTrait(graphics, new Rectangle(x, y, crystalWidth, height), DisplayedCrystalLabel);
            x += crystalWidth + ScaleLogical(5);
        }

        foreach (var trait in DisplayedTraitLabels)
        {
            var size = TextRenderer.MeasureText(graphics, trait, _captionFont,
                Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            var width = size.Width + ScaleLogical(13);
            if (x + width > startX + availableWidth)
                break;
            var chip = new Rectangle(x, y, width, height);
            using var path = BdoTheme.CreateRoundedRectangle(chip, height / 2);
            var (foreground, background) = SpotHistoryCard.ResolveTraitColors(trait);
            using var fill = new SolidBrush(background);
            graphics.FillPath(fill, path);
            TextRenderer.DrawText(graphics, trait, _captionFont, chip,
                foreground, TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            x += width + ScaleLogical(5);
        }
    }

    private void DrawCrystalTrait(Graphics graphics, Rectangle bounds, string text)
    {
        using var path = BdoTheme.CreateRoundedRectangle(bounds, bounds.Height / 2);
        using var fill = new SolidBrush(Color.FromArgb(222, 35, 61, 82));
        using var border = new Pen(Color.FromArgb(165, 107, 184, 226));
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
        var iconBounds = new Rectangle(bounds.X + ScaleLogical(2), bounds.Y + ScaleLogical(2),
            bounds.Height - ScaleLogical(4), bounds.Height - ScaleLogical(4));
        if (_crystalIcon is not null)
            graphics.DrawImage(_crystalIcon, iconBounds);
        TextRenderer.DrawText(graphics, text, _captionFont,
            new Rectangle(iconBounds.Right + ScaleLogical(4), bounds.Y,
                bounds.Right - iconBounds.Right - ScaleLogical(7), bounds.Height),
            Color.FromArgb(206, 232, 247), TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void DrawMetrics(Graphics graphics, Rectangle bounds)
    {
        var gap = ScaleLogical(7);
        var width = Math.Max(60, (bounds.Width - gap * 4) / 5);
        var metrics = new (string Label, string Value, Color Accent)[]
        {
            ("GESAMT SILBER", SpotHistoryCard.FormatSilver(_metrics.TotalSilver), Color.FromArgb(223, 103, 105)),
            ("Ø SILBER / STUNDE", SpotHistoryCard.FormatSilver(_metrics.AverageSilverPerHour), Color.FromArgb(225, 166, 82)),
            ("TRASH / STUNDE", FormatMetricQuantity(_metrics.TrashPerHour), Color.FromArgb(106, 184, 151)),
            ("Ø TRASH · LETZTE 5H", FormatMetricQuantity(_metrics.RecentFiveHourTrashPerHour), Color.FromArgb(92, 154, 202)),
            ("Ø TRASH · BESTE 5H", FormatMetricQuantity(_metrics.BestFiveHourTrashPerHour), Color.FromArgb(169, 116, 203))
        };

        for (var index = 0; index < metrics.Length; index++)
        {
            var x = bounds.X + index * (width + gap);
            var metricBounds = new Rectangle(x, bounds.Y, width, bounds.Height);
            using var path = BdoTheme.CreateRoundedRectangle(metricBounds, ScaleLogical(7));
            using var fill = new SolidBrush(Color.FromArgb(218, 20, 23, 26));
            using var border = new Pen(Color.FromArgb(70, metrics[index].Accent));
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            using var accent = new SolidBrush(metrics[index].Accent);
            graphics.FillRectangle(accent, metricBounds.X, metricBounds.Bottom - ScaleLogical(3),
                metricBounds.Width, ScaleLogical(3));
            TextRenderer.DrawText(graphics, metrics[index].Label, _metricFont,
                new Rectangle(metricBounds.X + ScaleLogical(10), metricBounds.Y + ScaleLogical(11),
                    metricBounds.Width - ScaleLogical(20), ScaleLogical(28)),
                Color.FromArgb(173, 182, 181), TextFormatFlags.Left | TextFormatFlags.Top |
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(graphics, metrics[index].Value, _valueFont,
                new Rectangle(metricBounds.X + ScaleLogical(10), metricBounds.Y + ScaleLogical(48),
                    metricBounds.Width - ScaleLogical(20), ScaleLogical(34)),
                BdoTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawTable(Graphics graphics, int startY)
    {
        var padding = ScaleLogical(15);
        var (classWidth, ageWidth, durationWidth, silverWidth) = GetFixedColumnWidths();
        var tableWidth = Math.Max(1, Width - padding * 2);
        var fixedWidth = classWidth + ageWidth + durationWidth + silverWidth;
        var lootWidth = Math.Max(1, tableWidth - fixedWidth);
        var itemWidth = ScaleLogical(LootColumnLogicalWidth);
        var headerHeight = ScaleLogical(TableHeaderLogicalHeight);
        var headerBounds = new Rectangle(padding, startY, tableWidth, headerHeight);
        using (var fill = new SolidBrush(Color.FromArgb(240, 25, 28, 31)))
            graphics.FillRectangle(fill, headerBounds);
        using var gridPen = new Pen(Color.FromArgb(72, 78, 79));
        graphics.DrawLine(gridPen, padding, startY, Width - padding, startY);
        graphics.DrawLine(gridPen, padding, headerBounds.Bottom, Width - padding, headerBounds.Bottom);

        var x = padding;
        DrawColumnHeader(graphics, "KLASSE", new Rectangle(x, startY, classWidth, headerHeight));
        x += classWidth;
        DrawColumnHeader(graphics, "WIE LANGE HER", new Rectangle(x, startY, ageWidth, headerHeight));
        x += ageWidth;
        DrawColumnHeader(graphics, "GRINDZEIT", new Rectangle(x, startY, durationWidth, headerHeight));
        x += durationWidth;
        DrawColumnHeader(graphics, "SILBER / H", new Rectangle(x, startY, silverWidth, headerHeight));
        x += silverWidth;
        var lootViewport = new Rectangle(x, startY, lootWidth, headerHeight);
        if (_lootItems.Count == 0)
        {
            DrawColumnHeader(graphics, "LOOT TABLE", lootViewport);
        }
        else
        {
            var clipState = graphics.Save();
            graphics.SetClip(lootViewport, CombineMode.Intersect);
            var lootX = lootViewport.X - _lootScroll.Value;
            foreach (var item in _lootItems)
            {
                DrawLootHeader(graphics, item.Key, new Rectangle(lootX, startY, itemWidth, headerHeight));
                lootX += itemWidth;
            }
            graphics.Restore(clipState);
        }
        using (var divider = new Pen(Color.FromArgb(95, 104, 103)))
            graphics.DrawLine(divider, lootViewport.X, startY, lootViewport.X, headerBounds.Bottom);

        if (_sessions.Count == 0)
        {
            TextRenderer.DrawText(graphics, "Noch keine Grindstunden an diesem Spot gespeichert.", _bodyFont,
                new Rectangle(padding, headerBounds.Bottom, tableWidth, ScaleLogical(82)),
                BdoTheme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
            return;
        }

        var rowY = headerBounds.Bottom;
        for (var row = 0; row < _sessions.Count; row++)
        {
            DrawSessionRow(graphics, _sessions[row], _lootItems, itemWidth,
                new Rectangle(padding, rowY, tableWidth, ScaleLogical(RowLogicalHeight)),
                classWidth, ageWidth, durationWidth, silverWidth, row % 2 == 1);
            rowY += ScaleLogical(RowLogicalHeight);
        }
    }

    private void DrawSessionRow(
        Graphics graphics,
        LootHistoryEntry session,
        IReadOnlyList<KeyValuePair<string, long>> items,
        int itemWidth,
        Rectangle bounds,
        int classWidth,
        int ageWidth,
        int durationWidth,
        int silverWidth,
        bool alternate)
    {
        using (var fill = new SolidBrush(alternate
                   ? Color.FromArgb(232, 29, 32, 35)
                   : Color.FromArgb(224, 23, 26, 29)))
            graphics.FillRectangle(fill, bounds);
        using var line = new Pen(Color.FromArgb(58, 68, 70));
        graphics.DrawLine(line, bounds.X, bounds.Bottom, bounds.Right, bounds.Bottom);

        var x = bounds.X;
        DrawClassSymbol(graphics, session.CharacterClass,
            new Rectangle(x, bounds.Y, classWidth, bounds.Height));
        x += classWidth;
        TextRenderer.DrawText(graphics, FormatTimeAgo(session.UpdatedAt, DateTimeOffset.Now), _bodyFont,
            new Rectangle(x + ScaleLogical(4), bounds.Y, ageWidth - ScaleLogical(8), bounds.Height),
            Color.FromArgb(198, 204, 204), TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        x += ageWidth;
        TextRenderer.DrawText(graphics, SpotHistoryCard.FormatDuration(session.Duration), _bodyFont,
            new Rectangle(x, bounds.Y, durationWidth, bounds.Height), BdoTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        x += durationWidth;
        var hourlySilver = session.Duration.TotalHours <= 0
            ? 0m
            : session.SilverAfterTax / (decimal)session.Duration.TotalHours;
        TextRenderer.DrawText(graphics, SpotHistoryCard.FormatSilver(hourlySilver), _valueFont,
            new Rectangle(x + ScaleLogical(3), bounds.Y, silverWidth - ScaleLogical(6), bounds.Height),
            BdoTheme.GoldBright, TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        x += silverWidth;

        var lootViewport = new Rectangle(x, bounds.Y, Math.Max(1, bounds.Right - x), bounds.Height);
        var clipState = graphics.Save();
        graphics.SetClip(lootViewport, CombineMode.Intersect);
        x -= _lootScroll.Value;
        for (var index = 0; index < items.Count; index++)
        {
            var quantity = session.Totals.GetValueOrDefault(items[index].Key);
            TextRenderer.DrawText(graphics,
                quantity > 0 ? SpotHistoryCard.FormatQuantity(quantity) : "—", _smallFont,
                new Rectangle(x, bounds.Y, itemWidth, bounds.Height),
                quantity > 0 ? Color.FromArgb(224, 228, 220) : Color.FromArgb(111, 119, 119),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            x += itemWidth;
        }
        graphics.Restore(clipState);
    }

    private void DrawColumnHeader(Graphics graphics, string text, Rectangle bounds)
    {
        TextRenderer.DrawText(graphics, text, _captionFont, bounds, Color.FromArgb(197, 204, 204),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawLootHeader(Graphics graphics, string itemName, Rectangle bounds)
    {
        var iconSize = Math.Min(ScaleLogical(39), Math.Min(bounds.Width - ScaleLogical(6), bounds.Height - ScaleLogical(8)));
        var iconBounds = new Rectangle(bounds.X + (bounds.Width - iconSize) / 2,
            bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
        var icon = _icons.GetIcon(itemName);
        if (icon is not null)
            graphics.DrawImage(icon, iconBounds);
        else
        {
            using var fill = new SolidBrush(BdoTheme.SurfaceRaised);
            graphics.FillRectangle(fill, iconBounds);
        }
    }

    private void DrawClassSymbol(Graphics graphics, string? characterClass, Rectangle bounds)
    {
        var className = string.IsNullOrWhiteSpace(characterClass) ? "?" : characterClass.Trim();
        var baseName = className.Split('·', StringSplitOptions.TrimEntries)[0];
        var specialization = className.Contains("Awakening", StringComparison.OrdinalIgnoreCase)
            ? "A"
            : className.Contains("Succession", StringComparison.OrdinalIgnoreCase) ? "S" : string.Empty;
        var words = baseName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = words.Length > 1
            ? string.Concat(words.Take(2).Select(static word => char.ToUpperInvariant(word[0])))
            : baseName.Length >= 2 ? baseName[..2].ToUpperInvariant() : baseName.ToUpperInvariant();
        var hash = baseName.Aggregate(17, static (current, character) => unchecked(current * 31 + character));
        var color = ClassColors[(hash & int.MaxValue) % ClassColors.Length];
        var size = ScaleLogical(36);
        var symbol = new Rectangle(bounds.X + (bounds.Width - size) / 2,
            bounds.Y + (bounds.Height - size) / 2, size, size);
        var classIcon = _classIcons.Get(CreateClassIconFileName(baseName));
        if (classIcon is not null)
        {
            graphics.DrawImage(classIcon, symbol);
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(225, color));
            using var border = new Pen(Color.FromArgb(220, 218, 198, 151));
            graphics.FillEllipse(fill, symbol);
            graphics.DrawEllipse(border, symbol);
            TextRenderer.DrawText(graphics, initials, _captionFont, symbol, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        if (specialization.Length == 0)
            return;
        var badgeSize = ScaleLogical(14);
        var badge = new Rectangle(symbol.Right - badgeSize + ScaleLogical(2),
            symbol.Bottom - badgeSize + ScaleLogical(1), badgeSize, badgeSize);
        using (var fill = new SolidBrush(Color.FromArgb(245, 20, 22, 24)))
            graphics.FillEllipse(fill, badge);
        TextRenderer.DrawText(graphics, specialization, _smallFont, badge, BdoTheme.GoldBright,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    internal static string CreateClassIconFileName(string className)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        return className.Trim().ToLowerInvariant().Replace(' ', '-') + ".png";
    }

    private void UpdateHeight()
    {
        var rowsHeight = _sessions.Count == 0 ? ScaleLogical(82) : _sessions.Count * ScaleLogical(RowLogicalHeight);
        var height = ScaleLogical(HeaderLogicalHeight + TableHeaderLogicalHeight + ScrollBarLogicalHeight + 12) + rowsHeight;
        MinimumSize = new Size(ScaleLogical(420), height);
        Height = height;
    }

    private void UpdateLootScrollBar()
    {
        var padding = ScaleLogical(15);
        var (classWidth, ageWidth, durationWidth, silverWidth) = GetFixedColumnWidths();
        var fixedWidth = classWidth + ageWidth + durationWidth + silverWidth;
        var viewportWidth = Math.Max(1, Width - padding * 2 - fixedWidth);
        var contentWidth = _lootItems.Count * ScaleLogical(LootColumnLogicalWidth);
        var overflow = Math.Max(0, contentWidth - viewportWidth);
        var rowsHeight = _sessions.Count == 0 ? ScaleLogical(82) : _sessions.Count * ScaleLogical(RowLogicalHeight);
        var scrollY = ScaleLogical(HeaderLogicalHeight + TableHeaderLogicalHeight) + rowsHeight + ScaleLogical(2);

        _lootScroll.Bounds = new Rectangle(padding + fixedWidth, scrollY, viewportWidth,
            Math.Min(ScaleLogical(ScrollBarLogicalHeight), SystemInformation.HorizontalScrollBarHeight));
        _lootScroll.SmallChange = Math.Max(1, ScaleLogical(LootColumnLogicalWidth));
        _lootScroll.LargeChange = Math.Max(1, viewportWidth);
        _lootScroll.ViewportSize = viewportWidth;
        _lootScroll.Maximum = overflow;
        if (_lootScroll.Value > overflow)
            _lootScroll.Value = overflow;
        _lootScroll.Visible = overflow > 0;
        Invalidate();
    }

    private (int Class, int Age, int Duration, int Silver) GetFixedColumnWidths()
    {
        var compact = Width < ScaleLogical(680);
        return compact
            ? (ScaleLogical(52), ScaleLogical(88), ScaleLogical(76), ScaleLogical(102))
            : (ScaleLogical(62), ScaleLogical(108), ScaleLogical(90), ScaleLogical(118));
    }

    private void RecreateFonts()
    {
        DisposeFonts();
        var scale = DeviceDpi / 96f;
        _titleFont = new Font("Georgia", 17f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _subtitleFont = new Font("Segoe UI Semibold", 9f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _captionFont = new Font("Segoe UI Semibold", 7.5f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _metricFont = new Font("Segoe UI Semibold", 7.5f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _valueFont = new Font("Segoe UI Semibold", 11f * scale, FontStyle.Bold, GraphicsUnit.Point);
        _bodyFont = new Font("Segoe UI", 9f * scale, FontStyle.Regular, GraphicsUnit.Point);
        _smallFont = new Font("Segoe UI Semibold", 8f * scale, FontStyle.Bold, GraphicsUnit.Point);
    }

    private void DisposeFonts()
    {
        _titleFont?.Dispose();
        _subtitleFont?.Dispose();
        _captionFont?.Dispose();
        _metricFont?.Dispose();
        _valueFont?.Dispose();
        _bodyFont?.Dispose();
        _smallFont?.Dispose();
    }

    private int ScaleLogical(int pixels) => Math.Max(1, (int)Math.Round(pixels * DeviceDpi / 96d));

    internal static IReadOnlyList<KeyValuePair<string, long>> BuildLootItems(
        LootSpotPresentation profile,
        IEnumerable<LootHistoryEntry> sessions,
        LootPriceSnapshot prices,
        SilverTaxOptions tax)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(tax);
        var sessionArray = sessions.ToArray();
        var trackedTotals = sessionArray
            .SelectMany(static session => session.Totals)
            .GroupBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Sum(static pair => pair.Value),
                StringComparer.Ordinal);
        var totalHours = sessionArray
            .Where(static session => session.Duration > TimeSpan.Zero)
            .Sum(static session => (decimal)session.Duration.Ticks / TimeSpan.TicksPerHour);
        var spotItems = LootSpotCatalog.GetRequired(profile.SpotId).AllowedItems;
        return new[] { profile.TrashItemName }
            .Concat(spotItems.Where(item => !string.Equals(item, profile.TrashItemName, StringComparison.Ordinal)))
            .Concat(trackedTotals.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select((name, stableIndex) =>
            {
                var quantity = trackedTotals.GetValueOrDefault(name);
                var hourlySilver = totalHours <= 0 || quantity <= 0
                    ? 0m
                    : SilverValuation.Calculate(
                        new Dictionary<string, long>(StringComparer.Ordinal) { [name] = quantity },
                        prices, tax).AfterTax / totalHours;
                return new
                {
                    Item = new KeyValuePair<string, long>(name, quantity),
                    HourlySilver = hourlySilver,
                    StableIndex = stableIndex
                };
            })
            .OrderByDescending(static item => item.HourlySilver)
            .ThenBy(static item => item.StableIndex)
            .Select(static item => item.Item)
            .ToArray();
    }

    internal static SpotHistoryMetrics CalculateMetrics(
        LootSpotPresentation profile,
        IEnumerable<LootHistoryEntry> sessions)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sessions);
        var valid = sessions.Where(static session => session.Duration > TimeSpan.Zero).ToArray();
        var totalHours = valid.Sum(static session => (decimal)session.Duration.Ticks / TimeSpan.TicksPerHour);
        var totalSilver = valid.Sum(static session => session.SilverAfterTax);
        var totalTrash = valid.Sum(session => (decimal)session.Totals.GetValueOrDefault(profile.TrashItemName));
        var recent = CalculateTrashWindow(
            valid.OrderByDescending(static session => session.UpdatedAt), profile.TrashItemName, 5m);
        var best = CalculateTrashWindow(
            valid.OrderByDescending(session => TrashPerHour(session, profile.TrashItemName)),
            profile.TrashItemName, 5m);
        return new SpotHistoryMetrics(
            totalSilver,
            totalHours > 0 ? totalSilver / totalHours : 0m,
            totalHours > 0 ? totalTrash / totalHours : 0m,
            recent,
            best,
            totalHours);
    }

    internal static string FormatTimeAgo(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var elapsed = now - timestamp;
        if (elapsed <= TimeSpan.FromMinutes(1))
            return "gerade eben";
        if (elapsed < TimeSpan.FromHours(1))
            return $"vor {(int)elapsed.TotalMinutes:N0} Min.";
        if (elapsed < TimeSpan.FromDays(1))
            return $"vor {(int)elapsed.TotalHours:N0} Std.";
        if (elapsed < TimeSpan.FromDays(2))
            return "gestern";
        if (elapsed < TimeSpan.FromDays(60))
            return $"vor {(int)elapsed.TotalDays:N0} Tagen";
        if (elapsed < TimeSpan.FromDays(730))
            return $"vor {(int)(elapsed.TotalDays / 30):N0} Mon.";
        return $"vor {(int)(elapsed.TotalDays / 365):N0} J.";
    }

    private static decimal CalculateTrashWindow(
        IEnumerable<LootHistoryEntry> orderedSessions,
        string trashItemName,
        decimal targetHours)
    {
        var remaining = targetHours;
        var usedHours = 0m;
        var trash = 0m;
        foreach (var session in orderedSessions)
        {
            if (remaining <= 0)
                break;
            var hours = (decimal)session.Duration.Ticks / TimeSpan.TicksPerHour;
            if (hours <= 0)
                continue;
            var used = Math.Min(hours, remaining);
            trash += TrashPerHour(session, trashItemName) * used;
            usedHours += used;
            remaining -= used;
        }
        return usedHours > 0 ? trash / usedHours : 0m;
    }

    private static decimal TrashPerHour(LootHistoryEntry session, string trashItemName)
    {
        var hours = (decimal)session.Duration.Ticks / TimeSpan.TicksPerHour;
        return hours > 0 ? session.Totals.GetValueOrDefault(trashItemName) / hours : 0m;
    }

    private static string FormatMetricQuantity(decimal quantity) =>
        decimal.Round(quantity, 0, MidpointRounding.AwayFromZero).ToString("N0", GermanCulture);

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
}
