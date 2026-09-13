using System.Runtime.InteropServices;
using BdoGrindTracker.Core;
using OpenCvSharp;
using DrawingRectangle = System.Drawing.Rectangle;
using CvSize = OpenCvSharp.Size;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Measures fading against stable item-specific glyphs, independently of OCR amounts.</summary>
internal sealed class NormalLootFadeTracker : IDisposable
{
    private const int MaximumTemplates = 320;
    private readonly Dictionary<(string Name, int Slot), Template> _templates = [];
    private Snapshot? _previous;
    private bool _disposed;

    public IReadOnlyList<LootObservation> Observe(Mat panel, IReadOnlyList<DrawingRectangle> bounds,
        IReadOnlyList<LootObservation> observations, DateTimeOffset capturedAt, float uiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(observations);
        if (panel.Empty() || panel.Type() != MatType.CV_8UC3 || !float.IsFinite(uiScale) || uiScale <= 0 ||
            observations.Any(row => row is null) || bounds.Count > 6 || bounds.Any(b =>
                b.X < 0 || b.Y < 0 || b.Width <= 0 || b.Height <= 0 ||
                (long)b.X + b.Width > panel.Width || (long)b.Y + b.Height > panel.Height))
            throw new ArgumentException("Fade tracking requires valid calibrated BGR rows.");
        var result = observations.Select(row => row with { FadeEvidence = null }).ToArray();
        if (_previous is { } latest && capturedAt <= latest.At) return result;

        var current = Capture(panel, bounds, observations, capturedAt, uiScale);
        var previous = _previous;
        _previous = current;
        if (previous is null || capturedAt - previous.At > NormalLootOccupancyTracker.MaximumFrameGap ||
            previous.Size != current.Size || previous.Scale != uiScale || !previous.Bounds.SequenceEqual(bounds))
        {
            _templates.Clear();
            return result;
        }

        // A common exposure/opacity change can dim young rows together. It is
        // not reliable age evidence, and its old brightness references must not
        // survive into later frames after the new exposure becomes stationary.
        if (HasUniformGainChange(previous, current, uiScale))
        {
            _templates.Clear();
            return result;
        }

        // A stationary bright plateau at each physical position seeds a reference.
        // Rasterization and background opacity differ between calibrated rows;
        // a bottom-row template cannot provide another position's absolute age.
        // A fading first image cannot seed itself, and anonymous OCR never seeds.
        for (var slot = 0; slot < current.Rows.Length; slot++)
        {
            if (current.Names[slot] is not { } name || previous.Names[slot] != name ||
                current.Rows[slot] is not { } glyph || previous.Rows[slot] is not { } oldGlyph ||
                MakeTemplate(oldGlyph, uiScale) is not { } oldTemplate ||
                Compare(oldTemplate, glyph) is not { Correlation: >= .98, ContrastRatio: >= .97 and <= 1.03 } ||
                MakeTemplate(glyph, uiScale) is not { } fresh || _templates.ContainsKey((name, slot))) continue;
            if (_templates.Count >= MaximumTemplates) _templates.Remove(_templates.Keys.First());
            _templates.Add((name, slot), fresh);
        }

        for (var index = 0; index < result.Length; index++)
        {
            var row = result[index];
            if (!Eligible(row) || row.Slot < 0 || row.Slot >= current.Rows.Length ||
                current.Rows[row.Slot] is not { } glyph || previous.Names[row.Slot] != row.ItemName ||
                previous.Rows[row.Slot] is not { } oldGlyph ||
                !_templates.TryGetValue((row.ItemName!, row.Slot), out var template))
                continue;
            var match = Compare(template, glyph);
            var oldMatch = Compare(template, oldGlyph);
            // Very weak remnants are unsafe age evidence. Full brightness needs
            // no annotation; it does not prove either a new or an existing drop.
            if (match is { Correlation: >= .90, ContrastRatio: >= .12 and <= .85 } &&
                oldMatch is { Correlation: >= .90 } && oldMatch.ContrastRatio - match.ContrastRatio >= .10)
                result[index] = row with { FadeEvidence = match };
        }
        return result;
    }

    public void Reset() { _previous = null; _templates.Clear(); }
    public void Dispose() { Reset(); _disposed = true; }

