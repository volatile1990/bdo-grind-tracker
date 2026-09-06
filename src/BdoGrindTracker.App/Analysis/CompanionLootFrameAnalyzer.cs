using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Restored 0.5.1 recognition and reconciliation, with an automatic spot allowlist.</summary>
internal sealed class CompanionLootFrameAnalyzer : ILootFrameAnalyzer, ILootFilterConfigurableAnalyzer
{
    internal const string ExactVariantName = "bdo-companion-0.7.4";
    private readonly CompanionCalibration _calibration;
    private readonly CompanionItemMatcher _itemMatcher;
    private readonly ICompanionBitmapDecoder _frameDecoder;
    private readonly ICompanionNormalRowPipeline _rowPipeline;
    private readonly ICompanionRareRowPipeline? _rareRowPipeline;
    private readonly ICompanionNameRecognizer _nameRecognizer;
    private readonly ICompanionReconciliation _reconciliation;
    private readonly ICompanionRareReconciliation? _rareReconciliation;
    private readonly CompanionLootLedger _ledger = new();
    private readonly AutomaticLootSpotLock _spotLock = new();
    private readonly Rectangle _panelBounds;
    private readonly Rectangle[] _slotBounds;
    private readonly Rectangle? _rarePanelBounds;
    private readonly Rectangle? _rareBandBounds;
    private bool _includeEventLoot;
    private bool _disposed;

    public CompanionLootFrameAnalyzer(
        CompanionCalibration calibration,
        CompanionItemMatcher itemMatcher,
        ICompanionNormalRowPipeline rowPipeline,
        ICompanionNameRecognizer nameRecognizer,
        ICompanionReconciliation? reconciliation = null,
        ICompanionBitmapDecoder? frameDecoder = null,
        ICompanionRareRowPipeline? rareRowPipeline = null,
        ICompanionRareReconciliation? rareReconciliation = null)
    {
        _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        _itemMatcher = itemMatcher ?? throw new ArgumentNullException(nameof(itemMatcher));
        _rowPipeline = rowPipeline ?? throw new ArgumentNullException(nameof(rowPipeline));
        _nameRecognizer = nameRecognizer ?? throw new ArgumentNullException(nameof(nameRecognizer));
        _reconciliation = reconciliation ?? new CompanionReconciliationAdapter();
        _frameDecoder = frameDecoder ?? CompanionBitmapDecoder.Instance;
        _panelBounds = CompanionNormalLootGeometry.CalculatePanelBounds(calibration);
        _slotBounds = CompanionNormalLootGeometry.CalculateSlotCrops(calibration).ToArray();
        if (calibration.HasRareLootAnchor)
        {
            _rarePanelBounds = CompanionNormalLootGeometry.CalculateRarePanelBounds(calibration);
            _rareBandBounds = CompanionNormalLootGeometry.CalculateRareBandCrop(calibration);
            _rareRowPipeline = rareRowPipeline ?? new CompanionRareRowPipeline();
            _rareReconciliation = rareReconciliation ?? new CompanionRareReconciliationAdapter(
                new CompanionRareFrameReconciler(itemMatcher.CatalogEntries, _ledger));
        }
        else
        {
            rareRowPipeline?.Dispose();
        }
    }

    public bool IsAvailable => !_disposed;
    public string Status => _disposed ? "BDO-Companion-Erkennung wurde beendet."
        : _spotLock.Spot is { } spot ? $"Bereit: Companion · {spot.DisplayName} (Lootfilter)."
        : "Bereit: Companion · Spot wird aus Trashloot erkannt.";

    public void ConfigureLootFilter(bool includeEventLoot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _includeEventLoot = includeEventLoot;
    }

