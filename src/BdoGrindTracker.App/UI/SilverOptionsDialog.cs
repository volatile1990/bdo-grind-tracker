using System.Globalization;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Edits a local copy of market preferences. Only Apply publishes a new selection;
/// the caller owns persistence. Opening this dialog never reads game settings or
/// fetches prices.
/// </summary>
internal sealed class SilverOptionsDialog : Form
{
    private readonly ComboBox _regionComboBox = new();
    private readonly CheckBox _valuePackCheckBox = new();
    private readonly CheckBox _merchantRingCheckBox = new();
    private readonly NumericUpDown _familyFameInput = new();
    private readonly Label _marketReturnLabel = new();
    private readonly Label _familyBonusLabel = new();
    private readonly BdoButton _applyButton = new();
    private readonly BdoButton _cancelButton = new();
    private readonly Font _baseFont = new("Segoe UI", 9.5f);
    private readonly Font _titleFont = new("Segoe UI Semibold", 18f, FontStyle.Bold);
    private readonly Font _rateFont = new("Segoe UI Semibold", 16f, FontStyle.Bold);

    public SilverOptionsDialog(
        string marketRegion = LootPriceCatalog.DefaultRegion,
        SilverTaxOptions? taxOptions = null)
    {
        SelectedRegion = LootPriceCatalog.NormalizeRegion(marketRegion);
        SelectedTaxOptions = taxOptions ?? new SilverTaxOptions();

        SuspendLayout();
        Text = "Silberbewertung einstellen";
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(620, 580);
        MinimumSize = new Size(600, 580);
        StartPosition = FormStartPosition.CenterParent;
        Font = _baseFont;
        BackColor = BdoTheme.Background;
        ForeColor = BdoTheme.Text;
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BuildInterface();

        foreach (var region in LootPriceCatalog.SupportedRegions)
            _regionComboBox.Items.Add(new RegionOption(region));
        _regionComboBox.SelectedItem = _regionComboBox.Items.Cast<RegionOption>()
            .Single(region => region.Code == SelectedRegion);
        _valuePackCheckBox.Checked = SelectedTaxOptions.ValuePack;
        _merchantRingCheckBox.Checked = SelectedTaxOptions.MerchantRing;
        _familyFameInput.Value = SelectedTaxOptions.FamilyFame;
        _valuePackCheckBox.CheckedChanged += (_, _) => RefreshTaxPreview();
        _merchantRingCheckBox.CheckedChanged += (_, _) => RefreshTaxPreview();
        _familyFameInput.ValueChanged += (_, _) => RefreshTaxPreview();
        _applyButton.Click += (_, _) => ApplyChanges();
        _cancelButton.Click += (_, _) => CancelChanges();
        AcceptButton = _applyButton;
        CancelButton = _cancelButton;
        RefreshTaxPreview();
        ResumeLayout(performLayout: true);
    }

    public string SelectedRegion { get; private set; }

