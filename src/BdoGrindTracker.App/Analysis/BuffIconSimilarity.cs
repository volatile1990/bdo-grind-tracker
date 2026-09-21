using OpenCvSharp;

namespace BdoGrindTracker.App.Analysis;

/// <summary>
/// Compares the colored artwork after a shared tone adjustment. Channel-wise
/// mean subtraction would erase the color differences between several meals
/// and Harmony variants, so all three channels share one mean and tone curve.
/// </summary>
internal static class BuffIconSimilarity
{
    private const int ComparisonSize = 48;
    private const int Inset = 4;
    private static readonly double[][] ToneCurves = new[] { .4, .5, .6, .7, .8, .9, 1, 1.1, 1.2, 1.4, 1.6, 1.8, 2 }
        .Select(gamma => Enumerable.Range(0, 256).Select(value => Math.Pow(value / 255d, gamma)).ToArray()).ToArray();

    /// <summary>
    /// Returns 0..1 for aligned BGR patches. A bounded, common gamma adjustment
    /// tolerates HDR transfer and neutral HUD opacity without recoloring a
    /// competing variant. The bevel and world pixels around the icon are ignored.
    /// </summary>
    internal static unsafe double Score(Mat observedPatch, Mat reference)
    {
        ArgumentNullException.ThrowIfNull(observedPatch);
        ArgumentNullException.ThrowIfNull(reference);
        if (observedPatch.Empty() || reference.Empty() || observedPatch.Type() != MatType.CV_8UC3
            || reference.Type() != MatType.CV_8UC3 || observedPatch.Width < 8 || observedPatch.Height < 8
            || reference.Width < 8 || reference.Height < 8)
            return 0;

        using var observed = new Mat();
        using var expected = new Mat();
        Cv2.Resize(observedPatch, observed, new(ComparisonSize, ComparisonSize), interpolation: InterpolationFlags.Linear);
        Cv2.Resize(reference, expected, observed.Size(), interpolation: InterpolationFlags.Linear);

        const int pixelCount = (ComparisonSize - 2 * Inset) * (ComparisonSize - 2 * Inset);
        const int valueCount = pixelCount * 3;
        Span<double> values = stackalloc double[valueCount];
        Span<byte> indices = stackalloc byte[valueCount];
        double sum = 0, squared = 0, luminanceSum = 0, luminanceSquared = 0;
        var index = 0;
        for (var y = Inset; y < ComparisonSize - Inset; y++)
        {
            var actualRow = (byte*)observed.Ptr(y);
            var referenceRow = (byte*)expected.Ptr(y);
            for (var x = Inset; x < ComparisonSize - Inset; x++)
            {
                double luminance = 0;
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = actualRow[x * 3 + channel] / 255d;
                    values[index] = value;
                    indices[index++] = referenceRow[x * 3 + channel];
                    sum += value;
                    squared += value * value;
                    luminance += value / 3;
                }
                luminanceSum += luminance;
                luminanceSquared += luminance * luminance;
            }
        }

        // Flat or almost flat patches have no reliable spatial symbol evidence,
        // even if their red/green/blue channel means differ strongly.
        if (luminanceSquared / pixelCount - Math.Pow(luminanceSum / pixelCount, 2) < Math.Pow(2d / 255, 2))
            return 0;
        var variance = squared - sum * sum / valueCount;
        if (variance <= 1e-12) return 0;
        var mean = sum / valueCount;
        for (var i = 0; i < values.Length; i++) values[i] -= mean;

        double best = 0;
        foreach (var curve in ToneCurves)
        {
            double referenceSum = 0, referenceSquared = 0, covariance = 0;
            for (var i = 0; i < values.Length; i++)
            {
                var value = curve[indices[i]];
                referenceSum += value;
                referenceSquared += value * value;
                covariance += values[i] * value;
            }
            var referenceVariance = referenceSquared - referenceSum * referenceSum / valueCount;
            if (referenceVariance <= 1e-12) continue;
            var correlation = covariance / Math.Sqrt(variance * referenceVariance);
            if (double.IsFinite(correlation)) best = Math.Max(best, correlation);
        }
        return Math.Clamp(best, 0, 1);
    }

    /// <summary>
    /// Requires absolute artwork evidence and a meaningful reduction in mismatch
    /// over a different identity. The relative rule scales with the remaining
    /// mismatch; a fixed .04 margin would reject verified HDR meal captures.
    /// </summary>
    internal static bool IsDecisive(double best, double runnerUp) =>
        double.IsFinite(best) && double.IsFinite(runnerUp) && best is >= .9 and <= 1
        && best - runnerUp >= Math.Max(.012, .2 * (1 - best));
}
