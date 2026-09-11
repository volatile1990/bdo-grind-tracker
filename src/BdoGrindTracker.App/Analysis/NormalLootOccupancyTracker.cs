using System.Runtime.InteropServices;
using BdoGrindTracker.Core;
using OpenCvSharp;
using DrawingRectangle = System.Drawing.Rectangle;
using CvSize = OpenCvSharp.Size;

namespace BdoGrindTracker.App.Analysis;

/// <summary>
/// Confirms glyph occupancy using a previous accepted row's mask. A compatible
/// template does not identify an event, supply an item, or determine a quantity.
/// </summary>
internal sealed class NormalLootOccupancyTracker : IDisposable
{
    internal static readonly TimeSpan MaximumFrameGap = TimeSpan.FromMilliseconds(600);
    private const double MinimumCorrelation = .90;
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
        if (observations.Any(row => row is null))
            throw new ArgumentException("Occupancy observations cannot contain null entries.", nameof(observations));
        if (panel.Empty() || panel.Type() != MatType.CV_8UC3)
            throw new ArgumentException("Occupancy tracking requires an original BGR panel.", nameof(panel));
        if (!float.IsFinite(uiScale) || uiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(uiScale));
        if (slotsNewestFirstRelativeToPanel.Count > 6 || slotsNewestFirstRelativeToPanel.Any(bounds =>
            bounds.Width <= 0 || bounds.Height <= 0 || bounds.X < 0 || bounds.Y < 0 ||
            (long)bounds.X + bounds.Width > panel.Width || (long)bounds.Y + bounds.Height > panel.Height))
            throw new ArgumentException("Occupancy slots must be inside the panel.", nameof(slotsNewestFirstRelativeToPanel));

        // Duplicate or out-of-order input cannot replace the last ordered image.
        if (_previous is { } latest && capturedAt <= latest.CapturedAt)
            return WithoutEvidence(observations);
        var current = Capture(panel, slotsNewestFirstRelativeToPanel, observations, capturedAt, uiScale);
        var previous = _previous;
        _previous = current;
        var result = WithoutEvidence(observations);
        if (previous is null || capturedAt - previous.CapturedAt > MaximumFrameGap ||
            previous.PanelSize != current.PanelSize || previous.UiScale != uiScale ||
            !previous.Bounds.SequenceEqual(current.Bounds))
            return result;

        for (var index = 0; index < result.Length; index++)
        {
            var observation = result[index];
            if (!Accepted(observation) || observation.Slot < 0 || observation.Slot >= current.Rows.Length ||
                current.Rows[observation.Slot] is not { } row)
                continue;
            var matches = new List<NormalLootOccupancyMatch>();
            for (var slot = 0; slot < previous.Rows.Length; slot++)
            {
                if (previous.Names[slot] is not { } previousName || previousName != observation.ItemName ||
                    previous.Rows[slot] is not { } old || old.Width != row.Width || old.Height != row.Height ||
                    !CreateMask(old, uiScale, out var mask, out var previousEnergy))
                    continue;
                // Mask and minimum energy belong to the accepted previous image.
                // Fading current glyphs need not pass the source-mask admission.
                var match = Compare(old.Pixels[1], row, mask, previousEnergy, slot);
                if (match is not null) matches.Add(match);
            }
            if (matches.Count > 0)
                result[index] = observation with { OccupancyEvidence = new(matches) };
        }
        return result;
    }

    public void Reset() => _previous = null;

    public void Dispose()
    {
        Reset();
        _disposed = true;
    }

    private static LootObservation[] WithoutEvidence(IReadOnlyList<LootObservation> observations) =>
        observations.Select(row => row.OccupancyEvidence is null ? row : row with { OccupancyEvidence = null }).ToArray();

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
            // Same calibrated name band as the recorded reverse-template probe;
            // quantity digits and the icon do not supply occupancy evidence.
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
        return mask.Length >= 80 * scale * scale && energy / Math.Max(1, mask.Length) >= 20 * 20 &&
            mask.Count(index => data[index] >= 12) >= 30 * scale * scale &&
            mask.Count(index => data[index] <= -12) >= 30 * scale * scale;
    }

    private static NormalLootOccupancyMatch? Compare(float[] previousTemplate, GlyphRow current,
        int[] mask, double previousEnergy, int previousSlot)
    {
        NormalLootOccupancyMatch? best = null;
        foreach (var pixels in current.Pixels)
        {
            double dot = 0, energy = 0;
            foreach (var index in mask)
            {
                dot += previousTemplate[index] * pixels[index];
                energy += pixels[index] * pixels[index];
            }
            if (energy <= 0 || dot <= 0) continue;
            var correlation = Math.Clamp(dot / Math.Sqrt(previousEnergy * energy), -1, 1);
            if (correlation < MinimumCorrelation || best is not null && correlation <= best.Correlation) continue;
            best = new(previousSlot, correlation);
        }
        return best;
    }

    private sealed record GlyphRow(int Width, int Height, float[][] Pixels);
    private sealed record Snapshot(DateTimeOffset CapturedAt, CvSize PanelSize, float UiScale,
        DrawingRectangle[] Bounds, GlyphRow?[] Rows, string?[] Names);
}
