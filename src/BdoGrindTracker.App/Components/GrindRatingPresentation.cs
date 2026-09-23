using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.Components;

internal sealed class GrindRatingPresentation(GrindRatingResult result, bool differingLootBuffs = false,
    string? benchmarkStatus = null, string? language = "de")
{
    private string T(string text) => AppText.Translate(text, language);
    private string F(string text, params object[] args) => AppText.Format(text, language, args);
    internal GrindRatingResult Result => result;
    internal GrindRatingSpectrum? Spectrum => GrindRatingSpectrum.Create(result, language);
    internal string Label => result.Tier switch
    {
        GrindRatingTier.BelowAverage => T("Unter Average"),
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
        result.IsProvisional ? T("Vorläufig") : differingLootBuffs ? T("Abweichende Loot-Buffs") : null;
    internal string Description
    {
        get
        {
            var refreshStatus = string.IsNullOrWhiteSpace(benchmarkStatus) ? "" : " " + T(benchmarkStatus.Trim());
            if (result.Tier == GrindRatingTier.Unavailable || result.Benchmark is not { } benchmark)
                return T("Keine Bewertung verfügbar.") + refreshStatus;
            var thresholds = F("Average ab {0}", Presentation.Number(benchmark.AverageTrashPerHour, language));
            if (benchmark.HighTrashPerHour is { } high) thresholds += " · " + F("High ab {0}", Presentation.Number(high, language));
            if (benchmark.TopTrashPerHour is { } top) thresholds += " · " + F("Top ab {0}", Presentation.Number(top, language));
            return F("Trash / h der aktiven Session: {0}. {1} Trash / h.", Presentation.Number(result.TrashPerHour!.Value, language), thresholds) + " " +
                F("Referenz: {0} · Stand {1}.", T(benchmark.Conditions), benchmark.UpdatedAt.ToString("d", AppText.Culture(language))) + " " +
                F("Quelle: {0}", benchmark.SourceUrl) +
                (result.IsProvisional ? " · " + T("Vorläufig: weniger als 5 Minuten aktive Grindzeit.") : "") +
                (differingLootBuffs ? " · " + T("Abweichende Loot-Buffs: Der Vergleich berücksichtigt keine Buff-Korrektur.") : "") +
                refreshStatus;
        }
    }
}
