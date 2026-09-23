using System.Diagnostics;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

internal sealed record LootRowReviewInput(LootObservation? Baseline, LootSource Source, int Slot,
    int NativeY, int TemplateQuantity, float TemplateScore,
    Func<string, DropQuantityBounds?> Bounds, Func<string, bool> Allows)
{
    public double UiScale { get; init; } = 1;
    public IReadOnlyList<PrimaryLootQuantityRead> PrimaryQuantityReads { get; init; } = [];
    public bool ReviewMissingAlignmentAnchor { get; init; }
    public bool ApplyNormalSafetyRules { get; init; }
    public TrashQuantityAnomaly? QuantityAnomaly { get; init; }
}

internal sealed record PrimaryLootQuantityRead(CompanionOcrResult Reading, float NameScale, float NormalizedNameTop = 0);

internal sealed record LootRowReviewReading(string Variant, string Text, double Confidence,
    string? ItemName, int? Quantity)
{
    public double? QuantityConfidence { get; init; }
    public double? NameConfidence { get; init; }
    public string? Error { get; init; }
}

internal sealed record LootRowReviewDiagnostics(LootSource Source, int NativeY, string Reason,
    string Backend, string Language, string Outcome, double ElapsedMilliseconds,
    LootObservation? Before, LootObservation? After, IReadOnlyList<LootRowReviewReading> Readings,
    int Errors)
{
    public TrashQuantityAnomaly? QuantityAnomaly { get; init; }
}

internal sealed record LootRowReviewResult(LootObservation? Observation, LootRowReviewDiagnostics? Diagnostics);

internal interface ILootRowReview : IDisposable
{
    void ConfigureLanguage(string languageTag);
    Task<LootRowReviewResult> ReviewAsync(Mat originalBand, LootRowReviewInput input,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reviews one screenshot row; it has no ledger, frame history or event callback.
/// The caller awaits all reviews and replaces the original observations before reconciliation.
/// Each worker owns an independent engine. No primary Windows OCR instance is shared with it.
/// </summary>
internal sealed class BackgroundLootRowReview : ILootRowReview
{
    internal const double NameReviewThreshold = .80;
    internal const float TrustedTemplateScore = .90f;
    internal const double MinimumReadingConfidence = .95;
    internal const float MinimumQuantityReplacementConfidence = .90f;
    internal const string UnconfirmedRareReason = LootObservation.RarePaddleUnconfirmedReason;
    internal static readonly TimeSpan RowBudget = TimeSpan.FromSeconds(2);
    private readonly CompanionItemMatcher _matcher;
    private readonly Func<string, ISecondaryLootOcrRecognizer> _createRecognizer;
    private readonly SemaphoreSlim _slots;
    private readonly object _sync = new();
    private readonly Stack<ISecondaryLootOcrRecognizer> _available = new();
    private string _language = "en-US";
    private int _active;
    private bool _disposed;

    public BackgroundLootRowReview(CompanionItemMatcher matcher,
        Func<string, ISecondaryLootOcrRecognizer> createRecognizer, int workerCount = 2)
    {
        if (workerCount is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(workerCount));
        _matcher = matcher;
        _createRecognizer = createRecognizer;
        _slots = new(workerCount, workerCount);
    }

    public void ConfigureLanguage(string languageTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_language == languageTag) return;
            if (_active != 0) throw new InvalidOperationException("OCR language cannot change during a review.");
            foreach (var engine in _available) engine.Dispose();
            _available.Clear();
            _language = languageTag;
        }
    }

