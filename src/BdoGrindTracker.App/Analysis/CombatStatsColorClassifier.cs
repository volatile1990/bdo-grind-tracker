using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Classifies only the foreground strokes inside OCR number bounds.</summary>
internal static class CombatStatsColorClassifier
{
    internal static CombatStatsCategory? Classify(IReadOnlyList<Color> pixels)
    {
        if (pixels.Count < 8) return null;
        var brightness = pixels.Select(value => Math.Max(value.R, Math.Max(value.G, value.B))).Order().ToArray();
        var minimum = Math.Max(150, brightness[brightness.Length / 2] + 22);
        var votes = new int[4];
        var foreground = 0;
        foreach (var pixel in pixels)
        {
            var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
            if (max < minimum) continue;
            foreground++;
            var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
            var saturation = (max - min) / (double)max;
            var hue = pixel.GetHue();
            if (saturation <= .06) votes[(int)CombatStatsCategory.General]++;
            else if (saturation >= .075 && hue is >= 250 and <= 305) votes[(int)CombatStatsCategory.Edania]++;
            else if (saturation >= .10 && hue is >= 18 and <= 68) votes[(int)CombatStatsCategory.Demihuman]++;
            else if (saturation >= .075 && hue is >= 185 and <= 240) votes[(int)CombatStatsCategory.Kamasylvian]++;
        }
        if (foreground < 8) return null;
        var best = Array.IndexOf(votes, votes.Max());
        return votes[best] >= 8 && votes[best] >= foreground * .65 ? (CombatStatsCategory)best : null;
    }

}