    public SilverTaxOptions SelectedTaxOptions { get; private set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BdoWindowChrome.Apply(this);
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 7,
            Margin = Padding.Empty,
            BackColor = BdoTheme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 56, 70, 72, 76, 82 })
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.Controls.Add(new Label
        {
            Text = "Silber für deinen Server",
            Font = _titleFont,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        }, 0, 0);

        var regionField = FieldLayout("SERVERREGION", 26);
        _regionComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _regionComboBox.Dock = DockStyle.Fill;
        _regionComboBox.Margin = new Padding(0, 0, 0, 8);
        _regionComboBox.AccessibleName = "Serverregion für Marktpreise";
        BdoTheme.StyleComboBox(_regionComboBox);
        regionField.Controls.Add(_regionComboBox, 0, 1);
        root.Controls.Add(regionField, 0, 1);

        var taxChecks = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        taxChecks.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        taxChecks.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _valuePackCheckBox.Text = "Vorteilspaket (Value Pack) aktiv";
        _valuePackCheckBox.AccessibleName = "Vorteilspaket für Silberbewertung";
        _valuePackCheckBox.Dock = DockStyle.Fill;
        _valuePackCheckBox.Margin = Padding.Empty;
        _merchantRingCheckBox.Text = "Reicher Handelsring aktiv";
        _merchantRingCheckBox.AccessibleName = "Handelsring für Silberbewertung";
        _merchantRingCheckBox.Dock = DockStyle.Fill;
        _merchantRingCheckBox.Margin = Padding.Empty;
        taxChecks.Controls.Add(_valuePackCheckBox, 0, 0);
        taxChecks.Controls.Add(_merchantRingCheckBox, 0, 1);
        root.Controls.Add(taxChecks, 0, 2);

        var fameField = FieldLayout("FAMILIENRUHM", 26);
        var fameContent = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        fameContent.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        fameContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _familyFameInput.Minimum = 0;
        _familyFameInput.Maximum = int.MaxValue;
        _familyFameInput.DecimalPlaces = 0;
        _familyFameInput.ThousandsSeparator = true;
        _familyFameInput.Dock = DockStyle.Top;
        _familyFameInput.BackColor = BdoTheme.SurfaceRaised;
        _familyFameInput.ForeColor = BdoTheme.Text;
        _familyFameInput.Margin = Padding.Empty;
        _familyFameInput.AccessibleName = "Familienruhm für Silberbewertung";
        _familyBonusLabel.Dock = DockStyle.Fill;
        _familyBonusLabel.TextAlign = ContentAlignment.TopLeft;
        _familyBonusLabel.Margin = new Padding(14, 3, 0, 0);
        _familyBonusLabel.ForeColor = BdoTheme.TextMuted;
        fameContent.Controls.Add(_familyFameInput, 0, 0);
        fameContent.Controls.Add(_familyBonusLabel, 1, 0);
        fameField.Controls.Add(fameContent, 0, 1);
        root.Controls.Add(fameField, 0, 3);

        var preview = new BdoSurfacePanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 12)
        };
        _marketReturnLabel.Dock = DockStyle.Fill;
        _marketReturnLabel.ForeColor = BdoTheme.GoldBright;
        _marketReturnLabel.Font = _rateFont;
        _marketReturnLabel.AccessibleName = "Effektive Marktplatz-Auszahlung";
        _marketReturnLabel.TextAlign = ContentAlignment.MiddleLeft;
        preview.Controls.Add(_marketReturnLabel);
        root.Controls.Add(preview, 0, 4);

        root.Controls.Add(new Label
        {
            Text = "Marktpreise gelten für die gewählte Region. NPC-Trashloot bleibt steuerfrei.\nStandard: EU, kein Vorteilspaket, kein Handelsring, Familienruhm 0. Deine Werte werden nicht aus Companion übernommen.",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = BdoTheme.TextMuted
        }, 0, 5);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _applyButton.Text = "Übernehmen";
        _applyButton.Width = 146;
        _applyButton.Margin = Padding.Empty;
        _applyButton.AccessibleName = "Silbereinstellungen übernehmen";
        _cancelButton.Text = "Abbrechen";
        _cancelButton.ButtonStyle = BdoButtonStyle.Secondary;
        _cancelButton.Width = 124;
        _cancelButton.Margin = new Padding(0, 0, 12, 0);
        _cancelButton.AccessibleName = "Silbereinstellungen verwerfen";
        actions.Controls.Add(_applyButton);
        actions.Controls.Add(_cancelButton);
        root.Controls.Add(actions, 0, 6);
        Controls.Add(root);
    }

    private static TableLayoutPanel FieldLayout(string title, int labelHeight)
    {
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, labelHeight));
        field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        field.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = BdoTheme.TextMuted,
            Margin = Padding.Empty
        }, 0, 0);
        return field;
    }

    private SilverTaxOptions EditedTaxOptions() => new(
        _valuePackCheckBox.Checked,
        _merchantRingCheckBox.Checked,
        decimal.ToInt32(_familyFameInput.Value));

    private void RefreshTaxPreview()
    {
        var edited = EditedTaxOptions();
        _marketReturnLabel.Text = $"Marktplatz-Auszahlung: {edited.MarketReturnRate.ToString("P3", CultureInfo.CurrentCulture)}";
        _familyBonusLabel.Text = $"Familienbonus: +{edited.FamilyFameBonus.ToString("P1", CultureInfo.CurrentCulture)}";
    }

    private void ApplyChanges()
    {
        if (_regionComboBox.SelectedItem is not RegionOption region)
            return;
        SelectedRegion = region.Code;
        SelectedTaxOptions = EditedTaxOptions();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelChanges()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _baseFont.Dispose();
            _titleFont.Dispose();
            _rateFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record RegionOption(string Code)
    {
        public override string ToString() => Code switch
        {
            "eu" => "EU · Europa",
            "na" => "NA · Nordamerika",
            _ => Code.ToUpperInvariant()
        };
    }
}
