using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using BdoGrindTracker.App.Diagnostics;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Companion baseline with optional recovery and per-spot single-drop quantity bounds.</summary>
internal sealed class CompanionLootFrameAnalyzer : ILootFrameAnalyzer
{
    internal const string ExactVariantName = "bdo-companion-0.7.4";
    internal const string RecoveryVariantName = "companion-0.7.4+normal-recovery-v1";
    private CompanionCalibration _calibration;
    private readonly CompanionItemMatcher _itemMatcher;
    private readonly ICompanionBitmapDecoder _frameDecoder;
    private readonly ICompanionNormalRowPipeline _rowPipeline;
    private ICompanionRareRowPipeline? _rareRowPipeline;
    private readonly ICompanionNameRecognizer _nameRecognizer;
    private readonly INormalLootRecovery? _normalRecovery;
    private readonly ILootRowReview? _rowReview;
    private readonly TrashQuantityAnomalyDetector? _trashQuantityAnomalies;
    private readonly LootPanelCaptureGuard? _captureGuard;
    private readonly Action<string>? _configureGameLanguage;
    private readonly Func<string?, string, DropQuantityBounds?> _quantityBoundsResolver;
    private readonly ICompanionReconciliation _reconciliation;
    private ICompanionRareReconciliation? _rareReconciliation;
    private readonly CompanionLootLedger _ledger = new();
    private readonly LifetimeLootProjectionComposer _projectionComposer = new();
    private string? _parsingSpotId;
    private readonly AutomaticLootSpotLock _spotLock = new();
    private Rectangle _panelBounds;
    private Rectangle[] _slotBounds;
    private Rectangle? _rarePanelBounds;
    private Rectangle? _rareBandBounds;
    private bool _disposed;
    private int _recoveryCursor;
    private readonly NormalLootAlignmentReview _alignmentReview = new();
    private readonly NormalLootAppearanceTracker _appearanceTracker = new();
    private (bool IsHdr, bool IsToneMapped)? _appearanceRepresentation;
    private readonly NormalLootOccupancyTracker _occupancyTracker = new();
    private (bool IsHdr, bool IsToneMapped)? _occupancyRepresentation;

    public CompanionLootFrameAnalyzer(
        CompanionCalibration calibration,
        CompanionItemMatcher itemMatcher,
        ICompanionNormalRowPipeline rowPipeline,
        ICompanionNameRecognizer nameRecognizer,
        ICompanionReconciliation? reconciliation = null,
        ICompanionBitmapDecoder? frameDecoder = null,
        ICompanionRareRowPipeline? rareRowPipeline = null,
        ICompanionRareReconciliation? rareReconciliation = null,
        INormalLootRecovery? normalRecovery = null,
        Func<string?, string, DropQuantityBounds?>? quantityBoundsResolver = null,
        LootPanelCaptureGuard? captureGuard = null,
        Action<string>? configureGameLanguage = null,
        ILootRowReview? rowReview = null,
        bool reviewTrashQuantityAnomalies = true)
    {
        _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        _itemMatcher = itemMatcher ?? throw new ArgumentNullException(nameof(itemMatcher));
        _rowPipeline = rowPipeline ?? throw new ArgumentNullException(nameof(rowPipeline));
        _nameRecognizer = nameRecognizer ?? throw new ArgumentNullException(nameof(nameRecognizer));
        _normalRecovery = normalRecovery;
        _rowReview = rowReview;
        _trashQuantityAnomalies = reviewTrashQuantityAnomalies ? new TrashQuantityAnomalyDetector() : null;
        _captureGuard = captureGuard;
        _configureGameLanguage = configureGameLanguage;
        _quantityBoundsResolver = quantityBoundsResolver ?? DropQuantityCatalog.GetBounds;
        _normalRecovery?.ConfigureQuantityBounds(name => _quantityBoundsResolver(_spotLock.Spot?.Id, name));
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

    public bool IsAvailable => !_disposed && _captureGuard?.Error is null;
    public bool RequiresLootPanel => true;
    public string Status => _disposed ? "BDO-Companion-Erkennung wurde beendet."
        : _captureGuard?.Error ?? (_spotLock.Spot is { } spot ? $"Bereit: Companion · {spot.DisplayName} (Lootfilter)."
        : "Bereit: Companion · Spot wird aus Trashloot erkannt.");

    public void ValidateCaptureSetup(System.Drawing.Size frameSize) =>
        RefreshCapturePosition(frameSize, DateTimeOffset.UtcNow, force: true);

    private void RefreshCapturePosition(System.Drawing.Size frameSize, DateTimeOffset now, bool force = false)
    {
        var current = _captureGuard?.Validate(frameSize, now, force);
        if (current is null || current == _calibration) return;

        // Executed by the serial capture producer before any crop or row review,
        // or during startup while capture is stopped. Build all geometry first.
        var panel = CompanionNormalLootGeometry.CalculatePanelBounds(current);
        var slots = CompanionNormalLootGeometry.CalculateSlotCrops(current).ToArray();
        Rectangle? rarePanel = current.HasRareLootAnchor
            ? CompanionNormalLootGeometry.CalculateRarePanelBounds(current) : null;
        Rectangle? rareBand = current.HasRareLootAnchor
            ? CompanionNormalLootGeometry.CalculateRareBandCrop(current) : null;
        if (current.HasRareLootAnchor && _rareRowPipeline is null)
        {
            _rareRowPipeline = new CompanionRareRowPipeline();
            _rareReconciliation ??= new CompanionRareReconciliationAdapter(
                new CompanionRareFrameReconciler(_itemMatcher.CatalogEntries, _ledger));
        }

        _calibration = current;
        _panelBounds = panel;
        _slotBounds = slots;
        _rarePanelBounds = rarePanel;
        _rareBandBounds = rareBand;
        _alignmentReview.Reset();
        _appearanceTracker.Reset();
        _recoveryCursor = 0;
        _occupancyTracker.Reset();
        // Preserve pending drops, event IDs, spot lock and the shared ledger.
        // Resetting reconciliation would count the still-visible log again.
    }

    public void ConfigureGameLanguage(string language)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _configureGameLanguage?.Invoke(language);
        _rowReview?.ConfigureLanguage(_nameRecognizer.LanguageTag ?? language);
        _alignmentReview.Reset();
        _appearanceTracker.Reset();
        _occupancyTracker.Reset();
    }

