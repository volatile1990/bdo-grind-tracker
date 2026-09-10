using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using BdoGrindTracker.Core;
using BdoGrindTracker.Ocr;
using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

internal interface ILootPrimaryRowReader : IDisposable
{
    void ConfigureLanguage(string languageTag);
    PrimaryReadResult Read(Mat originalBand, LootSource source, int slot, int nativeY, double uiScale,
        Func<string, DropQuantityBounds?> bounds, Func<string, bool> allows, CancellationToken cancellationToken);
}

internal sealed record PrimaryReadResult(LootObservation? Observation, LootRowReviewDiagnostics Diagnostics);

/// <summary>
/// Reads a calibrated row before Windows OCR. Color and grayscale must agree on
/// the item and its amount; abstention leaves the existing Windows path in charge.
/// Owns no row identity, counter state or fabricated OCR word geometry.
/// </summary>
internal sealed class PaddlePrimaryLootReader : ILootPrimaryRowReader
{
    private const double MinimumConfidence = .95;
    private const double MaximumNameDistance = .05;
    private static readonly Regex QuantitySuffix = new(@"[xX×]\s*([0-9]{1,9})\s*\.?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly CompanionItemMatcher _matcher;
    private readonly Func<string, ISecondaryLootOcrRecognizer> _createRecognizer;
    private readonly object _gate = new();
    private ISecondaryLootOcrRecognizer? _engine;
    private string _language = "en-US";
    private bool _disposed;

    public PaddlePrimaryLootReader(CompanionItemMatcher matcher,
        Func<string, ISecondaryLootOcrRecognizer> createRecognizer)
    {
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        _createRecognizer = createRecognizer ?? throw new ArgumentNullException(nameof(createRecognizer));
    }

    public void ConfigureLanguage(string languageTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        var language = CultureInfo.GetCultureInfo(languageTag).Name;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (language == _language) return;
            _engine?.Dispose();
            _engine = null;
            _language = language;
        }
    }

    public PrimaryReadResult Read(Mat originalBand, LootSource source, int slot, int nativeY, double uiScale,
        Func<string, DropQuantityBounds?> bounds, Func<string, bool> allows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalBand);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(allows);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            var readings = new List<LootRowReviewReading>(2);
            var candidates = new List<Candidate>(2);
            var backend = "paddle-pp-ocrv6-small-onnx";
            try
            {
                if (originalBand.Empty()) return Finish(null, "no-image-information");
                Cv2.MeanStdDev(originalBand, out _, out Scalar deviation);
                if (deviation.Val0 == 0 && deviation.Val1 == 0 && deviation.Val2 == 0)
                    return Finish(null, "no-image-information");
                _engine ??= _createRecognizer(_language);
                backend = _engine.BackendName;
                foreach (var grayscale in new[] { false, true })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var image = PaddleLootRowPreprocessor.Prepare(originalBand, uiScale,
                        source == LootSource.Rare, grayscale);
                    var reading = _engine.Recognize(image, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var suffix = QuantitySuffix.Match(reading.Text);
                    int? quantity = suffix.Success ? int.Parse(suffix.Groups[1].Value, CultureInfo.InvariantCulture) : null;
                    var name = suffix.Success ? reading.Text[..suffix.Index].TrimEnd(' ', '-', '\'', '’')
                        : CompanionTextPipeline.Process(reading.Text, -1, source == LootSource.Rare, 0).Name;
                    var candidate = Match(name, quantity, reading.Confidence);
                    readings.Add(new(grayscale ? "grayscale" : "original", reading.Text,
                        float.IsFinite(reading.Confidence) ? Math.Clamp(reading.Confidence, 0, 1) : 0,
                        candidate?.Name, quantity));
                    if (candidate is not null) candidates.Add(candidate);
                }
                if (candidates.Count != 2 || candidates[0].Name != candidates[1].Name)
                    return Finish(null, "no-consensus");
                var first = candidates[0];
                var second = candidates[1];
                if (first.Quantity is not > 0 || first.Quantity != second.Quantity)
                    return Finish(null, "quantity-unresolved");
                var observation = new LootObservation(source, slot, readings[0].Text, first.Name, first.Quantity,
                    Math.Min(first.NameConfidence, second.NameConfidence),
                    Math.Min(first.Confidence, second.Confidence), null, null)
                {
                    NativeY = nativeY,
                    QuantityBounds = first.Bounds,
                    UsesImplicitUnitQuantity = false,
                    UsesFixedUnitQuantity = first.Bounds?.IsFixedUnit == true,
                };
                return Finish(observation, "accepted");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Optional native/model failures abstain; the caller can still use
                // Windows OCR. No partial single reading becomes a counted row.
                return Finish(null, "primary-error:" + exception.GetType().Name, 1);
            }

            Candidate? Match(string name, int? quantity, double confidence)
            {
                if (!double.IsFinite(confidence) || confidence is < MinimumConfidence or > 1 ||
                    !_matcher.TryMatch(name.Trim(), quantity ?? -1, source == LootSource.Rare, out var match) ||
                    match is null || match.NormalizedDistance > MaximumNameDistance || !allows(match.CanonicalName))
                    return null;
                var policy = bounds(match.CanonicalName);
                // Keep catalog quantity policy at its existing counter boundary.
                // Only an explicitly fixed one-per-drop item can omit its suffix.
                return new(match.CanonicalName, policy?.IsFixedUnit == true ? 1 : quantity,
                    1 - match.NormalizedDistance, confidence, policy);
            }

            PrimaryReadResult Finish(LootObservation? observation, string outcome, int errors = 0) =>
                new(observation, new(source, nativeY, "primary-ocr", backend, _language, outcome,
                    timer.Elapsed.TotalMilliseconds, null, observation, readings.ToArray(), errors));
        }
    }

    private sealed record Candidate(string Name, int? Quantity, double NameConfidence,
        double Confidence, DropQuantityBounds? Bounds);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _engine?.Dispose();
            _engine = null;
        }
    }
}