    internal static string? ReviewReason(LootRowReviewInput input)
    {
        if (input.Source == LootSource.Rare) return "rare-ocr-review";
        var row = input.Baseline;
        if (input.ReviewMissingAlignmentAnchor)
            return input.Source == LootSource.Normal && row is null ? "missing-alignment-anchor" : null;
        if (row is null || row.ItemName is null && string.IsNullOrWhiteSpace(row.RawText)) return null;
        if (row?.ItemName is null || row.RejectionReason is not null) return "unrecognized-row";
        if (!input.Allows(row.ItemName)) return row.NameConfidence == 1 ? null : "outside-spot-pool";
        var bounds = input.Bounds(row.ItemName);
        if (HasQuantityAnomaly(input, row)) return "trash-quantity-anomaly";
        if (row.NameConfidence < NameReviewThreshold) return "uncertain-name";
        return QuantityReviewReason(input, row, bounds);
    }

    private static string? QuantityReviewReason(LootRowReviewInput input, LootObservation row, DropQuantityBounds? bounds)
    {
        if (bounds?.IsFixedUnit == true) return null;
        if (row.Quantity is not > 0) return "missing-quantity";
        if (row.Quantity > bounds?.Maximum) return "quantity-outside-range";
        // A complete primary OCR amount takes precedence over fallback digit templates.
        if (NormalLootRecovery.TryParseTrailingQuantity(row.RawText, out var parsed, out _))
            return parsed == row.Quantity ? null : "quantity-disagreement";
        if (HasTrustedTemplateQuantity(input, row)) return null;
        return "quantity-without-complete-text";
    }

    private static bool HasQuantityAnomaly(LootRowReviewInput input, LootObservation row) =>
        (input.Source == LootSource.Normal || input.ApplyNormalSafetyRules) &&
        row is { ItemName: not null, RejectionReason: null, Quantity: > 0 } &&
        input.QuantityAnomaly is { } anomaly && anomaly.BaselineQuantity == row.Quantity &&
        input.Allows(row.ItemName) && input.Bounds(row.ItemName)?.IsFixedUnit != true;

    private static bool HasTrustedTemplateQuantity(LootRowReviewInput input, LootObservation row) =>
        input.TemplateQuantity == row.Quantity && input.TemplateScore >= TrustedTemplateScore &&
        !System.Text.RegularExpressions.Regex.IsMatch(row.RawText, @"(?:^|\s)[xX×](?:\s|[0-9IOil]|$)");