    public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt,
        CancellationToken cancellationToken) => AnalyzeAsync(frame, capturedAt, false, cancellationToken);

    public Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt,
        bool isHdr, CancellationToken cancellationToken) =>
        AnalyzeAsync(frame, capturedAt, isHdr, false, cancellationToken);

    public async Task<FrameAnalysisResult> AnalyzeAsync(Bitmap frame, DateTimeOffset capturedAt,
        bool isHdr, bool isToneMapped, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        RefreshCapturePosition(frame.Size, capturedAt);
        if (UsesVisualAppearance && _appearanceRepresentation != (isHdr, isToneMapped))
        {
            _appearanceTracker.Reset();
            _appearanceRepresentation = (isHdr, isToneMapped);
        }
        if (UsesOccupancyEvidence && _occupancyRepresentation != (isHdr, isToneMapped))
        {
            _occupancyTracker.Reset();
            _occupancyRepresentation = (isHdr, isToneMapped);
        }
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
                    _calibration.UiScale, (int)_calibration.FontType, isHdr, isToneMapped));
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
            // Tone-mapped rows read the full suffix with Windows OCR instead of
            // digit templates. Missing templates therefore say nothing about
            // whether a leading row is occupied; its image blank gate decides.
            var first = isToneMapped || _reconciliation.UsesRawText ? 0 :
                CalculateRetainedStartIndex(preparedRows.Select(row => row.TemplateQuantity).ToArray());
            var normal = new List<LootObservation>();
            var rare = new List<LootObservation>();
            var rawLines = new List<string>();
            var primaryQuantityReads = _rowReview is null ? null : new Dictionary<int, List<PrimaryLootQuantityRead>>();
            var ocrCalls = 0;
            for (var index = first; index < preparedRows.Count; index++)
                Observe(preparedRows[index], LootSource.Normal, preparedRows.Count - 1 - index, normal);
            if (rareRow is not null) Observe(rareRow, LootSource.Rare, 0, rare);

            // Legacy parity selects from the first read. Temporal tracking waits
            // for distinct confirmed events; until then shared items use broad bounds.
            if (!UsesTemporalTracking)
                _spotLock.Observe(normal.AsEnumerable().Reverse().Where(IsAccepted).Select(row => row.ItemName!));
            normal = normal.Select(ApplyQuantityPolicy).ToList();
            rare = rare.Select(ApplyQuantityPolicy).ToList();

            var recoveryDiagnostics = NormalLootRecoveryDiagnostics.Empty;
            if (_normalRecovery is not null)
            {
                // Baseline trash takes priority over a rescued row when selecting
                // the spot. Finish every original read before spending time on retries.
                if (!UsesTemporalTracking)
                    _spotLock.Observe(normal.AsEnumerable().Reverse().Where(IsAccepted)
                        .Select(row => row.ItemName!));
                var baselineByY = normal.ToDictionary(row => row.NativeY!.Value);
                var cursor = _recoveryCursor;
                _recoveryCursor = (_recoveryCursor + 1) % Math.Max(preparedRows.Count, 1);
                var candidates = preparedRows.Select((row, index) =>
                    (Row: row, Index: index, Baseline: baselineByY.GetValueOrDefault(row.Y)))
                    .Where(candidate => candidate.Baseline is not { Quantity: not null } original ||
                        !IsAccepted(original))
                    .OrderBy(candidate => candidate.Baseline is { } original && IsAccepted(original)
                        ? 0 : candidate.Baseline is not null ? 1 : !candidate.Row.IsBlank ? 2 : 3)
                    .ThenBy(candidate => (candidate.Index - cursor + preparedRows.Count) % preparedRows.Count);
                var budget = new NormalLootRecoveryBudget();
                var quantitiesRecovered = 0;
                var rowsRecovered = 0;
                var rowsAttempted = 0;
                foreach (var candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!budget.CanContinue) break;
                    rowsAttempted++;
                    var bounds = _slotBounds.Single(bounds => bounds.Top - _panelBounds.Top == candidate.Row.Y);
                    using var originalBand = new Mat(panel, new Rect(bounds.Left - _panelBounds.Left,
                        candidate.Row.Y, bounds.Width, bounds.Height));
                    var recovered = _normalRecovery.Recover(originalBand, candidate.Row, candidate.Baseline,
                        preparedRows.Count - 1 - candidate.Index, _calibration.UiScale, budget, cancellationToken,
                        (reading, scale) => RememberPrimaryRead(candidate.Row.Y, reading, scale));
                    if (recovered is null || !IsAccepted(recovered)) continue;
                    recovered = ApplyQuantityPolicy(recovered);
                    if (candidate.Baseline is { } baseline && IsAccepted(baseline))
                    {
                        // Recovery is allowed to fill only the missing quantity of
                        // an accepted row, never change its identity or existing value.
                        if (baseline.Quantity.HasValue || recovered.Quantity is not > 0 ||
                            !string.Equals(baseline.ItemName, recovered.ItemName, StringComparison.Ordinal))
                            continue;
                        recovered = baseline with { Quantity = recovered.Quantity };
                        quantitiesRecovered++;
                    }
                    else
                    {
                        recovered = recovered with { NativeY = candidate.Row.Y,
                            Slot = preparedRows.Count - 1 - candidate.Index, Source = LootSource.Normal };
                        rowsRecovered++;
                    }
                    if (candidate.Baseline is not null) normal.Remove(candidate.Baseline);
                    normal.Add(recovered);
                }
                ocrCalls += budget.OcrCalls;
                recoveryDiagnostics = new(rowsAttempted, budget.OcrCalls,
                    quantitiesRecovered, rowsRecovered, budget.Errors);
            }

            var reviewDiagnostics = new List<LootRowReviewDiagnostics>();
            if (_rowReview is not null)
            {
                // Workers read only immutable per-frame policy and their own screenshot crop.
                // They never see the ledger, reconciliation state or another frame.
                var spotId = _spotLock.Spot?.Id;
                // The real lock can have been selected on an earlier frame.
                var allowed = _itemMatcher.CatalogEntries.Select(item => item.Name)
                    .Where(_spotLock.Allows).ToHashSet(StringComparer.Ordinal);
                var byY = normal.ToDictionary(row => row.NativeY!.Value);
                var jobs = new List<Task<LootRowReviewResult>>();
                foreach (var (row, index) in preparedRows.Select((row, index) => (row, index)))
                {
                    var baseline = byY.GetValueOrDefault(row.Y);
                    if (baseline is null && row.IsBlank) continue;
                    var bounds = _slotBounds.Single(bounds => bounds.Top - _panelBounds.Top == row.Y);
                    jobs.Add(ReviewBand(new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
                        row, baseline, LootSource.Normal, preparedRows.Count - 1 - index));
                }
                if (rareRow is not null && (!rareRow.IsBlank || rare.Count > 0) && _rareBandBounds is { } reviewRareBounds)
                    jobs.Add(ReviewBand(ToOpenCvRect(reviewRareBounds), rareRow, rare.SingleOrDefault(), LootSource.Rare, 0));

                // Exactly one finalized observation per source/Y reaches the existing counter.
                // Awaiting all jobs also keeps frame pixels alive and prevents late-session writes.
                var reviewed = await Task.WhenAll(jobs).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var result in reviewed)
                {
                    if (result.Diagnostics is { } diagnostic) reviewDiagnostics.Add(diagnostic);
                    if (result.Observation is not { } observation) continue;
                    var target = observation.Source == LootSource.Normal ? normal : rare;
                    target.RemoveAll(row => row.NativeY == observation.NativeY);
                    target.Add(observation);
                }
                ocrCalls += reviewDiagnostics.Sum(diagnostic => diagnostic.Readings.Count);

                async Task<LootRowReviewResult> ReviewBand(Rect bounds, ICompanionPreparedRow row,
                    LootObservation? baseline, LootSource source, int slot)
                {
                    using var originalBand = new Mat(decodedFrame, bounds);
                    var result = await _rowReview.ReviewAsync(originalBand,
                        new(baseline, source, slot, row.Y, row.TemplateQuantity, row.QuantityScore,
                            name => _quantityBoundsResolver(spotId, name), allowed.Contains)
                        {
                            UiScale = _calibration.UiScale,
                            QuantityAnomaly = baseline is null ? null : _trashQuantityAnomalies?.Assess(spotId, baseline),
                            PrimaryQuantityReads = source == LootSource.Normal &&
                                primaryQuantityReads?.TryGetValue(row.Y, out var reads) == true
                                    ? Array.AsReadOnly(reads.ToArray()) : [],
                        }, cancellationToken)
                        .ConfigureAwait(false);
                    return result with { Observation = result.Observation is { } revised
                        ? revised with { Source = source, Slot = slot, NativeY = row.Y } : null };
                }
            }

            if (_rowReview is not null && _reconciliation.TracksRows && !_reconciliation.UsesRawText)
            {
                var allowed = _itemMatcher.CatalogEntries.Select(item => item.Name)
                    .Where(_spotLock.Allows).ToHashSet(StringComparer.Ordinal);
                var plan = _alignmentReview.Prepare(normal.Where(r => IsAccepted(r) && allowed.Contains(r.ItemName!))
                    .ToArray(), capturedAt, preparedRows.Count);
                if (plan is not null)
                {
                    var spotId = _spotLock.Spot?.Id;
                    var probes = await Task.WhenAll(plan.Slots.Where(slot => normal.All(r => r.Slot != slot))
                        .Select(async slot =>
                        {
                            var row = preparedRows[preparedRows.Count - 1 - slot];
                            var bounds = _slotBounds.Single(b => b.Top - _panelBounds.Top == row.Y);
                            using var band = new Mat(decodedFrame, ToOpenCvRect(bounds));
                            return await _rowReview.ReviewAsync(band,
                                new(null, LootSource.Normal, slot, row.Y, -1, 0,
                                    name => _quantityBoundsResolver(spotId, name), allowed.Contains)
                                { UiScale = _calibration.UiScale, ReviewMissingAlignmentAnchor = true }, cancellationToken)
                                .ConfigureAwait(false);
                        })).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    var anchors = plan.Resolve(probes.Select(p => p.Observation));
                    normal.AddRange(anchors);
                    foreach (var probe in probes)
                        if (probe.Diagnostics is { } diagnostic)
                        {
                            var retained = anchors.Any(a => a.Slot == probe.Observation?.Slot);
                            reviewDiagnostics.Add(probe.Observation is { IsAlignmentAnchor: true }
                                ? diagnostic with { Outcome = retained ? "alignment-anchor-retained" : "alignment-anchor-discarded",
                                    After = anchors.FirstOrDefault(a => a.Slot == probe.Observation?.Slot) } : diagnostic);
                            ocrCalls += diagnostic.Readings.Count;
                        }
                }
            }

            normal.Sort((left, right) => right.NativeY!.Value.CompareTo(left.NativeY!.Value));
            // Apply the existing confirmed pool without rematching rejected names
            // into that pool. Temporal spot evidence is updated only after counting.
            if (!UsesTemporalTracking)
                _spotLock.Observe(normal.Where(IsAccepted).Select(row => row.ItemName!));
            var observations = normal.Concat(rare).Select(row =>
                IsAccepted(row) && !_spotLock.Allows(row.ItemName!)
                    ? row with { RejectionReason = AutomaticLootSpotLock.OutsideSpotPoolReason }
                    : IsAccepted(row)
                        ? ApplyQuantityPolicy(row)
                        : row).ToArray();
            if (UsesVisualAppearance)
            {
                var relativeSlots = _slotBounds.OrderByDescending(bounds => bounds.Top)
                    .Select(bounds => new Rectangle(bounds.Left - _panelBounds.Left,
                        bounds.Top - _panelBounds.Top, bounds.Width, bounds.Height)).ToArray();
                // Observe every calibrated slot, including frames where OCR read
                // nothing. Only final accepted normal rows receive visual evidence.
                observations = _appearanceTracker.Observe(panel, relativeSlots,
                    observations.Where(row => row.Source == LootSource.Normal).ToArray(), capturedAt,
                    _calibration.UiScale).Concat(observations.Where(row => row.Source == LootSource.Rare)).ToArray();
            }
            if (UsesOccupancyEvidence)
            {
                var relativeSlots = _slotBounds.OrderByDescending(bounds => bounds.Top)
                    .Select(bounds => new Rectangle(bounds.Left - _panelBounds.Left,
                        bounds.Top - _panelBounds.Top, bounds.Width, bounds.Height)).ToArray();
                // Only annotate final accepted reads. The OCR fields and the
                // historical appearance/identity rules remain independent.
                observations = _occupancyTracker.Observe(panel, relativeSlots,
                    observations.Where(row => row.Source == LootSource.Normal).ToArray(), capturedAt,
                    _calibration.UiScale).Concat(observations.Where(row => row.Source == LootSource.Rare)).ToArray();
            }
            var entries = observations.Where(row => row.Source == LootSource.Normal && IsAccepted(row))
                .Select(row => new CompanionRecognizedEntry(row.ItemName!,
                    unchecked((uint)(row.Quantity ?? -1)), row.NativeY!.Value)
                    { QuantityBounds = row.QuantityBounds, Slot = _reconciliation.TracksRows ? row.Slot : null,
                        IsAlignmentAnchor = row.IsAlignmentAnchor, AlignmentPreviousSlot = row.AlignmentPreviousSlot,
                        NameConfidence = UsesTemporalTracking ? row.NameConfidence : 1,
                        RawText = UsesTemporalTracking ? row.RawText : null,
                        AppearanceEvidence = UsesVisualAppearance ? row.AppearanceEvidence : null }).ToArray();
            var rareEntries = observations.Where(row => row.Source == LootSource.Rare && IsAccepted(row))
                .Select(row => new CompanionRareRecognizedEntry(row.ItemName!,
                    row.UsesImplicitUnitQuantity && row.QuantityBounds is not null ? -1 : row.Quantity ?? -1,
                    row.NativeY!.Value) { QuantityBounds = row.QuantityBounds }).ToArray();

            // Ordering and shared ledger are important for signed rare-loot corrections.
            RefreshParsingContext();
            var reconciled = _reconciliation.UsesRawText
                ? _reconciliation.ProcessObservations(observations.Where(row => row.Source == LootSource.Normal).ToArray(), capturedAt)
                : _reconciliation.ProcessFrame(entries, capturedAt);
            ObserveCountEvidence(reconciled);
            foreach (var entry in reconciled) _ledger.ApplyDelta(entry.Name, entry.QuantityDelta ?? (long)entry.Count);
            var rareChanges = _rareReconciliation?.ProcessFrame(rareEntries) ?? [];
            return CreateResult(reconciled, rareChanges, capturedAt, frame.Size,
                rawLines, observations, preparedRows.Count + (rareRow is null ? 0 : 1),
                preparedRows.Count(row => !row.IsBlank) + (rareRow is { IsBlank: false } ? 1 : 0), ocrCalls,
                isToneMapped) with
            {
                Recovery = recoveryDiagnostics,
                RowReviews = reviewDiagnostics,
            };

            void Observe(ICompanionPreparedRow row, LootSource source, int slot, List<LootObservation> target)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.IsBlank || row.NameImage is null) return;
                ocrCalls++;
                var ocr = _nameRecognizer.Recognize(row.NameImage, cancellationToken);
                if (source == LootSource.Normal) RememberPrimaryRead(row.Y, ocr, row.NameScale, row.NormalizedNameTop);
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
                        isRare, row.RecognizedTextWidth, name =>
                        {
                            if (!_itemMatcher.TryMatch(name, 1, isRare, out var candidate) || candidate is null ||
                                _quantityBoundsResolver(_spotLock.Spot?.Id, candidate.CanonicalName)?.IsFixedUnit != true)
                                return false;
                            match = candidate;
                            return true;
                        });
                    if (!CompanionTextPipeline.PassesExpectedWidth(text.Value,
                        row.RecognizedTextWidth, row.LeftmostQuantityX, row.NameScale))
                        rejection = "ocr-width-or-empty";
                    else if (match is null && (!_itemMatcher.TryMatch(text.Value.Name, text.Value.Quantity, isRare, out match) || match is null))
                        rejection = "native-catalog-miss";
                }
                target.Add(new LootObservation(source, slot, ocr.Text, match?.CanonicalName,
                    text is null || text.Value.Quantity <= 0 ? null : text.Value.Quantity,
                    match is null ? 0 : Math.Clamp(1 - match.NormalizedDistance, 0, 1),
                    0, null, rejection)
                {
                    NativeY = row.Y,
                    QuantityBounds = match is null ? null : _quantityBoundsResolver(_spotLock.Spot?.Id, match.CanonicalName),
                    UsesImplicitUnitQuantity = isRare && text is { HasParsedOcrQuantity: false, UsesFixedUnitQuantity: false },
                    UsesFixedUnitQuantity = text is { UsesFixedUnitQuantity: true },
                });
            }

            void RememberPrimaryRead(int y, CompanionOcrResult reading, float scale, float normalizedNameTop = 0)
            {
                if (primaryQuantityReads is null || reading.Words.Count == 0 || !float.IsFinite(scale) || scale <= 0 ||
                    !float.IsFinite(normalizedNameTop) || normalizedNameTop < 0 || normalizedNameTop >= 100)
                    return;
                if (!primaryQuantityReads.TryGetValue(y, out var reads))
                    primaryQuantityReads.Add(y, reads = []);
                // One initial name read and the existing two recovery variants;
                // no image or cross-frame history is retained with these hints.
                if (reads.Count < 3) reads.Add(new PrimaryLootQuantityRead(reading, scale, normalizedNameTop));
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
        _alignmentReview.Reset();
        _appearanceTracker.Reset();
        RefreshParsingContext();
        var reconciled = _reconciliation.Complete();
        ObserveCountEvidence(reconciled);
        foreach (var entry in reconciled) _ledger.ApplyDelta(entry.Name, entry.QuantityDelta ?? (long)entry.Count);
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
        _projectionComposer.Reset();
        _parsingSpotId = null;
        _spotLock.Reset();
        _recoveryCursor = 0;
        _alignmentReview.Reset();
        _appearanceTracker.Reset();
        _appearanceRepresentation = null;
        _occupancyTracker.Reset();
        _occupancyRepresentation = null;
        _trashQuantityAnomalies?.Reset();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _rowPipeline.Dispose();
        _rareRowPipeline?.Dispose();
        _rowReview?.Dispose();
        _appearanceTracker.Dispose();
        _occupancyTracker.Dispose();
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

    private bool UsesTemporalTracking => UsesVisualAppearance ||
        _reconciliation.AlgorithmName == TemporalLootReconciler.LegacyAlgorithmName ||
        _reconciliation.AlgorithmName == LifetimeLootReconciler.AlgorithmName || _reconciliation.UsesRawText;

    private void RefreshParsingContext()
    {
        if (_reconciliation is not LifetimeNormalReconciliationAdapter { UsesRawText: true } lifetime ||
            _parsingSpotId == _spotLock.Spot?.Id) return;
        var current = lifetime.ParsingContext!;
        var allowed = current.Catalog.Where(item => _spotLock.Allows(item.Name))
            .Select(item => new LifetimeParsingCatalogEntry(item.Name, item.Aliases,
                _quantityBoundsResolver(_spotLock.Spot?.Id, item.Name)?.IsFixedUnit == true)).ToArray();
        lifetime.UpdateParsingContext(new(checked(current.Revision + 1), allowed));
        _parsingSpotId = _spotLock.Spot?.Id;
    }

    private bool UsesVisualAppearance => _reconciliation.AlgorithmName == TemporalLootReconciler.AlgorithmName;

    private bool UsesOccupancyEvidence => _reconciliation.AlgorithmName == LifetimeLootReconciler.VisualSlotAlgorithmName;

    private void ObserveCountEvidence(IReadOnlyList<CompanionRecognizedEntry> reconciled)
    {
        if (_reconciliation.Projection is { } projection)
        {
            LifetimePolicyProjection.Apply(projection, _spotLock, _trashQuantityAnomalies, _quantityBoundsResolver);
            return;
        }
        if (UsesTemporalTracking) _spotLock.ObserveConfirmed(reconciled);
        _trashQuantityAnomalies?.ObserveCountedDrops(_spotLock.Spot?.Id, reconciled);
    }

    private LootObservation ApplyQuantityPolicy(LootObservation row)
    {
        if (!IsAccepted(row)) return row;
        var bounds = _quantityBoundsResolver(_spotLock.Spot?.Id, row.ItemName!);
        return bounds?.IsFixedUnit == true
            ? row with { Quantity = 1, QuantityBounds = bounds, UsesFixedUnitQuantity = true, UsesImplicitUnitQuantity = false }
            // Preserve the provenance of a unit inferred before this frame's
            // spot lock, even if the item is subsequently rejected by its pool.
            : row with { QuantityBounds = bounds ?? (row.UsesFixedUnitQuantity ? row.QuantityBounds : null) };
    }

    private FrameAnalysisResult CreateResult(IReadOnlyList<CompanionRecognizedEntry> reconciled,
        IReadOnlyList<CompanionRareCountDelta> rareChanges, DateTimeOffset timestamp,
        System.Drawing.Size frameSize, IReadOnlyList<string> rawLines,
        IReadOnlyList<LootObservation> observations, int prepared, int nonBlank, int ocrCalls,
        bool isToneMapped = false)
    {
        var projectionResult = _reconciliation.Projection is { } normalProjection
            ? _projectionComposer.Combine(normalProjection, rareChanges, timestamp) : default;
        var events = projectionResult.Projection is not null
            ? projectionResult.Events.Select(change => new LootEventView(change.EventId, change.DetectedAt, change.ItemName, change.Quantity)).ToArray()
            : reconciled.Select(entry => new LootEventView(entry.EventId ?? Guid.NewGuid(), entry.DetectedAt ?? timestamp,
                entry.Name, entry.QuantityDelta ?? checked((int)entry.Count))
                { Revision = entry.Revision, TotalDropQuantity = entry.TotalDropQuantity })
            .Concat(rareChanges.Select(change => new LootEventView(Guid.NewGuid(), timestamp,
                change.Name, change.Count))).ToArray();
        var accepted = observations.Where(IsAccepted).ToArray();
        var decisions = observations.Select(row => new LootTrackingDecision(row, null,
            IsAccepted(row) ? LootTrackingDecisionStatus.Pending : LootTrackingDecisionStatus.Rejected,
            row.RejectionReason ?? (row.UsesFixedUnitQuantity ? LootDiagnosticFormat.FixedUnitQuantityReason
                : !row.UsesImplicitUnitQuantity && row.Quantity is > 0 &&
                    row.QuantityBounds is { } bounds && row.Quantity < bounds.Minimum
                ? LootDiagnosticFormat.MinimumQuantityClampReason
                : row.QuantityBounds?.Maximum is { } maximum && row.Quantity > maximum
                ? LootDiagnosticFormat.MaximumQuantityClampReason : "companion-input"))).ToList();
        for (var index = 0; index < events.Length; index++)
        {
            var change = events[index];
            var source = projectionResult.Projection is not null
                ? _reconciliation.Projection!.Totals.GetValueOrDefault(change.ItemName) > 0 ? LootSource.Normal : LootSource.Rare
                : index < reconciled.Count ? LootSource.Normal : LootSource.Rare;
            decisions.Add(new(new LootObservation(source,
                0, "", change.ItemName, change.Quantity, 0, 0, null, null),
                change.EventId, LootTrackingDecisionStatus.Counted,
                projectionResult.Projection is not null ? "lifetime-projection-change" :
                change.Revision > 0 ? "drop-quantity-correction" :
                index < reconciled.Count && reconciled[index].IsMinimumQuantityEstimate
                    ? LootDiagnosticFormat.MinimumQuantityEstimateReason : "companion-delta") { TrackId = change.EventId });
        }
        return new FrameAnalysisResult(events, rawLines,
            accepted.Length == 0 ? 0 : accepted.Average(row => row.NameConfidence),
            (_normalRecovery is null ? ExactVariantName : RecoveryVariantName) +
                (isToneMapped ? "+tone-mapped-normal-v1" : string.Empty) +
                (_rowReview is null ? string.Empty : "+paddle-review-v2" +
                    (_reconciliation.UsesRawText ? string.Empty : "+alignment-review-v1")) +
                (_rowReview is not null && _trashQuantityAnomalies is not null ? "+trash-quantity-anomaly-v1" : string.Empty) +
                (UsesTemporalTracking ? "+" + _reconciliation.AlgorithmName :
                    _reconciliation.TracksRows ? "+row-tracks-v1" : string.Empty) +
                (UsesVisualAppearance ? "+" + LootDiagnosticFormat.VisualAppearanceVariantName : string.Empty) +
                (UsesOccupancyEvidence ? "+" + LootDiagnosticFormat.VisualOccupancyVariantName : string.Empty),
            prepared, nonBlank, ocrCalls, accepted.Length, _panelBounds)
        {
            LootProjection = projectionResult.Projection,
            LifetimeParsingContext = _reconciliation.ParsingContext,
            FrameSize = frameSize,
            SlotRegions = _slotBounds,
            RarePanelRegion = _rarePanelBounds,
            RareBandRegion = _rareBandBounds,
            TextRecognitionBackend = _nameRecognizer.BackendName,
            TextRecognitionLanguage = _nameRecognizer.LanguageTag,
            Observations = observations,
            TrackingResult = new(events.Select(change => new TrackedLootEvent(change.EventId,
                change.DetectedAt, change.ItemName, change.Quantity)
                { Revision = change.Revision, TotalDropQuantity = change.TotalDropQuantity }).ToArray(), decisions)
                { NormalCaptureIndex = _reconciliation.CaptureIndex, NormalReconciliation = _reconciliation.LastTrace,
                    LootProjection = projectionResult.Projection,
                    LifetimeParsingContext = _reconciliation.ParsingContext },
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

internal interface ICompanionReconciliation
{
    LifetimeSnapshot? Projection => null;
    LifetimeParsingContext? ParsingContext => null;
    bool UsesRawText => false;
    string? AlgorithmName => null;
    bool TracksRows => false;
    long? CaptureIndex => null;
    IReadOnlyList<NormalLootReconciliationTrace> LastTrace => [];
    IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries);
    IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries,
        DateTimeOffset capturedAt) => ProcessFrame(entries);
    IReadOnlyList<CompanionRecognizedEntry> ProcessObservations(IReadOnlyList<LootObservation> observations,
        DateTimeOffset capturedAt) => throw new NotSupportedException("This counter does not process raw observations.");
    IReadOnlyList<CompanionRecognizedEntry> Complete();
    void Reset();
}

/// <summary>Normal tracking with capture-time evidence; the legacy adapter stays available for parity replay.</summary>
internal sealed class TemporalNormalReconciliationAdapter(
    IReadOnlyDictionary<string, uint>? minimumQuantities = null, bool legacyMode = false) : ICompanionReconciliation
{
    private readonly TemporalLootReconciler _reconciler = new(minimumQuantities, legacyMode);
    public string AlgorithmName => legacyMode ? TemporalLootReconciler.LegacyAlgorithmName : TemporalLootReconciler.AlgorithmName;
    public bool TracksRows => true;
    public long? CaptureIndex => _reconciler.CaptureIndex;
    public IReadOnlyList<NormalLootReconciliationTrace> LastTrace => _reconciler.LastTrace;
    public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries) =>
        throw new InvalidOperationException("Temporal reconciliation requires a capture timestamp.");
    public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries,
        DateTimeOffset capturedAt) => _reconciler.ProcessFrame(entries, capturedAt);
    public IReadOnlyList<CompanionRecognizedEntry> Complete() => _reconciler.Complete();
    public void Reset() => _reconciler.Reset();
}

