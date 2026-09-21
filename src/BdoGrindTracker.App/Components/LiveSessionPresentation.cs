using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.Components;

/// <summary>One presentation of the current session for the dashboard and every overlay module.</summary>
internal sealed class LiveSessionPresentation(TrackerState state, string? language = "de")
{
    private string T(string text) => AppText.Translate(text, language);
    internal TimeSpan Elapsed => state.Elapsed < TimeSpan.Zero ? TimeSpan.Zero : state.Elapsed;
    internal bool CanShowSilver => state.Loot.Totals.Count == 0 || state.Silver.HasKnownValue;
    internal decimal? SilverHourly => CanShowSilver ? Hourly(state.Silver.AfterTax, Elapsed) : null;
    internal bool PartialSilver => CanShowSilver && !state.Silver.IsComplete;
    internal bool PartialSilverHourly => PartialSilver && SilverHourly is not null;
    internal string Duration => Presentation.Duration(Elapsed);
    internal string DurationNote => T(state.IsDemo ? "Beispielsession" :
        state.IsRunning && state.IsWaitingForFirstDrop
            ? Elapsed == TimeSpan.Zero ? "Wartet auf den ersten Drop" : "Wartet auf den nächsten Drop"
            : "Ab erstem Drop · ohne Pausen");
    internal string DurationDescription => T("Nach jedem Start oder Fortsetzen beginnt die aktive Zeit erst mit dem ersten neu erkannten Drop. " +
        "Die Wartezeit bis dahin und Pausen zählen nicht mit. Bereits erfasste aktive Zeit bleibt erhalten.");
    internal string Trash => Presentation.Number(Presentation.Trash(state.Loot.Totals, state.SpotId), language);
    internal string TrashHourly => Elapsed == TimeSpan.Zero ? "0" :
        Hourly(Presentation.Trash(state.Loot.Totals, state.SpotId), Elapsed) is { } rate ? Presentation.Number(rate, language) : "—";
    internal string Silver => CanShowSilver ? Presentation.Silver(state.Silver.AfterTax, language) : "—";
    internal string SilverPerHour => SilverHourly is { } rate ? Presentation.Silver(rate, language) : "—";
    internal string SilverDetail => T(!state.Silver.IsComplete ? "Teilbetrag · Preise fehlen" :
        state.Silver.IsStale ? "Gespeicherte Marktpreise" : "Nach Marktsteuern");
    internal string Status => T(state.IsDemo ? "Vorschau" : state.IsSubmitted ? "Abgeschlossen" :
        state.IsError ? "Fehler" : state.IsRunning ? "Live" : state.HasSession ? "Pausiert" : "Bereit");
    internal string LootScroll => state.LootScroll.Status switch
    {
        LootScrollStatus.Active when state.LootScroll.Level is 1 or 2 => AppText.Format("Aktiv · Lvl. {0}", language, state.LootScroll.Level),
        LootScrollStatus.Active => T("Aktiv"),
        LootScrollStatus.Inactive => T("Inaktiv"),
        _ => T("Nicht erkannt"),
    };
    internal bool LootScrollWarning => state.IsRunning && !state.IsDemo && state.LootScroll.ShouldWarn;
    internal string Agris => state.Agris.Status switch
    {
        AgrisStatus.Active => T("Aktiv"),
        AgrisStatus.Inactive => T("Inaktiv"),
        _ => T("Nicht erkannt"),
    };
    internal AgrisPresentation AgrisTime => new(state.AgrisActiveDuration, state.AgrisObservedDuration, Elapsed, language);
    internal ExperiencePresentation Experience => new(state.ExperienceGainedPercentagePoints, state.ExperienceObservedDuration,
        Elapsed, state.ExperienceStartLevel, state.ExperienceEndLevel, state.Experience.Level, state.Experience.Percent, language);
    internal GrindRatingPresentation GrindRating => new(GrindRatingEvaluator.Evaluate(state.SpotId,
        Presentation.Trash(state.Loot.Totals, state.SpotId), Elapsed, state.GrindBenchmark),
        state.Agris.Status == AgrisStatus.Active || state.AgrisActiveDuration > TimeSpan.Zero ||
        state.LootScroll.Status == LootScrollStatus.Inactive ||
        state.LootScroll.Status == LootScrollStatus.Active && state.LootScroll.Level == 1,
        state.GrindBenchmarkStatus, language);

    internal static decimal? Hourly(decimal amount, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero) return null;
        try { return Presentation.Hourly(amount, elapsed); }
        catch (OverflowException) { return null; }
    }
}