    public async Task<LootRowReviewResult> ReviewAsync(Mat originalBand, LootRowReviewInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) ObjectDisposedException.ThrowIf(_disposed, this);
        var reason = ReviewReason(input);
        if (reason is null) return new(input.Baseline, null);
        // Empty/background OCR is not evidence of an uncertain drop. Retry only
        // text which the first pass already relates to an item in the catalog.
        if (input.Source != LootSource.Rare && input.Baseline is { ItemName: null } unidentified)
        {
            var text = CompanionTextPipeline.Process(unidentified.RawText, -1, input.Source == LootSource.Rare, 0);
            if (!_matcher.TryMatch(text.Name, -1, input.Source == LootSource.Rare, out var hint) ||
                hint is null || hint.NormalizedDistance > .22)
                return new(input.Baseline, null);
        }
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _active++;
        }
        var entered = false;
        try
        {
            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            // Source pixels belong to the awaited frame. The worker receives its own crop.
            using var copy = originalBand.Clone();
            return await Task.Run(() => Review(copy, input, reason, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (entered) _slots.Release();
            lock (_sync) _active--;
        }
    }

    private LootRowReviewResult Review(Mat source, LootRowReviewInput input, string reason,
        CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        var readings = new List<LootRowReviewReading>(2);
        var candidates = new List<Candidate>(2);
        var errors = 0;
        var isRare = input.Source == LootSource.Rare;
        var reviewMissingAnchor = input.Source == LootSource.Normal && input.ReviewMissingAlignmentAnchor;
        // The rare banner includes decorative borders. Locate its text vertically
        // using this frame's primary OCR boxes before Paddle normalizes to 48px.
        // Keep the full width so enhancement prefixes and quantities stay visible.
        var rareReading = isRare ? input.PrimaryQuantityReads
            .Where(read => read.NameScale == 1 && read.NormalizedNameTop == 0 && read.Reading.Words.Count > 0)
            .OrderByDescending(read => read.Reading.Text == input.Baseline?.RawText)
            .Select(read => read.Reading).FirstOrDefault() : null;
        ISecondaryLootOcrRecognizer? engine = null;
        var backend = "paddle-pp-ocrv6-small-onnx";
        try
        {
            if (source.Empty()) return Finish(input.Baseline, "no-image-information");
            Cv2.MeanStdDev(source, out _, out Scalar deviation);
            if (deviation.Val0 == 0 && deviation.Val1 == 0 && deviation.Val2 == 0)
                return Finish(input.Baseline, "no-image-information");
            lock (_sync) engine = _available.TryPop(out var available) ? available : null;
            engine ??= _createRecognizer(_language);
            backend = engine.BackendName;
            // Original color and grayscale retain strokes lost by the former threshold masks.
            foreach (var grayscale in new[] { false, true })
            {
                token.ThrowIfCancellationRequested();
                if (timer.Elapsed >= RowBudget) break;
                try
                {
                    using var image = PaddleLootRowPreprocessor.Prepare(source, input.UiScale,
                        input.Source == LootSource.Rare, grayscale, rareReading?.Words);
                    var read = engine.Recognize(image, token);
                    var suffix = System.Text.RegularExpressions.Regex.Match(read.Text, @"[xX×]\s*([0-9]{1,9})\s*\.?$");
                    int? quantity = suffix.Success ? int.Parse(suffix.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) : null;
                    var quantityConfidence = suffix.Success
                        ? ReadQuantityConfidence(read, suffix.Groups[1].Index, suffix.Groups[1].Length) : null;
                    var name = suffix.Success ? read.Text[..suffix.Index].TrimEnd(' ', '-', '\'', '’') : read.Text;
                    var candidate = Match(name, quantity, read.Confidence);
                    if (candidate is not null)
                    {
                        // A standalone rare candidate must have reliable digits,
                        // even when there is no existing amount to compare with.
                        if (isRare && candidate.Bounds?.IsFixedUnit != true &&
                            quantityConfidence is not null && quantityConfidence < MinimumQuantityReplacementConfidence)
                            candidate = candidate with { Quantity = null };
                        // A long, confidently read item name can hide a weak extra digit
                        // in the whole-row mean. Overwriting an existing amount needs
                        // reliable digits; filling a missing amount keeps its usual rules.
                        if (input.Baseline is { ItemName: not null, RejectionReason: null, Quantity: > 0 } existing &&
                            candidate.Name == existing.ItemName && candidate.Quantity != existing.Quantity &&
                            candidate.Bounds?.IsFixedUnit != true &&
                            quantityConfidence < MinimumQuantityReplacementConfidence)
                            candidate = candidate with { Quantity = null };
                        candidate = candidate with
                        {
                            QuantityConfidence = candidate.Bounds?.IsFixedUnit == true ? candidate.Confidence
                                : Math.Min(candidate.Confidence, quantityConfidence ?? candidate.Confidence),
                        };
                    }
                    readings.Add(new(grayscale ? "grayscale" : "original", read.Text,
                        float.IsFinite(read.Confidence) ? Math.Clamp(read.Confidence, 0, 1) : 0,
                        candidate?.Name, candidate?.Quantity)
                    { QuantityConfidence = quantityConfidence, NameConfidence = candidate?.NameConfidence });
                    if (candidate is not null) candidates.Add(candidate);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    errors++;
                    readings.Add(new(grayscale ? "grayscale" : "original", "", 0, null, null)
                    { Error = exception.GetType().Name });
                }
            }
            // Rare readings compete independently. A failed view cannot veto a
            // good primary read or the other Paddle view.
            if (isRare) return Finish(input.Baseline, "rare-ocr-review");
            // Both views must establish the same catalog item. An OCR engine score alone
            // is insufficient to turn scenery or another item into a new observation.
            if (candidates.Count != 2 || candidates[0].Name != candidates[1].Name)
                return Finish(input.Baseline, "no-consensus");
            var winner = candidates[0];
            var baseline = input.Baseline;
            if (baseline is not null && HasQuantityAnomaly(input, baseline))
            {
                // This review may correct only a suspicious amount on an existing
                // Windows row. It cannot introduce an item or extend row visibility.
                if (winner.Name != baseline.ItemName)
                    return Finish(baseline, "anomaly-item-disagreement");
                if (readings.Any(reading => reading.Quantity is not > 0) ||
                    readings[0].Quantity != readings[1].Quantity)
                    return Finish(baseline, "anomaly-no-quantity-consensus");
                var correction = readings[0].Quantity!.Value;
                if (correction == baseline.Quantity)
                    return Finish(baseline, "baseline-confirmed");
                if (!_matcher.TryMatch(baseline.ItemName!, correction, false, out var correctedMatch) ||
                    correctedMatch?.CanonicalName != baseline.ItemName)
                    return Finish(baseline, "anomaly-quantity-filtered");
                if (!input.QuantityAnomaly!.IsPlausibleCorrection(correction, input.Bounds(baseline.ItemName!)))
                    return Finish(baseline, "anomaly-implausible-correction");
                return Finish(baseline with
                {
                    RawText = readings[0].Text,
                    Quantity = correction,
                    QuantityConfidence = Math.Min(candidates[0].QuantityConfidence, candidates[1].QuantityConfidence),
                    QuantityBounds = input.Bounds(baseline.ItemName!),
                    UsesImplicitUnitQuantity = false,
                    UsesFixedUnitQuantity = false,
                }, "anomaly-quantity-corrected");
            }
            if (reviewMissingAnchor &&
                (readings.Any(r => r.Quantity is not > 0) || readings[0].Quantity != readings[1].Quantity ||
                 winner.Bounds is { } anchorBounds && (readings[0].Quantity < anchorBounds.Minimum ||
                     readings[0].Quantity > anchorBounds.Maximum)))
                return Finish(baseline, "no-anchor-quantity-consensus");
            if (baseline is { ItemName: not null, RejectionReason: null, NameConfidence: >= NameReviewThreshold } &&
                baseline.ItemName != winner.Name)
                return Finish(baseline, "primary-name-preserved");
            int? amount = null;
            if (winner.Bounds?.IsFixedUnit == true) amount = 1;
            else
            {
                var quantities = candidates.Where(c => c.Quantity is > 0).Select(c => c.Quantity!.Value).Distinct().ToArray();
                if (quantities.Length != 1) return Finish(baseline, "quantity-unresolved");
                // Zero is impossible. A complete 6/0 pair is usable; a missing suffix is
                // not an independent reading and cannot corroborate a lone guessed amount.
                if (candidates.Any(c => c.Quantity is null)) return Finish(baseline, "no-quantity-consensus");
                amount = quantities[0];
            }
            if (baseline is { ItemName: not null, RejectionReason: null, Quantity: > 0 } &&
                baseline.ItemName == winner.Name && winner.Bounds?.IsFixedUnit != true &&
                QuantityReviewReason(input, baseline, winner.Bounds) is null)
                // Name and quantity can both be uncertain. The reason that started
                // this review must not protect an independently unreliable amount.
                amount = baseline.Quantity;
            var revised = (baseline ?? new LootObservation(input.Source, input.Slot, "", null, null, 0, 0, null, null)) with
            {
                RawText = readings.FirstOrDefault(r => r.Quantity == amount || winner.Bounds?.IsFixedUnit == true)?.Text
                    ?? baseline?.RawText ?? readings[0].Text,
                ItemName = winner.Name,
                Quantity = amount,
                RejectionReason = null,
                NameConfidence = Math.Min(candidates[0].NameConfidence, candidates[1].NameConfidence),
                QuantityConfidence = candidates.Where(candidate => candidate.Quantity == amount)
                    .Select(candidate => candidate.QuantityConfidence).DefaultIfEmpty(baseline?.QuantityConfidence ?? 0).Min(),
                QuantityBounds = winner.Bounds,
                UsesImplicitUnitQuantity = false,
                UsesFixedUnitQuantity = winner.Bounds?.IsFixedUnit == true,
                Source = input.Source,
                Slot = input.Slot,
                NativeY = input.NativeY,
                IsAlignmentAnchor = reviewMissingAnchor,
            };
            if (baseline is not null && baseline.ItemName == revised.ItemName && baseline.Quantity == revised.Quantity &&
                baseline.RejectionReason == revised.RejectionReason)
                return Finish(baseline, "baseline-confirmed");
            return Finish(revised, "observation-revised");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            errors++;
            // Preserve usable results even if another engine cannot run.
            return Finish(input.Baseline, "review-error:" + exception.GetType().Name);
        }
        finally
        {
            if (engine is not null) lock (_sync) _available.Push(engine);
        }

        Candidate? Match(string name, int? quantity, double confidence)
        {
            if (!double.IsFinite(confidence) || confidence is < MinimumReadingConfidence or > 1 ||
                !_matcher.TryMatch(name.Trim(), -1, input.Source == LootSource.Rare, out var match) ||
                match is null || match.NormalizedDistance > .05 || !input.Allows(match.CanonicalName)) return null;
            var bounds = input.Bounds(match.CanonicalName);
            return new(match.CanonicalName, bounds?.IsFixedUnit == true && !reviewMissingAnchor ? 1 : quantity,
                1 - match.NormalizedDistance, confidence, bounds);
        }

        LootRowReviewResult Finish(LootObservation? observation, string outcome)
        {
            if (isRare)
            {
                var choices = new List<RareCandidate>();
                if (input.Baseline is { ItemName: not null, RejectionReason: null, Quantity: > 0 } primary &&
                    double.IsFinite(primary.NameConfidence) && primary.NameConfidence is >= NameReviewThreshold and <= 1 &&
                    input.Allows(primary.ItemName) && IsValidAmount(primary.ItemName, primary.Quantity.Value) &&
                    QuantityReviewReason(input, primary, input.Bounds(primary.ItemName)) is null)
                    choices.Add(new(primary, true, primary.NameConfidence));
                foreach (var reading in readings)
                {
                    if (reading is not { ItemName: not null, Quantity: > 0, NameConfidence: not null } ||
                        !IsValidAmount(reading.ItemName, reading.Quantity.Value)) continue;
                    var bounds = input.Bounds(reading.ItemName);
                    if (input.Baseline is { } original && HasQuantityAnomaly(input, original) &&
                        (reading.ItemName != original.ItemName || reading.Quantity != original.Quantity &&
                         !input.QuantityAnomaly!.IsPlausibleCorrection(reading.Quantity.Value, bounds))) continue;
                    var row = (input.Baseline ?? new LootObservation(input.Source, input.Slot, "", null, null, 0, 0, null, null)) with
                    {
                        RawText = reading.Text,
                        ItemName = reading.ItemName,
                        Quantity = reading.Quantity,
                        NameConfidence = reading.NameConfidence.Value,
                        QuantityConfidence = bounds?.IsFixedUnit == true ? reading.Confidence
                            : Math.Min(reading.Confidence, reading.QuantityConfidence ?? reading.Confidence),
                        RejectionReason = null,
                        QuantityBounds = bounds,
                        UsesImplicitUnitQuantity = false,
                        UsesFixedUnitQuantity = bounds?.IsFixedUnit == true,
                        Source = input.Source,
                        Slot = input.Slot,
                        NativeY = input.NativeY,
                        IsAlignmentAnchor = false,
                    };
                    choices.Add(new(row, false, reading.Confidence));
                }
                // Catalog similarity is comparable across engines; their native
                // confidence scales are not. Agreement only breaks ties, never
                // acts as a prerequisite. Preserve primary on an otherwise equal tie.
                var best = choices.OrderByDescending(choice => choice.Observation.NameConfidence)
                    .ThenByDescending(choice => choices.Count(other =>
                        other.Observation.ItemName == choice.Observation.ItemName && other.Observation.Quantity == choice.Observation.Quantity))
                    .ThenByDescending(choice => choice.Primary)
                    .ThenByDescending(choice => choice.RecognitionConfidence).FirstOrDefault();
                if (best is not null)
                {
                    observation = best.Observation;
                    outcome = best.Primary ? "rare-primary-selected" : "rare-secondary-selected";
                }
                else
                {
                    // A catalog-related but unreadable banner holds its existing
                    // arrival. Unrelated scenery must allow a real absence interval.
                    // One failed view cannot veto successful background evidence.
                    var possiblyVisible = HasRareHint(input.Baseline?.RawText) || readings.Any(read => HasRareHint(read.Text)) ||
                        errors > 0 && readings.All(read => read.Error is not null);
                    observation = (input.Baseline ?? new LootObservation(input.Source, input.Slot,
                        readings.FirstOrDefault()?.Text ?? "", null, null, 0, 0, null, null)) with
                    {
                        ItemName = null,
                        Quantity = null,
                        NameConfidence = 0,
                        QuantityConfidence = 0,
                        RejectionReason = possiblyVisible ? UnconfirmedRareReason : LootObservation.RareOcrNoMatchReason,
                        QuantityBounds = null,
                        UsesImplicitUnitQuantity = false,
                        UsesFixedUnitQuantity = false,
                        Source = input.Source,
                        Slot = input.Slot,
                        NativeY = input.NativeY,
                        IsAlignmentAnchor = false,
                    };
                    outcome = "rare-no-valid-reading";
                }
            }
            return new(observation,
                new(input.Source, input.NativeY, reason, backend, _language, outcome,
                    timer.Elapsed.TotalMilliseconds, input.Baseline, observation, readings.ToArray(), errors)
                { QuantityAnomaly = input.QuantityAnomaly });
        }

        bool IsValidAmount(string name, int quantity) => input.Bounds(name) is not { } bounds ||
            quantity >= bounds.Minimum && (!bounds.Maximum.HasValue || quantity <= bounds.Maximum.Value);

        bool HasRareHint(string? rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return false;
            var text = CompanionTextPipeline.Process(rawText, -1, true, 0);
            return _matcher.TryMatch(text.Name, -1, true, out var match) && match is not null && input.Allows(match.CanonicalName);
        }
    }

    private sealed record Candidate(string Name, int? Quantity, double NameConfidence,
        double Confidence, DropQuantityBounds? Bounds)
    {
        public double QuantityConfidence { get; init; }
    }

    private sealed record RareCandidate(LootObservation Observation, bool Primary, double RecognitionConfidence);

    private static double? ReadQuantityConfidence(SecondaryLootOcrResult reading, int start, int length)
    {
        if (reading.CharacterConfidences.Count == 0) return null;
        if (reading.CharacterConfidences.Count != reading.Text.Length) return 0;
        var minimum = 1f;
        for (var index = start; index < start + length; index++)
        {
            var confidence = reading.CharacterConfidences[index];
            if (!float.IsFinite(confidence) || confidence is < 0 or > 1) return 0;
            minimum = Math.Min(minimum, confidence);
        }
        return minimum;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (_active != 0) throw new InvalidOperationException("Drain OCR reviews before disposing them.");
            foreach (var engine in _available) engine.Dispose();
            _available.Clear();
            _slots.Dispose();
            _disposed = true;
        }
    }
}
