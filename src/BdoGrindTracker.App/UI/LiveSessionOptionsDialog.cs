using Grindcrest.Live;

namespace BdoGrindTracker.App.UI;

internal sealed class LiveSessionOptionsDialog : Form
{
    private readonly CheckBox _enabled = new() { Text = "Session öffentlich teilen", AutoSize = true };
    private readonly TextBox _endpoint = new() { MaxLength = 500 };
    private readonly TextBox _name = new() { MaxLength = 40 };
    private readonly TextBox _token = new() { MaxLength = 64, UseSystemPasswordChar = true };
    private readonly Label _message = new() { AutoSize = true, ForeColor = BdoTheme.Warning };
    private readonly Font _font = new("Segoe UI", 10f);
    public bool SharingEnabled => _enabled.Checked;
    public string Endpoint => LiveEndpoint.TryParse(_endpoint.Text, out var uri) ? uri.AbsoluteUri : "";
    public string DisplayName => _name.Text.Trim();
    public string Token => _token.Text.Trim();

    public LiveSessionOptionsDialog(bool enabled, string endpoint, string displayName, string token, string status = "")
    {
        Text = "Grindcrest · Live-Freigabe";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 500);
        MinimumSize = new Size(530, 530);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = BdoTheme.Background;
        ForeColor = BdoTheme.Text;
        Font = _font;
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        MaximizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24),
            AutoScroll = true, ColumnCount = 1, RowCount = 10 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(_enabled);
        layout.Controls.Add(new Label { Text = "Dein Anzeigename, Spot, Klasse, Region, Dauer, Loot und Silberwert sind auf der Homepage für alle sichtbar. Aktualisierung alle 15 Sekunden; Pausen werden angezeigt.",
            AutoSize = true, MaximumSize = new Size(530, 0), ForeColor = BdoTheme.TextMuted, Margin = new Padding(0, 12, 0, 14) });
        AddField(layout, "Anzeigename", _name);
        AddField(layout, "API-Adresse", _endpoint);
        AddField(layout, "Persönlicher Schreibschlüssel vom Betreiber", _token);
        layout.Controls.Add(_message);
        _message.Text = status;
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 15, 0, 0) };
        var save = new BdoButton { Text = "Speichern", Width = 130 };
        var cancel = new BdoButton { Text = "Abbrechen", Width = 130, DialogResult = DialogResult.Cancel, ButtonStyle = BdoButtonStyle.Secondary };
        var forget = new BdoButton { Text = "Schlüssel entfernen", Width = 175, ButtonStyle = BdoButtonStyle.Secondary };
        forget.Click += (_, _) => { _token.Clear(); _enabled.Checked = false; };
        save.Click += (_, _) =>
        {
            if (SharingEnabled && (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Any(char.IsControl) ||
                !LiveEndpoint.TryParse(_endpoint.Text, out _) || !LiveEndpoint.IsValidToken(Token)))
            { _message.Text = "Bitte Anzeigename, HTTPS-API-Adresse und Schreibschlüssel prüfen."; return; }
            if (Token.Length > 0 && (!LiveEndpoint.IsValidToken(Token) || !LiveEndpoint.TryParse(_endpoint.Text, out _)))
            { _message.Text = "Schlüssel und API-Adresse prüfen oder den Schlüssel entfernen."; return; }
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.AddRange([save, cancel, forget]);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        _enabled.Checked = enabled;
        _endpoint.Text = endpoint;
        _name.Text = displayName;
        _token.Text = token;
        // A stored token is valid only for the endpoint the user connected previously.
        _endpoint.TextChanged += (_, _) => _token.Clear();
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void AddField(TableLayoutPanel layout, string label, TextBox input)
    {
        var field = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 12) };
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        field.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 5) });
        input.AccessibleName = label;
        input.Dock = DockStyle.Top;
        input.BackColor = BdoTheme.SurfaceRaised;
        input.ForeColor = BdoTheme.Text;
        input.BorderStyle = BorderStyle.FixedSingle;
        field.Controls.Add(input);
        layout.Controls.Add(field);
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); BdoWindowChrome.Apply(this); }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) _font.Dispose(); }
}
