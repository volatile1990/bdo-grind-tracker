using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Edits a key locally. Only Save publishes the new value; the caller performs
/// protected persistence. No validation request or upload is sent here.
/// </summary>
internal sealed class GarmothOptionsDialog : Form
{
    private readonly TextBox _apiKeyTextBox = new();
    private readonly Label _statusLabel = new();
    private readonly BdoButton _saveButton = new();
    private readonly BdoButton _cancelButton = new();
    private readonly BdoButton _forgetButton = new();
    private readonly Font _baseFont = new("Segoe UI", 9.5f);
    private readonly Font _titleFont = new("Segoe UI Semibold", 18f, FontStyle.Bold);

    public GarmothOptionsDialog(string apiKey = "")
    {
        ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        SuspendLayout();
        Text = "Garmoth verbinden";
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(640, 530);
        MinimumSize = new Size(620, 530);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = BdoTheme.Background;
        ForeColor = BdoTheme.Text;
        Font = _baseFont;
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BuildInterface();
        _apiKeyTextBox.Text = apiKey;
        _apiKeyTextBox.TextChanged += (_, _) => UpdateReadiness();
        _saveButton.Click += (_, _) => SaveChanges();
        _cancelButton.Click += (_, _) => CancelChanges();
        _forgetButton.Click += (_, _) => StageRemoval();
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
        UpdateReadiness();
        ResumeLayout(performLayout: true);
    }

    public string ApiKey { get; private set; }

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
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = BdoTheme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 46, 64, 68, 40, 70 })
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.Controls.Add(new Label
        {
            Text = "Einmal verbinden. Einfach senden.",
            Font = _titleFont,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        }, 0, 0);
        root.Controls.Add(TextLabel("Trage deinen API-Schlüssel aus den Garmoth-Einstellungen ein. Er wird mit Windows DPAPI für dein Windows-Konto verschlüsselt gespeichert – nicht im Klartext und nicht in den normalen Einstellungen."), 0, 1);

        var keyField = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        keyField.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        keyField.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        keyField.Controls.Add(TextLabel("GARMOTH-API-SCHLÜSSEL"), 0, 0);
        _apiKeyTextBox.UseSystemPasswordChar = true;
        _apiKeyTextBox.AutoCompleteMode = AutoCompleteMode.None;
        _apiKeyTextBox.MaxLength = GarmothApiKeyStore.MaximumApiKeyLength;
        _apiKeyTextBox.Dock = DockStyle.Top;
        _apiKeyTextBox.BackColor = BdoTheme.SurfaceRaised;
        _apiKeyTextBox.ForeColor = BdoTheme.Text;
        _apiKeyTextBox.BorderStyle = BorderStyle.FixedSingle;
        _apiKeyTextBox.Margin = Padding.Empty;
        _apiKeyTextBox.AccessibleName = "Gespeicherter Garmoth-API-Schlüssel";
        keyField.Controls.Add(_apiKeyTextBox, 0, 1);
        root.Controls.Add(keyField, 0, 2);

        _forgetButton.Text = "Schlüssel entfernen";
        _forgetButton.ButtonStyle = BdoButtonStyle.Secondary;
        _forgetButton.Width = 176;
        _forgetButton.Height = 34;
        _forgetButton.Anchor = AnchorStyles.Left;
        _forgetButton.Margin = Padding.Empty;
        _forgetButton.AccessibleName = "Garmoth-Schlüssel zum Entfernen vormerken";
        root.Controls.Add(_forgetButton, 0, 3);
        root.Controls.Add(TextLabel("Der Upload-Button sendet die aktuelle Sitzung anschließend mit einem Klick an Garmoth, ohne erneute Konfiguration. Nicht zuordenbare Items werden ausgelassen; entsprechende Hinweise stehen im Tracker. Keine Screenshots oder Spieldateien werden hochgeladen."), 0, 4);
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Margin = new Padding(0, 6, 0, 0);
        _statusLabel.ForeColor = BdoTheme.TextMuted;
        _statusLabel.AccessibleName = "Garmoth-Schlüsselstatus";
        root.Controls.Add(_statusLabel, 0, 5);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _saveButton.Text = "Speichern";
        _saveButton.Width = 146;
        _saveButton.Margin = Padding.Empty;
        _saveButton.AccessibleName = "Garmoth-Schlüssel speichern";
        _cancelButton.Text = "Abbrechen";
        _cancelButton.ButtonStyle = BdoButtonStyle.Secondary;
        _cancelButton.Width = 124;
        _cancelButton.Margin = new Padding(0, 0, 12, 0);
        _cancelButton.AccessibleName = "Garmoth-Schlüsseländerung verwerfen";
        actions.Controls.Add(_saveButton);
        actions.Controls.Add(_cancelButton);
        root.Controls.Add(actions, 0, 6);
        Controls.Add(root);
    }

    private static Label TextLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Margin = Padding.Empty,
        ForeColor = BdoTheme.TextMuted
    };

    private void UpdateReadiness()
    {
        var value = _apiKeyTextBox.Text.Trim();
        var valid = value.Length == 0 || GarmothApiKeyStore.IsValidApiKey(value);
        _saveButton.Enabled = valid;
        _forgetButton.Enabled = value.Length > 0;
        _statusLabel.ForeColor = valid ? BdoTheme.TextMuted : BdoTheme.Warning;
        _statusLabel.Text = !valid
            ? "Der Schlüssel darf keine Leerzeichen enthalten und muss aus sichtbaren ASCII-Zeichen bestehen."
            : value.Length == 0
                ? "Speichern entfernt einen vorhandenen Schlüssel. Abbrechen lässt ihn unverändert."
                : "Erst „Speichern“ übernimmt den Schlüssel. Es wird jetzt keine Sitzung übertragen.";
    }

    private void StageRemoval() => _apiKeyTextBox.Clear();

    private void SaveChanges()
    {
        var value = _apiKeyTextBox.Text.Trim();
        if (value.Length != 0 && !GarmothApiKeyStore.IsValidApiKey(value))
        {
            UpdateReadiness();
            return;
        }
        ApiKey = value;
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
        }
        base.Dispose(disposing);
    }
}
