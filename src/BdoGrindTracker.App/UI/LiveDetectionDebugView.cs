using BdoGrindTracker.Core;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Compact diagnostics for the fixed BDO Companion 0.7.4 normal-loot pipeline.
/// Captured pixels stay in memory; only the bounded preview owned by
/// <see cref="DetectionRegionPreview"/> is retained.
/// </summary>
internal sealed class LiveDetectionDebugView : UserControl
{
    private const int MaximumOcrLines = 3;

    private readonly DetectionRegionPreview _regionPreview = new();
    private readonly Label _modeLabel = new();
    private readonly Label _regionLabel = new();
    private readonly Label _captureMetricsLabel = new();
    private readonly Label _pipelineMetricsLabel = new();
    private readonly Label _ocrMetricsLabel = new();
    private readonly BdoButton _copyButton = new();
    private readonly ToolTip _toolTip = new();
    private readonly Font _headingFont = new(
        "Segoe UI Semibold",
        10.5f,
        FontStyle.Bold,
        GraphicsUnit.Point);
    private readonly Font _captionFont = new(
        "Segoe UI Semibold",
        7.5f,
        FontStyle.Bold,
        GraphicsUnit.Point);

    private Rectangle? _monitorDesktopBounds;
    private LiveDetectionDebugSnapshot? _lastSnapshot;
    private bool _resourcesDisposed;

    public LiveDetectionDebugView()
    {
        BackColor = BdoTheme.Background;
        Margin = new Padding(0, 0, 0, 10);
        MinimumSize = new Size(0, 166);
        AccessibleName = "Live-Erkennungsdiagnose";

        var surface = new BdoSurfacePanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 13,
            Padding = new Padding(12, 10, 14, 10),
            Margin = Padding.Empty,
            BackColor = BdoTheme.Surface,
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BdoTheme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 274f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _regionPreview.Dock = DockStyle.Fill;
        _regionPreview.Margin = new Padding(0, 0, 14, 0);
        layout.Controls.Add(_regionPreview, 0, 0);
        layout.Controls.Add(BuildDetails(), 1, 0);
        surface.Controls.Add(layout);
        Controls.Add(surface);

        ShowWaitingState(isTracking: false);
    }

    internal LiveDetectionDebugSnapshot? LastSnapshot => _lastSnapshot;

    internal string DiagnosticText => _lastSnapshot is { } snapshot
        ? BuildClipboardText(snapshot, _monitorDesktopBounds)
        : string.Empty;

    internal void SetMonitor(Rectangle? desktopBounds)
    {
        if (_monitorDesktopBounds == desktopBounds)
        {
            return;
        }

        _monitorDesktopBounds = desktopBounds;
        _lastSnapshot = null;
        _regionPreview.ClearSnapshot();
        ShowWaitingState(isTracking: false);
    }

    internal void SetTrackingState(bool isTracking)
    {
        if (_lastSnapshot is null)
        {
            ShowWaitingState(isTracking);
            return;
        }

        _modeLabel.Text = isTracking
            ? "COMPANION-ABGLEICH · AUTO-SPOTFILTER"
            : "PAUSIERT · LETZTES FRAME";
        _modeLabel.ForeColor = isTracking ? BdoTheme.Positive : BdoTheme.TextMuted;
    }

    internal void Clear(Rectangle? desktopBounds)
    {
        _monitorDesktopBounds = desktopBounds;
        _lastSnapshot = null;
        _regionPreview.ClearSnapshot();
        ShowWaitingState(isTracking: false);
    }

