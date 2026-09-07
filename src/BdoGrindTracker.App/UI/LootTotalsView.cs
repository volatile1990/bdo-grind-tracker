using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Owner-drawn, virtual tile grid for aggregate loot. Its memory usage depends on
/// the number of distinct item types, never on the number of detected drops.
/// </summary>
internal sealed class LootTotalsView : ScrollableControl
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private readonly LootIconRepository _icons;
    private readonly List<LootTotalEntry> _entries = [];
    private readonly ToolTip _itemToolTip;
    private Font? _emptyTitleFont;
    private Font? _emptyDescriptionFont;
    private Font? _fallbackFont;
    private Font? _itemNameFont;
    private Font? _quantityFont;
    private Font? _priceFont;
    private int _hotIndex = -1;
    private LootPriceSnapshot _prices = LootPriceCatalog.FixedSnapshot(LootPriceCatalog.DefaultRegion);
    private SilverTaxOptions _tax = SilverTaxOptions.Default;
    private Image? _spotBackground;
    private IReadOnlyDictionary<string, long> _totals = new Dictionary<string, long>();

    public LootTotalsView()
        : this(new LootIconRepository(Path.Combine(AppContext.BaseDirectory, "data", "icons")))
    {
    }

    internal LootTotalsView(LootIconRepository icons)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        AutoScroll = true;
        BackColor = BdoTheme.Surface;
        ForeColor = BdoTheme.Text;
        TabStop = true;
        AccessibleName = "Loot dieser Sitzung";
        AccessibleRole = AccessibleRole.List;
        AccessibleDescription = "Noch kein Loot erkannt.";
        _itemToolTip = new ToolTip
        {
            AutoPopDelay = 10_000,
            InitialDelay = 350,
            ReshowDelay = 100,
            ShowAlways = true
        };
        RecreateFonts();
    }

    internal int EntryCount => _entries.Count;

    internal long TotalQuantity => _entries.Sum(static entry => entry.Quantity);

    internal bool HasSpotBackground => _spotBackground is not null;

    internal (string UnitValue, string TotalValue) GetDisplayedValues(string itemName)
    {
        var entry = _entries.First(candidate =>
            string.Equals(candidate.ItemName, itemName, StringComparison.Ordinal));
        return (entry.UnitValue, entry.TotalValue);
    }

    public void SetTotals(IEnumerable<KeyValuePair<string, long>> totals)
    {
        ArgumentNullException.ThrowIfNull(totals);

        _totals = totals
                     .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
                     .GroupBy(static pair => pair.Key, StringComparer.Ordinal)
                     .Select(static group => new KeyValuePair<string, long>(
                         group.Key,
                         group.Sum(static pair => pair.Value)))
                     .Where(static pair => pair.Value > 0)
                     .ToDictionary(static pair => pair.Key, static pair => pair.Value,
                         StringComparer.OrdinalIgnoreCase);
        RebuildEntries();
    }

    internal void SetPricing(LootPriceSnapshot prices, SilverTaxOptions tax)
    {
        _prices = prices ?? throw new ArgumentNullException(nameof(prices));
        _tax = tax ?? throw new ArgumentNullException(nameof(tax));
        RebuildEntries();
    }

    internal void SetSpotBackground(Image? background)
    {
        if (ReferenceEquals(_spotBackground, background))
            return;
        _spotBackground = background;
        Invalidate();
    }

    private void RebuildEntries()
    {
        _entries.Clear();
        foreach (var pair in _totals
                     .OrderByDescending(static pair => pair.Value)
                     .ThenBy(static pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var unit = SilverValuation.Calculate(
                new Dictionary<string, long>(StringComparer.Ordinal) { [pair.Key] = 1 }, _prices, _tax);
            var total = SilverValuation.Calculate(
                new Dictionary<string, long>(StringComparer.Ordinal) { [pair.Key] = pair.Value }, _prices, _tax);
            _entries.Add(new LootTotalEntry(
                pair.Key,
                pair.Value,
                _icons.GetIcon(pair.Key),
                FormatSilver(unit),
                FormatSilver(total)));
        }

        ClearHotItem();
        AccessibleDescription = _entries.Count == 0
            ? "Noch kein Loot erkannt."
            : $"{_entries.Count:N0} Loot-Arten mit insgesamt {TotalQuantity:N0} Gegenst\u00e4nden.";
        UpdateScrollExtent();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        RecreateFonts();
        UpdateScrollExtent();
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateScrollExtent();
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RecreateFonts();
        UpdateScrollExtent();
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        ClearHotItem();
        base.OnScroll(se);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hotIndex = HitTest(e.Location);
        if (hotIndex == _hotIndex)
        {
            return;
        }

        var previousHotIndex = _hotIndex;
        _hotIndex = hotIndex;
        InvalidateTile(previousHotIndex);
        InvalidateTile(_hotIndex);
        _itemToolTip.SetToolTip(this, GetToolTipText(_hotIndex));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        ClearHotItem();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && CanFocus)
        {
            Focus();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        DrawSpotBackground(e.Graphics);

        if (_entries.Count == 0)
        {
            DrawEmptyState(e.Graphics);
            return;
        }

        EnsureFonts();
        var layout = CalculateLayout();
        // Visit only visible rows. A long session never grows the number of
        // controls or makes painting walk over off-screen item cards.
        var firstRow = Math.Max(0,
            (e.ClipRectangle.Top - AutoScrollPosition.Y - layout.OuterPadding) /
            (layout.TileHeight + layout.Gap));
        var lastRow = Math.Max(firstRow,
            (e.ClipRectangle.Bottom - AutoScrollPosition.Y - layout.OuterPadding) /
            (layout.TileHeight + layout.Gap));
        var firstIndex = firstRow * layout.ColumnCount;
        var lastIndex = Math.Min(_entries.Count, (lastRow + 1) * layout.ColumnCount);
        for (var index = firstIndex; index < lastIndex; index++)
        {
            var tileBounds = GetTileBounds(index, layout);
            if (!e.ClipRectangle.IntersectsWith(tileBounds))
            {
                continue;
            }

            DrawTile(e.Graphics, tileBounds, _entries[index], index == _hotIndex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _itemToolTip.Dispose();
            DisposeFonts();
            _icons.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawTile(Graphics graphics, Rectangle bounds, LootTotalEntry entry, bool isHot)
    {
        var scale = DeviceDpi / 96f;
        using var tilePath = CreateRoundedRectangle(bounds, ScaleLogical(10));
        using var background = new SolidBrush(Color.FromArgb(isHot ? 242 : 230,
            isHot ? BdoTheme.SurfaceHover : BdoTheme.SurfaceRaised));
        using var border = new Pen(isHot ? BdoTheme.Gold : BdoTheme.Border, Math.Max(1f, scale));
        graphics.FillPath(background, tilePath);
        graphics.DrawPath(border, tilePath);

        var quantityText = $"\u00d7 {entry.Quantity.ToString("N0", CultureInfo.CurrentCulture)}";
        var contentBounds = new Rectangle(bounds.X, bounds.Y, bounds.Width,
            Math.Max(1, bounds.Height - ScaleLogical(20)));
        var content = CalculateCardContent(graphics, contentBounds, DeviceDpi,
            entry.ItemName, quantityText, _itemNameFont!, _quantityFont!);
        DrawIcon(graphics, content.Icon, entry);
        var textLeft = content.ItemName.Left;
        var textWidth = content.ItemName.Width;
        var nameBounds = new Rectangle(textLeft, bounds.Top + ScaleLogical(9),
            textWidth, ScaleLogical(19));
        var quantityBounds = new Rectangle(textLeft, nameBounds.Bottom + ScaleLogical(2),
            textWidth, ScaleLogical(24));

        TextRenderer.DrawText(
            graphics,
            entry.ItemName,
            _itemNameFont,
            nameBounds,
            BdoTheme.Text,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(
            graphics,
            quantityText,
            _quantityFont,
            quantityBounds,
            BdoTheme.GoldBright,
            TextFormatFlags.Left |
            TextFormatFlags.Top |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix);

        var priceTop = bounds.Bottom - ScaleLogical(21);
        var priceBounds = new Rectangle(content.Quantity.X, priceTop,
            content.Quantity.Width, Math.Max(1, bounds.Bottom - ScaleLogical(6) - priceTop));
        TextRenderer.DrawText(graphics,
            $"Stück {entry.UnitValue}   ·   Gesamt {entry.TotalValue}", _priceFont,
            priceBounds, BdoTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    private void DrawSpotBackground(Graphics graphics)
    {
        if (_spotBackground is null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;
        var destination = ClientRectangle;
        var scale = Math.Max((double)destination.Width / _spotBackground.Width,
            (double)destination.Height / _spotBackground.Height);
        var sourceWidth = destination.Width / scale;
        var sourceHeight = destination.Height / scale;
        var source = new RectangleF(
            (float)((_spotBackground.Width - sourceWidth) / 2d),
            (float)((_spotBackground.Height - sourceHeight) / 2d),
            (float)sourceWidth, (float)sourceHeight);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(_spotBackground, destination, source.X, source.Y,
            source.Width, source.Height, GraphicsUnit.Pixel);
        using (var dark = new SolidBrush(Color.FromArgb(154, 4, 7, 9)))
            graphics.FillRectangle(dark, destination);
        using var fade = new LinearGradientBrush(destination,
            Color.FromArgb(38, BdoTheme.Background), Color.FromArgb(246, BdoTheme.Background),
            LinearGradientMode.Vertical);
        graphics.FillRectangle(fade, destination);
    }

    private void DrawIcon(Graphics graphics, Rectangle bounds, LootTotalEntry entry)
    {
        using var iconPath = CreateRoundedRectangle(bounds, ScaleLogical(7));
        using var iconBackground = new SolidBrush(Color.FromArgb(22, 24, 27));
        using var iconBorder = new Pen(Color.FromArgb(88, 78, 57), Math.Max(1f, DeviceDpi / 96f));
        graphics.FillPath(iconBackground, iconPath);
        graphics.DrawPath(iconBorder, iconPath);

        if (entry.Icon is not null)
        {
            var graphicsState = graphics.Save();
            graphics.SetClip(iconPath, CombineMode.Intersect);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(entry.Icon, bounds);
            graphics.Restore(graphicsState);
            return;
        }

        TextRenderer.DrawText(
            graphics,
            GetInitials(entry.ItemName),
            _fallbackFont,
            bounds,
            BdoTheme.Gold,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);
    }

    private void DrawEmptyState(Graphics graphics)
    {
        EnsureFonts();
        var centerY = Math.Max(ScaleLogical(72), ClientSize.Height / 2);
        var markerSize = ScaleLogical(42);
        var markerBounds = new Rectangle(
            (ClientSize.Width - markerSize) / 2,
            centerY - ScaleLogical(68),
            markerSize,
            markerSize);
        using var markerPath = CreateRoundedRectangle(markerBounds, ScaleLogical(12));
        using var markerFill = new SolidBrush(BdoTheme.SurfaceRaised);
        using var markerBorder = new Pen(BdoTheme.Border, Math.Max(1f, DeviceDpi / 96f));
        graphics.FillPath(markerFill, markerPath);
        graphics.DrawPath(markerBorder, markerPath);

        var diamondInset = ScaleLogical(13);
        var diamond = new[]
        {
            new Point(markerBounds.Left + (markerBounds.Width / 2), markerBounds.Top + diamondInset),
            new Point(markerBounds.Right - diamondInset, markerBounds.Top + (markerBounds.Height / 2)),
            new Point(markerBounds.Left + (markerBounds.Width / 2), markerBounds.Bottom - diamondInset),
            new Point(markerBounds.Left + diamondInset, markerBounds.Top + (markerBounds.Height / 2))
        };
        using var diamondBrush = new SolidBrush(BdoTheme.Gold);
        graphics.FillPolygon(diamondBrush, diamond);

        var titleBounds = new Rectangle(
            ScaleLogical(20),
            centerY - ScaleLogical(14),
            Math.Max(1, ClientSize.Width - ScaleLogical(40)),
            ScaleLogical(28));
        var descriptionBounds = new Rectangle(
            ScaleLogical(20),
            centerY + ScaleLogical(18),
            Math.Max(1, ClientSize.Width - ScaleLogical(40)),
            ScaleLogical(42));

        TextRenderer.DrawText(
            graphics,
            "Noch kein Loot",
            _emptyTitleFont,
            titleBounds,
            BdoTheme.Text,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            graphics,
            "Deine erkannten Drops erscheinen hier automatisch.",
            _emptyDescriptionFont,
            descriptionBounds,
            BdoTheme.TextMuted,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.Top |
            TextFormatFlags.WordBreak);
    }

    private void UpdateScrollExtent()
    {
        var layout = CalculateLayout();
        var rowCount = GetRowCount(layout.ColumnCount);
        var height = (rowCount * layout.TileHeight) +
            (Math.Max(0, rowCount - 1) * layout.Gap) +
            (layout.OuterPadding * 2);
        AutoScrollMinSize = new Size(0, height);
    }

    private GridLayout CalculateLayout()
    {
        var scrollBarWidth = SystemInformation.VerticalScrollBarWidth;
        // WinForms already subtracts a visible scrollbar from ClientSize.
        // Reconstruct the full viewport so the pure layout reserves it once.
        return CalculateGridLayout(
            new Size(ClientSize.Width + (VerticalScroll.Visible ? scrollBarWidth : 0), ClientSize.Height),
            _entries.Count,
            DeviceDpi,
            scrollBarWidth);
    }

    internal static GridLayout CalculateGridLayout(
        Size viewport,
        int entryCount,
        int dpi,
        int scrollBarWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        ArgumentOutOfRangeException.ThrowIfNegative(scrollBarWidth);
        var outerPadding = ScaleLogical(12, dpi);
        var gap = ScaleLogical(10, dpi);
        var minimumTileWidth = ScaleLogical(280, dpi);
        var tileHeight = ScaleLogical(84, dpi);

        GridLayout ForWidth(int width)
        {
            var contentWidth = Math.Max(1, width - (outerPadding * 2));
            var columnCount = Math.Max(1, (contentWidth + gap) / (minimumTileWidth + gap));
            var tileWidth = Math.Max(1, (contentWidth - ((columnCount - 1) * gap)) / columnCount);
            return new GridLayout(outerPadding, gap, columnCount, tileWidth, tileHeight);
        }

        var layout = ForWidth(viewport.Width);
        return layout.GetContentHeight(entryCount) > viewport.Height
            ? ForWidth(viewport.Width - scrollBarWidth)
            : layout;
    }

    internal static CardContentLayout CalculateCardContent(
        IDeviceContext deviceContext,
        Rectangle bounds,
        int dpi,
        string itemName,
        string quantityText,
        Font itemNameFont,
        Font quantityFont)
    {
        ArgumentNullException.ThrowIfNull(deviceContext);
        ArgumentNullException.ThrowIfNull(itemName);
        ArgumentNullException.ThrowIfNull(quantityText);
        ArgumentNullException.ThrowIfNull(itemNameFont);
        ArgumentNullException.ThrowIfNull(quantityFont);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        var inset = ScaleLogical(14, dpi);
        var iconSize = Math.Max(1, Math.Min(ScaleLogical(44, dpi), bounds.Width - (inset * 2)));
        var icon = new Rectangle(bounds.Left + inset, bounds.Top + ((bounds.Height - iconSize) / 2), iconSize, iconSize);
        var textLeft = icon.Right + ScaleLogical(14, dpi);
        var textWidth = Math.Max(1, bounds.Right - inset - textLeft);
        const TextFormatFlags measureFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var lineHeight = TextRenderer.MeasureText(deviceContext, "Ag", itemNameFont,
            new Size(int.MaxValue, int.MaxValue), measureFlags | TextFormatFlags.SingleLine).Height;
        var measuredName = TextRenderer.MeasureText(deviceContext, itemName, itemNameFont,
            new Size(textWidth, int.MaxValue), measureFlags | TextFormatFlags.WordBreak);
        var quantityHeight = TextRenderer.MeasureText(deviceContext, quantityText, quantityFont,
            new Size(textWidth, int.MaxValue), measureFlags | TextFormatFlags.SingleLine).Height;
        var gap = ScaleLogical(4, dpi);
        var maximumNameHeight = Math.Max(1, Math.Min(lineHeight * 2,
            bounds.Height - (ScaleLogical(8, dpi) * 2) - quantityHeight - gap));
        var nameHeight = Math.Max(1, Math.Min(measuredName.Height, maximumNameHeight));
        // A one-line name needs only one line of space. Center the actual text
        // group instead of pinning the name and quantity to opposite card edges.
        var groupHeight = nameHeight + gap + quantityHeight;
        var groupTop = bounds.Top + ((bounds.Height - groupHeight) / 2);
        return new CardContentLayout(
            icon,
            new Rectangle(textLeft, groupTop, textWidth, nameHeight),
            new Rectangle(textLeft, groupTop + nameHeight + gap, textWidth, quantityHeight));
    }

    private Rectangle GetTileBounds(int index, GridLayout layout)
    {
        var row = index / layout.ColumnCount;
        var column = index % layout.ColumnCount;
        return new Rectangle(
            layout.OuterPadding + (column * (layout.TileWidth + layout.Gap)),
            layout.OuterPadding + (row * (layout.TileHeight + layout.Gap)) + AutoScrollPosition.Y,
            layout.TileWidth,
            layout.TileHeight);
    }

    private int GetRowCount(int columnCount) =>
        _entries.Count == 0 ? 0 : (_entries.Count + columnCount - 1) / columnCount;

    private int HitTest(Point location)
    {
        if (_entries.Count == 0)
        {
            return -1;
        }

        var layout = CalculateLayout();
        var contentX = location.X - layout.OuterPadding;
        var contentY = location.Y - AutoScrollPosition.Y - layout.OuterPadding;
        if (contentX < 0 || contentY < 0)
        {
            return -1;
        }

        var columnStride = layout.TileWidth + layout.Gap;
        var rowStride = layout.TileHeight + layout.Gap;
        var column = contentX / columnStride;
        var row = contentY / rowStride;
        if (column >= layout.ColumnCount ||
            contentX % columnStride >= layout.TileWidth ||
            contentY % rowStride >= layout.TileHeight)
        {
            return -1;
        }

        var index = (row * layout.ColumnCount) + column;
        return index < _entries.Count ? index : -1;
    }

    private string GetToolTipText(int index) =>
        index < 0 || index >= _entries.Count
            ? string.Empty
            : $"{_entries[index].ItemName}\n\u00d7 {_entries[index].Quantity.ToString("N0", CultureInfo.CurrentCulture)}";

    private void ClearHotItem()
    {
        var previousHotIndex = _hotIndex;
        _hotIndex = -1;
        _itemToolTip.SetToolTip(this, string.Empty);
        InvalidateTile(previousHotIndex);
    }

    private void InvalidateTile(int index)
    {
        if (index < 0 || index >= _entries.Count || IsDisposed)
        {
            return;
        }

        Invalidate(GetTileBounds(index, CalculateLayout()));
    }

    private void RecreateFonts()
    {
        if (IsDisposed)
        {
            return;
        }

        DisposeFonts();
        _emptyTitleFont = new Font(Font.FontFamily, 11f, FontStyle.Bold, GraphicsUnit.Point);
        _emptyDescriptionFont = new Font(Font.FontFamily, 9f, FontStyle.Regular, GraphicsUnit.Point);
        _fallbackFont = new Font(Font.FontFamily, 10f, FontStyle.Bold, GraphicsUnit.Point);
        // Explicit pixel sizes keep GDI measurement and drawing at the same
        // per-monitor DPI rather than reusing a system-DPI point conversion.
        _itemNameFont = new Font(Font.FontFamily, 9f * DeviceDpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        _quantityFont = new Font(Font.FontFamily, 12.5f * DeviceDpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
        _priceFont = new Font(Font.FontFamily, 8.5f * DeviceDpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private void EnsureFonts()
    {
        if (_emptyTitleFont is null ||
            _emptyDescriptionFont is null ||
            _fallbackFont is null ||
            _itemNameFont is null ||
            _quantityFont is null ||
            _priceFont is null)
        {
            RecreateFonts();
        }
    }

    private void DisposeFonts()
    {
        _emptyTitleFont?.Dispose();
        _emptyDescriptionFont?.Dispose();
        _fallbackFont?.Dispose();
        _itemNameFont?.Dispose();
        _quantityFont?.Dispose();
        _priceFont?.Dispose();
        _emptyTitleFont = null;
        _emptyDescriptionFont = null;
        _fallbackFont = null;
        _itemNameFont = null;
        _quantityFont = null;
        _priceFont = null;
    }

    private int ScaleLogical(int logicalPixels) =>
        ScaleLogical(logicalPixels, DeviceDpi);

    private static int ScaleLogical(int logicalPixels, int dpi) =>
        Math.Max(1, (int)Math.Round(logicalPixels * dpi / 96d));

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
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

    private static string GetInitials(string itemName)
    {
        var initials = itemName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .Where(static part => part.Length > 0)
            .Select(static part => char.ToUpperInvariant(part[0]))
            .ToArray();
        return initials.Length == 0 ? "?" : new string(initials);
    }

    internal readonly record struct GridLayout(
        int OuterPadding,
        int Gap,
        int ColumnCount,
        int TileWidth,
        int TileHeight)
    {
        internal int GetContentHeight(int entryCount)
        {
            var rows = entryCount == 0 ? 0 : (entryCount + ColumnCount - 1) / ColumnCount;
            return (rows * TileHeight) + (Math.Max(0, rows - 1) * Gap) + (OuterPadding * 2);
        }
    }

    internal readonly record struct CardContentLayout(Rectangle Icon, Rectangle ItemName, Rectangle Quantity);

    private static string FormatSilver(SilverValuationResult value)
    {
        if (!value.HasKnownValue)
            return "—";
        var amount = decimal.Truncate(value.AfterTax);
        return amount switch
        {
            >= 1_000_000_000m => (amount / 1_000_000_000m).ToString("0.##", GermanCulture) + " Mrd.",
            >= 1_000_000m => (amount / 1_000_000m).ToString("0.##", GermanCulture) + " Mio.",
            >= 1_000m => (amount / 1_000m).ToString("0.#", GermanCulture) + " Tsd.",
            _ => amount.ToString("N0", GermanCulture)
        };
    }

    private sealed record LootTotalEntry(
        string ItemName, long Quantity, Bitmap? Icon, string UnitValue, string TotalValue);
}

/// <summary>
/// Loads detached bitmap copies, so source files are never held open. The cache is
/// bounded by the catalog's distinct item names and is released with the view.
/// </summary>
internal sealed class LootIconRepository(string iconDirectory) : IDisposable
{
    private readonly string _iconDirectory = iconDirectory ?? throw new ArgumentNullException(nameof(iconDirectory));
    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public Bitmap? GetIcon(string itemName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);

        var slug = CreateSlug(itemName);
        if (_cache.TryGetValue(slug, out var cached))
        {
            return cached;
        }

        var path = Path.Combine(_iconDirectory, $"{slug}.png");
        Bitmap? bitmap = null;
        try
        {
            if (File.Exists(path))
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                bitmap.SetResolution(
                    source.HorizontalResolution > 0 ? source.HorizontalResolution : 96f,
                    source.VerticalResolution > 0 ? source.VerticalResolution : 96f);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.DrawImageUnscaled(source, 0, 0);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            OutOfMemoryException)
        {
            bitmap?.Dispose();
            bitmap = null;
        }

        _cache[slug] = bitmap;
        return bitmap;
    }

    internal static string CreateSlug(string itemName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);

        var decomposed = itemName.Trim().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // Asset slugs omit apostrophes instead of treating them as word
            // boundaries: "Nev's Fragment" -> "nevs-fragment.png".
            if (character is '\'' or '\u2019')
            {
                continue;
            }

            if (character is >= 'A' and <= 'Z')
            {
                if (pendingSeparator && result.Length > 0)
                {
                    result.Append('-');
                }

                result.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingSeparator && result.Length > 0)
                {
                    result.Append('-');
                }

                result.Append(character);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = result.Length > 0;
            }
        }

        return result.Length == 0 ? "unknown-item" : result.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var bitmap in _cache.Values)
        {
            bitmap?.Dispose();
        }

        _cache.Clear();
        _disposed = true;
    }
}
