using OpenCvSharp;

namespace BdoGrindTracker.Ocr;

/// <summary>
/// A caller-owned digit template used by <see cref="CompanionQuantityRecognizer"/>.
/// The recognizer clones <see cref="Image"/> and does not take ownership of it.
/// </summary>
public sealed record CompanionQuantityTemplate(
    int Digit,
    Mat Image,
    float Threshold);

/// <summary>
/// An encoded caller-owned digit template used by <see cref="CompanionQuantityRecognizer"/>.
/// </summary>
public sealed record CompanionEncodedQuantityTemplate(
    int Digit,
    ReadOnlyMemory<byte> ImageBytes,
    float Threshold);

/// <summary>The quantity reconstructed from the rightmost connected digit group.</summary>
public sealed record CompanionQuantityRecognition(
    int X,
    int Quantity,
    float AverageScore,
    bool IsSpecialOne = false);

/// <summary>
/// Recognizes a quantity with the statically verified BDO Companion normal-path algorithm.
/// The caller is responsible for supplying the already prepared quantity region and all digit
/// templates; this type contains no bundled template assets or image preprocessing heuristics.
/// </summary>
public sealed class CompanionQuantityRecognizer : IDisposable
{
    private const int DuplicateDistanceSquared = 160;
    private const int MaximumConnectedXDistance = 30;
    private const int SpecialOneMaximumXExclusive = 75;
    private const int SpecialOneMinimumY = 41;
    private const int SpecialOneMaximumY = 49;

    private readonly StoredTemplate[] _templates;
    private bool _disposed;

    /// <summary>
    /// Creates a reusable recognizer from caller-supplied matrices. Each matrix is cloned.
    /// </summary>
    public CompanionQuantityRecognizer(IEnumerable<CompanionQuantityTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        _templates = CloneTemplates(templates);
    }

    /// <summary>
    /// Creates a reusable recognizer from caller-supplied encoded image bytes.
    /// Images are decoded without changing their channel count.
    /// </summary>
    public CompanionQuantityRecognizer(IEnumerable<CompanionEncodedQuantityTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        _templates = DecodeTemplates(templates);
    }

    /// <summary>
    /// Matches every template with TM_CCOEFF_NORMED, reconciles overlapping candidates, and
    /// returns the rightmost connected number. An isolated zero is not a valid result.
    /// </summary>
    public CompanionQuantityRecognition? Recognize(Mat quantityRegion)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(quantityRegion);

        if (quantityRegion.Empty())
        {
            throw new ArgumentException("The quantity region must not be empty.", nameof(quantityRegion));
        }

        var candidates = new List<DigitCandidate>();

        foreach (var template in _templates)
        {
            ValidateCompatibleImage(quantityRegion, template.Image);

            using var scores = new Mat();
            Cv2.MatchTemplate(
                quantityRegion,
                template.Image,
                scores,
                TemplateMatchModes.CCoeffNormed);

            var scoreRows = scores.Rows;
            var scoreColumns = scores.Cols;
            for (var y = 0; y < scoreRows; y++)
            {
                for (var x = 0; x < scoreColumns; x++)
                {
                    var score = scores.At<float>(y, x);
                    if (score >= template.Threshold)
                    {
                        ReconcileCandidate(
                            candidates,
                            new DigitCandidate(
                                template.Digit,
                                x,
                                y,
                                score,
                                template.Threshold));
                    }
                }
            }
        }

        var specialOne = FindSpecialOne(candidates);
        if (specialOne is not null)
        {
            return specialOne;
        }

