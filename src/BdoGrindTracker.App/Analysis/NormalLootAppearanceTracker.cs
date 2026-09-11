using System.Runtime.InteropServices;
using BdoGrindTracker.Core;
using OpenCvSharp;
using DrawingRectangle = System.Drawing.Rectangle;
using CvSize = OpenCvSharp.Size;

namespace BdoGrindTracker.App.Analysis;

/// <summary>
/// Measures whether a current glyph rendering can continue each previous physical
/// row. Original grayscale contrast survives OCR failures and is independent of
/// item/quantity voting. Pixel evidence never emits an item or invents a quantity.
/// </summary>
internal sealed class NormalLootAppearanceTracker : IDisposable
{
    internal static readonly TimeSpan MaximumFrameGap = TimeSpan.FromMilliseconds(600);
    private const double MinimumCorrelation = .90;
    private const double MinimumFadedRatio = .15;
    private const double MaximumFadedRatio = .65;
    private Snapshot? _previous;
    private bool _disposed;

    public IReadOnlyList<LootObservation> Observe(Mat panel,
        IReadOnlyList<DrawingRectangle> slotsNewestFirstRelativeToPanel,
        IReadOnlyList<LootObservation> observations, DateTimeOffset capturedAt, float uiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(slotsNewestFirstRelativeToPanel);
        ArgumentNullException.ThrowIfNull(observations);
        if (panel.Empty() || panel.Type() != MatType.CV_8UC3)
            throw new ArgumentException("Appearance tracking requires an original BGR panel.", nameof(panel));
        if (!float.IsFinite(uiScale) || uiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(uiScale));
        if (slotsNewestFirstRelativeToPanel.Count > 6 || slotsNewestFirstRelativeToPanel.Any(bounds =>
            bounds.Width <= 0 || bounds.Height <= 0 || bounds.X < 0 || bounds.Y < 0 ||
            bounds.Right > panel.Width || bounds.Bottom > panel.Height))
            throw new ArgumentException("Appearance slots must be inside the panel.", nameof(slotsNewestFirstRelativeToPanel));

        // A duplicate/out-of-order frame must not replace the last ordered snapshot.
        if (_previous is { } latest && capturedAt <= latest.CapturedAt)
            return WithoutEvidence(observations);
        var current = Capture(panel, slotsNewestFirstRelativeToPanel, observations, capturedAt, uiScale);
        var previous = _previous;
        _previous = current;
        if (previous is null || capturedAt - previous.CapturedAt > MaximumFrameGap ||
            previous.PanelSize != current.PanelSize || previous.UiScale != uiScale ||
            !previous.Bounds.SequenceEqual(current.Bounds))
            return WithoutEvidence(observations);

        var result = WithoutEvidence(observations);
        for (var index = 0; index < result.Length; index++)
        {
            var observation = result[index];
            if (!Accepted(observation) || observation.Slot < 0 || observation.Slot >= current.Rows.Length ||
                current.Rows[observation.Slot] is not { } row || !CreateMask(row, uiScale, out var mask, out var energy))
                continue;
            var matches = new List<NormalLootAppearanceMatch>();
            var faded = 0;
            for (var slot = 0; slot < previous.Rows.Length; slot++)
            {
                if (previous.Rows[slot] is not { } old || old.Width != row.Width || old.Height != row.Height ||
                    previous.Names[slot] is { } name && name != observation.ItemName)
                    continue;
                var match = Compare(row.Pixels[1], old, mask, energy, slot);
                if (match is null) continue;
                matches.Add(match);
                if (match.PreviousContrastRatio is >= MinimumFadedRatio and <= MaximumFadedRatio)
                    faded |= 1 << slot;
            }
            if (matches.Count > 0)
                result[index] = observation with { AppearanceEvidence = new(faded, matches) };
        }

        // A uniform opacity/exposure increase of the whole visible log is not a
        // reliable arrival. Retain diagnostics but suppress hard incompatibilities
        // when every readable row changes by essentially the same multiplier.
        var readable = result.Where(Accepted).ToArray();
        var sameSlot = readable.Select(row => row.AppearanceEvidence?.Matches
            .FirstOrDefault(match => match.PreviousSlot == row.Slot)).ToArray();
        if (readable.Length >= 2 && sameSlot.All(match => match is
                { PreviousContrastRatio: >= MinimumFadedRatio and <= MaximumFadedRatio }) &&
            sameSlot.Max(match => match!.PreviousContrastRatio) - sameSlot.Min(match => match!.PreviousContrastRatio) <= .10)
            for (var index = 0; index < result.Length; index++)
                if (result[index].AppearanceEvidence is { } evidence)
                    result[index] = result[index] with { AppearanceEvidence = evidence with { FadedPreviousSlots = 0 } };
        return result;
    }