    public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt,
        CancellationToken cancellationToken) => AnalyzeAsync(frame, capturedAt, false, cancellationToken);

    public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt,
        bool isHdr, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        var preparedRows = new List<ICompanionPreparedRow>(_slotBounds.Length);
        ICompanionPreparedRow? rareRow = null;
        try
        {
            using var decodedFrame = _frameDecoder.Decode(frame);
            ValidateBoundsInsideFrame(decodedFrame, _panelBounds, "Normal-Loot-Panel");
            using var panel = new Mat(decodedFrame, ToOpenCvRect(_panelBounds));
            foreach (var bounds in _slotBounds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = new Rectangle(bounds.Left - _panelBounds.Left,
                    bounds.Top - _panelBounds.Top, bounds.Width, bounds.Height);
                using var band = new Mat(panel, ToOpenCvRect(relative));
                preparedRows.Add(_rowPipeline.Process(band, relative.Y,
                    _calibration.UiScale, (int)_calibration.FontType, isHdr));
            }
            if (_rareBandBounds is { } rareBounds && _rareRowPipeline is not null)
            {
                ValidateBoundsInsideFrame(decodedFrame, rareBounds, "Rare-Loot-Band");
                using var band = new Mat(decodedFrame, ToOpenCvRect(rareBounds));
                rareRow = _rareRowPipeline.Process(band, 0,
                    _calibration.UiScale, (int)_calibration.FontType, isHdr);
            }

            // Preserve Companion's trimming and OCR order, including the one row
            // immediately above the first quantity. Do not add persistence gates.
            preparedRows.Sort((left, right) => left.Y.CompareTo(right.Y));
            var first = CalculateRetainedStartIndex(preparedRows.Select(row => row.TemplateQuantity).ToArray());
            var normal = new List<LootObservation>();
            var rare = new List<LootObservation>();
            var rawLines = new List<string>();
            var ocrCalls = 0;
            for (var index = first; index < preparedRows.Count; index++)
                Observe(preparedRows[index], LootSource.Normal, preparedRows.Count - 1 - index, normal);
            if (rareRow is not null) Observe(rareRow, LootSource.Rare, 0, rare);

            normal.Reverse();
            // The only added recognition restriction: identify from the newest native
            // trash match, then filter canonical items without rematching them into the pool.
            _spotLock.Observe(normal.Where(IsAccepted).Select(row => row.ItemName!));
            var observations = normal.Concat(rare).Select(row =>
                IsAccepted(row) && !_spotLock.Allows(row.ItemName!, _includeEventLoot)
                    ? row with { RejectionReason = AutomaticLootSpotLock.OutsideSpotPoolReason }
                    : row).ToArray();
            var entries = observations.Where(row => row.Source == LootSource.Normal && IsAccepted(row))
                .Select(row => new CompanionRecognizedEntry(row.ItemName!,
                    unchecked((uint)(row.Quantity ?? -1)), row.NativeY!.Value)).ToArray();
            var rareEntries = observations.Where(row => row.Source == LootSource.Rare && IsAccepted(row))
                .Select(row => new CompanionRareRecognizedEntry(row.ItemName!,
                    row.Quantity ?? -1, row.NativeY!.Value)).ToArray();

            // Ordering and shared ledger are important for signed rare-loot corrections.
            var reconciled = _reconciliation.ProcessFrame(entries);
            foreach (var entry in reconciled) _ledger.Add(entry.Name, entry.Count);
            var rareChanges = _rareReconciliation?.ProcessFrame(rareEntries) ?? [];
            return Task.FromResult(CreateResult(reconciled, rareChanges, capturedAt, frame.Size,
                rawLines, observations, preparedRows.Count + (rareRow is null ? 0 : 1),
                preparedRows.Count(row => !row.IsBlank) + (rareRow is { IsBlank: false } ? 1 : 0), ocrCalls));

            void Observe(ICompanionPreparedRow row, LootSource source, int slot, List<LootObservation> target)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.IsBlank || row.NameImage is null) return;
                ocrCalls++;
                var ocr = _nameRecognizer.Recognize(row.NameImage, cancellationToken);
                if (ocr.Text.Length > 0) rawLines.Add(ocr.Text);
                var isRare = source == LootSource.Rare;
                string? rejection = null;
                CompanionItemMatch? match = null;
                CompanionTextResult? text = null;
                if (!isRare && !CompanionWindowsOcrRecognizer.PassesGeometryGate(
                    ocr.FirstWord, _calibration.UiScale, false))
                    rejection = "ocr-geometry";
                else
                {
                    text = CompanionTextPipeline.Process(ocr.Text, row.TemplateQuantity,
                        isRare, row.RecognizedTextWidth);
                    if (!CompanionTextPipeline.PassesExpectedWidth(text.Value,
                        row.RecognizedTextWidth, row.LeftmostQuantityX, row.NameScale))
                        rejection = "ocr-width-or-empty";
                    else if (!_itemMatcher.TryMatch(text.Value.Name, text.Value.Quantity, isRare, out match) || match is null)
                        rejection = "native-catalog-miss";
                }
                target.Add(new LootObservation(source, slot, ocr.Text, match?.CanonicalName,
                    text is null || text.Value.Quantity == -1 ? null : text.Value.Quantity,
                    match is null ? 0 : Math.Clamp(1 - match.NormalizedDistance, 0, 1),
                    0, null, rejection)
                {
                    NativeY = row.Y,
                });
            }
        }
        finally
        {
            foreach (var row in preparedRows) row.Dispose();
            rareRow?.Dispose();
        }
    }

    public FrameAnalysisResult CompleteSession(DateTimeOffset completedAt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var reconciled = _reconciliation.Complete();
        foreach (var entry in reconciled) _ledger.Add(entry.Name, entry.Count);
        var rareChanges = _rareReconciliation?.Complete() ?? [];
        return CreateResult(reconciled, rareChanges, completedAt,
            new System.Drawing.Size(_calibration.ScreenWidth, _calibration.ScreenHeight),
            [], [], 0, 0, 0);
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _reconciliation.Reset();
        _rareReconciliation?.Reset();
        _ledger.Reset();
        _spotLock.Reset();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _rowPipeline.Dispose();
        _rareRowPipeline?.Dispose();
        _disposed = true;
    }

    internal static int CalculateRetainedStartIndex(IReadOnlyList<int> templateQuantities)
    {
        ArgumentNullException.ThrowIfNull(templateQuantities);
        var leadingMissing = 0;
        while (leadingMissing < templateQuantities.Count && templateQuantities[leadingMissing] == -1)
            leadingMissing++;
        return Math.Max(leadingMissing - 1, 0);
    }

    private static bool IsAccepted(LootObservation row) => row.ItemName is not null && row.RejectionReason is null;

    private FrameAnalysisResult CreateResult(IReadOnlyList<CompanionRecognizedEntry> reconciled,
        IReadOnlyList<CompanionRareCountDelta> rareChanges, DateTimeOffset timestamp,
        System.Drawing.Size frameSize, IReadOnlyList<string> rawLines,
        IReadOnlyList<LootObservation> observations, int prepared, int nonBlank, int ocrCalls)
    {
        var events = reconciled.Select(entry => new LootEventView(Guid.NewGuid(), timestamp,
                entry.Name, checked((int)entry.Count)))
            .Concat(rareChanges.Select(change => new LootEventView(Guid.NewGuid(), timestamp,
                change.Name, change.Count))).ToArray();
        var accepted = observations.Where(IsAccepted).ToArray();
        var decisions = observations.Select(row => new LootTrackingDecision(row, null,
            IsAccepted(row) ? LootTrackingDecisionStatus.Pending : LootTrackingDecisionStatus.Rejected,
            row.RejectionReason ?? "companion-input")).ToList();
        for (var index = 0; index < events.Length; index++)
        {
            var change = events[index];
            decisions.Add(new(new LootObservation(index < reconciled.Count ? LootSource.Normal : LootSource.Rare,
                0, "", change.ItemName, change.Quantity, 0, 0, null, null),
                change.EventId, LootTrackingDecisionStatus.Counted, "companion-delta"));
        }
        return new FrameAnalysisResult(events, rawLines,
            accepted.Length == 0 ? 0 : accepted.Average(row => row.NameConfidence),
            ExactVariantName, prepared, nonBlank, ocrCalls, accepted.Length, _panelBounds)
        {
            FrameSize = frameSize,
            SlotRegions = _slotBounds,
            RarePanelRegion = _rarePanelBounds,
            RareBandRegion = _rareBandBounds,
            TextRecognitionBackend = _nameRecognizer.BackendName,
            TextRecognitionLanguage = _nameRecognizer.LanguageTag,
            Observations = observations,
            TrackingResult = new(events.Select(change => new TrackedLootEvent(change.EventId,
                change.DetectedAt, change.ItemName, change.Quantity)).ToArray(), decisions),
            SpotId = _spotLock.Spot?.Id,
        };
    }

    private static void ValidateBoundsInsideFrame(Mat frame, Rectangle bounds, string label)
    {
        if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > frame.Width ||
            bounds.Bottom > frame.Height || bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException(
                $"Der kalibrierte BDO-Companion-Bereich {label} {bounds} liegt nicht im erfassten Frame {frame.Width}x{frame.Height}.");
    }

    private static Rect ToOpenCvRect(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}