    /// <summary>Transfers ownership of <paramref name="thumbnail"/> to the preview.</summary>
    internal void UpdateSnapshot(LiveDetectionDebugSnapshot snapshot, Bitmap thumbnail)
    {
        ArgumentNullException.ThrowIfNull(thumbnail);
        ArgumentOutOfRangeException.ThrowIfLessThan(snapshot.CapturedSequence, 1);
        if (snapshot.FrameSize.Width <= 0 || snapshot.FrameSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshot),
                "The analyzed frame size must be positive.");
        }

        _regionPreview.SetSnapshot(
            thumbnail,
            snapshot.FrameSize,
            snapshot.SlotRegions,
            snapshot.PanelRegion is { } panel ? [panel] : []);
        _lastSnapshot = snapshot;
        _copyButton.Enabled = true;
        _copyButton.Text = "Diagnose kopieren";
        _toolTip.SetToolTip(_copyButton, "Letzten Live-Snapshot in die Zwischenablage kopieren");

        _modeLabel.Text = "COMPANION-ABGLEICH · AUTO-SPOTFILTER";
        _modeLabel.ForeColor = BdoTheme.Positive;
        _regionLabel.Text =
            $"Panel (gold): {FormatRegion(snapshot.PanelRegion, snapshot.FrameSize)} · " +
            $"6 Bänder (grün): {FormatRegions(snapshot.SlotRegions, snapshot.FrameSize)}";

        var localCapturedAt = snapshot.CapturedAt.ToLocalTime();
        _captureMetricsLabel.Text =
            $"Frame {snapshot.CapturedSequence:N0} · {localCapturedAt:HH:mm:ss.fff} · " +
            $"{snapshot.FrameSize.Width:N0} × {snapshot.FrameSize.Height:N0} px · " +
            $"DXGI · HDR {(snapshot.IsHdr ? "an" : "aus")} · " +
            $"Analyse {snapshot.AnalysisDuration.TotalMilliseconds:N0} ms";

        _pipelineMetricsLabel.Text =
            $"Bänder {snapshot.PreparedRowCount:N0} · nicht leer {snapshot.NonBlankRowCount:N0} · " +
            $"OCR {snapshot.OcrRowCount:N0} · Katalogtreffer {snapshot.CatalogMatchCount:N0} · " +
            $"Buchungen/Korrekturen {snapshot.NewEventCount:N0}";
        _ocrMetricsLabel.Text = FormatOcrSummary(snapshot);

        AccessibleDescription =
            $"{_modeLabel.Text}. {_regionLabel.Text}. {_captureMetricsLabel.Text}. " +
            $"{_pipelineMetricsLabel.Text}. {_ocrMetricsLabel.Text}.";
    }

    internal static string BuildClipboardText(
        LiveDetectionDebugSnapshot snapshot,
        Rectangle? monitorDesktopBounds)
    {
        var culture = CultureInfo.CurrentCulture;
        var confidence = double.IsFinite(snapshot.MeanMatchConfidence)
            ? Math.Clamp(snapshot.MeanMatchConfidence, 0, 1)
            : 0;
        var text = new StringBuilder()
            .AppendLine(AppBranding.Name + " – Ereignisdiagnose")
            .Append("Aufnahme: ")
            .AppendLine(snapshot.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz", culture))
            .Append("Monitor (Desktop): ")
            .AppendLine(monitorDesktopBounds is { } monitor ? FormatRectangle(monitor) : "nicht bekannt")
            .Append("Frame: ")
            .Append(snapshot.CapturedSequence.ToString("N0", culture))
            .Append(" · ")
            .Append(snapshot.FrameSize.Width.ToString("N0", culture))
            .Append(" × ")
            .Append(snapshot.FrameSize.Height.ToString("N0", culture))
            .AppendLine(" px")
            .Append("Aufnahmepfad / HDR: DXGI Desktop Duplication / ")
            .AppendLine(snapshot.IsHdr ? "an" : "aus")
            .Append("Analysezeit: ")
            .Append(snapshot.AnalysisDuration.TotalMilliseconds.ToString("N1", culture))
            .AppendLine(" ms")
            .Append("Panel: ")
            .AppendLine(snapshot.PanelRegion is { } panel ? FormatRectangle(panel) : "nicht gemeldet")
            .Append("Zeilenbänder: ")
            .AppendLine(FormatRegionList(snapshot.SlotRegions, "keine"))
            .AppendLine()
            .AppendLine("Companion-Bildaufbereitung, Spotfilter und 10-Frame-Abgleich")
            .Append("Vorbereitete Bänder: ")
            .AppendLine(snapshot.PreparedRowCount.ToString("N0", culture))
            .Append("Nicht leere Bänder: ")
            .AppendLine(snapshot.NonBlankRowCount.ToString("N0", culture))
            .Append("Windows-OCR-Aufrufe: ")
            .AppendLine(snapshot.OcrRowCount.ToString("N0", culture))
            .Append("Katalogtreffer: ")
            .AppendLine(snapshot.CatalogMatchCount.ToString("N0", culture))
            .Append("Ausgegebene Buchungen/Korrekturen: ")
            .AppendLine(snapshot.NewEventCount.ToString("N0", culture))
            .Append("Backend / Sprache: ")
            .Append(string.IsNullOrWhiteSpace(snapshot.TextRecognitionBackend)
                ? "unbekannt"
                : snapshot.TextRecognitionBackend.Trim())
            .Append(" / ")
            .AppendLine(string.IsNullOrWhiteSpace(snapshot.TextRecognitionLanguage)
                ? "unbekannt"
                : snapshot.TextRecognitionLanguage.Trim())
            .Append("Mittlere Namensähnlichkeit (keine Trefferquote): ")
            .AppendLine(FormatConfidence(confidence, culture))
            .Append("Variante: ")
            .AppendLine(string.IsNullOrWhiteSpace(snapshot.VariantName)
                ? "keine"
                : snapshot.VariantName.Trim())
            .Append("Rohe OCR-Zeilen: ")
            .AppendLine(snapshot.RecognizedLines.Count.ToString("N0", culture));

        if (snapshot.RecognizedLines.Count == 0)
        {
            text.AppendLine("- keine");
        }
        else
        {
            foreach (var line in snapshot.RecognizedLines)
            {
                text.Append("- ").AppendLine(NormalizeSingleLine(line));
            }
        }

        text.AppendLine().Append("Grindspot: ").AppendLine(snapshot.SpotId ?? "noch nicht aus Trashloot erkannt");
        text.AppendLine("Entscheidungen (Diagnosewerte, keine zusätzlichen Bestätigungsgates):");
        foreach (var decision in snapshot.Decisions)
        {
            var observation = decision.Observation;
            text.Append("- ").Append(decision.Status).Append(" · ")
                .Append(observation.Source).Append('/').Append(observation.Slot)
                .Append(" · Y ").Append(observation.NativeY?.ToString(culture) ?? "–")
                .Append(" · Event ").Append(decision.EventId?.ToString() ?? "–")
                .Append(" · ").Append(observation.ItemName ?? "ungeklärt")
                .Append(" ×").Append(observation.Quantity?.ToString(culture) ?? "?")
                .Append(" · Namensähnlichkeit ").Append(observation.NameConfidence.ToString("F3", culture))
                .Append(" · ").Append(decision.Reason)
                .Append(" · OCR: ").AppendLine(NormalizeSingleLine(observation.RawText));
        }
        return text.ToString().TrimEnd();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _headingFont.Dispose();
            _captionFont.Dispose();
            _toolTip.Dispose();
            _resourcesDisposed = true;
        }

        base.Dispose(disposing);
    }

    private Control BuildDetails()
    {
        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = BdoTheme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var heading = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = BdoTheme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.Controls.Add(new Label
        {
            Text = "Live-Erkennung",
            AutoSize = true,
            Font = _headingFont,
            ForeColor = BdoTheme.Text,
            Margin = new Padding(0, 2, 10, 0),
        }, 0, 0);

        _modeLabel.AutoSize = true;
        _modeLabel.Font = _captionFont;
        _modeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _modeLabel.Anchor = AnchorStyles.Left;
        _modeLabel.Margin = new Padding(0, 5, 10, 0);
        heading.Controls.Add(_modeLabel, 1, 0);

        ConfigureCopyButton();
        heading.Controls.Add(_copyButton, 2, 0);
        details.Controls.Add(heading, 0, 0);

        ConfigureValueLabel(_regionLabel, "Companion-Panel", BdoTheme.Text, new Padding(0, 1, 0, 1));
        ConfigureValueLabel(_captureMetricsLabel, "Diagnose Aufnahme", BdoTheme.TextMuted, Padding.Empty);
        ConfigureValueLabel(_pipelineMetricsLabel, "Diagnose Erkennung", BdoTheme.TextMuted, Padding.Empty);
        ConfigureValueLabel(_ocrMetricsLabel, "Diagnose OCR-Zeilen", BdoTheme.TextMuted, Padding.Empty);
        details.Controls.Add(_regionLabel, 0, 1);
        details.Controls.Add(_captureMetricsLabel, 0, 2);
        details.Controls.Add(_pipelineMetricsLabel, 0, 3);
        details.Controls.Add(_ocrMetricsLabel, 0, 4);
        return details;
    }

    private void ConfigureCopyButton()
    {
        _copyButton.Text = "Diagnose kopieren";
        _copyButton.AutoSize = true;
        _copyButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _copyButton.MinimumSize = new Size(124, 28);
        _copyButton.ButtonStyle = BdoButtonStyle.Secondary;
        _copyButton.CornerRadius = 8;
        _copyButton.Margin = Padding.Empty;
        _copyButton.Padding = new Padding(9, 1, 9, 1);
        _copyButton.AccessibleName = "Diagnose kopieren";
        _copyButton.Enabled = false;
        _copyButton.Click += CopyButton_Click;
        _toolTip.SetToolTip(_copyButton, "Noch kein Live-Snapshot vorhanden");
    }

    private void CopyButton_Click(object? sender, EventArgs e)
    {
        var diagnosticText = DiagnosticText;
        if (diagnosticText.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(diagnosticText);
            _copyButton.Text = "Kopiert";
            _toolTip.SetToolTip(_copyButton, "Diagnose wurde in die Zwischenablage kopiert");
        }
        catch (ExternalException exception)
        {
            _copyButton.Text = "Kopieren fehlgeschlagen";
            _toolTip.SetToolTip(_copyButton, exception.Message);
        }
    }

    private void ShowWaitingState(bool isTracking)
    {
        var monitorSize = _monitorDesktopBounds?.Size ?? Size.Empty;
        _modeLabel.Text = isTracking ? "WARTE AUF LOOT-ANALYSE" : "BEREIT";
        _modeLabel.ForeColor = isTracking ? BdoTheme.Warning : BdoTheme.TextMuted;
        _regionLabel.Text = monitorSize.Width > 0 && monitorSize.Height > 0
            ? $"Monitor: {monitorSize.Width:N0} × {monitorSize.Height:N0} px · noch kein Frame"
            : "Kein Monitor ausgewählt";
        _captureMetricsLabel.Text = isTracking
            ? "Der erste analysierte Frame steht noch aus."
            : "Tracking starten, um Live-Diagnosen zu sehen.";
        _pipelineMetricsLabel.Text = "Bänder · Windows OCR · automatischer Spotfilter · Companion-Abgleich";
        _ocrMetricsLabel.Text = "OCR: keine erkannten Zeilen";
        _copyButton.Text = "Diagnose kopieren";
        _copyButton.Enabled = false;
        _toolTip.SetToolTip(_copyButton, "Noch kein Live-Snapshot vorhanden");
        AccessibleDescription = $"{_modeLabel.Text}. {_regionLabel.Text}.";
    }

    private static void ConfigureValueLabel(
        Label label,
        string accessibleName,
        Color color,
        Padding margin)
    {
        label.Dock = DockStyle.Fill;
        label.AutoEllipsis = true;
        label.ForeColor = color;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Margin = margin;
        label.UseMnemonic = false;
        label.AccessibleName = accessibleName;
    }

    private static string FormatOcrSummary(LiveDetectionDebugSnapshot snapshot)
    {
        var confidence = double.IsFinite(snapshot.MeanMatchConfidence)
            ? Math.Clamp(snapshot.MeanMatchConfidence, 0, 1)
            : 0;
        var lines = snapshot.RecognizedLines
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Take(MaximumOcrLines)
            .Select(static line => $"„{Abbreviate(NormalizeSingleLine(line), 48)}“")
            .ToArray();
        var samples = lines.Length == 0 ? "keine Zeilen" : string.Join(" / ", lines);
        var omitted = snapshot.RecognizedLines.Count - lines.Length;
        var suffix = omitted > 0 ? $" / … +{omitted:N0}" : string.Empty;
        return
            $"OCR: {snapshot.TextRecognitionBackend} / {snapshot.TextRecognitionLanguage ?? "unbekannt"} · " +
            $"{FormatConfidence(confidence, CultureInfo.CurrentCulture)} · {samples}{suffix}";
    }

    private static string FormatRegion(Rectangle? region, Size frameSize)
    {
        if (region is not { } bounds)
        {
            return "nicht gemeldet";
        }

        var frameArea = (double)frameSize.Width * frameSize.Height;
        var clipped = Rectangle.Intersect(bounds, new Rectangle(Point.Empty, frameSize));
        var coverage = frameArea > 0
            ? Math.Clamp((double)Math.Max(0, clipped.Width) * Math.Max(0, clipped.Height) / frameArea, 0, 1)
            : 0;
        return $"x {bounds.X:N0}, y {bounds.Y:N0}, {bounds.Width:N0} × {bounds.Height:N0} px ({coverage:P0})";
    }

    private static string FormatRegions(IReadOnlyList<Rectangle> regions, Size frameSize) =>
        regions.Count == 0
            ? "keine"
            : string.Join(" | ", regions.Select(region => FormatRegion(region, frameSize)));

    private static string FormatRegionList(IReadOnlyList<Rectangle> regions, string emptyText) =>
        regions.Count == 0
            ? emptyText
            : string.Join(" | ", regions.Select(FormatRectangle));

    private static string FormatRectangle(Rectangle rectangle) =>
        $"x={rectangle.X}, y={rectangle.Y}, width={rectangle.Width}, height={rectangle.Height}";

    private static string FormatConfidence(double confidence, CultureInfo culture)
    {
        var normalized = double.IsFinite(confidence) ? Math.Clamp(confidence, 0, 1) : 0;
        return $"{(normalized * 100).ToString("0.0", culture)} %";
    }

    private static string NormalizeSingleLine(string? value) =>
        string.Join(
            " ",
            (value ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Abbreviate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 1)] + "…";
}

internal readonly record struct LiveDetectionDebugSnapshot(
    DateTimeOffset CapturedAt,
    long CapturedSequence,
    TimeSpan AnalysisDuration,
    Size FrameSize,
    Rectangle? PanelRegion,
    IReadOnlyList<Rectangle> SlotRegions,
    int PreparedRowCount,
    int NonBlankRowCount,
    int OcrRowCount,
    int CatalogMatchCount,
    IReadOnlyList<string> RecognizedLines,
    int NewEventCount,
    double MeanMatchConfidence,
    string VariantName,
    string TextRecognitionBackend,
    string? TextRecognitionLanguage,
    bool IsHdr = false)
{
    public string? SpotId { get; init; }
    public IReadOnlyList<LootTrackingDecision> Decisions { get; init; } = [];
}
