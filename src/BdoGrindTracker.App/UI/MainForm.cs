using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Capture;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Diagnostics;
using BdoGrindTracker.Core;
using System.Globalization;
using System.Security.Cryptography;

namespace BdoGrindTracker.App.UI;

internal sealed class MainForm : Form
{
    private readonly ILootFrameAnalyzer _analyzer;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly PassiveCaptureSession _captureSession;
    private readonly PictureBox _brandLogo = new();
    private readonly Bitmap _brandLogoImage = AppBranding.CreateLogo();
    private readonly Icon _brandIcon = AppBranding.CreateWindowIcon();

    private readonly Label _activeSpotLabel = new();
    private readonly CheckBox _eventLootCheckBox = new();
    private readonly CheckBox _recordingCheckBox = new();
    private readonly Label _recordingStatus = new();
    private readonly BdoSurfacePanel _optionsPanel = new();
    private readonly BdoButton _optionsButton = new();
    private readonly GrindSessionClock _sessionClock;
    private readonly GrindInactivityTimer _inactivityTimer;
    private readonly NumericUpDown _autoPauseMinutes = new();
    private readonly ComboBox _classOverrideComboBox = new();
    private readonly Label _characterClassLabel = new();
    private readonly Func<CharacterClassDetection> _detectCharacterClass;
    private CharacterClassDetection _classDetection = CharacterClassDetection.Unknown;
    private CharacterClass? _sessionClass;
    private bool _classDetectionInProgress;
    private readonly GarmothUploadClient _garmothClient;
    private readonly GarmothApiKeyStore _garmothKeyStore;
    private readonly BdoButton _garmothButton = new();
    private readonly BdoButton _garmothOptionsButton = new();
    private string _garmothApiKey = string.Empty;
    private bool _garmothUploadInProgress;
    private Guid _sessionId = Guid.NewGuid();
    private DateTimeOffset? _sessionStartedAt;
    private string? _sessionSpotId;
    private bool _sessionSubmitted;
    private readonly MetricValueLabel _sessionDurationValue = new();
    private readonly Label _sessionStateLabel = new();
    private readonly Label _optionsHint = new();
    private bool _optionsExpanded;
    private readonly System.Windows.Forms.Timer _uiRefreshTimer = new() { Interval = 500 };
    private readonly FrameUiMailbox _uiMailbox = new();
    private LootSessionSnapshot _sessionSummary = LootSessionSnapshot.Empty;
    private DiagnosticRecordingSession? _recording;
    private bool _hasSession;
    private readonly ComboBox _monitorComboBox = new();
    private readonly TableLayoutPanel _monitorPicker = new();
    private readonly BdoButton _trackingButton = new();
    private readonly BdoButton _resetButton = new();
    private readonly LootTotalsView _totalsView = new();
    private readonly MetricValueLabel _silverBeforeTaxValue = new();
    private readonly MetricValueLabel _silverAfterTaxValue = new();
    private readonly Label _priceStatusLabel = new();
    private readonly BdoButton _silverOptionsButton = new();
    private readonly ToolTip _priceDetails = new() { AutoPopDelay = 15000 };
    private readonly ILootPriceProvider _priceProvider;
    private readonly CancellationTokenSource _priceLifetime = new();
    private LootPriceSnapshot _prices;
    private SilverValuationResult? _silverValuation;
    private bool _priceRefreshEnabled;
    private bool _priceRefreshInProgress;
    private Task? _priceRefreshTask;
    private string? _priceRefreshRegion;
    private DateTimeOffset _nextPriceRefreshAt = DateTimeOffset.MinValue;
    private static readonly CultureInfo SilverCulture = CultureInfo.GetCultureInfo("de-DE");
    private readonly Label _statusDot = new();
    private readonly Label _statusLabel = new();

