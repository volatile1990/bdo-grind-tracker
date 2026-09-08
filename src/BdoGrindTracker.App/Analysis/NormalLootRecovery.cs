using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

internal interface INormalLootRecovery
{
    void ConfigureQuantityBounds(Func<string, DropQuantityBounds?> resolve) { }
    LootObservation? Recover(Mat sourceBand, ICompanionPreparedRow original,
        LootObservation? baseline, int slot, float uiScale,
        NormalLootRecoveryBudget budget, CancellationToken cancellationToken);
}

/// <summary>
/// Bounds optional work, never baseline recognition. No frame waiting, confidence
/// voting, quantity guessing or extra inputs to the Companion counter.
/// </summary>
internal sealed class NormalLootRecoveryBudget
{
    internal const int MaximumOcrCalls = 8;
    internal static readonly TimeSpan MaximumElapsed = TimeSpan.FromMilliseconds(120);
    private readonly TimeProvider _time;
    private readonly long _startedAt;

    public NormalLootRecoveryBudget(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _startedAt = _time.GetTimestamp();
    }

    public int OcrCalls { get; private set; }
    public int Errors { get; private set; }
    public bool CanContinue => OcrCalls < MaximumOcrCalls &&
        _time.GetElapsedTime(_startedAt) < MaximumElapsed;

    public bool TryBeginOcr()
    {
        if (!CanContinue) return false;
        OcrCalls++;
        return true;
    }

    public void RecordError() => Errors++;
}

