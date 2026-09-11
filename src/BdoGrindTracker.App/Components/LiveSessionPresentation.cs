using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Components;

/// <summary>One presentation of the current session for the dashboard and every overlay module.</summary>
internal sealed class LiveSessionPresentation(TrackerState state)
{
    internal TimeSpan Elapsed => state.Elapsed < TimeSpan.Zero ? TimeSpan.Zero : state.Elapsed;
    internal bool CanShowSilver => state.Loot.Totals.Count == 0 || state.Silver.HasKnownValue;
    internal decimal? SilverHourly => CanShowSilver ? Hourly(state.Silver.AfterTax, Elapsed) : null;
    internal bool PartialSilver => CanShowSilver && !state.Silver.IsComplete;
    internal bool PartialSilverHourly => PartialSilver && SilverHourly is not null;
    internal string Duration => Presentation.Duration(Elapsed);
    internal string Trash => Presentation.Number(Presentation.Trash(state.Loot.Totals, state.SpotId));
    internal string TrashHourly => Elapsed == TimeSpan.Zero ? "0" :
        Hourly(Presentation.Trash(state.Loot.Totals, state.SpotId), Elapsed) is { } rate ? Presentation.Number(rate) : "—";
    internal string Silver => CanShowSilver ? Presentation.Silver(state.Silver.AfterTax) : "—";
    internal string SilverPerHour => SilverHourly is { } rate ? Presentation.Silver(rate) : "—";
    internal string SilverDetail => !state.Silver.IsComplete ? "Teilbetrag · Preise fehlen" :
        state.Silver.IsStale ? "Gespeicherte Marktpreise" : "Nach Marktsteuern";
    internal string Status => state.IsDemo ? "Vorschau" : state.IsSubmitted ? "Abgeschlossen" :
        state.IsError ? "Fehler" : state.IsRunning ? "Live" : state.HasSession ? "Pausiert" : "Bereit";
    internal string LootScroll => state.LootScroll.Status switch
    {
        LootScrollStatus.Active when state.LootScroll.Level is 1 or 2 => $"Aktiv · Lvl. {state.LootScroll.Level}",
        LootScrollStatus.Active => "Aktiv",
        LootScrollStatus.Inactive => "Inaktiv",
        _ => "Nicht erkannt",
    };
    internal bool LootScrollWarning => state.IsRunning && !state.IsDemo && state.LootScroll.ShouldWarn;
    internal string Agris => state.Agris.Status switch
    {
        AgrisStatus.Active => "Aktiv",
        AgrisStatus.Inactive => "Inaktiv",
        _ => "Nicht erkannt",
    };
    internal AgrisPresentation AgrisTime => new(state.AgrisActiveDuration, state.AgrisObservedDuration, Elapsed);
    internal ExperiencePresentation Experience => new(state.ExperienceGainedPercentagePoints, state.ExperienceObservedDuration,
        Elapsed, state.ExperienceStartLevel, state.ExperienceEndLevel, state.Experience.Level, state.Experience.Percent);
    internal GrindRatingPresentation GrindRating => new(GrindRatingEvaluator.Evaluate(state.SpotId,
        Presentation.Trash(state.Loot.Totals, state.SpotId), Elapsed, state.GrindBenchmark),
        state.Agris.Status == AgrisStatus.Active || state.AgrisActiveDuration > TimeSpan.Zero ||
        state.LootScroll.Status == LootScrollStatus.Inactive ||
        state.LootScroll.Status == LootScrollStatus.Active && state.LootScroll.Level == 1,
        state.GrindBenchmarkStatus);

    internal static decimal? Hourly(decimal amount, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero) return null;
        try { return Presentation.Hourly(amount, elapsed); }
        catch (OverflowException) { return null; }
    }
}