internal sealed class CompanionReconciliationAdapter(
    IReadOnlyDictionary<string, uint>? minimumQuantities = null, bool trackRows = false) : ICompanionReconciliation
{
    public bool TracksRows => trackRows;
    private readonly CompanionFrameReconciler _reconciler = new(minimumQuantities, trackRows);
    public long? CaptureIndex => trackRows ? _reconciler.CaptureIndex : null;
    public IReadOnlyList<NormalLootReconciliationTrace> LastTrace => _reconciler.LastTrace;
    public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries) => _reconciler.ProcessFrame(entries);
    public IReadOnlyList<CompanionRecognizedEntry> ProcessFrame(IReadOnlyList<CompanionRecognizedEntry> entries,
        DateTimeOffset capturedAt) => _reconciler.ProcessFrame(entries, capturedAt);
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

    // Existing adapters/fakes and full-height recovery images retain Y=0.
    float NormalizedNameTop => 0;

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

    ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType,
        bool isHdr, bool isToneMapped) => Process(band, y, uiScale, fontType, isHdr);
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

    public ICompanionPreparedRow Process(Mat band, int y, float uiScale, int fontType,
        bool isHdr, bool isToneMapped)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return isToneMapped
            ? new CompanionPreparedRow(ToneMappedNormalRowProcessor.Process(band, y))
            : Process(band, y, uiScale, fontType, isHdr);
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

        public float NormalizedNameTop => result.NormalizedNameTop;

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
    private CompanionWindowsOcrRecognizer _recognizer = recognizer ??
        throw new ArgumentNullException(nameof(recognizer));

    public string BackendName => _recognizer.BackendName;

    public string LanguageTag => _recognizer.LanguageTag;

    public void SetRecognizer(CompanionWindowsOcrRecognizer recognizer) =>
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));

    public CompanionOcrResult Recognize(Mat image, CancellationToken cancellationToken) =>
        _recognizer.Recognize(image, cancellationToken);
}
