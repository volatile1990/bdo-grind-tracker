namespace BdoGrindTracker.App.UI;

/// <summary>Shared compact pager for locally stored grind-history entries.</summary>
internal sealed class HistoryPaginationBar : UserControl
{
    internal const int DefaultPageSize = 25;
    internal static IReadOnlyList<int> PageSizeOptions { get; } = [10, 25, 50, 100];

    private readonly Label _status = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = BdoTheme.TextMuted,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty
    };
    private readonly BdoButton _previous = CreateButton("‹", "Vorherige Seite");
    private readonly BdoButton _next = CreateButton("›", "Nächste Seite");
    private readonly IReadOnlyDictionary<int, BdoButton> _pageSizeButtons;
    private int _totalItems;
    private int _pageIndex;
    private int _selectedPageSize = DefaultPageSize;

    public HistoryPaginationBar()
    {
        _pageSizeButtons = PageSizeOptions.ToDictionary(
            static pageSize => pageSize,
            static pageSize => CreatePageSizeButton(pageSize));
        Height = 42;
        MinimumSize = new Size(260, 42);
        Margin = new Padding(0, 3, 0, 8);
        BackColor = BdoTheme.Surface;
        AccessibleName = "Seitennavigation im Loot-Verlauf";

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = BdoTheme.Surface
        };
        controls.Controls.Add(_previous);
        controls.Controls.Add(_next);
        controls.Controls.Add(new Label
        {
            Text = "pro Seite",
            AutoSize = true,
            ForeColor = BdoTheme.TextMuted,
            Margin = new Padding(12, 11, 0, 0)
        });
        foreach (var (pageSize, button) in _pageSizeButtons)
        {
            button.Click += (_, _) => RequestPageSize(pageSize);
            controls.Controls.Add(button);
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12, 3, 8, 3),
            Margin = Padding.Empty,
            BackColor = BdoTheme.Surface
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(_status, 0, 0);
        root.Controls.Add(controls, 1, 0);
        Controls.Add(root);

        _previous.Click += (_, _) => RequestPage(_pageIndex - 1);
        _next.Click += (_, _) => RequestPage(_pageIndex + 1);
    }

    internal event Action<int>? PageRequested;
    internal event Action<int>? PageSizeRequested;

    internal int PageIndex => _pageIndex;
    internal int SelectedPageSize => _selectedPageSize;
    internal int PageCount => Math.Max(1, (_totalItems + _selectedPageSize - 1) / _selectedPageSize);
    internal IEnumerable<BdoButton> PageSizeButtons => _pageSizeButtons.Values;

    internal void Configure(int totalItems, int pageIndex, int pageSize)
    {
        _totalItems = Math.Max(0, totalItems);
        _selectedPageSize = PageSizeOptions.Contains(pageSize) ? pageSize : DefaultPageSize;
        _pageIndex = Math.Clamp(pageIndex, 0, PageCount - 1);
        foreach (var (option, button) in _pageSizeButtons)
            button.ButtonStyle = option == _selectedPageSize
                ? BdoButtonStyle.Primary
                : BdoButtonStyle.Secondary;

        var first = _totalItems == 0 ? 0 : _pageIndex * _selectedPageSize + 1;
        var last = Math.Min(_totalItems, first + _selectedPageSize - 1);
        _status.Text = $"{first:N0}–{last:N0} von {_totalItems:N0}  ·  Seite {_pageIndex + 1:N0}/{PageCount:N0}";
        _previous.Enabled = _pageIndex > 0;
        _next.Enabled = _pageIndex + 1 < PageCount;
        AccessibleDescription = $"Seite {_pageIndex + 1} von {PageCount}, {_selectedPageSize} Einträge pro Seite.";
    }

    internal void RequestPage(int pageIndex)
    {
        var normalized = Math.Clamp(pageIndex, 0, PageCount - 1);
        if (normalized != _pageIndex)
            PageRequested?.Invoke(normalized);
    }

    internal void RequestPageSize(int pageSize)
    {
        if (PageSizeOptions.Contains(pageSize) && pageSize != _selectedPageSize)
            PageSizeRequested?.Invoke(pageSize);
    }

    private static BdoButton CreateButton(string text, string accessibleName) => new()
    {
        Text = text,
        ButtonStyle = BdoButtonStyle.Secondary,
        Size = new Size(36, 32),
        Padding = Padding.Empty,
        Margin = new Padding(3),
        AccessibleName = accessibleName
    };

    private static BdoButton CreatePageSizeButton(int pageSize)
    {
        var button = CreateButton(pageSize.ToString(), $"{pageSize} Einträge pro Seite");
        button.Size = new Size(pageSize == 100 ? 48 : 42, 32);
        button.CornerRadius = 16;
        return button;
    }
}
