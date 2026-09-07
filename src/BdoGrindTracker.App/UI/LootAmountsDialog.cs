using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.UI;

internal sealed class LootAmountsDialog : Form
{
    private readonly Dictionary<string, NumericUpDown> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly BdoButton _saveButton = new() { Text = "Änderungen speichern", Width = 180 };
    private readonly BdoButton _cancelButton = new()
    {
        Text = "Abbrechen",
        Width = 120,
        ButtonStyle = BdoButtonStyle.Secondary
    };
    private readonly Font _baseFont = new("Segoe UI", 9.5f);
    private readonly Font _titleFont = new("Segoe UI Semibold", 17f, FontStyle.Bold);

    public LootAmountsDialog(LootHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var spot = LootSpotCatalog.GetRequired(entry.SpotId);
        Text = "Lootmengen bearbeiten";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(610, 650);
        MinimumSize = new Size(520, 480);
        BackColor = BdoTheme.Background;
        ForeColor = BdoTheme.Text;
        Font = _baseFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        var items = spot.AllowedItems
            .Concat(entry.Totals.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => !entry.Totals.ContainsKey(item))
            .ThenBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        BuildInterface(spot.DisplayName, entry, items);
        _saveButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    internal Dictionary<string, long> Totals => _inputs
        .Where(static pair => pair.Value.Value > 0)
        .ToDictionary(static pair => pair.Key, static pair => decimal.ToInt64(pair.Value.Value),
            StringComparer.OrdinalIgnoreCase);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BdoWindowChrome.Apply(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _baseFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildInterface(string spotName, LootHistoryEntry entry, IReadOnlyList<string> items)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BdoTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.Controls.Add(new Label
        {
            Text = spotName,
            Dock = DockStyle.Fill,
            Font = _titleFont,
            ForeColor = BdoTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Text = $"{entry.UpdatedAt.ToLocalTime():dd.MM.yyyy HH:mm}  ·  {SpotHistoryCard.FormatDuration(entry.Duration)}\nNur Mengen über 0 werden im Verlauf gespeichert.",
            Dock = DockStyle.Fill,
            ForeColor = BdoTheme.TextMuted
        }, 0, 1);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            RowCount = items.Count,
            Padding = new Padding(0, 6, 10, 6),
            BackColor = BdoTheme.Surface
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        for (var index = 0; index < items.Count; index++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            var item = items[index];
            fields.Controls.Add(new Label
            {
                Text = item,
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 0, 8, 0),
                ForeColor = BdoTheme.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            }, 0, index);
            var input = new NumericUpDown
            {
                Minimum = 0,
                Maximum = long.MaxValue,
                DecimalPlaces = 0,
                ThousandsSeparator = true,
                Value = Math.Min(long.MaxValue, Math.Max(0, entry.Totals.GetValueOrDefault(item))),
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 7, 10, 7),
                BackColor = BdoTheme.SurfaceRaised,
                ForeColor = BdoTheme.Text,
                AccessibleName = $"Menge für {item}"
            };
            _inputs[item] = input;
            fields.Controls.Add(input, 1, index);
        }
        root.Controls.Add(fields, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        actions.Controls.Add(_saveButton);
        _cancelButton.Margin = new Padding(0, 0, 10, 0);
        actions.Controls.Add(_cancelButton);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
    }
}