        StableSortByX(candidates);
        return ComposeRightmostQuantity(candidates);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var template in _templates)
        {
            template.Image.Dispose();
        }

        _disposed = true;
    }

    private static StoredTemplate[] CloneTemplates(
        IEnumerable<CompanionQuantityTemplate> templates)
    {
        var stored = new List<StoredTemplate>();

        try
        {
            foreach (var template in templates)
            {
                ArgumentNullException.ThrowIfNull(template);
                ValidateTemplate(template.Digit, template.Image, template.Threshold);
                stored.Add(new StoredTemplate(
                    template.Digit,
                    template.Image.Clone(),
                    template.Threshold));
            }

            return RequireTemplates(stored);
        }
        catch
        {
            DisposeTemplates(stored);
            throw;
        }
    }

    private static StoredTemplate[] DecodeTemplates(
        IEnumerable<CompanionEncodedQuantityTemplate> templates)
    {
        var stored = new List<StoredTemplate>();

        try
        {
            foreach (var template in templates)
            {
                ArgumentNullException.ThrowIfNull(template);
                ValidateDigitAndThreshold(template.Digit, template.Threshold);
                if (template.ImageBytes.IsEmpty)
                {
                    throw new ArgumentException("Encoded template bytes must not be empty.", nameof(templates));
                }

                var image = Cv2.ImDecode(template.ImageBytes.Span, ImreadModes.Unchanged);
                if (image.Empty())
                {
                    image.Dispose();
                    throw new ArgumentException("Encoded template bytes do not contain a supported image.", nameof(templates));
                }

                stored.Add(new StoredTemplate(template.Digit, image, template.Threshold));
            }

            return RequireTemplates(stored);
        }
        catch
        {
            DisposeTemplates(stored);
            throw;
        }
    }

    private static StoredTemplate[] RequireTemplates(List<StoredTemplate> templates)
    {
        if (templates.Count == 0)
        {
            throw new ArgumentException("At least one digit template is required.", nameof(templates));
        }

        return templates.ToArray();
    }

    private static void ValidateTemplate(int digit, Mat image, float threshold)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateDigitAndThreshold(digit, threshold);

        if (image.Empty())
        {
            throw new ArgumentException("Digit template images must not be empty.", nameof(image));
        }
    }

    private static void ValidateDigitAndThreshold(int digit, float threshold)
    {
        if ((uint)digit > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(digit), digit, "A template digit must be between 0 and 9.");
        }

        if (!float.IsFinite(threshold) || threshold <= 0f || threshold > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                threshold,
                "A template threshold must be finite and in the interval (0, 1].");
        }
    }

    private static void ValidateCompatibleImage(Mat source, Mat template)
    {
        if (source.Type() != template.Type())
        {
            throw new ArgumentException(
                "The quantity region and every digit template must have the same OpenCV matrix type.",
                nameof(source));
        }

        if (template.Rows > source.Rows || template.Cols > source.Cols)
        {
            throw new ArgumentException(
                "The quantity region must be at least as large as every digit template.",
                nameof(source));
        }
    }

    private static void ReconcileCandidate(
        List<DigitCandidate> candidates,
        DigitCandidate candidate)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            var existing = candidates[index];
            var deltaX = candidate.X - existing.X;
            var deltaY = candidate.Y - existing.Y;
            var distanceSquared = unchecked((deltaX * deltaX) + (deltaY * deltaY));

            if (distanceSquared >= DuplicateDistanceSquared)
            {
                continue;
            }

            var existingRatio = existing.Score / existing.Threshold;
            var candidateRatio = candidate.Score / candidate.Threshold;
            if (existingRatio < candidateRatio)
            {
                candidates[index] = candidate;
            }

            return;
        }

        candidates.Add(candidate);
    }

    private static void StableSortByX(List<DigitCandidate> candidates)
    {
        for (var index = 1; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var insertionIndex = index;

            while (insertionIndex > 0 && candidate.X < candidates[insertionIndex - 1].X)
            {
                candidates[insertionIndex] = candidates[insertionIndex - 1];
                insertionIndex--;
            }

            candidates[insertionIndex] = candidate;
        }
    }

    private static CompanionQuantityRecognition? FindSpecialOne(
        IReadOnlyList<DigitCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Digit == 1 &&
                candidate.X < SpecialOneMaximumXExclusive &&
                candidate.Y >= SpecialOneMinimumY &&
                candidate.Y <= SpecialOneMaximumY)
            {
                return new CompanionQuantityRecognition(
                    X: 0,
                    Quantity: 1,
                    AverageScore: candidate.Threshold,
                    IsSpecialOne: true);
            }
        }

        return null;
    }

    private static CompanionQuantityRecognition? ComposeRightmostQuantity(
        List<DigitCandidate> candidates)
    {
        while (candidates.Count > 0)
        {
            var firstIndex = candidates.Count - 1;
            while (firstIndex > 0)
            {
                var current = candidates[firstIndex];
                var previous = candidates[firstIndex - 1];
                if (Math.Abs(current.X - previous.X) > MaximumConnectedXDistance)
                {
                    break;
                }

                firstIndex--;
            }

            var groupCount = candidates.Count - firstIndex;
            if (groupCount == 1 && candidates[firstIndex].Digit == 0)
            {
                candidates.RemoveAt(firstIndex);
                continue;
            }

            var quantity = 0;
            var scoreTotal = -0f;
            for (var index = firstIndex; index < candidates.Count; index++)
            {
                quantity = unchecked((quantity * 10) + candidates[index].Digit);
                scoreTotal += candidates[index].Score;
            }

            return new CompanionQuantityRecognition(
                candidates[firstIndex].X,
                quantity,
                scoreTotal / groupCount);
        }

        return null;
    }

    private static void DisposeTemplates(IEnumerable<StoredTemplate> templates)
    {
        foreach (var template in templates)
        {
            template.Image.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CompanionQuantityRecognizer));
        }
    }

    private sealed record StoredTemplate(int Digit, Mat Image, float Threshold);

    private readonly record struct DigitCandidate(
        int Digit,
        int X,
        int Y,
        float Score,
        float Threshold);
}
