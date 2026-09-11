using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Components;

internal sealed class GrindRatingPresentation(GrindRatingResult result, bool differingLootBuffs = false,
    string? benchmarkStatus = null)
{
    internal GrindRatingResult Result => result;
    internal string Label => result.Tier switch
    {
        GrindRatingTier.BelowAverage => "Unter Average",
        GrindRatingTier.Average => "Average Tier",
        GrindRatingTier.High => "High Tier",
        GrindRatingTier.Top => "Top Tier",
        _ => "—",
    };
    internal OverlayMetricTone Tone => result.Tier switch
    {
        GrindRatingTier.High => OverlayMetricTone.Positive,
        GrindRatingTier.Top => OverlayMetricTone.Accent,
        GrindRatingTier.Average => OverlayMetricTone.Default,
        _ => OverlayMetricTone.Muted,
    };
    internal string ToneClass => "metric-tone-" + Tone.ToString().ToLowerInvariant();
    internal string? Detail => result.Tier == GrindRatingTier.Unavailable ? null :
        result.IsProvisional ? "Vorläufig" : differingLootBuffs ? "Abweichende Loot-Buffs" : null;
    internal string Description
    {
        get
        {
            var refreshStatus = string.IsNullOrWhiteSpace(benchmarkStatus) ? "" : $" {benchmarkStatus.Trim()}";
            if (result.Tier == GrindRatingTier.Unavailable || result.Benchmark is not { } benchmark)
                return "Keine Bewertung verfügbar." + refreshStatus;
            var thresholds = $"Average ab {Presentation.Number(benchmark.AverageTrashPerHour)}";
            if (benchmark.HighTrashPerHour is { } high) thresholds += $" · High ab {Presentation.Number(high)}";
            if (benchmark.TopTrashPerHour is { } top) thresholds += $" · Top ab {Presentation.Number(top)}";
            return $"Trash / h der aktiven Session: {Presentation.Number(result.TrashPerHour!.Value)}. {thresholds} Trash / h. " +
                $"Referenz: {benchmark.Conditions} · Stand {benchmark.UpdatedAt.ToString("dd.MM.yyyy", Presentation.German)}. " +
                $"Quelle: {benchmark.SourceUrl}" +
                (result.IsProvisional ? " · Vorläufig: weniger als 5 Minuten aktive Grindzeit." : "") +
                (differingLootBuffs ? " · Abweichende Loot-Buffs: Der Vergleich berücksichtigt keine Buff-Korrektur." : "") +
                refreshStatus;
        }
    }
}
