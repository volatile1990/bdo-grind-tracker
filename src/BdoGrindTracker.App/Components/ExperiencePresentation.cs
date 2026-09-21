using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.Components;

/// <summary>Net percentage-point changes read from the level display, using the session's active duration.</summary>
internal sealed class ExperiencePresentation(decimal? gained, TimeSpan? observed, TimeSpan duration,
    int? startLevel = null, int? endLevel = null, int? currentLevel = null, decimal? currentPercent = null,
    string? language = "de")
{
    internal bool HasObservation => gained is not null && observed is { } known && known > TimeSpan.Zero;
    internal bool IsLoss => HasObservation && gained < 0;
    private bool IsPartial => HasObservation && observed < duration;
    private string Estimate => IsPartial ? "≈ " : "";
    private decimal? HourlyValue => HasObservation ? LiveSessionPresentation.Hourly(gained!.Value, duration) : null;
    internal string Gain => HasObservation ? Estimate + Percentage(gained!.Value) : "—";
    internal string Hourly => HourlyValue is { } value ? Estimate + Percentage(value) : "—";
    internal string Description
    {
        get
        {
            var description = AppText.Translate(HasObservation
                ? "Nettozuwachs in Prozentpunkten der Levelanzeige. Stundenwert bezogen auf die aktive Sessionzeit."
                : observed is null ? "Erfahrung nicht erfasst." : "Noch kein Erfahrungszuwachs erkannt.", language);
            if (IsPartial) description += " " + AppText.Format("Teilweise erfasst: {0} von {1}", language, ShortTime(observed!.Value), ShortTime(duration));
            if (startLevel is { } from && endLevel is { } to && from != to)
                description += " " + AppText.Format("Lvl. {0} → {1}; Levelwechsel in Prozentpunkten summiert.", language, from, to);
            if (currentLevel is > 0 && currentPercent is >= 0 and < 100)
                description += " " + AppText.Format("Aktuell: Lvl. {0} · {1} %.", language, currentLevel, currentPercent.Value.ToString("0.000", AppText.Culture(language)));
            return description;
        }
    }

    private string Percentage(decimal value) => value.ToString("+0.000;-0.000;+0.000", AppText.Culture(language)) + " %";
    private string ShortTime(TimeSpan value) => value > TimeSpan.Zero && value < TimeSpan.FromMinutes(1)
        ? AppText.Translate("unter 1 Min.", language) : Presentation.ShortDuration(value, language);
}