    private static bool Eligible(LootObservation row) => row is
        { Source: LootSource.Normal, ItemName: not null, RejectionReason: null, IsAlignmentAnchor: false } &&
        // OCR confidence crosses float/double calculations. Preserve an intended
        // 0.8 boundary without admitting meaningfully lower-confidence names.
        row.NameConfidence >= .8 - 1e-6;

    private static bool HasUniformGainChange(Snapshot previous, Snapshot current, float scale)
    {
        if (!previous.Names.SequenceEqual(current.Names)) return false;
        var ratios = new List<double>();
        for (var slot = 0; slot < current.Names.Length; slot++)
        {
            if (current.Names[slot] is null) continue;
            if (previous.Rows[slot] is not { } oldGlyph || current.Rows[slot] is not { } glyph ||
                MakeTemplate(oldGlyph, scale) is not { } template ||
                Compare(template, glyph) is not { Correlation: >= .95 } match)
                return false;
            ratios.Add(match.ContrastRatio);
        }
        if (ratios.Count < 2) return false;
        var minimum = ratios.Min();
        var maximum = ratios.Max();
        return minimum >= .15 && maximum <= 3 && maximum / minimum <= 1.10 &&
            (maximum <= .90 || minimum >= 1.10);
    }

    private static Snapshot Capture(Mat panel, IReadOnlyList<DrawingRectangle> bounds,
        IReadOnlyList<LootObservation> observations, DateTimeOffset at, float scale)
    {
        using var gray = new Mat();
        Cv2.CvtColor(panel, gray, ColorConversionCodes.BGR2GRAY);
        var rows = new Glyph?[bounds.Count];
        for (var slot = 0; slot < bounds.Count; slot++)
        {
            var b = bounds[slot];
            var left = checked((int)Math.Round(10 * scale));
            var top = checked((int)Math.Round(12 * scale));
            var width = Math.Min(checked((int)Math.Round(180 * scale)), b.Width - left);
            var height = checked((int)Math.Round(24 * scale));
            if (width < 20 || height < 8 || top < 1 || top + height + 1 > b.Height) continue;
            var pixels = new float[3][];
            for (var offset = -1; offset <= 1; offset++)
            {
                using var crop = new Mat(gray, new Rect(b.X + left, b.Y + top + offset, width, height));
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
        var names = new string?[bounds.Count];
        foreach (var observation in observations.Where(Eligible))
            if (observation.Slot >= 0 && observation.Slot < names.Length) names[observation.Slot] = observation.ItemName;
        return new(at, panel.Size(), scale, bounds.ToArray(), rows, names);
    }

    private static Template? MakeTemplate(Glyph row, float scale)
    {
        var pixels = row.Pixels[1];
        var mask = Enumerable.Range(0, pixels.Length).Where(index => Math.Abs(pixels[index]) >= 8).ToArray();
        var energy = mask.Sum(index => (double)pixels[index] * pixels[index]);
        if (mask.Length < 80 * scale * scale || energy / Math.Max(1, mask.Length) < 400 ||
            mask.Count(index => pixels[index] >= 12) < 30 * scale * scale ||
            mask.Count(index => pixels[index] <= -12) < 30 * scale * scale) return null;
        return new(row.Width, row.Height, pixels, mask, energy);
    }

    private static NormalLootFadeEvidence? Compare(Template template, Glyph current)
    {
        if (template.Width != current.Width || template.Height != current.Height) return null;
        NormalLootFadeEvidence? best = null;
        foreach (var pixels in current.Pixels)
        {
            double dot = 0, energy = 0;
            foreach (var index in template.Mask)
            {
                dot += template.Pixels[index] * pixels[index];
                energy += pixels[index] * pixels[index];
            }
            if (dot <= 0 || energy <= 0) continue;
            var correlation = Math.Clamp(dot / Math.Sqrt(template.Energy * energy), -1, 1);
            if (best is null || correlation > best.Correlation)
                best = new(dot / template.Energy, correlation);
        }
        return best;
    }

    private sealed record Glyph(int Width, int Height, float[][] Pixels);
    private sealed record Template(int Width, int Height, float[] Pixels, int[] Mask, double Energy);
    private sealed record Snapshot(DateTimeOffset At, CvSize Size, float Scale, DrawingRectangle[] Bounds,
        Glyph?[] Rows, string?[] Names);
}
