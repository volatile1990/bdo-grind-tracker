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
    private readonly LootHistoryStore _historyStore;
    private readonly List<LootHistoryEntry> _historyEntries;
    private readonly PassiveCaptureSession _captureSession;
    private readonly PictureBox _brandLogo = new();
    private readonly Bitmap _brandLogoImage = AppBranding.CreateLogo();
    private readonly Icon _brandIcon = AppBranding.CreateWindowIcon();

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
    private readonly PictureBox _characterClassIcon = new();
    private readonly PictureBox _sessionSpotIcon = new();
    private readonly Label _sessionSpotNameLabel = new();
    private TableLayoutPanel _sessionMetricLayout = null!;
    private readonly Func<CharacterClassDetection> _detectCharacterClass;
    private CharacterClassDetection _classDetection = CharacterClassDetection.Unknown;
    private CharacterClass? _sessionClass;
    private bool _classDetectionInProgress;
    private readonly GarmothUploadClient _garmothClient;
    private readonly GarmothApiKeyStore _garmothKeyStore;
    private readonly GarmothUploadIntervals _garmothIntervals = new();
    private readonly BdoButton _garmothButton = new();
    private readonly BdoButton _garmothOptionsButton = new();
    private string _garmothApiKey = string.Empty;
    private bool _garmothUploadInProgress;
    private bool _uploadOptionsOpen;
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
    private readonly BdoButton _demoButton = new();
    private readonly LootTotalsView _totalsView = new();
    private readonly ImageAssetRepository _liveSpotBackgrounds = new(
        Path.Combine(AppContext.BaseDirectory, "data", "spot-backgrounds"));
    private readonly ImageAssetRepository _liveSpotIcons = new(
        Path.Combine(AppContext.BaseDirectory, "data", "spot-icons"));
    private readonly ImageAssetRepository _liveClassIcons = new(
        Path.Combine(AppContext.BaseDirectory, "data", "class-icons"));
    private readonly MetricValueLabel _silverBeforeTaxValue = new();
    private readonly MetricValueLabel _silverAfterTaxValue = new();
    private readonly Label _priceStatusLabel = new();
    private readonly BdoButton _silverOptionsButton = new();
    private readonly ToolTip _priceDetails = new() { AutoPopDelay = 15000 };
    private readonly ILootPriceProvider _priceProvider;
    private readonly CancellationTokenSource _priceLifetime = new();
    private LootPriceSnapshot _prices;
    private bool _priceRefreshEnabled;
    private bool _priceRefreshInProgress;
    private Task? _priceRefreshTask;
    private string? _priceRefreshRegion;
    private DateTimeOffset _nextPriceRefreshAt = DateTimeOffset.MinValue;
    private static readonly CultureInfo SilverCulture = CultureInfo.GetCultureInfo("de-DE");
    private readonly Label _statusDot = new();
    private readonly Label _statusLabel = new();
    private readonly Panel _mainContentHost = new();
    private readonly FlowLayoutPanel _liveIdentity = new();
    private readonly BdoButton _liveTabButton = new();
    private readonly BdoButton _historyTabButton = new();
    private Control _liveTrackerView = null!;
    private readonly LootHistoryView _historyView;

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
    private bool _demoMode;
    private TimeSpan _demoDuration;
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
        GarmothApiKeyStore? garmothKeyStore = null,
        LootHistoryStore? historyStore = null)
    {
        ArgumentNullException.ThrowIfNull(screenCapture);
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _settings = settingsStore.Load();
        _historyStore = historyStore ?? new LootHistoryStore(
            Path.Combine(_settingsStore.BaseDirectory, "loot-history-v1.json"));
        _historyEntries = _historyStore.Load().ToList();
        _historyView = new LootHistoryView();
        _historyView.SetEntries(_historyEntries);
        _historyView.DeleteRequested += sessionId => DeleteHistorySession(sessionId, requireConfirmation: true);
        _historyView.EditRequested += EditHistorySession;
        _historyView.UploadRequested += UploadHistorySession;
        _captureSession = new PassiveCaptureSession(screenCapture);
        _sessionClock = sessionClock ?? new GrindSessionClock();
        _inactivityTimer = inactivityTimer ?? new GrindInactivityTimer();
        _detectCharacterClass = classDetector ?? new CompanionCharacterClassDetector().DetectDefault;
        _priceProvider = priceProvider ?? new ArshaLootPriceProvider();
        _prices = _priceProvider.GetCachedSnapshot(_settings.MarketRegion);
        _historyView.SetPricing(_prices, _settings.GetSilverTaxOptions());
        _totalsView.SetPricing(_prices, _settings.GetSilverTaxOptions());
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
            RowCount = 4,
            Padding = new Padding(24, 18, 24, 12),
            BackColor = BdoTheme.Background,
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildNavigationStrip(), 0, 1);
        root.Controls.Add(BuildMainContent(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildMainContent()
    {
        _mainContentHost.Dock = DockStyle.Fill;
        _mainContentHost.Margin = Padding.Empty;
        _mainContentHost.BackColor = BdoTheme.Background;
        _mainContentHost.AccessibleName = "Hauptbereiche";

        _liveTrackerView = BuildLiveTrackerView();
        _liveTrackerView.Dock = DockStyle.Fill;
        _historyView.Dock = DockStyle.Fill;
        _historyView.Visible = false;
        _mainContentHost.Controls.Add(_historyView);
        _mainContentHost.Controls.Add(_liveTrackerView);
        ShowMainArea(showHistory: false);
        return _mainContentHost;
    }

    private Control BuildLiveTrackerView()
    {
        var live = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            BackColor = BdoTheme.Background
        };
        live.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        live.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        live.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        live.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        live.Controls.Add(BuildTrackingOptions(), 0, 0);
        live.Controls.Add(BuildSummary(), 0, 1);
        live.Controls.Add(BuildLootArea(), 0, 2);
        return live;
    }

    private void ShowMainArea(bool showHistory)
    {
        _liveTrackerView.Visible = !showHistory;
        _historyView.Visible = showHistory;
        _liveIdentity.Visible = !showHistory;
        _optionsButton.Visible = true;
        StyleMainAreaButton(_liveTabButton, selected: !showHistory);
        StyleMainAreaButton(_historyTabButton, selected: showHistory);

        if (showHistory)
        {
            _historyView.SetEntries(_historyEntries);
            _historyView.BringToFront();
        }
        else
        {
            _liveTrackerView.BringToFront();
        }
    }

    private void StyleMainAreaButton(BdoButton button, bool selected)
    {
        button.Selected = selected;
        button.AccessibleDescription = selected ? "Ausgewählt" : "Nicht ausgewählt";
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
        _demoButton.Text = "Demostunde";
        _demoButton.ButtonStyle = BdoButtonStyle.Secondary;
        _demoButton.Width = 116;
        _demoButton.Margin = new Padding(0, 0, 10, 0);
        _demoButton.AccessibleName = "Eine nicht gespeicherte Demostunde im Live-Tracker anzeigen";
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
        _garmothButton.AccessibleDescription = "Pausiert bei Bedarf und überträgt den noch nicht gesendeten Grind sofort.";
        actions.Controls.Add(_resetButton);
        actions.Controls.Add(_demoButton);
        actions.Controls.Add(_garmothButton);
        actions.Controls.Add(_trackingButton);
        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(actions, 1, 0);
        return header;
    }

    private Control BuildNavigationStrip()
    {
        var strip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10)
        };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _liveIdentity.AutoSize = true;
        _liveIdentity.WrapContents = false;
        _liveIdentity.Dock = DockStyle.Fill;
        _liveIdentity.Margin = Padding.Empty;
        _liveIdentity.Padding = new Padding(0, 5, 0, 0);
        _liveIdentity.Controls.Add(new Label
        {
            Text = "Klasse:",
            AutoSize = true,
            Font = _sectionFont,
            ForeColor = BdoTheme.TextMuted,
            Margin = new Padding(2, 4, 7, 0)
        });
        _characterClassIcon.Size = new Size(28, 28);
        _characterClassIcon.SizeMode = PictureBoxSizeMode.Zoom;
        _characterClassIcon.Margin = new Padding(0, 0, 7, 0);
        _characterClassIcon.Visible = false;
        _characterClassIcon.TabStop = false;
        _characterClassIcon.AccessibleName = "Symbol der erkannten Klasse";
        _liveIdentity.Controls.Add(_characterClassIcon);
        _characterClassLabel.AccessibleName = "Automatisch erkannte Klasse";
        _characterClassLabel.AutoSize = true;
        _characterClassLabel.ForeColor = BdoTheme.Gold;
        _characterClassLabel.Font = _sectionFont;
        _characterClassLabel.Text = "wird beim Start erkannt";
        _characterClassLabel.Margin = new Padding(0, 4, 0, 0);
        _liveIdentity.Controls.Add(_characterClassLabel);

        var tabs = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(12, 0, 12, 0)
        };
        ConfigureMainAreaButton(_liveTabButton, "Live Tracker", "Live-Tracker anzeigen");
        ConfigureMainAreaButton(_historyTabButton, "Loot Verlauf", "Loot-Verlauf anzeigen");
        _liveTabButton.Click += (_, _) => ShowMainArea(showHistory: false);
        _historyTabButton.Click += (_, _) => ShowMainArea(showHistory: true);
        tabs.Controls.Add(_liveTabButton);
        tabs.Controls.Add(_historyTabButton);

        _optionsButton.Text = "Optionen";
        _optionsButton.ButtonStyle = BdoButtonStyle.Secondary;
        _optionsButton.Width = 110;
        _optionsButton.Height = 34;
        _optionsButton.Margin = Padding.Empty;
        _optionsButton.AccessibleName = "Tracking-Optionen öffnen oder schließen";
        strip.Controls.Add(_liveIdentity, 0, 0);
        strip.Controls.Add(tabs, 1, 0);
        strip.Controls.Add(_optionsButton, 2, 0);
        return strip;
    }

    private void ConfigureMainAreaButton(BdoButton button, string text, string accessibleName)
    {
        button.Text = text;
        button.AccessibleName = accessibleName;
        button.Width = 116;
        button.Height = 34;
        button.Margin = Padding.Empty;
        button.Padding = Padding.Empty;
        button.CornerRadius = 9;
        button.ButtonStyle = BdoButtonStyle.Navigation;
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

    private void OptionsButton_Click(object? sender, EventArgs e)
    {
        if (_historyView.Visible)
            ShowMainArea(showHistory: false);
        ToggleOptions();
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
            Margin = new Padding(0, 0, 0, 8),
            Padding = Padding.Empty
        };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
        summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _sessionDurationValue.AccessibleName = "Dauer der aktuellen Grindsession";
        summary.Controls.Add(CreateSessionMetricCard(new Padding(0, 0, 6, 0)), 0, 0);
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

    private Control CreateSessionMetricCard(Padding margin)
    {
        var card = new BdoSurfacePanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            CornerRadius = 14,
            Padding = new Padding(14, 4, 14, 4),
            Margin = margin
        };
        _sessionMetricLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        _sessionMetricLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _sessionMetricLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, _sectionFont.Height + 2));
        _sessionMetricLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var captionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        captionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        captionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _sessionSpotIcon.Size = new Size(64, 64);
        _sessionSpotIcon.SizeMode = PictureBoxSizeMode.Zoom;
        _sessionSpotIcon.BackColor = Color.Transparent;
        _sessionSpotIcon.Margin = Padding.Empty;
        _sessionSpotIcon.Visible = false;
        _sessionSpotIcon.TabStop = false;
        _sessionSpotIcon.AccessibleName = "Symbol des erkannten Grindspots";
        _sessionSpotNameLabel.Text = "SESSIONDAUER";
        _sessionSpotNameLabel.AutoSize = true;
        _sessionSpotNameLabel.Dock = DockStyle.Fill;
        _sessionSpotNameLabel.TextAlign = ContentAlignment.MiddleCenter;
        _sessionSpotNameLabel.Font = _captionFont;
        _sessionSpotNameLabel.ForeColor = BdoTheme.TextMuted;
        _sessionSpotNameLabel.BackColor = Color.Transparent;
        _sessionSpotNameLabel.Margin = Padding.Empty;
        _sessionSpotNameLabel.AccessibleName = "Erkannter Grindspot in der Sessionkarte";
        captionRow.Controls.Add(_sessionSpotNameLabel, 0, 0);
        _sessionStateLabel.AutoSize = true;
        _sessionStateLabel.Font = _captionFont;
        _sessionStateLabel.ForeColor = BdoTheme.TextMuted;
        _sessionStateLabel.Margin = Padding.Empty;
        captionRow.Controls.Add(_sessionStateLabel, 1, 0);
        _sessionMetricLayout.Controls.Add(captionRow, 0, 0);

        _sessionDurationValue.Text = "0";
        _sessionDurationValue.AutoSize = true;
        _sessionDurationValue.Dock = DockStyle.Fill;
        _sessionDurationValue.TextAlign = ContentAlignment.MiddleCenter;
        _sessionDurationValue.Font = _metricFont;
        _sessionDurationValue.ForeColor = BdoTheme.GoldBright;
        _sessionDurationValue.BackColor = Color.Transparent;
        _sessionDurationValue.Margin = Padding.Empty;
        _sessionMetricLayout.Controls.Add(_sessionDurationValue, 0, 1);
        _sessionMetricLayout.Paint += (_, e) =>
        {
            if (_sessionSpotIcon.Image is null)
                return;
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(
                _sessionSpotIcon.Image,
                new Rectangle(0, Math.Max(0, (_sessionMetricLayout.ClientSize.Height - 64) / 2), 64, 64));
        };
        card.Controls.Add(_sessionMetricLayout);
        void ArrangeSessionIdentity()
        {
            if (_sessionSpotIcon.Image is null)
            {
                _sessionSpotNameLabel.Padding = Padding.Empty;
                _sessionDurationValue.Padding = Padding.Empty;
                _sessionSpotNameLabel.TextAlign = ContentAlignment.MiddleCenter;
                _sessionDurationValue.TextAlign = ContentAlignment.MiddleCenter;
                return;
            }

            const int symbolReserve = 66;
            var timeWidth = TextRenderer.MeasureText(
                _sessionDurationValue.Text,
                _sessionDurationValue.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            var availableTextWidth = Math.Max(0,
                _sessionMetricLayout.ClientSize.Width - symbolReserve);
            var groupLeft = symbolReserve + Math.Max(0, (availableTextWidth - timeWidth) / 2);
            var alignedPadding = new Padding(groupLeft, 0, 0, 0);
            _sessionSpotNameLabel.Padding = alignedPadding;
            _sessionDurationValue.Padding = alignedPadding;
            _sessionSpotNameLabel.TextAlign = ContentAlignment.MiddleLeft;
            _sessionDurationValue.TextAlign = ContentAlignment.MiddleLeft;
        }
        card.Layout += (_, _) => ArrangeSessionIdentity();
        _sessionDurationValue.TextChanged += (_, _) => card.PerformLayout();
        return card;
    }

    private Control CreateMetricCard(string caption, MetricValueLabel valueLabel, Padding margin,
        Label? state = null)
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
        _demoButton.Click += (_, _) => ShowDemoHour();
        _garmothButton.Click += GarmothButton_Click;
        _garmothOptionsButton.Click += GarmothOptionsButton_Click;
        _optionsButton.Click += OptionsButton_Click;
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
            await UploadHourlyToGarmothAsync();
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

        if (_demoMode)
            ClearDemoHour();
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
                _sessionSpotId = null;
                UpdateLiveIdentity();

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
            _sessionClock.Start();
            _captureSession.StartCompanion(
                monitor.Bounds,
                ProcessFrameAsync);
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
        if (!automatic)
        {
            _sessionClock.Pause();
            _inactivityTimer.Pause();
        }
        UpdateSessionDuration();
        SetStatus(UiStatusKind.Paused, "Wird pausiert", "Die letzten Drops werden noch übernommen …");
        await _captureSession.StopAsync();
        if (automatic)
        {
            // Drain an in-flight analysis before freezing activity: a last accepted
            // drop must still update the cutoff. Exclude actual idle time (which
            // may exceed the timeout), only within the current active segment.
            _sessionClock.Pause(_inactivityTimer.PauseAndGetIdleDuration());
            UpdateSessionDuration();
        }
        CompleteCaptureSegment(DateTimeOffset.UtcNow);
        _uiRunning = false;
        PersistCurrentSession(DateTimeOffset.UtcNow);

        var failure = Interlocked.CompareExchange(ref _lastCaptureStopError, null, null);
        if (failure is null)
        {
            SetStatus(UiStatusKind.Paused, automatic ? "Automatisch pausiert" : "Pausiert",
                automatic
                    ? $"Seit {_autoPauseMinutes.Value:0} {(_autoPauseMinutes.Value == 1 ? "Minute" : "Minuten")} kein neuer Drop. Die Zeit ohne Drops wurde abgezogen. Mit Fortsetzen geht es weiter."
                    : "Die Session bleibt erhalten.");
        }
        else
        {
            SetStatus(UiStatusKind.Error, "Tracking gestoppt", failure.Message);
        }
    }

    private async Task PauseIfInactiveAsync()
    {
        // Background hourly HTTP must not defer the inactivity cutoff. Manual
        // upload/pause operations already hold _operationInProgress.
        if (!_uiRunning || _operationInProgress || _shutdownStarted ||
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

        PersistCurrentSession(DateTimeOffset.UtcNow);
        _analyzer.Reset();
        _recording?.Dispose();
        _recording = null;
        _hasSession = false;
        _sessionId = Guid.NewGuid();
        _sessionStartedAt = null;
        _sessionSpotId = null;
        _demoMode = false;
        _demoDuration = TimeSpan.Zero;
        _sessionSubmitted = false;
        _garmothIntervals.Reset();
        _garmothButton.Text = "Garmoth-Upload";
        _sessionClass = null;
        _classOverrideComboBox.SelectedIndex = 0;
        UpdateCharacterClassLabel();
        _recordingCheckBox.Checked = false;
        _recordingStatus.Text = "Aufzeichnung aus · für die nächste Session erneut aktivieren.";
        _sessionClock.Reset();
        _inactivityTimer.Reset();
        UpdateSessionDuration();
        _uiMailbox.Reset();
        _sessionSummary = LootSessionSnapshot.Empty;
        _totalsView.SetTotals(_sessionSummary.Totals);
        UpdateLiveIdentity();
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
            analysis.TrackingResult, frame, analysis.PanelRegion, analysis.RareBandRegion, analysis.Recovery);

        // Apply every event before returning; render only the latest aggregate.
        // No UI screenshots, raw-text formatting, or growing decision log.
        _uiMailbox.Publish(analysis, onPublished: ObserveGarmothTotals);
    }

    private void ObserveGarmothTotals(IReadOnlyDictionary<string, long> totals, bool hasNewDrop)
    {
        if (hasNewDrop) _inactivityTimer.RecordDrop();
        var confirmedDuration = _sessionClock.GetElapsedExcludingTrailingIdle(_inactivityTimer.IdleDuration);
        _garmothIntervals.Observe(confirmedDuration, totals, DateTimeOffset.UtcNow);
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
        UpdateLiveIdentity();

        if (update.Totals is { } totals)
        {
            _sessionSummary = totals;
            _totalsView.SetTotals(totals.Totals);
            UpdateSessionSummary();
            UpdateControlState();
        }

        if (!_uiRunning || _shutdownStarted || _garmothUploadInProgress ||
            _garmothIntervals.IsBlocked || _garmothIntervals.AutomaticSuspended)
            return;

        var message = update.Analysis.PanelRegion is null
            ? "Lootbereich nicht verfügbar. Companion-Kalibrierung prüfen."
            : "Drops werden automatisch erkannt und gezählt.";
        SetStatus(UiStatusKind.Active, "Tracking aktiv", message);
    }

    private void UpdateSessionDuration()
    {
        var elapsedText = GrindSessionClock.FormatElapsed(_demoMode ? _demoDuration : _sessionClock.Elapsed);
        if (_sessionDurationValue.Text != elapsedText)
            _sessionDurationValue.Text = elapsedText;
        _sessionStateLabel.Text = _demoMode ? "DEMO" : _sessionClock.IsRunning ? "LIVE" : _hasSession ? "PAUSE" : "BEREIT";
        _sessionStateLabel.ForeColor = _sessionClock.IsRunning ? BdoTheme.Positive : BdoTheme.TextMuted;
    }

    private CharacterClass? SelectedCharacterClass =>
        _classOverrideComboBox.SelectedItem as CharacterClass ?? _classDetection.Class;

    private async void GarmothButton_Click(object? sender, EventArgs e) => await UploadToGarmothAsync();

    private async Task UploadToGarmothAsync()
    {
        if (IsBusy || _sessionSubmitted || _garmothIntervals.IsBlocked || _shutdownStarted)
            return;
        RefreshPendingUi();
        if (_sessionSummary.ItemTypeCount == 0) return;
        if (string.IsNullOrEmpty(_garmothApiKey))
        {
            if (!_optionsExpanded) ToggleOptions();
            SetStatus(UiStatusKind.Error, "Garmoth", "API-Key einmal unter Optionen → Garmoth-Key hinterlegen.");
            return;
        }
        _operationInProgress = true;
        _garmothUploadInProgress = true;
        UpdateControlState();
        GarmothUploadInterval? interval = null;
        try
        {
            if (_uiRunning)
                await StopTrackingAsync();
            RefreshPendingUi();
            interval = _garmothIntervals.PrepareManual(_sessionClock.Elapsed, _sessionSummary.Totals,
                _sessionStartedAt ?? DateTimeOffset.UtcNow);
            if (interval is null)
                throw new ArgumentException("Kein neuer Loot mit mindestens einer vollen Minute seit dem letzten Upload vorhanden.");
            SetStatus(UiStatusKind.Paused, "Garmoth", "Noch nicht übertragener Grind wird gesendet …");
            var result = await SendGarmothIntervalAsync(interval);
            _garmothIntervals.Complete(interval, result);
            ApplyGarmothResult(result);
        }
        catch (ArgumentException exception)
        {
            if (interval is not null)
                _garmothIntervals.Complete(interval, new(GarmothUploadStatus.Rejected, exception.Message));
            SetStatus(UiStatusKind.Error, "Garmoth nicht gesendet", exception.Message);
        }
        finally
        {
            _garmothUploadInProgress = false;
            _operationInProgress = false;
            UpdateControlState();
        }
    }

    private async Task UploadHourlyToGarmothAsync()
    {
        if (!_settings.GarmothAutoUploadEnabled || !_hasSession || IsBusy || _sessionSubmitted ||
            _shutdownStarted || _uploadOptionsOpen || _garmothIntervals.IsBlocked || _garmothIntervals.AutomaticSuspended)
            return;

        var interval = _garmothIntervals.PrepareAutomatic();
        if (interval is null) return;
        _garmothUploadInProgress = true;
        UpdateControlState();
        try
        {
            if (string.IsNullOrEmpty(_garmothApiKey))
                throw new ArgumentException("API-Key unter Optionen → Garmoth-Key hinterlegen.");
            SetStatus(_uiRunning ? UiStatusKind.Active : UiStatusKind.Paused, "Garmoth automatisch",
                "Abgeschlossene Grindstunde wird übertragen …");
            var result = await SendGarmothIntervalAsync(interval);
            _garmothIntervals.Complete(interval, result);
            if (result.BlocksAnotherUpload)
                MarkHistoryUploadBlocked(_sessionId, result.Status == GarmothUploadStatus.Succeeded);
            _garmothButton.Text = _garmothIntervals.IsBlocked ? "Upload prüfen" : "Garmoth-Upload";
            var guidance = result.Status == GarmothUploadStatus.Succeeded
                ? " Nur dieser Stundenabschnitt wurde übertragen."
                : _garmothIntervals.IsBlocked
                    ? " Weitere Uploads dieser Sitzung sind gesperrt. Tracking läuft weiter; bitte in Garmoth prüfen."
                    : " Auto-Upload angehalten. Nach der Korrektur Garmoth-Optionen speichern oder manuell hochladen.";
            SetStatus(result.Status == GarmothUploadStatus.Succeeded
                    ? (_uiRunning ? UiStatusKind.Active : UiStatusKind.Paused) : UiStatusKind.Error,
                "Garmoth automatisch", result.Message + guidance);
        }
        catch (ArgumentException exception)
        {
            _garmothIntervals.Complete(interval, new(GarmothUploadStatus.Rejected, exception.Message));
            SetStatus(UiStatusKind.Error, "Garmoth automatisch",
                exception.Message + " Auto-Upload angehalten. Angaben korrigieren und Garmoth-Optionen speichern oder manuell hochladen.");
        }
        finally
        {
            _garmothUploadInProgress = false;
            UpdateControlState();
        }
    }

    private async Task<GarmothUploadResult> SendGarmothIntervalAsync(GarmothUploadInterval interval)
    {
        await RefreshPricesAsync();
        var character = _sessionClass ?? SelectedCharacterClass;
        if (character is null)
        {
            await RefreshClassDetectionAsync();
            character = _sessionClass ?? SelectedCharacterClass;
        }
        if (character is null)
            throw new ArgumentException("Klasse noch unbekannt. Unter Optionen einmal wählen.");

        // Value only this frozen quantity delta. Subtracting two lifetime silver
        // values would charge old loot again whenever prices or tax options change.
        var valuation = SilverValuation.Calculate(interval.Totals, _prices, _settings.GetSilverTaxOptions());
        if (!valuation.IsComplete && !valuation.HasKnownValue)
            throw new ArgumentException("Noch kein Silberpreis verfügbar. Nach dem nächsten Preisabruf erneut hochladen.");
        if (valuation.AfterTax < 0 || valuation.AfterTax > long.MaxValue || valuation.OverflowItems.Count > 0)
            throw new ArgumentException("Der berechnete Silberwert ist nicht für Garmoth darstellbar.");
        var draft = new GarmothSessionDraft(interval.Id, _sessionSpotId ?? "", character.Name,
            character.Specialization switch
            {
                CharacterSpecialization.Succession => GarmothSpecialization.Succession,
                CharacterSpecialization.Awakening => GarmothSpecialization.Awakening,
                _ => GarmothSpecialization.Unique
            }, interval.ActiveDuration, interval.Totals,
            (long)decimal.Truncate(valuation.AfterTax), interval.StartedAt)
        {
            SourceSessionId = _sessionId
        };
        var payload = GarmothSessionPayload.Create(draft);
        var result = await _garmothClient.UploadAsync(draft, _garmothApiKey);
        var notes = new List<string>();
        if (payload.OmittedItems.Count > 0)
            notes.Add("Ohne Garmoth-Zuordnung ausgelassen: " + string.Join(", ", payload.OmittedItems) + ".");
        if (!valuation.IsComplete)
            notes.Add("Silber ist eine Teilsumme; fehlende Preise wurden nicht geschätzt.");
        if (valuation.IsStale)
            notes.Add("Silber verwendet den letzten gespeicherten Preisstand.");
        return notes.Count == 0 ? result
            : result with { Message = result.Message + " " + string.Join(" ", notes) };
    }

    private void GarmothOptionsButton_Click(object? sender, EventArgs e)
    {
        if (IsBusy || _shutdownStarted) return;
        using var dialog = new GarmothOptionsDialog(_garmothApiKey, _settings.GarmothAutoUploadEnabled);
        _uploadOptionsOpen = true;
        try
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
                SaveGarmothPreferences(dialog.ApiKey, dialog.AutoUploadEnabled);
        }
        finally { _uploadOptionsOpen = false; }
    }

    private void SaveGarmothApiKey(string apiKey) =>
        SaveGarmothPreferences(apiKey, _settings.GarmothAutoUploadEnabled);

    private void SaveGarmothPreferences(string apiKey, bool autoUploadEnabled)
    {
        apiKey = apiKey.Trim();
        try
        {
            _garmothKeyStore.Save(apiKey);
            _garmothApiKey = apiKey;
            _settings.GarmothAutoUploadEnabled = autoUploadEnabled && apiKey.Length > 0;
            _settingsStore.Save(_settings);
            _garmothIntervals.ResumeAutomatic();
            SetStatus(UiStatusKind.Ready, "Garmoth", string.IsNullOrEmpty(apiKey)
                ? "API-Key entfernt. Automatischer Upload ist aus."
                : _garmothIntervals.IsBlocked
                    ? "Optionen gespeichert. Unklares Upload-Ergebnis zuerst in Garmoth prüfen; diese Sitzung bleibt für Uploads gesperrt."
                    : _settings.GarmothAutoUploadEnabled
                        ? "Automatischer Upload aktiv: jede volle Grindstunde wird einmal übertragen. Pausen zählen nicht mit."
                        : "API-Key Windows-verschlüsselt gespeichert. Automatischer Upload ist aus.");
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
        if (result.BlocksAnotherUpload)
            MarkHistoryUploadBlocked(_sessionId, result.Status == GarmothUploadStatus.Succeeded);
        _garmothButton.Text = result.Status == GarmothUploadStatus.Succeeded
            ? "Übertragen"
            : result.BlocksAnotherUpload ? "Upload prüfen" : "Garmoth-Upload";
        SetStatus(result.Status == GarmothUploadStatus.Rejected ? UiStatusKind.Error : UiStatusKind.Paused,
            "Garmoth", result.Message + (_sessionSubmitted ? " Zum Weitergrinden eine neue Sitzung starten." : ""));
        UpdateControlState();
    }

    private void MarkHistoryUploadBlocked(Guid sessionId, bool succeeded)
    {
        // An hourly upload can finish before the first pause creates a history
        // entry. Persist its guard now so the full session cannot be sent again.
        if (_hasSession && sessionId == _sessionId)
            PersistCurrentSession(DateTimeOffset.UtcNow);
        var index = _historyEntries.FindIndex(candidate => candidate.SessionId == sessionId);
        if (index < 0)
            return;
        var previous = _historyEntries[index];
        _historyEntries[index] = previous with
        {
            GarmothUploadBlocked = true,
            GarmothUploadedAt = succeeded ? DateTimeOffset.UtcNow : null
        };
        try
        {
            _historyStore.Save(_historyEntries);
            _historyView.SetEntries(_historyEntries);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Keep the in-memory guard: the remote write may already be committed.
            SetStatus(UiStatusKind.Error, "Garmoth-Verlauf",
                "Upload abgeschlossen, der lokale Upload-Status konnte aber nicht gespeichert werden: " +
                exception.Message);
        }
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
        var character = _hasSession || _demoMode ? _sessionClass : SelectedCharacterClass;
        _characterClassLabel.Text = character is not null
            ? FormatLiveClassName(character)
            : _classDetection.Status == CharacterClassDetectionStatus.Ambiguous
                ? "mehrdeutig – unter Optionen wählen"
                : "unbekannt – unter Optionen wählen";
        _characterClassIcon.Image = character is null
            ? null
            : _liveClassIcons.Get(SpotHistoryDetailView.CreateClassIconFileName(character.Name));
        _characterClassIcon.Visible = _characterClassIcon.Image is not null;
        UpdateLiveIdentity();
    }

    private static string FormatLiveClassName(CharacterClass character)
    {
        var specialization = character.Specialization switch
        {
            CharacterSpecialization.Awakening => "Awakening",
            CharacterSpecialization.Succession => "Succession",
            _ => "Ascension"
        };
        return $"{character.Name} – {specialization}";
    }

    private void UpdateLiveIdentity()
    {
        LootSpotPresentation? profile = null;
        if (_sessionSpotId is { } spotId)
            profile = LootSpotPresentationCatalog.GetRequired(spotId);
        var spotIcon = profile is null ? null : _liveSpotIcons.Get(profile.IconFileName);
        var background = profile is null ? null : _liveSpotBackgrounds.Get(profile.BackgroundFileName);
        _sessionSpotIcon.Image = spotIcon;
        _sessionSpotIcon.Visible = false;
        _sessionSpotNameLabel.Text = profile is null
            ? "SESSIONDAUER"
            : LootSpotCatalog.GetRequired(profile.SpotId).DisplayName;
        _sessionSpotNameLabel.Font = profile is null ? _captionFont : _baseFont;
        _sessionSpotNameLabel.ForeColor = profile is null
            ? BdoTheme.TextMuted : Color.FromArgb(255, 232, 185);
        _sessionMetricLayout.Parent?.PerformLayout();
        _sessionMetricLayout.Invalidate(true);
        _totalsView.SetSpotBackground(background);
    }

    private void ShowDemoHour()
    {
        if (_uiRunning || IsBusy || _hasSession)
            return;
        _demoMode = true;
        _demoDuration = TimeSpan.FromHours(1);
        _sessionSpotId = LootSpotCatalog.AphrodonId;
        _sessionClass = CompanionCharacterClassCatalog.FindById("warrior-awakening");
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["Branch of Abundance"] = 18_420,
            ["Ancient Spirit Dust"] = 76,
            ["Black Stone"] = 51,
            ["WON Origin Shard"] = 4,
            ["WON Wandering Origin Crystal"] = 1,
            ["Broken Vestige of Goldroot"] = 1,
            ["Nev's Fragment"] = 9
        };
        _sessionSummary = new LootSessionSnapshot(totals, totals.Values.Sum(), 147);
        _totalsView.SetTotals(totals);
        UpdateCharacterClassLabel();
        UpdateSessionSummary();
        UpdateControlState();
        SetStatus(UiStatusKind.Ready, "Demostunde",
            "Nicht gespeicherte Beispielstunde. Mit Tracking starten oder „Neue Sitzung“ beenden.");
    }

    private void ClearDemoHour()
    {
        if (!_demoMode)
            return;
        _demoMode = false;
        _demoDuration = TimeSpan.Zero;
        _sessionSpotId = null;
        _sessionClass = null;
        _sessionSummary = LootSessionSnapshot.Empty;
        _totalsView.SetTotals(_sessionSummary.Totals);
        UpdateCharacterClassLabel();
        UpdateSessionSummary();
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
        _uploadOptionsOpen = true;
        try
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            ApplySilverPreferences(dialog.SelectedRegion, dialog.SelectedTaxOptions);
        }
        finally { _uploadOptionsOpen = false; }
        _priceRefreshEnabled = true;
        await RefreshPricesAsync();
    }

    private void ApplySilverPreferences(string region, SilverTaxOptions tax)
    {
        _settings.UpdateSilverPreferences(region, tax);
        SaveSettings();
        // Never apply a response from another region to this session's valuation.
        _prices = _priceProvider.GetCachedSnapshot(_settings.MarketRegion);
        _historyView.SetPricing(_prices, tax);
        _totalsView.SetPricing(_prices, tax);
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
            _historyView.SetPricing(_prices, _settings.GetSilverTaxOptions());
            _totalsView.SetPricing(_prices, _settings.GetSilverTaxOptions());
            UpdateSilverValuation();
        }
        catch (OperationCanceledException) when (_priceLifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            InvalidDataException or OperationCanceledException)
        {
            if (!_shutdownStarted && !_uiResourcesDisposed && !IsDisposed && region == _settings.MarketRegion)
            {
                _prices = _priceProvider.GetCachedSnapshot(region);
                _historyView.SetPricing(_prices, _settings.GetSilverTaxOptions());
                _totalsView.SetPricing(_prices, _settings.GetSilverTaxOptions());
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
                    PersistCurrentSession(DateTimeOffset.UtcNow);
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
        _uiMailbox.Publish(completed, onPublished: ObserveGarmothTotals);
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
            PersistCurrentSession(DateTimeOffset.UtcNow);
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

    private void PersistCurrentSession(DateTimeOffset updatedAt)
    {
        if (!_hasSession || _sessionSpotId is null ||
            _sessionClock.Elapsed <= TimeSpan.Zero || _sessionSummary.ItemTypeCount == 0)
            return;

        var totals = _sessionSummary.Totals
            .Where(static pair => pair.Value > 0)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        if (totals.Count == 0)
            return;

        var valuation = SilverValuation.Calculate(totals, _prices, _settings.GetSilverTaxOptions());
        var character = (_sessionClass ?? SelectedCharacterClass)?.DisplayName;
        var entry = new LootHistoryEntry
        {
            SessionId = _sessionId,
            StartedAt = _sessionStartedAt ?? updatedAt - _sessionClock.Elapsed,
            UpdatedAt = updatedAt,
            Duration = _sessionClock.Elapsed,
            SpotId = _sessionSpotId,
            CharacterClass = character,
            Totals = totals,
            SilverBeforeTax = valuation.BeforeTax,
            SilverAfterTax = valuation.AfterTax,
            SilverIsComplete = valuation.IsComplete
        };

        var existingIndex = _historyEntries.FindIndex(candidate =>
            candidate.SessionId == entry.SessionId);
        if (existingIndex >= 0)
            _historyEntries[existingIndex] = entry with
            {
                GarmothUploadedAt = _historyEntries[existingIndex].GarmothUploadedAt,
                GarmothUploadBlocked = _historyEntries[existingIndex].GarmothUploadBlocked
            };
        else
            _historyEntries.Add(entry);
        _historyEntries.Sort(static (left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
        if (_historyEntries.Count > LootHistoryStore.MaximumEntries)
            _historyEntries.RemoveRange(LootHistoryStore.MaximumEntries,
                _historyEntries.Count - LootHistoryStore.MaximumEntries);

        try
        {
            _historyStore.Save(_historyEntries);
            if (!_historyView.IsDisposed)
                _historyView.SetEntries(_historyEntries);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!_shutdownStarted)
                SetStatus(UiStatusKind.Error, "Verlauf nicht gespeichert", exception.Message);
        }
    }

    private void DeleteHistorySession(Guid sessionId, bool requireConfirmation)
    {
        var entry = _historyEntries.FirstOrDefault(candidate => candidate.SessionId == sessionId);
        if (entry is null)
            return;
        if (_hasSession && sessionId == _sessionId)
        {
            if (requireConfirmation)
                MessageBox.Show(this,
                    "Die aktuell laufende Sitzung kann erst nach einer neuen Sitzung gelöscht werden.",
                    "Grind-Stunde löschen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var spotName = LootSpotCatalog.GetRequired(entry.SpotId).DisplayName;
        if (requireConfirmation && MessageBox.Show(this,
                $"Soll die Grind-Stunde in {spotName} vom {entry.UpdatedAt.ToLocalTime():dd.MM.yyyy HH:mm} " +
                $"({SpotHistoryCard.FormatDuration(entry.Duration)}) dauerhaft gelöscht werden?",
                "Grind-Stunde löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _historyEntries.Remove(entry);
        try
        {
            _historyStore.Save(_historyEntries);
            _historyView.SetEntries(_historyEntries);
            SetStatus(UiStatusKind.Ready, "Stunde gelöscht", $"{spotName} wurde aus dem Loot-Verlauf entfernt.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _historyEntries.Add(entry);
            _historyEntries.Sort(static (left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
            _historyView.SetEntries(_historyEntries);
            SetStatus(UiStatusKind.Error, "Löschen fehlgeschlagen", exception.Message);
        }
    }

    private void EditHistorySession(Guid sessionId)
    {
        var entry = _historyEntries.FirstOrDefault(candidate => candidate.SessionId == sessionId);
        if (entry is null)
            return;
        if (_hasSession && sessionId == _sessionId)
        {
            MessageBox.Show(this,
                "Die aktuell laufende Sitzung kann erst nach einer neuen Sitzung bearbeitet werden.",
                "Lootmengen bearbeiten", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new LootAmountsDialog(entry);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        if (dialog.Totals.Count == 0)
        {
            MessageBox.Show(this, "Mindestens eine Lootmenge muss größer als 0 sein.",
                "Lootmengen bearbeiten", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        UpdateHistorySessionLoot(sessionId, dialog.Totals);
    }

    private async void UploadHistorySession(Guid sessionId)
    {
        if (IsBusy || _shutdownStarted)
            return;
        if (_hasSession && sessionId == _sessionId)
        {
            // Share the live upload ledger even when invoked from the history.
            await UploadToGarmothAsync();
            return;
        }
        var index = _historyEntries.FindIndex(candidate => candidate.SessionId == sessionId);
        if (index < 0 || _historyEntries[index].GarmothUploadBlocked)
            return;
        if (string.IsNullOrEmpty(_garmothApiKey))
        {
            ShowMainArea(showHistory: false);
            if (!_optionsExpanded)
                ToggleOptions();
            SetStatus(UiStatusKind.Error, "Garmoth",
                "API-Key einmal unter Optionen → Garmoth-Key hinterlegen.");
            return;
        }

        _operationInProgress = true;
        _garmothUploadInProgress = true;
        UpdateControlState();
        try
        {
            await RefreshPricesAsync();
            var entry = _historyEntries[index];
            var draft = CreateHistoricalGarmothDraft(entry, _prices,
                _settings.GetSilverTaxOptions());
            var payload = GarmothSessionPayload.Create(draft);
            SetStatus(UiStatusKind.Paused, "Garmoth",
                $"{LootSpotCatalog.GetRequired(entry.SpotId).DisplayName} wird übertragen …");
            var result = await _garmothClient.UploadAsync(draft, _garmothApiKey);
            if (payload.OmittedItems.Count > 0)
                result = result with
                {
                    Message = result.Message + " Ohne Garmoth-Zuordnung ausgelassen: " +
                              string.Join(", ", payload.OmittedItems) + "."
                };
            if (result.BlocksAnotherUpload)
            {
                _historyEntries[index] = entry with
                {
                    GarmothUploadBlocked = true,
                    GarmothUploadedAt = result.Status == GarmothUploadStatus.Succeeded
                        ? DateTimeOffset.UtcNow
                        : null
                };
                _historyStore.Save(_historyEntries);
                _historyView.SetEntries(_historyEntries);
            }
            SetStatus(result.Status == GarmothUploadStatus.Rejected
                    ? UiStatusKind.Error : UiStatusKind.Paused,
                "Garmoth", result.Message);
        }
        catch (ArgumentException exception)
        {
            SetStatus(UiStatusKind.Error, "Garmoth nicht gesendet", exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(UiStatusKind.Error, "Garmoth-Verlauf", exception.Message);
        }
        finally
        {
            _garmothUploadInProgress = false;
            _operationInProgress = false;
            UpdateControlState();
        }
    }

    internal static GarmothSessionDraft CreateHistoricalGarmothDraft(
        LootHistoryEntry entry, LootPriceSnapshot prices, SilverTaxOptions tax)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(tax);
        if (string.IsNullOrWhiteSpace(entry.CharacterClass))
            throw new ArgumentException("Für diesen Grind fehlt die Klasse. Bitte die Klasse zuerst in den Optionen festlegen.");
        var className = SpotHistoryDetailView.ExtractBaseClassName(entry.CharacterClass);
        var specialization = entry.CharacterClass.Contains("Awakening", StringComparison.OrdinalIgnoreCase)
            ? GarmothSpecialization.Awakening
            : entry.CharacterClass.Contains("Succession", StringComparison.OrdinalIgnoreCase)
                ? GarmothSpecialization.Succession
                : GarmothSpecialization.Unique;
        var valuation = SilverValuation.Calculate(entry.Totals, prices, tax);
        if (!valuation.HasKnownValue)
            throw new ArgumentException("Für den Grind ist noch kein Silberpreis verfügbar.");
        if (valuation.AfterTax < 0 || valuation.AfterTax > long.MaxValue || valuation.OverflowItems.Count > 0)
            throw new ArgumentException("Der berechnete Silberwert ist nicht für Garmoth darstellbar.");
        return new GarmothSessionDraft(entry.SessionId, entry.SpotId, className,
            specialization, entry.Duration, entry.Totals,
            (long)decimal.Truncate(valuation.AfterTax), entry.StartedAt)
        {
            SourceSessionId = entry.SessionId
        };
    }

    private bool UpdateHistorySessionLoot(Guid sessionId, IReadOnlyDictionary<string, long> totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        var index = _historyEntries.FindIndex(candidate => candidate.SessionId == sessionId);
        if (index < 0)
            return false;
        var cleaned = totals
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
            .ToDictionary(static pair => pair.Key.Trim(), static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        if (cleaned.Count == 0)
            return false;

        var previous = _historyEntries[index];
        var valuation = SilverValuation.Calculate(cleaned, _prices, _settings.GetSilverTaxOptions());
        var edited = previous with
        {
            Totals = cleaned,
            SilverBeforeTax = valuation.BeforeTax,
            SilverAfterTax = valuation.AfterTax,
            SilverIsComplete = valuation.IsComplete
        };
        _historyEntries[index] = edited;
        try
        {
            _historyStore.Save(_historyEntries);
            _historyView.SetEntries(_historyEntries);
            SetStatus(UiStatusKind.Ready, "Loot aktualisiert",
                $"Die Dropmengen für {LootSpotCatalog.GetRequired(edited.SpotId).DisplayName} wurden gespeichert.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _historyEntries[index] = previous;
            _historyView.SetEntries(_historyEntries);
            SetStatus(UiStatusKind.Error, "Änderung nicht gespeichert", exception.Message);
            return false;
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
            !_garmothIntervals.IsBlocked && !_demoMode && _sessionSummary.ItemTypeCount > 0;
        _demoButton.Enabled = !IsBusy && !_uiRunning && !_hasSession;
        _garmothOptionsButton.Enabled = !IsBusy;
        _garmothOptionsButton.Text = string.IsNullOrEmpty(_garmothApiKey) ? "Garmoth-Key"
            : _settings.GarmothAutoUploadEnabled
                ? _garmothIntervals.AutomaticSuspended ? "Garmoth · prüfen" : "Garmoth · Auto"
                : "Garmoth-Key ✓";
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
            _characterClassIcon.Image = null;
            _sessionSpotIcon.Image = null;
            _brandLogoImage.Dispose();
            Icon = null;
            _brandIcon.Dispose();
            _uiRefreshTimer.Dispose();
            _uiMailbox.Dispose();
            _recording?.Dispose();
            _garmothClient.Dispose();
            _liveSpotBackgrounds.Dispose();
            _liveSpotIcons.Dispose();
            _liveClassIcons.Dispose();
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