    private readonly Font _baseFont = new("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Font _titleFont = new("Segoe UI Semibold", 21f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _metricFont = new("Segoe UI Semibold", 25f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _sectionFont = new("Segoe UI Semibold", 12f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _captionFont = new("Segoe UI Semibold", 7.5f, FontStyle.Bold, GraphicsUnit.Point);

    private Rectangle? _lastCaptureDesktopRegion;
    private Exception? _lastCaptureStopError;
    private bool _captureSegmentCompleted = true;
    private bool _initializing = true;
    private bool _uiRunning;
    private bool _operationInProgress;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;
    private bool _uiResourcesDisposed;
    private bool IsBusy => _operationInProgress || _garmothUploadInProgress;

    public MainForm(
        PassiveScreenCapture screenCapture,
        ILootFrameAnalyzer analyzer,
        SettingsStore settingsStore,
        GrindSessionClock? sessionClock = null,
        GrindInactivityTimer? inactivityTimer = null,
        Func<CharacterClassDetection>? classDetector = null,
        ILootPriceProvider? priceProvider = null,
        GarmothUploadClient? garmothClient = null,
        GarmothApiKeyStore? garmothKeyStore = null)
    {
        ArgumentNullException.ThrowIfNull(screenCapture);
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _settings = settingsStore.Load();
        _captureSession = new PassiveCaptureSession(screenCapture);
        _sessionClock = sessionClock ?? new GrindSessionClock();
        _inactivityTimer = inactivityTimer ?? new GrindInactivityTimer();
        _detectCharacterClass = classDetector ?? new CompanionCharacterClassDetector().DetectDefault;
        _priceProvider = priceProvider ?? new ArshaLootPriceProvider();
        _prices = _priceProvider.GetCachedSnapshot(_settings.MarketRegion);
        _garmothClient = garmothClient ?? new GarmothUploadClient();
        _garmothKeyStore = garmothKeyStore ?? new GarmothApiKeyStore();
        try { _garmothApiKey = _garmothKeyStore.Load(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // A key encrypted for a different Windows user must never be exposed or reused.
            _garmothApiKey = string.Empty;
        }

        // Keep the 96-DPI design baseline until the entire control tree exists.
        // Scaling an empty form would leave subsequently added pixel dimensions unscaled.
        SuspendLayout();
        Text = AppBranding.WindowTitle;
        MinimumSize = new Size(860, 640);
        Size = new Size(1160, 840);
        StartPosition = FormStartPosition.Manual;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = _baseFont;
        BackColor = BdoTheme.Background;
        ForeColor = BdoTheme.Text;
        Icon = _brandIcon;
        ShowIcon = true;

        PositionOnNonPrimaryScreenWhenAvailable();
        BuildInterface();
        PopulateMonitors();
        WireEvents();
        _uiRefreshTimer.Start();

        _initializing = false;
        UpdateSessionSummary();
        UpdateControlState();
        if (!_analyzer.IsAvailable)
        {
            SetStatus(UiStatusKind.Error, "Erkennung nicht verfügbar", _analyzer.Status);
        }
        else if (_monitorComboBox.Items.Count == 0)
        {
            SetStatus(UiStatusKind.Error, "Kein Monitor", "Es wurde kein Bildschirm gefunden.");
        }
        else
        {
            SetStatus(UiStatusKind.Ready, "Bereit", "Spielmonitor unter Optionen prüfen und Tracking starten.");
        }
        ResumeLayout(performLayout: true);
    }

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
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(24, 18, 24, 12),
            BackColor = BdoTheme.Background,
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSpotStrip(), 0, 1);
        root.Controls.Add(BuildTrackingOptions(), 0, 2);
        root.Controls.Add(BuildSummary(), 0, 3);
        root.Controls.Add(BuildLootArea(), 0, 4);
        root.Controls.Add(BuildFooter(), 0, 5);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Dock = DockStyle.Fill
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _brandLogo.Image = _brandLogoImage;
        _brandLogo.SizeMode = PictureBoxSizeMode.Zoom;
        _brandLogo.Size = new Size(52, 52);
        _brandLogo.Anchor = AnchorStyles.Left;
        _brandLogo.Margin = new Padding(0, 0, 10, 0);
        _brandLogo.TabStop = false;
        _brandLogo.AccessibleName = "Grindcrest-Logo";
        brand.Controls.Add(_brandLogo, 0, 0);
        brand.SetRowSpan(_brandLogo, 2);
        brand.Controls.Add(new Label
        {
            Text = "BLACK DESERT  /  LOOT TRACKER",
            AutoSize = true,
            Font = _captionFont,
            ForeColor = BdoTheme.Gold,
            Margin = new Padding(2, 0, 0, 3)
        }, 1, 0);
        brand.Controls.Add(new Label
        {
            Text = AppBranding.Name,
            AutoSize = true,
            Font = _titleFont,
            Margin = Padding.Empty
        }, 1, 1);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(12, 15, 0, 0),
            Anchor = AnchorStyles.Right
        };
        _resetButton.Text = "Neue Sitzung";
        _resetButton.ButtonStyle = BdoButtonStyle.Secondary;
        _resetButton.Width = 124;
        _resetButton.Margin = new Padding(0, 0, 10, 0);
        _trackingButton.Text = "Tracking starten";
        _trackingButton.ButtonStyle = BdoButtonStyle.Primary;
        _trackingButton.Width = 150;
        _trackingButton.Margin = Padding.Empty;
        _trackingButton.AccessibleName = "Tracking starten oder pausieren";
        _garmothButton.Text = "Garmoth-Upload";
        _garmothButton.ButtonStyle = BdoButtonStyle.Secondary;
        _garmothButton.Width = 144;
        _garmothButton.Margin = new Padding(0, 0, 10, 0);
        _garmothButton.AccessibleName = "Sitzung mit einem Klick nach Garmoth hochladen";
        _garmothButton.AccessibleDescription = "Pausiert bei Bedarf und überträgt die aktuellen Sitzungswerte sofort.";
        actions.Controls.Add(_resetButton);
        actions.Controls.Add(_garmothButton);
        actions.Controls.Add(_trackingButton);
        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(actions, 1, 0);
        return header;
    }

    private Control BuildSpotStrip()
    {
        var strip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10)
        };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _activeSpotLabel.AutoSize = true;
        _activeSpotLabel.AccessibleName = "Automatisch erkannter Grindspot";
        _activeSpotLabel.ForeColor = BdoTheme.TextMuted;
        _activeSpotLabel.Text = "Spot: wird aus Trashloot erkannt";
        _activeSpotLabel.Anchor = AnchorStyles.Left;
        _activeSpotLabel.Margin = new Padding(2, 0, 12, 0);
        var identity = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, Dock = DockStyle.Fill,
            Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 0)
        };
        _characterClassLabel.AccessibleName = "Automatisch erkannte Klasse";
        _characterClassLabel.AutoSize = true;
        _characterClassLabel.ForeColor = BdoTheme.Gold;
        _characterClassLabel.Text = "Klasse: wird beim Start erkannt";
        _characterClassLabel.Margin = Padding.Empty;
        identity.Controls.Add(_activeSpotLabel);
        identity.Controls.Add(_characterClassLabel);
        _optionsButton.Text = "Optionen";
        _optionsButton.ButtonStyle = BdoButtonStyle.Secondary;
        _optionsButton.Width = 110;
        _optionsButton.Height = 34;
        _optionsButton.Margin = Padding.Empty;
        _optionsButton.AccessibleName = "Tracking-Optionen öffnen oder schließen";
        strip.Controls.Add(identity, 0, 0);
        strip.Controls.Add(_optionsButton, 1, 0);
        return strip;
    }

    private Control BuildTrackingOptions()
    {
        _optionsPanel.AccessibleName = "Tracking-Optionen";
        _optionsPanel.Dock = DockStyle.Fill;
        _optionsPanel.AutoSize = true;
        _optionsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _optionsPanel.Padding = new Padding(16, 10, 16, 10);
        _optionsPanel.Margin = new Padding(0, 0, 0, 8);
        _optionsPanel.Visible = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 4, Margin = Padding.Empty,
            BackColor = BdoTheme.Surface
        };
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
        _monitorPicker.AutoSize = true;
        _monitorPicker.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _monitorPicker.ColumnCount = 1;
        _monitorPicker.RowCount = 2;
        _monitorPicker.Margin = Padding.Empty;
        _monitorPicker.Controls.Add(CreateOptionCaption("SPIELMONITOR"), 0, 0);
        _monitorComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _monitorComboBox.Width = 214;
        _monitorComboBox.Margin = Padding.Empty;
        _monitorComboBox.AccessibleName = "Spielmonitor";
        BdoTheme.StyleComboBox(_monitorComboBox);
        _monitorPicker.Controls.Add(_monitorComboBox, 0, 1);
        fields.Controls.Add(_monitorPicker, 0, 0);

        var pauseOption = new TableLayoutPanel
        {
            AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty
        };
        pauseOption.Controls.Add(CreateOptionCaption("AUTOMATISCHE PAUSE"), 0, 0);
        var pauseInput = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, Margin = Padding.Empty
        };
        _autoPauseMinutes.Minimum = AppSettings.MinimumAutoPauseMinutes;
        _autoPauseMinutes.Maximum = AppSettings.MaximumAutoPauseMinutes;
        _autoPauseMinutes.Value = _settings.AutoPauseMinutes;
        _autoPauseMinutes.Width = 64;
        _autoPauseMinutes.BackColor = BdoTheme.SurfaceRaised;
        _autoPauseMinutes.ForeColor = BdoTheme.Text;
        _autoPauseMinutes.TextAlign = HorizontalAlignment.Center;
        _autoPauseMinutes.BorderStyle = BorderStyle.FixedSingle;
        _autoPauseMinutes.Margin = new Padding(0, 3, 10, 0);
        _autoPauseMinutes.AccessibleName = "Auto-Pause nach Minuten ohne Drop";
        pauseInput.Controls.Add(_autoPauseMinutes);
        pauseInput.Controls.Add(new Label
        {
            Text = "Minuten ohne neuen Drop", AutoSize = true,
            ForeColor = BdoTheme.TextMuted, Margin = new Padding(0, 6, 0, 0)
        });
        pauseOption.Controls.Add(pauseInput, 0, 1);
        fields.Controls.Add(pauseOption, 1, 0);
        var classOption = new TableLayoutPanel
        {
            AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty,
            Dock = DockStyle.Fill
        };
        classOption.Controls.Add(CreateOptionCaption("KLASSE / SPEZIALISIERUNG"), 0, 0);
        _classOverrideComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _classOverrideComboBox.Dock = DockStyle.Top;
        _classOverrideComboBox.DropDownWidth = 280;
        _classOverrideComboBox.Margin = Padding.Empty;
        _classOverrideComboBox.AccessibleName = "Klasse automatisch oder manuell";
        BdoTheme.StyleComboBox(_classOverrideComboBox);
        _classOverrideComboBox.Items.Add("Automatisch erkennen");
        _classOverrideComboBox.Items.AddRange(CompanionCharacterClassCatalog.Classes.Cast<object>().ToArray());
        _classOverrideComboBox.SelectedIndex = 0;
        classOption.Controls.Add(_classOverrideComboBox, 0, 1);
        fields.Controls.Add(classOption, 2, 0);

        var flags = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            Margin = new Padding(0, 0, 0, 3)
        };
        _eventLootCheckBox.Text = "Event-Loot zulassen";
        _eventLootCheckBox.AutoSize = true;
        _eventLootCheckBox.Margin = new Padding(0, 0, 24, 0);
        _recordingCheckBox.Text = "Loot-Diagnose lokal aufzeichnen";
        _recordingCheckBox.AutoSize = true;
        _recordingCheckBox.Margin = Padding.Empty;
        flags.Controls.Add(_eventLootCheckBox);
        flags.Controls.Add(_recordingCheckBox);
        _garmothOptionsButton.Text = "Garmoth-Key";
        _garmothOptionsButton.ButtonStyle = BdoButtonStyle.Secondary;
        _garmothOptionsButton.Size = new Size(138, 24);
        _garmothOptionsButton.Margin = new Padding(16, 0, 0, 0);
        _garmothOptionsButton.AccessibleName = "Garmoth-API-Key hinterlegen";
        flags.Controls.Add(_garmothOptionsButton);

        _optionsHint.AutoSize = true;
        _optionsHint.ForeColor = BdoTheme.TextMuted;
        _optionsHint.Margin = Padding.Empty;
        _recordingStatus.AutoSize = true;
        _recordingStatus.ForeColor = BdoTheme.TextMuted;
        _recordingStatus.MaximumSize = new Size(720, 0);
        _recordingStatus.Text = "Nur lokale Loot-Ausschnitte, keine Übertragung.";
        _recordingStatus.Margin = new Padding(0, 4, 0, 0);
        _recordingStatus.Visible = false;
        layout.Controls.Add(fields, 0, 0);
        layout.Controls.Add(flags, 0, 1);
        layout.Controls.Add(_optionsHint, 0, 2);
        layout.Controls.Add(_recordingStatus, 0, 3);
        _optionsPanel.Controls.Add(layout);
        return _optionsPanel;
    }

    private Label CreateOptionCaption(string text) => new()
    {
        Text = text, AutoSize = true, Font = _captionFont, ForeColor = BdoTheme.TextMuted,
        Margin = new Padding(0, 0, 0, 4)
    };

    private void ToggleOptions()
    {
        _optionsExpanded = !_optionsExpanded;
        _optionsPanel.Visible = _optionsExpanded;
        _optionsButton.Text = _optionsExpanded ? "Schließen" : "Optionen";
        _optionsButton.AccessibleDescription = _optionsExpanded ? "Ausgeklappt" : "Eingeklappt";
    }

    private Control BuildSummary()
    {
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 16),
            Padding = Padding.Empty
        };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
        summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _sessionDurationValue.AccessibleName = "Dauer der aktuellen Grindsession";
        summary.Controls.Add(CreateMetricCard("SESSIONDAUER", _sessionDurationValue,
            new Padding(0, 0, 6, 0), _sessionStateLabel), 0, 0);
        _silverBeforeTaxValue.AccessibleName = "Silberwert vor Steuer";
        _silverAfterTaxValue.AccessibleName = "Silberwert nach Steuer";
        summary.Controls.Add(CreateMetricCard("SILBER VOR STEUER", _silverBeforeTaxValue,
            new Padding(6, 0, 6, 0)), 1, 0);
        summary.Controls.Add(CreateMetricCard("SILBER NACH STEUER", _silverAfterTaxValue,
            new Padding(6, 0, 0, 0)), 2, 0);
        _sessionDurationValue.ForeColor = BdoTheme.GoldBright;
        _silverAfterTaxValue.ForeColor = BdoTheme.GoldBright;
        return summary;
    }

    private Control CreateMetricCard(string caption, MetricValueLabel valueLabel, Padding margin, Label? state = null)
    {
        var card = new BdoSurfacePanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            CornerRadius = 14,
            Padding = new Padding(16, 12, 16, 10),
            Margin = margin
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = BdoTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var captionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        captionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        captionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        captionRow.Controls.Add(new Label
        {
            Text = caption,
            AutoSize = true,
            Font = _captionFont,
            ForeColor = BdoTheme.TextMuted,
            Margin = Padding.Empty
        }, 0, 0);
        if (state is not null)
        {
            state.AutoSize = true;
            state.Font = _captionFont;
            state.ForeColor = BdoTheme.TextMuted;
            state.Margin = Padding.Empty;
            captionRow.Controls.Add(state, 1, 0);
        }
        valueLabel.Text = "0";
        valueLabel.AutoSize = true;
        valueLabel.AutoEllipsis = false;
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.Font = _metricFont;
        valueLabel.ForeColor = BdoTheme.Text;
        valueLabel.Margin = Padding.Empty;
        layout.Controls.Add(captionRow, 0, 0);
        layout.Controls.Add(valueLabel, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildLootArea()
    {
        var surface = new BdoSurfacePanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 16,
            Padding = new Padding(18, 14, 18, 16),
            Margin = new Padding(0, 0, 0, 12),
            AccessibleName = "Session-Loot Übersicht"
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BdoTheme.Surface,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        var header = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(2, 0, 2, 6)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label
        {
            Text = "Session-Loot",
            AutoSize = true,
            Font = _sectionFont,
            ForeColor = BdoTheme.Text,
            Margin = Padding.Empty
        }, 0, 0);
        _priceStatusLabel.AutoSize = false;
        _priceStatusLabel.AutoEllipsis = true;
        _priceStatusLabel.Dock = DockStyle.Fill;
        _priceStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _priceStatusLabel.ForeColor = BdoTheme.TextMuted;
        _priceStatusLabel.Margin = new Padding(12, 0, 12, 0);
        _priceStatusLabel.AccessibleName = "Status der Silberbewertung";
        header.Controls.Add(_priceStatusLabel, 1, 0);
        _silverOptionsButton.Text = "Preise / Steuer";
        _silverOptionsButton.ButtonStyle = BdoButtonStyle.Secondary;
        _silverOptionsButton.Size = new Size(134, 26);
        _silverOptionsButton.Margin = Padding.Empty;
        _silverOptionsButton.AccessibleName = "Preisregion und Steuer konfigurieren";
        header.Controls.Add(_silverOptionsButton, 2, 0);
        _totalsView.Dock = DockStyle.Fill;
        _totalsView.Margin = Padding.Empty;
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_totalsView, 0, 1);
        surface.Controls.Add(layout);
        return surface;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(2, 2, 2, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _statusDot.Text = "●";
        _statusDot.AutoSize = true;
        _statusDot.ForeColor = BdoTheme.TextMuted;
        _statusDot.Margin = new Padding(0, 0, 7, 0);
        _statusLabel.AutoSize = true;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = BdoTheme.TextMuted;
        _statusLabel.Margin = Padding.Empty;
        footer.Controls.Add(_statusDot, 0, 0);
        footer.Controls.Add(_statusLabel, 1, 0);
        return footer;
    }

    private void WireEvents()
    {
        _monitorComboBox.SelectedIndexChanged += MonitorComboBox_SelectedIndexChanged;
        _trackingButton.Click += TrackingButton_Click;
        _resetButton.Click += ResetButton_Click;
        _garmothButton.Click += GarmothButton_Click;
        _garmothOptionsButton.Click += GarmothOptionsButton_Click;
        _optionsButton.Click += (_, _) => ToggleOptions();
        _silverOptionsButton.Click += SilverOptionsButton_Click;
        Shown += async (_, _) =>
        {
            _priceRefreshEnabled = true;
            await Task.WhenAll(RefreshClassDetectionAsync(), RefreshPricesAsync());
        };
        _classOverrideComboBox.SelectedIndexChanged += (_, _) =>
        {
            _sessionClass = SelectedCharacterClass;
            UpdateCharacterClassLabel();
        };
        _recordingCheckBox.CheckedChanged += (_, _) =>
            _recordingStatus.Visible = _recordingCheckBox.Checked;
        _autoPauseMinutes.ValueChanged += (_, _) =>
        {
            if (!_initializing)
                SaveSettings();
        };
        _uiRefreshTimer.Tick += async (_, _) =>
        {
            RefreshPendingUi();
            await PauseIfInactiveAsync();
            if (_priceRefreshEnabled && !_priceRefreshInProgress && DateTimeOffset.UtcNow >= _nextPriceRefreshAt)
                await RefreshPricesAsync();
        };
        _captureSession.Stopped += CaptureSession_Stopped;
        FormClosing += MainForm_FormClosing;
    }

    private void PopulateMonitors()
    {
        var allScreens = Screen.AllScreens;
        var monitors = allScreens
            .Select((screen, index) => new MonitorOption(
                index + 1,
                screen.DeviceName,
                screen.Bounds,
                screen.Primary))
            .OrderByDescending(static screen => screen.IsPrimary)
            .ThenBy(static screen => screen.Bounds.Left)
            .ToArray();

        _monitorComboBox.Items.Clear();
        _monitorComboBox.Items.AddRange(monitors.Cast<object>().ToArray());
        _monitorPicker.Visible = monitors.Length > 1;

        if (monitors.Length == 0)
        {
            return;
        }

        var savedIndex = Array.FindIndex(
            monitors,
            option => string.Equals(
                option.DeviceName,
                _settings.MonitorDeviceName,
                StringComparison.OrdinalIgnoreCase));
        _monitorComboBox.SelectedIndex = savedIndex >= 0 ? savedIndex : 0;
    }

    private void PositionOnNonPrimaryScreenWhenAvailable()
    {
        var destination = Screen.AllScreens.FirstOrDefault(static screen => !screen.Primary)
            ?? Screen.PrimaryScreen;
        if (destination is null)
        {
            return;
        }

        var workingArea = destination.WorkingArea;
        var width = Math.Min(Size.Width, workingArea.Width);
        var height = Math.Min(Size.Height, workingArea.Height);
        Bounds = new Rectangle(
            workingArea.Left + Math.Max(0, (workingArea.Width - width) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - height) / 2),
            width,
            height);
    }

    private void MonitorComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        UpdateControlState();
        SetStatus(UiStatusKind.Ready, "Bereit", "Spielmonitor gewählt. Tracking kann starten.");
    }

    private async void TrackingButton_Click(object? sender, EventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        _operationInProgress = true;
        UpdateControlState();
        try
        {
            if (_uiRunning)
            {
                await StopTrackingAsync();
            }
            else
            {
                await StartTrackingAsync();
            }
        }
        finally
        {
            _operationInProgress = false;
            UpdateControlState();
        }
    }

    private async Task StartTrackingAsync()
    {
        var monitor = GetSelectedMonitor();
        if (monitor is null)
        {
            SetStatus(UiStatusKind.Error, "Kein Monitor", "Es wurde kein Bildschirm gefunden.");
            return;
        }

        if (!_analyzer.IsAvailable)
        {
            SetStatus(UiStatusKind.Error, "Erkennung nicht verfügbar", _analyzer.Status);
            return;
        }
        if (_sessionSubmitted)
            return;

        var continuesExistingSession = _hasSession;
        var captureGeometryChanged = _lastCaptureDesktopRegion is { } previousRegion &&
            previousRegion != monitor.Bounds;

        try
        {
            SaveSettings();
            await RefreshClassDetectionAsync();
            if (_shutdownStarted)
                return;
            _sessionClass ??= SelectedCharacterClass;
            UpdateCharacterClassLabel();
            if (_analyzer is ILootFilterConfigurableAnalyzer configurable)
                configurable.ConfigureLootFilter(_eventLootCheckBox.Checked);
            if (!continuesExistingSession || captureGeometryChanged)
            {
                _analyzer.Reset();
                _activeSpotLabel.Text = "Spot: wird aus Trashloot erkannt";

                _recording?.Dispose();
                _recording = null;
                if (_recordingCheckBox.Checked)
                {
                    var directory = Path.Combine(Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData), "BdoGrindTracker", "diagnostics");
                    _recording = DiagnosticRecordingSession.Start(directory, null);
                    _recordingStatus.Text = _recording.IsRecording
                        ? $"Lokal: {_recording.RecordingPath}"
                        : $"Aufzeichnung nicht möglich: {_recording.LastError}";
                }
            }

            Interlocked.Exchange(ref _lastCaptureStopError, null);
            _captureSegmentCompleted = false;
            if (!_hasSession)
                _sessionStartedAt = DateTimeOffset.UtcNow;
            _hasSession = true;
            _uiRunning = true;

            UpdateControlState();

            _inactivityTimer.Start();
            _captureSession.StartCompanion(
                monitor.Bounds,
                ProcessFrameAsync);
            _sessionClock.Start();
            UpdateSessionDuration();
            if (_optionsExpanded)
                ToggleOptions();
            _lastCaptureDesktopRegion = monitor.Bounds;
            SetStatus(
                UiStatusKind.Active,
                "Tracking aktiv",
                "Drops werden automatisch erkannt und gezählt.");
        }
        catch (Exception exception)
        {
            _uiRunning = false;
            _sessionClock.Pause();
            _inactivityTimer.Pause();
            UpdateSessionDuration();
            UpdateControlState();
            SetStatus(
                UiStatusKind.Error,
                "Start fehlgeschlagen",
                exception.Message);
            await _captureSession.StopAsync();
            CompleteCaptureSegment(DateTimeOffset.UtcNow);
        }
    }

    private async Task StopTrackingAsync(bool automatic = false)
    {
        _sessionClock.Pause();
        _inactivityTimer.Pause();
        UpdateSessionDuration();
        SetStatus(UiStatusKind.Paused, "Wird pausiert", "Die letzten Drops werden noch übernommen …");
        await _captureSession.StopAsync();
        CompleteCaptureSegment(DateTimeOffset.UtcNow);
        _uiRunning = false;

        var failure = Interlocked.CompareExchange(ref _lastCaptureStopError, null, null);
        if (failure is null)
        {
            SetStatus(UiStatusKind.Paused, automatic ? "Automatisch pausiert" : "Pausiert",
                automatic
                    ? $"Seit {_autoPauseMinutes.Value:0} {(_autoPauseMinutes.Value == 1 ? "Minute" : "Minuten")} kein neuer Drop. Mit Fortsetzen geht es weiter."
                    : "Die Session bleibt erhalten.");
        }
        else
        {
            SetStatus(UiStatusKind.Error, "Tracking gestoppt", failure.Message);
        }
    }

    private async Task PauseIfInactiveAsync()
    {
        if (!_uiRunning || IsBusy || _shutdownStarted ||
            !_inactivityTimer.ShouldPause(TimeSpan.FromMinutes((double)_autoPauseMinutes.Value)))
            return;

        _operationInProgress = true;
        UpdateControlState();
        try
        {
            await StopTrackingAsync(automatic: true);
        }
        finally
        {
            _operationInProgress = false;
            UpdateControlState();
        }
    }

    private void ResetButton_Click(object? sender, EventArgs e)
    {
        if (_uiRunning || IsBusy)
        {
            return;
        }

        _analyzer.Reset();
        _recording?.Dispose();
        _recording = null;
        _hasSession = false;
        _sessionId = Guid.NewGuid();
        _sessionStartedAt = null;
        _sessionSpotId = null;
        _sessionSubmitted = false;
        _garmothButton.Text = "Garmoth-Upload";
        _sessionClass = null;
        _classOverrideComboBox.SelectedIndex = 0;
        UpdateCharacterClassLabel();
        _activeSpotLabel.Text = "Spot: wird aus Trashloot erkannt";
        _recordingCheckBox.Checked = false;
        _recordingStatus.Text = "Aufzeichnung aus · für die nächste Session erneut aktivieren.";
        _sessionClock.Reset();
        _inactivityTimer.Reset();
        UpdateSessionDuration();
        _uiMailbox.Reset();
        _sessionSummary = LootSessionSnapshot.Empty;
        _totalsView.SetTotals(_sessionSummary.Totals);
        _lastCaptureDesktopRegion = null;

        UpdateSessionSummary();
        UpdateControlState();
        SetStatus(UiStatusKind.Ready, "Bereit", "Neue Session angelegt.");
    }

    private async Task ProcessFrameAsync(
        Bitmap frame,
        CapturedFrameMetadata metadata,
        CancellationToken cancellationToken)
    {
        var analysis = await _analyzer
            .AnalyzeAsync(
                frame,
                metadata.CapturedAtUtc,
                metadata.IsHdr,
                cancellationToken)
            .ConfigureAwait(false);
        _recording?.RecordFrame(metadata.CapturedAtUtc, analysis.Observations,
            analysis.TrackingResult, frame, analysis.PanelRegion, analysis.RareBandRegion);

        // Apply every event before returning; render only the latest aggregate.
        // No UI screenshots, raw-text formatting, or growing decision log.
        if (_uiMailbox.Publish(analysis))
            _inactivityTimer.RecordDrop();
    }

    private void RefreshPendingUi()
    {
        if (IsDisposed)
            return;

        // Session time advances even when no frame or drop arrives.
        UpdateSessionDuration();
        using var update = _uiMailbox.TakeLatest();
        if (update is null)
            return;

        if (_recording?.LastError is { } recordingError)
            _recordingStatus.Text = $"Aufzeichnung beendet: {recordingError}";
        _sessionSpotId = update.Analysis.SpotId;
        _activeSpotLabel.Text = update.Analysis.SpotId is { } spotId
            ? $"Spot: {LootSpotCatalog.GetRequired(spotId).DisplayName}"
            : "Spot: wird aus Trashloot erkannt";

        if (update.Totals is { } totals)
        {
            _sessionSummary = totals;
            _totalsView.SetTotals(totals.Totals);
            UpdateSessionSummary();
            UpdateControlState();
        }

        if (!_uiRunning || _shutdownStarted)
            return;

        var message = update.Analysis.PanelRegion is null
            ? "Lootbereich nicht verfügbar. Companion-Kalibrierung prüfen."
            : "Drops werden automatisch erkannt und gezählt.";
        SetStatus(UiStatusKind.Active, "Tracking aktiv", message);
    }

    private void UpdateSessionDuration()
    {
        var elapsedText = GrindSessionClock.FormatElapsed(_sessionClock.Elapsed);
        if (_sessionDurationValue.Text != elapsedText)
            _sessionDurationValue.Text = elapsedText;
        _sessionStateLabel.Text = _sessionClock.IsRunning ? "LIVE" : _hasSession ? "PAUSE" : "BEREIT";
        _sessionStateLabel.ForeColor = _sessionClock.IsRunning ? BdoTheme.Positive : BdoTheme.TextMuted;
    }

    private CharacterClass? SelectedCharacterClass =>
        _classOverrideComboBox.SelectedItem as CharacterClass ?? _classDetection.Class;

    private async void GarmothButton_Click(object? sender, EventArgs e) => await UploadToGarmothAsync();

    private async Task UploadToGarmothAsync()
    {
        if (IsBusy || _sessionSubmitted || _sessionSummary.ItemTypeCount == 0 || _shutdownStarted)
            return;
        if (string.IsNullOrEmpty(_garmothApiKey))
        {
            if (!_optionsExpanded) ToggleOptions();
            SetStatus(UiStatusKind.Error, "Garmoth", "API-Key einmal unter Optionen → Garmoth-Key hinterlegen.");
            return;
        }
        RefreshPendingUi();
        _operationInProgress = true;
        _garmothUploadInProgress = true;
        UpdateControlState();
        try
        {
            if (_uiRunning)
                await StopTrackingAsync();
            RefreshPendingUi();
            await RefreshPricesAsync();
            var character = _sessionClass ?? SelectedCharacterClass;
            if (character is null)
            {
                await RefreshClassDetectionAsync();
                character = _sessionClass ?? SelectedCharacterClass;
            }
            if (character is null)
                throw new ArgumentException("Klasse noch unbekannt. Unter Optionen einmal wählen.");
            UpdateSilverValuation();
            var valuation = _silverValuation!;
            if (!valuation.IsComplete && !valuation.HasKnownValue)
                throw new ArgumentException("Noch kein Silberpreis verfügbar. Nach dem nächsten Preisabruf erneut hochladen.");
            if (valuation.AfterTax < 0 || valuation.AfterTax > long.MaxValue || valuation.OverflowItems.Count > 0)
                throw new ArgumentException("Der berechnete Silberwert ist nicht für Garmoth darstellbar.");
            var draft = new GarmothSessionDraft(_sessionId, _sessionSpotId ?? "", character.Name,
                character.Specialization switch
                {
                    CharacterSpecialization.Succession => GarmothSpecialization.Succession,
                    CharacterSpecialization.Awakening => GarmothSpecialization.Awakening,
                    _ => GarmothSpecialization.Unique
                }, _sessionClock.Elapsed, _sessionSummary.Totals,
                (long)decimal.Truncate(valuation.AfterTax), _sessionStartedAt ?? DateTimeOffset.UtcNow);
            var payload = GarmothSessionPayload.Create(draft);
            SetStatus(UiStatusKind.Paused, "Garmoth", "Sitzung wird übertragen …");
            var result = await _garmothClient.UploadAsync(draft, _garmothApiKey);
            var notes = new List<string>();
            if (payload.OmittedItems.Count > 0)
                notes.Add("Ohne Garmoth-Zuordnung ausgelassen: " + string.Join(", ", payload.OmittedItems) + ".");
            if (!valuation.IsComplete)
                notes.Add("Silber ist eine Teilsumme; fehlende Preise wurden nicht geschätzt.");
            if (valuation.IsStale)
                notes.Add("Silber verwendet den letzten gespeicherten Preisstand.");
            if (notes.Count > 0)
                result = result with { Message = result.Message + " " + string.Join(" ", notes) };
            ApplyGarmothResult(result);
        }
        catch (ArgumentException exception)
        {
            SetStatus(UiStatusKind.Error, "Garmoth nicht gesendet", exception.Message);
        }
        finally
        {
            _garmothUploadInProgress = false;
            _operationInProgress = false;
            UpdateControlState();
        }
    }

    private void GarmothOptionsButton_Click(object? sender, EventArgs e)
    {
        if (IsBusy || _shutdownStarted) return;
        using var dialog = new GarmothOptionsDialog(_garmothApiKey);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SaveGarmothApiKey(dialog.ApiKey);
    }

    private void SaveGarmothApiKey(string apiKey)
    {
        apiKey = apiKey.Trim();
        try
        {
            _garmothKeyStore.Save(apiKey);
            _garmothApiKey = apiKey;
            SetStatus(UiStatusKind.Ready, "Garmoth", string.IsNullOrEmpty(apiKey)
                ? "API-Key entfernt." : "API-Key Windows-verschlüsselt gespeichert. Upload ist jetzt mit einem Klick möglich.");
            UpdateControlState();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            CryptographicException or ArgumentException)
        {
            SetStatus(UiStatusKind.Error, "Garmoth-Key", "Key konnte nicht sicher gespeichert werden. Einstellungen prüfen.");
        }
    }

    private void ApplyGarmothResult(GarmothUploadResult result)
    {
        _sessionSubmitted |= result.BlocksAnotherUpload;
        _garmothButton.Text = result.Status == GarmothUploadStatus.Succeeded
            ? "Übertragen"
            : result.BlocksAnotherUpload ? "Upload prüfen" : "Garmoth-Upload";
        SetStatus(result.Status == GarmothUploadStatus.Rejected ? UiStatusKind.Error : UiStatusKind.Paused,
            "Garmoth", result.Message + (_sessionSubmitted ? " Zum Weitergrinden eine neue Sitzung starten." : ""));
        UpdateControlState();
    }

    private async Task RefreshClassDetectionAsync()
    {
        if (_classDetectionInProgress || _shutdownStarted)
            return;
        _classDetectionInProgress = true;
        try
        {
            var detected = await Task.Run(_detectCharacterClass);
            if (_shutdownStarted || IsDisposed)
                return;
            _classDetection = detected;
            if (!_hasSession || _sessionClass is null)
                _sessionClass = SelectedCharacterClass;
            UpdateCharacterClassLabel();
        }
        finally
        {
            _classDetectionInProgress = false;
        }
    }

    private void UpdateCharacterClassLabel()
    {
        var character = _hasSession ? _sessionClass : SelectedCharacterClass;
        _characterClassLabel.Text = character is not null
            ? $"Klasse: {character.DisplayName}"
            : _classDetection.Status == CharacterClassDetectionStatus.Ambiguous
                ? "Klasse: mehrdeutig · unter Optionen wählen"
                : "Klasse: unbekannt · unter Optionen wählen";
    }

    private void UpdateSessionSummary()
    {
        UpdateSessionDuration();
        UpdateSilverValuation();
    }

    private void UpdateSilverValuation()
    {
        var tax = _settings.GetSilverTaxOptions();
        var valuation = SilverValuation.Calculate(_sessionSummary.Totals, _prices, tax);
        _silverValuation = valuation;
        var prefix = valuation.IsComplete ? "" : "≥ ";
        _silverBeforeTaxValue.Text = !valuation.IsComplete && !valuation.HasKnownValue ? "—"
            : prefix + decimal.Truncate(valuation.BeforeTax).ToString("N0", SilverCulture);
        _silverAfterTaxValue.Text = !valuation.IsComplete && !valuation.HasKnownValue ? "—"
            : prefix + decimal.Truncate(valuation.AfterTax).ToString("N0", SilverCulture);
        var region = _prices.Region.ToUpperInvariant();
        _priceStatusLabel.Text = !valuation.IsComplete ? $"{region} · Preise fehlen"
            : valuation.IsStale ? $"{region} · letzter Preisstand"
            : _prices.RetrievedAt is { } time ? $"{region} · Preise abgerufen {time.ToLocalTime():HH:mm}"
            : $"{region} · NPC-/Festwerte";
        _priceStatusLabel.ForeColor = !valuation.IsComplete || valuation.IsStale
            ? BdoTheme.Gold : BdoTheme.TextMuted;
        var details = $"Marktplatz-Auszahlung: {tax.MarketReturnRate.ToString("P3", SilverCulture)}. " +
            "NPC-/Companion-Festwerte bleiben steuerfrei.\n" + _prices.StatusMessage;
        if (valuation.MissingItems.Count > 0)
            details += "\nTeilsumme (≥): Preise fehlen für " + string.Join(", ", valuation.MissingItems) + ".";
        if (valuation.OverflowItems.Count > 0)
            details += "\nTeilsumme (≥): Nicht darstellbarer Wert für " + string.Join(", ", valuation.OverflowItems) + ".";
        if (valuation.IsStale)
            details += "\nEnthält zwischengespeicherte, nicht aktuelle Marktpreise.";
        _priceStatusLabel.AccessibleDescription = details;
        _silverBeforeTaxValue.AccessibleDescription = details;
        _silverAfterTaxValue.AccessibleDescription = details;
        _priceDetails.SetToolTip(_priceStatusLabel, details);
        _priceDetails.SetToolTip(_silverBeforeTaxValue, details);
        _priceDetails.SetToolTip(_silverAfterTaxValue, details);
    }

    private async void SilverOptionsButton_Click(object? sender, EventArgs e)
    {
        if (_shutdownStarted || _garmothUploadInProgress)
            return;
        using var dialog = new SilverOptionsDialog(_settings.MarketRegion, _settings.GetSilverTaxOptions());
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        ApplySilverPreferences(dialog.SelectedRegion, dialog.SelectedTaxOptions);
        _priceRefreshEnabled = true;
        await RefreshPricesAsync();
    }

    private void ApplySilverPreferences(string region, SilverTaxOptions tax)
    {
        _settings.UpdateSilverPreferences(region, tax);
        SaveSettings();
        // Never apply a response from another region to this session's valuation.
        _prices = _priceProvider.GetCachedSnapshot(_settings.MarketRegion);
        _nextPriceRefreshAt = DateTimeOffset.MinValue;
        UpdateSilverValuation();
    }

    private Task RefreshPricesAsync()
    {
        if (_shutdownStarted || _uiResourcesDisposed)
            return Task.CompletedTask;
        if (_priceRefreshTask is { IsCompleted: false } pending)
            return _priceRefreshRegion == _settings.MarketRegion ? pending : RefreshChangedRegionAsync(pending);
        return _priceRefreshTask = RefreshPricesCoreAsync();
    }

    private async Task RefreshChangedRegionAsync(Task previousRegion)
    {
        await previousRegion;
        await RefreshPricesAsync();
    }

    private async Task RefreshPricesCoreAsync()
    {
        if (_priceRefreshInProgress || _shutdownStarted || _uiResourcesDisposed)
            return;
        _priceRefreshInProgress = true;
        var region = _settings.MarketRegion;
        _priceRefreshRegion = region;
        try
        {
            // Pricing has no connection to capture, matching, or the event ledger.
            var snapshot = await _priceProvider.GetSnapshotAsync(region, _priceLifetime.Token);
            if (_shutdownStarted || _uiResourcesDisposed || IsDisposed || region != _settings.MarketRegion)
                return;
            _prices = snapshot;
            UpdateSilverValuation();
        }
        catch (OperationCanceledException) when (_priceLifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            InvalidDataException or OperationCanceledException)
        {
            if (!_shutdownStarted && !_uiResourcesDisposed && !IsDisposed && region == _settings.MarketRegion)
            {
                _prices = _priceProvider.GetCachedSnapshot(region);
                UpdateSilverValuation();
                _priceStatusLabel.Text = $"{region.ToUpperInvariant()} · Preise offline";
                _priceStatusLabel.ForeColor = BdoTheme.Gold;
            }
        }
        finally
        {
            _priceRefreshInProgress = false;
            _nextPriceRefreshAt = region == _settings.MarketRegion
                ? DateTimeOffset.UtcNow.AddMinutes(1) : DateTimeOffset.MinValue;
        }
    }

    private void CaptureSession_Stopped(object? sender, CaptureSessionStoppedEventArgs e)
    {
        if (e.Error is not null)
        {
            Interlocked.Exchange(ref _lastCaptureStopError, e.Error);
        }

        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke((Action)(() =>
            {
                if (_shutdownStarted)
                {
                    return;
                }

                if (e.Error is not null)
                {
                    _sessionClock.Pause();
                    _inactivityTimer.Pause();
                    UpdateSessionDuration();
                    CompleteCaptureSegment(DateTimeOffset.UtcNow);
                    _uiRunning = false;
                    if (!_garmothUploadInProgress)
                        _operationInProgress = false;
                    UpdateControlState();
                    SetStatus(UiStatusKind.Error, "Tracking gestoppt", e.Error.Message);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The form is already closing.
        }
    }

    private void CompleteCaptureSegment(DateTimeOffset completedAt)
    {
        if (_captureSegmentCompleted)
        {
            return;
        }

        var completed = _analyzer.CompleteSession(completedAt);
        _captureSegmentCompleted = true;
        _recording?.RecordCompletion(completedAt, completed.TrackingResult);
        _uiMailbox.Publish(completed);
        RefreshPendingUi();
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        if (_garmothUploadInProgress)
        {
            e.Cancel = true;
            SetStatus(UiStatusKind.Paused, "Garmoth", "Bitte das Upload-Ergebnis vor dem Schließen abwarten.");
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _priceLifetime.Cancel();
        _sessionClock.Pause();
        _inactivityTimer.Pause();
        _uiRefreshTimer.Stop();
        Enabled = false;
        try
        {
            SaveSettings();
            await _captureSession.DisposeAsync();
            CompleteCaptureSegment(DateTimeOffset.UtcNow);
            _analyzer.Dispose();
            _recording?.Dispose();
        }
        finally
        {
            _shutdownCompleted = true;
            Close();
        }
    }

    private void SaveSettings()
    {
        _settings.UpdateCapturePreferences(GetSelectedMonitor()?.DeviceName);
        _settings.AutoPauseMinutes = (int)_autoPauseMinutes.Value;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (IOException exception)
        {
            SetStatus(UiStatusKind.Error, "Einstellungen nicht gespeichert", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            SetStatus(UiStatusKind.Error, "Einstellungen nicht gespeichert", exception.Message);
        }
    }

    private MonitorOption? GetSelectedMonitor() => _monitorComboBox.SelectedItem as MonitorOption;

    private void UpdateControlState()
    {
        _optionsHint.Text = _hasSession
            ? "Monitor und Lootfilter sind gesperrt. Auto-Pause kann jederzeit angepasst werden."
            : "Optionen vor dem Start festlegen. Der Spot wird automatisch erkannt.";
        var hasMonitor = GetSelectedMonitor() is not null;
        _monitorComboBox.Enabled = !_uiRunning && !IsBusy && !_hasSession;
        _eventLootCheckBox.Enabled = !_uiRunning && !IsBusy && !_hasSession;
        _recordingCheckBox.Enabled = !_uiRunning && !IsBusy && !_hasSession;
        _classOverrideComboBox.Enabled = !_uiRunning && !IsBusy && !_sessionSubmitted;
        _garmothButton.Enabled = !IsBusy && !_sessionSubmitted &&
            _sessionSummary.ItemTypeCount > 0;
        _garmothOptionsButton.Enabled = !IsBusy;
        _garmothOptionsButton.Text = string.IsNullOrEmpty(_garmothApiKey) ? "Garmoth-Key" : "Garmoth-Key ✓";
        _silverOptionsButton.Enabled = !_garmothUploadInProgress;
        _trackingButton.Text = _uiRunning
            ? "Pausieren"
            : _hasSession ? "Fortsetzen" : "Tracking starten";
        _trackingButton.ButtonStyle = _uiRunning
            ? BdoButtonStyle.Secondary
            : BdoButtonStyle.Primary;
        _trackingButton.Enabled = !IsBusy && !_sessionSubmitted &&
            (_uiRunning || (hasMonitor && _analyzer.IsAvailable));
        _resetButton.Enabled = !_uiRunning &&
            !IsBusy &&
            _hasSession;
    }

    private void SetStatus(UiStatusKind kind, string badge, string message)
    {
        var color = kind switch
        {
            UiStatusKind.Active => BdoTheme.Positive,
            UiStatusKind.Paused => BdoTheme.Gold,
            UiStatusKind.Error => BdoTheme.Error,
            _ => BdoTheme.TextMuted
        };

        _statusDot.ForeColor = color;
        _statusLabel.Text = $"{badge} · {message}";
        _statusLabel.ForeColor = kind == UiStatusKind.Error ? BdoTheme.Error : BdoTheme.TextMuted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_uiResourcesDisposed)
        {
            _uiResourcesDisposed = true;
            _priceLifetime.Cancel();
            _priceProvider.Dispose();
            _priceLifetime.Dispose();
            _priceDetails.Dispose();
            _brandLogo.Image = null;
            _brandLogoImage.Dispose();
            Icon = null;
            _brandIcon.Dispose();
            _uiRefreshTimer.Dispose();
            _uiMailbox.Dispose();
            _recording?.Dispose();
            _garmothClient.Dispose();
            _garmothApiKey = string.Empty;
            _baseFont.Dispose();
            _titleFont.Dispose();
            _metricFont.Dispose();
            _sectionFont.Dispose();
            _captionFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private enum UiStatusKind
    {
        Ready,
        Active,
        Paused,
        Error
    }

    private sealed record MonitorOption(
        int DisplayIndex,
        string DeviceName,
        Rectangle Bounds,
        bool IsPrimary)
    {
        public override string ToString()
        {
            var primary = IsPrimary ? " · Hauptmonitor" : string.Empty;
            return $"Monitor {DisplayIndex}{primary}";
        }
    }
}