    public void Reset() => _previous = null;

    public void Dispose()
    {
        Reset();
        _disposed = true;
    }

    private static LootObservation[] WithoutEvidence(IReadOnlyList<LootObservation> observations) =>
        observations.Select(row => row.AppearanceEvidence is null ? row : row with { AppearanceEvidence = null }).ToArray();

    private static bool Accepted(LootObservation row) => row is
        { Source: LootSource.Normal, ItemName: not null, RejectionReason: null, IsAlignmentAnchor: false, NameConfidence: >= .8 };

    private static Snapshot Capture(Mat panel, IReadOnlyList<DrawingRectangle> bounds,
        IReadOnlyList<LootObservation> observations, DateTimeOffset at, float scale)
    {
        var rows = new GlyphRow?[bounds.Count];
        var names = new string?[bounds.Count];
        using var gray = new Mat();
        Cv2.CvtColor(panel, gray, ColorConversionCodes.BGR2GRAY);
        for (var slot = 0; slot < bounds.Count; slot++)
        {
            var band = bounds[slot];
            // Slot crops already exclude the icon. Keep the central name line and
            // a bounded prefix: distant quantity digits/panel edges cannot dominate.
            var left = checked((int)Math.Round(10 * scale));
            var top = checked((int)Math.Round(12 * scale));
            var width = Math.Min(checked((int)Math.Round(180 * scale)), band.Width - left);
            var height = checked((int)Math.Round(24 * scale));
            if (width < 20 || height < 8 || top < 1 || top + height + 1 > band.Height) continue;
            var pixels = new float[3][];
            for (var offset = -1; offset <= 1; offset++)
            {
                using var crop = new Mat(gray, new Rect(band.X + left, band.Y + top + offset, width, height));
                using var original = new Mat();
                using var blurred = new Mat();
                using var highpass = new Mat();
                crop.ConvertTo(original, MatType.CV_32FC1);
                Cv2.GaussianBlur(original, blurred, new CvSize(), scale * 4d / 3d);
                Cv2.Subtract(original, blurred, highpass);
                pixels[offset + 1] = new float[checked(width * height)];
                Marshal.Copy(highpass.Data, pixels[offset + 1], 0, pixels[offset + 1].Length);
            }
            rows[slot] = new(width, height, pixels);
        }
        foreach (var observation in observations.Where(Accepted))
            if (observation.Slot >= 0 && observation.Slot < names.Length) names[observation.Slot] = observation.ItemName;
        return new(at, panel.Size(), scale, bounds.ToArray(), rows, names);
    }

    private static bool CreateMask(GlyphRow row, float scale, out int[] mask, out double energy)
    {
        var data = row.Pixels[1];
        mask = Enumerable.Range(0, data.Length).Where(index => Math.Abs(data[index]) >= 8).ToArray();
        energy = mask.Sum(index => (double)data[index] * data[index]);
        // No decision from uniform bands, tiny marks, or a nearly vanished glyph.
        return mask.Length >= 80 * scale * scale && energy / Math.Max(1, mask.Length) >= 20 * 20 &&
            mask.Count(index => data[index] >= 12) >= 30 * scale * scale &&
            mask.Count(index => data[index] <= -12) >= 30 * scale * scale;
    }

    private static NormalLootAppearanceMatch? Compare(float[] current, GlyphRow previous,
        int[] mask, double currentEnergy, int previousSlot)
    {
        NormalLootAppearanceMatch? best = null;
        foreach (var pixels in previous.Pixels)
        {
            double dot = 0, energy = 0;
            foreach (var index in mask)
            {
                dot += current[index] * pixels[index];
                energy += pixels[index] * pixels[index];
            }
            if (energy <= 0 || dot <= 0) continue;
            var correlation = Math.Clamp(dot / Math.Sqrt(currentEnergy * energy), -1, 1);
            if (correlation < MinimumCorrelation || best is not null && correlation <= best.Correlation) continue;
            best = new(previousSlot, correlation, dot / currentEnergy);
        }
        return best;
    }

    private sealed record GlyphRow(int Width, int Height, float[][] Pixels);
    private sealed record Snapshot(DateTimeOffset CapturedAt, CvSize PanelSize, float UiScale,
        DrawingRectangle[] Bounds, GlyphRow?[] Rows, string?[] Names);
}
