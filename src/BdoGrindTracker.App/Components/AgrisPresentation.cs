using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.Components;

/// <summary>Displays only the Agris durations observed by the session; missing observations are never zero activity.</summary>
internal sealed class AgrisPresentation(TimeSpan? active, TimeSpan? observed, TimeSpan duration, string? language = "de")
{
    internal bool HasObservation => active is not null && observed is { } known && known > TimeSpan.Zero;
    private TimeSpan SessionDuration => duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
    private TimeSpan Observed => TimeSpan.FromTicks(Math.Clamp(observed?.Ticks ?? 0, 0, SessionDuration.Ticks));
    private TimeSpan Active => TimeSpan.FromTicks(Math.Clamp(active?.Ticks ?? 0, 0, Observed.Ticks));
    private bool IsPartial => Observed < SessionDuration;
    internal string Duration => HasObservation ? $"≈ {ShortTime(Active)}{(IsPartial ? " *" : "")}" : "—";
    internal string Description => !HasObservation
        ? AppText.Translate(active is null || observed is null ? "Agris nicht erfasst." : "Agris nicht erkannt.", language)
        : AppText.Format("Agris aktiv: ungefähr {0}", language, ShortTime(Active)) +
            (IsPartial ? " " + AppText.Format("Nicht erkannt: {0}", language, ShortTime(SessionDuration - Observed)) : "");

    private string ShortTime(TimeSpan value) => value > TimeSpan.Zero && value < TimeSpan.FromMinutes(1)
        ? AppText.Translate("unter 1 Min.", language) : Presentation.ShortDuration(value, language);
}