internal sealed partial class NormalLootRecovery(
    CompanionItemMatcher matcher,
    ICompanionNameRecognizer recognizer,
    Func<Mat, NormalLootRecoveryVariant, NormalLootRecoveryImages>? prepare = null)
    : INormalLootRecovery
{
    private readonly Func<Mat, NormalLootRecoveryVariant, NormalLootRecoveryImages> _prepare =
        prepare ?? NormalLootRecoveryPreprocessor.Prepare;
    private Func<string, DropQuantityBounds?> _quantityBounds = name => DropQuantityCatalog.GetBounds(null, name);

    public void ConfigureQuantityBounds(Func<string, DropQuantityBounds?> resolve) =>
        _quantityBounds = resolve ?? throw new ArgumentNullException(nameof(resolve));

    public LootObservation? Recover(Mat sourceBand, ICompanionPreparedRow original,
        LootObservation? baseline, int slot, float uiScale,
        NormalLootRecoveryBudget budget, CancellationToken cancellationToken)
    {
        if (Accepted(baseline) && _quantityBounds(baseline!.ItemName!)?.IsFixedUnit == true)
            return FixedUnit(baseline);
        // Zero cannot be a drop quantity. Keep its identified row eligible for
        // recovery instead of allowing it to reach the positive-only ledger.
        if (baseline is { Quantity: <= 0 }) baseline = baseline with { Quantity = null };
        // A successfully read baseline row is authoritative, even if a different
        // image variant would claim a higher number or another item.
        if (Accepted(baseline) && baseline!.Quantity.HasValue) return baseline;
        cancellationToken.ThrowIfCancellationRequested();
        // No information can be recovered from a perfectly uniform original band.
        // This fast path is only for optional retries, not a new baseline blank gate.
        Cv2.MeanStdDev(sourceBand, out _, out Scalar deviation);
        if (deviation.Val0 == 0 && deviation.Val1 == 0 && deviation.Val2 == 0) return baseline;
        var best = baseline;
        foreach (var variant in new[] { NormalLootRecoveryVariant.Grayscale,
                     NormalLootRecoveryVariant.AdaptiveThreshold })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!budget.CanContinue) break;
            try
            {
                using var images = _prepare(sourceBand, variant);
                var quantityAttempted = false;
                if (Accepted(best) && !best!.Quantity.HasValue)
                {
                    quantityAttempted = true;
                    if (ReadQuantity(images.QuantityImage) is { } quantity)
                        return best with { Quantity = quantity };
                }

                if (!budget.TryBeginOcr()) break;
                var ocr = recognizer.Recognize(images.NameImage, cancellationToken);
                if (!CompanionWindowsOcrRecognizer.PassesNormalGeometryGate(ocr.FirstWord, uiScale))
                    continue;
                var knownQuantity = baseline?.Quantity ??
                    (original.TemplateQuantity > 0 ? original.TemplateQuantity : (int?)null);
                var hasFullQuantity = TryParseTrailingQuantity(ocr.Text, out var fullQuantity, out var nameText);
                // The original row failed recognition, so its number is only a
                // fallback. A complete quantity in the recovered text takes the
                // same precedence as in the baseline text pipeline. Otherwise a
                // template misread (for example 1 instead of x8) changes the row's
                // identity and can count an already visible drop again.
                var rowQuantity = hasFullQuantity ? fullQuantity : knownQuantity ?? -1;
                var text = CompanionTextPipeline.Process(hasFullQuantity ? nameText : ocr.Text,
                    rowQuantity, false, images.RecognizedTextWidth);
                // Companion's legacy regex can parse only a prefix of x4O or
                // x12345. Its baseline contract stays untouched, but a NEW
                // recovery quantity must come from a complete numeric token.
                text = text with
                {
                    Quantity = rowQuantity,
                    HasParsedOcrQuantity = hasFullQuantity,
                };
                if (!CompanionTextPipeline.PassesExpectedWidth(text,
                        images.RecognizedTextWidth, 0, images.NameScale) ||
                    !matcher.TryMatch(text.Name, text.Quantity, false, out var match) || match is null)
                    continue;
                // An already identified item is not replaced just to obtain a number.
                if (Accepted(best) && !string.Equals(best!.ItemName, match.CanonicalName,
                        StringComparison.Ordinal))
                    continue;

                best = new LootObservation(LootSource.Normal, slot, ocr.Text, match.CanonicalName,
                    text.Quantity > 0 ? text.Quantity : null,
                    Math.Clamp(1 - match.NormalizedDistance, 0, 1), 0, null, null)
                {
                    NativeY = original.Y,
                };
                if (_quantityBounds(best.ItemName!)?.IsFixedUnit == true) return FixedUnit(best);
                if (best.Quantity.HasValue) return best;
                if (!quantityAttempted && ReadQuantity(images.QuantityImage) is { } recoveredQuantity)
                    return best with { Quantity = recoveredQuantity };
            }
            catch (Exception exception) when (exception is OpenCVException or COMException or
                ArgumentException or InvalidOperationException or NotSupportedException)
            {
                // The additional path is best-effort; failure cannot discard a
                // baseline observation or stop an otherwise healthy capture session.
                budget.RecordError();
            }
        }
        return best;

        int? ReadQuantity(Mat? image)
        {
            if (image is null || !budget.TryBeginOcr()) return null;
            var ocr = recognizer.Recognize(image, cancellationToken);
            return TryParseQuantity(ocr.Text, out var quantity) ? quantity : null;
        }
    }

    internal static bool TryParseQuantity(string? text, out int quantity)
    {
        quantity = 0;
        if (text is null) return false;
        var match = QuantityOnlyRegex().Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out quantity) && quantity > 0;
    }

    internal static bool TryParseTrailingQuantity(string text, out int quantity, out string name)
    {
        quantity = 0;
        name = text;
        var match = TrailingQuantityRegex().Match(text);
        if (!match.Success || !TryParseQuantity(match.Value, out quantity)) return false;
        name = text[..match.Index].TrimEnd();
        return name.Length > 0;
    }

    private static bool Accepted(LootObservation? row) =>
        row?.ItemName is not null && row.RejectionReason is null;

    private LootObservation FixedUnit(LootObservation row) => row with
    {
        Quantity = 1, QuantityBounds = _quantityBounds(row.ItemName!), UsesFixedUnitQuantity = true,
        UsesImplicitUnitQuantity = false,
    };

    // Parse the whole isolated token, never remove arbitrary letters or concatenate
    // numbers from separate words. Windows OCR itself has no digit whitelist here.
    [GeneratedRegex(@"\A\s*[xX×]?\s*([0-9]+)\s*\z", RegexOptions.CultureInvariant)]
    private static partial Regex QuantityOnlyRegex();

    [GeneratedRegex(@"[xX×]\s*([0-9]+)\s*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingQuantityRegex();
}