internal interface ILootFilterConfigurableAnalyzer
{
    void ConfigureLootFilter(bool includeEventLoot);
}

internal interface ICompanionReconciliation
{
    IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries);
    IReadOnlyList<CompanionRecognizedEntry> Complete();
    void Reset();
}

internal sealed class CompanionReconciliationAdapter : ICompanionReconciliation
{
    private readonly CompanionFrameReconciler _reconciler = new();
    public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries) => _reconciler.ProcessFrame(entries);
    public IReadOnlyList<CompanionRecognizedEntry> Complete() => _reconciler.Complete();
    public void Reset() => _reconciler.Reset();
}

internal interface ICompanionRareReconciliation
{
    IReadOnlyList<CompanionRareCountDelta> ProcessFrame(IReadOnlyList<CompanionRareRecognizedEntry> entries);
    IReadOnlyList<CompanionRareCountDelta> Complete();
    void Reset();
}

internal sealed class CompanionRareReconciliationAdapter(CompanionRareFrameReconciler reconciler) : ICompanionRareReconciliation
{
    public IReadOnlyList<CompanionRareCountDelta> ProcessFrame(IReadOnlyList<CompanionRareRecognizedEntry> entries) => reconciler.ProcessFrame(entries);
    public IReadOnlyList<CompanionRareCountDelta> Complete() => reconciler.Complete();
    public void Reset() => reconciler.Reset();
}

