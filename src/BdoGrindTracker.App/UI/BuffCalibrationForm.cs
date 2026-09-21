using System.Drawing.Imaging;
using System.Text.Json;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.UI;

/// <summary>Creates a small, explicitly identified template set from the player's actual HUD.</summary>
internal sealed class BuffCalibrationForm : Form
{
    private enum SelectionTarget { Bar, Icon, Timer }

    private sealed record Mapping(BuffDefinition Definition, Bitmap Icon, BuffRegion Timer,
        double MinimumSimilarity, bool ConsumptionAttributionConfirmed) : IDisposable
    {
        public override string ToString() => Definition.DisplayName;
        public void Dispose() => Icon.Dispose();
    }

    private readonly string _outputDirectory;
    private readonly BuffCalibrationCanvas _canvas = new();
    private readonly ComboBox _buffChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340, DropDownWidth = 650 };
    private readonly ListBox _mappingsList = new() { Width = 340, Height = 130, HorizontalScrollbar = true };
    private readonly Label _selectionInfo = new() { AutoSize = true, MaximumSize = new(340, 0) };
    private readonly TextBox _status = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Margin = new Padding(8), AccessibleName = "Kalibrierungsstatus und Testergebnisse" };
    private readonly Label _buffInfo = new() { AutoSize = true, MaximumSize = new(340, 0) };
    private readonly CheckBox _ownConsumption = new() { Text = "Diesen Gruppenbuff verbrauche ich selbst", AutoSize = true, MaximumSize = new(340, 0) };
    private readonly Button _load = new() { Text = "Screenshot &laden …", AutoSize = true };
    private readonly Button _new = new() { Text = "&Neues Profil", AutoSize = true };
    private readonly Button _add = new() { Text = "Vorlage &hinzufügen / ersetzen", AutoSize = true };
    private readonly Button _remove = new() { Text = "Ausgewählte Vorlage &entfernen", AutoSize = true };
    private readonly Button _test = new() { Text = "Am Screenshot &testen", AutoSize = true };
    private readonly Button _save = new() { Text = "Profil &speichern", AutoSize = true };
    private readonly FlowLayoutPanel _editor = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
    private readonly RadioButton _barMode = new() { Text = "1. Buffleiste markieren (blau)", AutoSize = true };
    private readonly RadioButton _iconMode = new() { Text = "2. Symbol innen markieren (grün)", AutoSize = true };
    private readonly RadioButton _timerMode = new() { Text = "3. Restlaufzeit markieren (gelb)", AutoSize = true };
    private readonly List<Mapping> _mappings = [];
    private Bitmap? _screenshot;
    private Size _screenSize;
    private Rectangle _bar;
    private Rectangle _icon;
    private Rectangle _timer;
    private SelectionTarget _target;
    private bool _testing;
    private bool _anchoredProfileNeedsBar;
    private CancellationTokenSource? _testCancellation;

    internal string? SavedProfilePath { get; private set; }

    internal BuffCalibrationForm(string outputDirectory, string? existingProfilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _outputDirectory = Path.GetFullPath(outputDirectory);
        Text = "Buff-Erkennung · optionale Kalibrierung";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1220, 830);
        MinimumSize = new Size(940, 720);
        BuildLayout();
        foreach (var definition in BuffPriceCatalog.Definitions)
            _buffChoice.Items.Add(definition);
        _buffChoice.DisplayMember = nameof(BuffDefinition.DisplayName);
        if (_buffChoice.Items.Count > 0) _buffChoice.SelectedIndex = 0;
        _buffChoice.SelectedIndexChanged += (_, _) => RefreshChoice();
        _load.Click += (_, _) => LoadScreenshot();
        _new.Click += (_, _) => ResetProfile();
        _canvas.SelectionCompleted += SelectRegion;
        _barMode.CheckedChanged += (_, _) => { if (_barMode.Checked) SetTarget(SelectionTarget.Bar); };
        _iconMode.CheckedChanged += (_, _) => { if (_iconMode.Checked) SetTarget(SelectionTarget.Icon); };
        _timerMode.CheckedChanged += (_, _) => { if (_timerMode.Checked) SetTarget(SelectionTarget.Timer); };
        _ownConsumption.CheckedChanged += (_, _) => RefreshButtons();
        _add.Click += (_, _) => AddMapping();
        _remove.Click += (_, _) => RemoveMapping();
        _test.Click += async (_, _) => await TestAsync();
        _save.Click += (_, _) => SaveProfile();
        _mappingsList.SelectedIndexChanged += (_, _) => RefreshButtons();
        _barMode.Checked = true;
        RefreshChoice();
        if (!string.IsNullOrWhiteSpace(existingProfilePath)) LoadExisting(existingProfilePath);
        else SetStatus("Ein vollständiger Screenshot mit derselben Auflösung wie die spätere Spielaufnahme wird benötigt. Ein zugeschnittener Ausschnitt reicht für die Positionskalibrierung nicht aus.");
        RefreshButtons();
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8), WrapContents = true };
        toolbar.Controls.Add(_load);
        toolbar.Controls.Add(_new);
        AddToolbarButton(toolbar, "Ganzes Bild", () => _canvas.FitImage());
        AddToolbarButton(toolbar, "100 %", () => _canvas.SetZoom(1));
        AddToolbarButton(toolbar, "200 %", () => _canvas.SetZoom(2));
        AddToolbarButton(toolbar, "400 %", () => _canvas.SetZoom(4));
        AddToolbarButton(toolbar, "Leiste zeigen", () => _canvas.ShowBar());
        layout.Controls.Add(toolbar, 0, 0);
        layout.SetColumnSpan(toolbar, 2);
        layout.Controls.Add(_canvas, 0, 1);
        layout.Controls.Add(_editor, 1, 1);
        layout.Controls.Add(_status, 0, 2);
        layout.SetColumnSpan(_status, 2);
        _editor.Controls.Add(Instructions("Diese optionale Kalibrierung ersetzt die automatische Standarderkennung. Nur Buffs hinzufügen, die du tatsächlich verwendest. Die Liste der Vorlagen ist zugleich das aktive Match-Set."));
        _editor.Controls.Add(_barMode);
        _editor.Controls.Add(_iconMode);
        _editor.Controls.Add(_timerMode);
        _editor.Controls.Add(Instructions("Mit der Maus ein Rechteck ziehen. Symbol ohne Rand, Pfeile und Zeittext wählen; beim Timer alle Ziffern und die Einheit einschließen. In der vergrößerten Ansicht kannst du über die Scrollleisten navigieren."));
        _editor.Controls.Add(_selectionInfo);
        _editor.Controls.Add(Instructions("Buff / Variante auswählen:"));
        _editor.Controls.Add(_buffChoice);
        _editor.Controls.Add(_buffInfo);
        _editor.Controls.Add(_ownConsumption);
        _editor.Controls.Add(_add);
        _editor.Controls.Add(Instructions("Nur die verwendete Preis- und Laufzeitvariante hinterlegen. Normale und unsterbliche Varianten haben eigene Preise. Ein Gruppenbuff von anderen belegt keinen eigenen Verbrauch."));
        _editor.Controls.Add(Instructions("Aktive Vorlagen:"));
        _editor.Controls.Add(_mappingsList);
        _editor.Controls.Add(_remove);
        _editor.Controls.Add(_test);
        _editor.Controls.Add(_save);
        _editor.Controls.Add(Instructions("Dieser Screenshot-Test erzeugt keine Kostenbuchungen."));
        var close = new Button { Text = "Abbrechen", AutoSize = true, DialogResult = DialogResult.Cancel };
        _editor.Controls.Add(close);
        CancelButton = close;
        Controls.Add(layout);
    }

    private static Label Instructions(string text) => new() { Text = text, AutoSize = true, MaximumSize = new(340, 0), Margin = new Padding(3, 7, 3, 7) };

    private static void AddToolbarButton(Control toolbar, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        toolbar.Controls.Add(button);
    }

    private void LoadExisting(string path)
    {
        var store = new BuffRecognitionProfileStore(path);
        var profile = store.Load();
        if (profile is null) { SetStatus(store.LastError ?? "Das vorhandene Profil konnte nicht geladen werden."); return; }
        var loaded = new List<Mapping>();
        try
        {
            foreach (var template in profile.Templates)
            {
                using var source = OpenImage(template.IconPath, 256, 256, 1024 * 1024);
                if (source.Width < 8 || source.Height < 8) throw new InvalidDataException("Eine vorhandene Iconvorlage ist zu klein.");
                var definition = BuffPriceCatalog.Definitions.Single(definition => definition.Id == template.BuffId);
                loaded.Add(new(definition, new Bitmap(source), template.TimerRegion,
                    template.MinimumSimilarity, template.ConsumptionAttributionConfirmed));
            }
            _mappings.AddRange(loaded);
            loaded.Clear();
            _screenSize = new(profile.ScreenWidth, profile.ScreenHeight);
            _anchoredProfileNeedsBar = profile.UiDataIndex.HasValue;
            _bar = _anchoredProfileNeedsBar ? Rectangle.Empty : profile.Region.Rectangle;
            RefreshMappings();
            SetStatus(_anchoredProfileNeedsBar
                ? "Vorlagen geladen. Dieses Profil verwendet einen BDO-Konfigurationsanker. Bitte einen passenden vollständigen Screenshot laden und die Leiste neu markieren. Die neue Profilkopie verwendet dann die sichtbare absolute Position. Das alte Profil bleibt erhalten."
                : $"{_mappings.Count} Vorlagen geladen. Zum Ergänzen oder Testen einen Screenshot mit {_screenSize.Width} × {_screenSize.Height} Pixeln laden. Das bestehende Profil bleibt erhalten.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or OutOfMemoryException)
        {
            SetStatus($"Vorlagen konnten nicht geladen werden: {error.Message}");
        }
        finally { foreach (var mapping in loaded) mapping.Dispose(); }
    }

    private void ResetProfile()
    {
        if (_testing) return;
        if (_mappings.Count > 0 && MessageBox.Show(this,
            "Mit einer leeren Vorlagenliste beginnen? Nicht gespeicherte Änderungen dieser Bearbeitung werden verworfen. Bereits gespeicherte Profile bleiben erhalten.",
            "Neues Buff-Profil", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        foreach (var mapping in _mappings) mapping.Dispose();
        _mappings.Clear();
        _screenSize = _screenshot?.Size ?? Size.Empty;
        _bar = _icon = _timer = Rectangle.Empty;
        _anchoredProfileNeedsBar = false;
        SavedProfilePath = null;
        _ownConsumption.Checked = false;
        _barMode.Checked = true;
        RefreshMappings();
        RefreshRegions();
        RefreshButtons();
        SetStatus("Neues Profil begonnen. Einen vollständigen Screenshot in der gewünschten Spielauflösung laden und die gesamte Buffleiste markieren. Bereits gespeicherte Profile bleiben erhalten.");
    }

    private void LoadScreenshot()
    {
        using var picker = new OpenFileDialog { Title = "Vollständigen Spiel-Screenshot auswählen", Filter = "Screenshots (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        Bitmap? loaded = null;
        try
        {
            loaded = OpenImage(picker.FileName, 16384, 16384, 64 * 1024 * 1024);
            if ((long)loaded.Width * loaded.Height > 40_000_000) throw new InvalidDataException("Der Screenshot ist zu groß (maximal 40 Millionen Pixel).");
            if (_mappings.Count > 0 && _screenSize != loaded.Size)
                throw new InvalidDataException($"Die vorhandenen Vorlagen stammen aus {_screenSize.Width} × {_screenSize.Height}. Bitte dieselbe Spielauflösung verwenden oder ein neues Profil beginnen.");
            _screenSize = loaded.Size;
            if (!_bar.IsEmpty && !Contains(_screenSize, _bar)) _bar = Rectangle.Empty;
            _screenshot?.Dispose();
            _screenshot = loaded;
            loaded = null;
            _icon = _timer = Rectangle.Empty;
            _canvas.SetImage(_screenshot);
            RefreshRegions();
            if (_bar.IsEmpty) _barMode.Checked = true;
            else _iconMode.Checked = true;
            SetStatus($"Screenshot geladen: {_screenSize.Width} × {_screenSize.Height}. " + (_bar.IsEmpty
                ? "Zuerst die gesamte Buffleiste inklusive Zeitzeile markieren."
                : "Die Buffleiste prüfen, dann ein Symbol und seine Restlaufzeit markieren."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or OutOfMemoryException)
        { SetStatus($"Screenshot konnte nicht geladen werden: {error.Message}"); }
        finally { loaded?.Dispose(); RefreshButtons(); }
    }

    private static Bitmap OpenImage(string path, int maxWidth, int maxHeight, long maxBytes)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 || file.Length > maxBytes)
            throw new InvalidDataException("Die Bilddatei fehlt, ist leer oder überschreitet die Größenbegrenzung.");
        using var stream = file.OpenRead();
        using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        if (image.Width <= 0 || image.Height <= 0 || image.Width > maxWidth || image.Height > maxHeight ||
            (long)image.Width * image.Height > 40_000_000)
            throw new InvalidDataException("Die Bildabmessungen überschreiten die Größenbegrenzung.");
        return new Bitmap(image);
    }

    private void SetTarget(SelectionTarget target)
    {
        _target = target;
        RefreshButtons();
    }

    private void SelectRegion(Rectangle rectangle)
    {
        if (_screenshot is null || _testing) return;
        if (_target == SelectionTarget.Bar)
        {
            if (rectangle.Width > 8192 || rectangle.Height > 8192)
            { SetStatus("Die Buffleiste darf höchstens 8192 × 8192 Pixel umfassen."); return; }
            _bar = rectangle;
            _anchoredProfileNeedsBar = false;
            _icon = _timer = Rectangle.Empty;
            _iconMode.Checked = true;
            SetStatus("Buffleiste gesetzt. Jetzt die unveränderliche Mitte eines bekannten Buff-Symbols markieren. Die Leiste sollte genug Platz für später hinzukommende Buffs enthalten.");
        }
        else if (_bar.IsEmpty || !_bar.Contains(rectangle))
        { SetStatus("Symbol und Restlaufzeit müssen vollständig innerhalb der markierten Buffleiste liegen."); return; }
        else if (_target == SelectionTarget.Icon)
        {
            if (rectangle.Width is < 8 or > 256 || rectangle.Height is < 8 or > 256)
            { SetStatus("Das Symbol muss zwischen 8 × 8 und 256 × 256 Pixel groß sein."); return; }
            _icon = rectangle;
            _timer = Rectangle.Empty;
            _timerMode.Checked = true;
            SetStatus("Symbol gesetzt. Jetzt die zugehörige Restlaufzeit unter diesem Symbol markieren; etwas Platz für längere Zahlen lassen.");
        }
        else
        {
            if (_icon.IsEmpty) { SetStatus("Bitte zuerst das zugehörige Symbol markieren."); return; }
            if (rectangle.Width > 256 || rectangle.Height > 128)
            { SetStatus("Die Restlaufzeit darf höchstens 256 × 128 Pixel umfassen."); return; }
            _timer = rectangle;
            SetStatus("Exakten Buff und Variante auswählen, dann die Vorlage hinzufügen. Weitere Buffs können anschließend aus demselben oder einem neuen Screenshot ergänzt werden.");
        }
        RefreshRegions();
        RefreshButtons();
    }

    private void RefreshRegions()
    {
        _canvas.BarRegion = _bar;
        _canvas.IconRegion = _icon;
        _canvas.TimerRegion = _timer;
        _canvas.Invalidate();
        _selectionInfo.Text = $"Leiste: {Describe(_bar)}\nSymbol: {Describe(_icon)}\nRestlaufzeit: {Describe(_timer)}";
    }

    private static string Describe(Rectangle rectangle) => rectangle.IsEmpty ? "noch nicht markiert" :
        $"{rectangle.X}, {rectangle.Y} · {rectangle.Width} × {rectangle.Height}";

    private void RefreshChoice()
    {
        var definition = _buffChoice.SelectedItem as BuffDefinition;
        _ownConsumption.Checked = false;
        _ownConsumption.Visible = definition?.RequiresConsumptionConfirmation == true;
        _buffInfo.Text = definition is null ? "" :
            $"Dauer: {definition.Duration.TotalMinutes:0} Min. · {definition.Category}\n" +
            (definition.FixedUnitPrice is { } fixedPrice
                ? $"Kosten: {fixedPrice:N0} Silber (fester Zeltpreis)"
                : "Kosten: Zentralmarktpreis der gewählten Region");
        RefreshButtons();
    }

    private void AddMapping()
    {
        if (_screenshot is null || _icon.IsEmpty || _timer.IsEmpty ||
            _buffChoice.SelectedItem is not BuffDefinition definition) return;
        if (definition.RequiresConsumptionConfirmation && !_ownConsumption.Checked)
        { SetStatus("Gruppenbuffs nur zuordnen, wenn du die Kosten deinem eigenen Verbrauch zurechnen kannst."); return; }
        var replacements = _mappings.Where(mapping => mapping.Definition.Id == definition.Id ||
            !string.IsNullOrWhiteSpace(definition.RecognitionGroup) &&
            mapping.Definition.RecognitionGroup == definition.RecognitionGroup).ToArray();
        if (replacements.Length > 0 && MessageBox.Show(this,
            $"Diese Auswahl ersetzt die vorhandene Variante: {string.Join(", ", replacements.Select(mapping => mapping.Definition.DisplayName))}.\n\nEine Buff-Familie kann nur einer Kostenvariante zugeordnet werden.",
            "Vorlage ersetzen", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        var crop = _screenshot.Clone(_icon, PixelFormat.Format24bppRgb);
        foreach (var mapping in replacements) { _mappings.Remove(mapping); mapping.Dispose(); }
        var added = new Mapping(definition, crop,
            new BuffRegion(_timer.X - _icon.X, _timer.Y - _icon.Y, _timer.Width, _timer.Height),
            .92, definition.RequiresConsumptionConfirmation && _ownConsumption.Checked);
        _mappings.Add(added);
        RefreshMappings();
        _mappingsList.SelectedItem = added;
        _icon = _timer = Rectangle.Empty;
        _iconMode.Checked = true;
        RefreshRegions();
        SetStatus($"{definition.DisplayName} hinzugefügt. Das Profil enthält {_mappings.Count} Vorlage(n). Am Screenshot testen oder ein weiteres Symbol markieren.");
        RefreshButtons();
    }

    private void RemoveMapping()
    {
        if (_mappingsList.SelectedItem is not Mapping mapping) return;
        _mappings.Remove(mapping);
        mapping.Dispose();
        RefreshMappings();
        SetStatus($"{mapping.Definition.DisplayName} aus diesem neuen Profil entfernt.");
        RefreshButtons();
    }

    private void RefreshMappings()
    {
        _mappingsList.BeginUpdate();
        _mappingsList.Items.Clear();
        foreach (var mapping in _mappings) _mappingsList.Items.Add(mapping);
        _mappingsList.EndUpdate();
    }

    private void RefreshButtons()
    {
        var usableBar = !_bar.IsEmpty && !_anchoredProfileNeedsBar && Contains(_screenSize, _bar);
        _load.Enabled = !_testing;
        _new.Enabled = !_testing;
        _editor.Enabled = !_testing;
        _add.Enabled = !_testing && usableBar && !_icon.IsEmpty && !_timer.IsEmpty &&
            (_buffChoice.SelectedItem is BuffDefinition definition &&
                (!definition.RequiresConsumptionConfirmation || _ownConsumption.Checked));
        _remove.Enabled = !_testing && _mappingsList.SelectedItem is Mapping;
        _test.Enabled = !_testing && usableBar && _screenshot is not null && _mappings.Count > 0;
        _save.Enabled = !_testing && usableBar && _mappings.Count > 0;
        _iconMode.Enabled = usableBar && _screenshot is not null;
        _timerMode.Enabled = usableBar && !_icon.IsEmpty;
    }

    private async Task TestAsync()
    {
        if (_screenshot is null || _testing) return;
        _testing = true;
        RefreshButtons();
        SetStatus("Symbole und Restlaufzeiten werden geprüft … Dieser Screenshot-Test erzeugt keine Verbrauchsbuchungen.");
        var directory = Path.Combine(Path.GetTempPath(), "Grindcrest-buff-test-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _testCancellation = cancellation;
        try
        {
            var path = WriteProfile(directory);
            var store = new BuffRecognitionProfileStore(path);
            var profile = store.Load() ?? throw new InvalidDataException(store.LastError);
            using var frame = new Bitmap(_screenshot);
            var result = await Task.Run(() =>
            {
                using var reader = new BuffFrameReader(() => profile);
                var reading = reader.Read(frame, cancellation.Token);
                return (Reading: reading, Diagnostic: reader.LastDiagnostic);
            }, cancellation.Token);
            if (IsDisposed) return;
            SetStatus(result.Reading is null ? result.Diagnostic ?? "Kein Buff mit lesbarer Restlaufzeit erkannt." :
                $"{result.Diagnostic}{Environment.NewLine}{string.Join(Environment.NewLine, result.Reading.Observations.Select(observation =>
                    BuffPriceCatalog.Definitions.Single(definition => definition.Id == observation.BuffId).DisplayName +
                    $": {observation.Remaining.TotalMinutes:0.#} Min. verbleibend"))}");
        }
        catch (OperationCanceledException)
        { if (!IsDisposed) SetStatus("Der Screenshot-Test wurde abgebrochen oder hat das Zeitlimit erreicht."); }
        catch (Exception error)
        { if (!IsDisposed) SetStatus($"Der Screenshot-Test konnte nicht abgeschlossen werden: {error.Message}"); }
        finally
        {
            _testCancellation = null;
            TryDeleteDirectory(directory);
            _testing = false;
            if (!IsDisposed) RefreshButtons();
        }
    }

    private void SaveProfile()
    {
        if (!_icon.IsEmpty && !_timer.IsEmpty && MessageBox.Show(this,
            "Die aktuelle Markierung wurde noch nicht als Vorlage hinzugefügt. Nur die bereits aufgeführten Vorlagen speichern?",
            "Offene Markierung", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        var directory = Path.Combine(_outputDirectory, $"buff-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        try
        {
            SavedProfilePath = WriteProfile(directory);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or System.Runtime.InteropServices.ExternalException)
        {
            TryDeleteDirectory(directory);
            SetStatus($"Das Profil konnte nicht gespeichert werden: {error.Message}");
        }
    }

    private string WriteProfile(string directory)
    {
        var templates = _mappings.Select(mapping => new BuffIconTemplate(mapping.Definition.Id,
            mapping.Definition.Id + ".png", mapping.Timer)
        {
            MinimumSimilarity = mapping.MinimumSimilarity,
            ConsumptionAttributionConfirmed = mapping.ConsumptionAttributionConfirmed,
        }).ToArray();
        var profile = new BuffRecognitionProfile
        {
            ScreenWidth = _screenSize.Width, ScreenHeight = _screenSize.Height, UiScale = 1,
            Region = new(_bar.X, _bar.Y, _bar.Width, _bar.Height), Templates = templates,
        };
        if (_anchoredProfileNeedsBar || !Contains(_screenSize, _bar) || !profile.IsValid)
            throw new InvalidDataException(profile.ValidationError ?? "Bitte die Buffleiste im vollständigen Screenshot neu markieren.");
        Directory.CreateDirectory(directory);
        foreach (var mapping in _mappings)
            mapping.Icon.Save(Path.Combine(directory, mapping.Definition.Id + ".png"), ImageFormat.Png);
        var path = Path.Combine(directory, "profile.json");
        File.WriteAllText(path, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static bool Contains(Size size, Rectangle rectangle) => rectangle.Width > 0 && rectangle.Height > 0 &&
        rectangle.X >= 0 && rectangle.Y >= 0 && (long)rectangle.Right <= size.Width && (long)rectangle.Bottom <= size.Height;

    private static void TryDeleteDirectory(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void SetStatus(string text) => _status.Text = text;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _testCancellation?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _testCancellation?.Cancel();
            _screenshot?.Dispose();
            _screenshot = null;
            foreach (var mapping in _mappings) mapping.Dispose();
            _mappings.Clear();
        }
        base.Dispose(disposing);
    }
}
