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
}

internal sealed record PrimaryLootQuantityRead(CompanionOcrResult Reading, float NameScale, float NormalizedNameTop = 0);

internal sealed record LootRowReviewReading(string Variant, string Text, double Confidence,
    string? ItemName, int? Quantity);

internal sealed record LootRowReviewDiagnostics(LootSource Source, int NativeY, string Reason,
    string Backend, string Language, string Outcome, double ElapsedMilliseconds,
    LootObservation? Before, LootObservation? After, IReadOnlyList<LootRowReviewReading> Readings,
    int Errors);

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
        var row = input.Baseline;
        if (input.ReviewMissingAlignmentAnchor)
            return input.Source == LootSource.Normal && row is null ? "missing-alignment-anchor" : null;
        if (row is null || row.ItemName is null && string.IsNullOrWhiteSpace(row.RawText)) return null;
        if (row?.ItemName is null || row.RejectionReason is not null) return "unrecognized-row";
        if (!input.Allows(row.ItemName)) return row.NameConfidence == 1 ? null : "outside-spot-pool";
        var bounds = input.Bounds(row.ItemName);
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
        if (input.Baseline is { ItemName: null } unidentified)
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
        ISecondaryLootOcrRecognizer? engine = null;
        var backend = "paddle-pp-ocrv6-small-onnx";
        try
        {
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
                using var image = PaddleLootRowPreprocessor.Prepare(source, input.UiScale,
                    input.Source == LootSource.Rare, grayscale);
                var read = engine.Recognize(image, token);
                var suffix = System.Text.RegularExpressions.Regex.Match(read.Text, @"[xX×]\s*([0-9]{1,9})\s*\.?$");
                int? quantity = suffix.Success ? int.Parse(suffix.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) : null;
                var name = suffix.Success ? read.Text[..suffix.Index].TrimEnd(' ', '-', '\'', '’') : read.Text;
                var candidate = Match(name, quantity, read.Confidence);
                readings.Add(new(grayscale ? "grayscale" : "original", read.Text,
                    float.IsFinite(read.Confidence) ? Math.Clamp(read.Confidence, 0, 1) : 0,
                    candidate?.Name, candidate?.Quantity));
                if (candidate is not null) candidates.Add(candidate);
            }
            // Both views must establish the same catalog item. An OCR engine score alone
            // is insufficient to turn scenery or another item into a new observation.
            if (candidates.Count != 2 || candidates[0].Name != candidates[1].Name)
                return Finish(input.Baseline, "no-consensus");
            var winner = candidates[0];
            var baseline = input.Baseline;
            if (input.ReviewMissingAlignmentAnchor &&
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
                ItemName = winner.Name, Quantity = amount, RejectionReason = null,
                NameConfidence = Math.Min(candidates[0].NameConfidence, candidates[1].NameConfidence),
                QuantityConfidence = Math.Min(candidates[0].Confidence, candidates[1].Confidence),
                QuantityBounds = winner.Bounds, UsesImplicitUnitQuantity = false,
                UsesFixedUnitQuantity = winner.Bounds?.IsFixedUnit == true,
                Source = input.Source, Slot = input.Slot, NativeY = input.NativeY,
                IsAlignmentAnchor = input.ReviewMissingAlignmentAnchor,
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
            // A broken optional model must never delete otherwise usable primary loot.
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
            return new(match.CanonicalName, bounds?.IsFixedUnit == true && !input.ReviewMissingAlignmentAnchor ? 1 : quantity,
                1 - match.NormalizedDistance, confidence, bounds);
        }

        LootRowReviewResult Finish(LootObservation? observation, string outcome) => new(observation,
            new(input.Source, input.NativeY, reason, backend, _language, outcome,
                timer.Elapsed.TotalMilliseconds, input.Baseline, observation, readings.ToArray(), errors));
    }

    private sealed record Candidate(string Name, int? Quantity, double NameConfidence,
        double Confidence, DropQuantityBounds? Bounds);

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