internal interface ICompanionBitmapDecoder
{
    Mat Decode(Bitmap bitmap);
}

internal sealed class CompanionBitmapDecoder : ICompanionBitmapDecoder
{
    public static CompanionBitmapDecoder Instance { get; } = new();

    private CompanionBitmapDecoder()
    {
    }

    public Mat Decode(Bitmap bitmap) => CompanionFrameDecoder.Decode(bitmap);
}

internal interface ICompanionPreparedRow : IDisposable
{
    int Y { get; }

    bool IsBlank { get; }

    int RecognizedTextWidth { get; }

    int TemplateQuantity { get; }

    int LeftmostQuantityX { get; }

    float QuantityScore { get; }

    float NameScale { get; }

    Mat? NameImage { get; }
}

internal interface ICompanionNormalRowPipeline : IDisposable
{
    ICompanionPreparedRow Process(
        Mat band,
        int y,
        float uiScale,
        int fontType,
        bool isHdr);
}

internal sealed class CompanionNormalRowPipeline : ICompanionNormalRowPipeline
{
    private readonly CompanionQuantityRecognizer _quantityRecognizer;
    private readonly CompanionNormalRowProcessor _processor;
    private bool _disposed;

    public CompanionNormalRowPipeline(CompanionQuantityRecognizer quantityRecognizer)
    {
        _quantityRecognizer = quantityRecognizer ??
            throw new ArgumentNullException(nameof(quantityRecognizer));
        _processor = new CompanionNormalRowProcessor(quantityRecognizer);
    }

    public ICompanionPreparedRow Process(
        Mat band,
        int y,
        float uiScale,
        int fontType,
        bool isHdr)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new CompanionPreparedRow(
            _processor.Process(band, y, uiScale, fontType, isHdr));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _quantityRecognizer.Dispose();
        _disposed = true;
    }

    private sealed class CompanionPreparedRow(CompanionNormalRowResult result) :
        ICompanionPreparedRow
    {
        public int Y => result.Y;

        public bool IsBlank => result.IsBlank;

        public int RecognizedTextWidth => result.RecognizedTextWidth;

        public int TemplateQuantity => result.TemplateQuantity;

        public int LeftmostQuantityX => result.LeftmostQuantityX;

        public float QuantityScore => result.QuantityScore;

        public float NameScale => result.NameScale;

        public Mat? NameImage => result.NameImage;

        public void Dispose() => result.Dispose();
    }
}

internal interface ICompanionRareRowPipeline : IDisposable
{
    ICompanionPreparedRow Process(
        Mat band,
        int y,
        float uiScale,
        int fontType,
        bool isHdr);
}

internal sealed class CompanionRareRowPipeline : ICompanionRareRowPipeline
{
    private readonly CompanionRareRowProcessor _processor = new();
    private bool _disposed;

    public ICompanionPreparedRow Process(
        Mat band,
        int y,
        float uiScale,
        int fontType,
        bool isHdr)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new CompanionPreparedRareRow(
            _processor.Process(band, y, uiScale, fontType, isHdr));
    }

    public void Dispose() => _disposed = true;

    private sealed class CompanionPreparedRareRow(CompanionRareRowResult result) :
        ICompanionPreparedRow
    {
        public int Y => result.Y;

        public bool IsBlank => result.IsBlank;

        public int RecognizedTextWidth => result.RecognizedTextWidth;

        public int TemplateQuantity => result.TemplateQuantity;

        public int LeftmostQuantityX => result.LeftmostQuantityX;

        public float QuantityScore => result.QuantityScore;

        public float NameScale => result.NameScale;

        public Mat? NameImage => result.NameImage;

        public void Dispose() => result.Dispose();
    }
}

internal interface ICompanionNameRecognizer
{
    string BackendName { get; }

    string? LanguageTag { get; }

    CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken);
}

internal sealed class CompanionNameRecognizer(CompanionWindowsOcrRecognizer recognizer) :
    ICompanionNameRecognizer
{
    private readonly CompanionWindowsOcrRecognizer _recognizer = recognizer ??
        throw new ArgumentNullException(nameof(recognizer));

    public string BackendName => _recognizer.BackendName;

    public string LanguageTag => _recognizer.LanguageTag;

    public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken) =>
        _recognizer.Recognize(image, cancellationToken);
}
