namespace BdoGrindTracker.App.Analysis;

/// <summary>Bounded, managed pixel work shared by the standby detector and its synthetic image tests.</summary>
internal sealed class GrindStartVisualEvidence
{
    private readonly int _width;
    private readonly float[] _contrast;
    private readonly bool[] _glyphMask;

    private GrindStartVisualEvidence(int width, float[] contrast, bool[] glyphMask, bool plausible)
    {
        _width = width;
        _contrast = contrast;
        _glyphMask = glyphMask;
        IsPlausible = plausible;
    }

    internal bool IsPlausible { get; }

    internal static GrindStartVisualEvidence Create(byte[] grayscale, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(grayscale);
        if (width < 1 || height < 1 || (long)width * height != grayscale.Length)
            throw new ArgumentException("The grayscale image dimensions must match its pixels.");
        var contrast = HighPass(grayscale, width, height);
        var foreground = new bool[grayscale.Length];
        var foregroundCount = 0;
        for (var index = 0; index < grayscale.Length; index++)
            if (grayscale[index] >= 60 && contrast[index] >= 12)
            {
                foreground[index] = true;
                foregroundCount++;
            }

        var mask = new bool[grayscale.Length];
        if (foregroundCount < 55 || foregroundCount > grayscale.Length * .35)
            return new(width, contrast, mask, false);

        var components = FindGlyphs(foreground, width, height);
        // Text has several small components along a common baseline. Reject
        // speckles, broad scene edges and dense texture before comparing images.
        var aligned = components.Select(anchor => components.Where(component =>
                Math.Abs(component.Bottom - anchor.Bottom) <= 3).ToArray())
            .OrderByDescending(group => group.Sum(component => component.Pixels.Count)).FirstOrDefault();
        if (aligned is null || aligned.Length < 4 ||
            aligned.Sum(component => component.Pixels.Count) < Math.Max(55, foregroundCount * .6) ||
            aligned.Max(component => component.Right) - aligned.Min(component => component.Left) < 25)
            return new(width, contrast, mask, false);

        foreach (var component in aligned)
        foreach (var pixel in component.Pixels)
            mask[pixel] = true;
        return new(width, contrast, mask, true);
    }

    internal bool IsNewComparedTo(GrindStartVisualEvidence previous)
    {
        if (!IsPlausible || _width != previous._width || _contrast.Length != previous._contrast.Length)
            return false;
        if (!previous.IsPlausible) return true;

        double bestCorrelation = -1, contrastRatio = 1;
        var bestOffset = 0;
        for (var verticalOffset = -1; verticalOffset <= 1; verticalOffset++)
        {
            double dot = 0, currentEnergy = 0, previousEnergy = 0;
            for (var index = 0; index < _contrast.Length; index++)
            {
                var previousIndex = index + verticalOffset * _width;
                if (previousIndex < 0 || previousIndex >= previous._contrast.Length ||
                    !_glyphMask[index] && !previous._glyphMask[previousIndex]) continue;
                var current = _contrast[index];
                var old = previous._contrast[previousIndex];
                dot += current * old;
                currentEnergy += current * current;
                previousEnergy += old * old;
            }
            var correlation = currentEnergy > 0 && previousEnergy > 0
                ? dot / Math.Sqrt(currentEnergy * previousEnergy) : 0;
            if (correlation <= bestCorrelation) continue;
            bestCorrelation = correlation;
            contrastRatio = previousEnergy > 0 ? Math.Sqrt(currentEnergy / previousEnergy) : 1;
            bestOffset = verticalOffset;
        }

        // Stable glyphs survive background movement, exposure changes and normal
        // fade-out. A much brighter copy can be a fresh drop of the same item.
        return bestCorrelation < .83 || contrastRatio >= 1.65 || HasChangedGlyphRegion(previous, bestOffset);
    }

    private bool HasChangedGlyphRegion(GrindStartVisualEvidence previous, int verticalOffset)
    {
        // A quantity digit may change while a long item name remains identical.
        // Compare overlapping 32-pixel windows so the unchanged prefix cannot
        // drown out that signal. Require substantial, admitted glyph pixels.
        const int step = 16;
        var bins = new (double Dot, double Current, double Previous, int Pixels)[(_width + step - 1) / step];
        for (var index = 0; index < _contrast.Length; index++)
        {
            var oldIndex = index + verticalOffset * _width;
            if (oldIndex < 0 || oldIndex >= previous._contrast.Length ||
                !_glyphMask[index] && !previous._glyphMask[oldIndex]) continue;
            ref var bin = ref bins[index % _width / step];
            var current = _contrast[index];
            var old = previous._contrast[oldIndex];
            bin.Dot += current * old;
            bin.Current += current * current;
            bin.Previous += old * old;
            if (_glyphMask[index]) bin.Pixels++;
        }
        for (var index = 0; index < bins.Length - 1; index++)
        {
            var left = bins[index];
            var right = bins[index + 1];
            if (left.Pixels + right.Pixels < 35 || left.Current + right.Current < 35 * 30 * 30) continue;
            var denominator = Math.Sqrt((left.Current + right.Current) * (left.Previous + right.Previous));
            if (denominator == 0 || (left.Dot + right.Dot) / denominator < .70) return true;
        }
        return false;
    }

    private static float[] HighPass(byte[] pixels, int width, int height)
    {
        var stride = width + 1;
        var integral = new int[stride * (height + 1)];
        for (var y = 0; y < height; y++)
        {
            var sum = 0;
            for (var x = 0; x < width; x++)
            {
                sum += pixels[y * width + x];
                integral[(y + 1) * stride + x + 1] = integral[y * stride + x + 1] + sum;
            }
        }
        var result = new float[pixels.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var left = Math.Max(0, x - 3);
            var right = Math.Min(width, x + 4);
            var top = Math.Max(0, y - 3);
            var bottom = Math.Min(height, y + 4);
            var sum = integral[bottom * stride + right] - integral[top * stride + right] -
                integral[bottom * stride + left] + integral[top * stride + left];
            result[y * width + x] = pixels[y * width + x] - sum / (float)((right - left) * (bottom - top));
        }
        return result;
    }

    private static List<Glyph> FindGlyphs(bool[] foreground, int width, int height)
    {
        var visited = new bool[foreground.Length];
        var queue = new Queue<int>();
        var glyphs = new List<Glyph>();
        for (var start = 0; start < foreground.Length; start++)
        {
            if (!foreground[start] || visited[start]) continue;
            var pixels = new List<int>();
            var left = width;
            var right = 0;
            var top = height;
            var bottom = 0;
            queue.Enqueue(start);
            visited[start] = true;
            while (queue.TryDequeue(out var index))
            {
                pixels.Add(index);
                var x = index % width;
                var y = index / width;
                left = Math.Min(left, x);
                right = Math.Max(right, x + 1);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var neighborX = x + dx;
                    var neighborY = y + dy;
                    if (neighborX < 0 || neighborX >= width || neighborY < 0 || neighborY >= height) continue;
                    var neighbor = neighborY * width + neighborX;
                    if (visited[neighbor] || !foreground[neighbor]) continue;
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }
            if (pixels.Count is >= 5 and <= 220 && right - left <= 24 && bottom - top is >= 5 and <= 21)
                glyphs.Add(new(left, right, bottom, pixels));
        }
        return glyphs;
    }

    private sealed record Glyph(int Left, int Right, int Bottom, List<int> Pixels);
}
