using System.Globalization;

namespace BdoGrindTracker.App.Components;

/// <summary>Net percentage-point changes read from the level display, using the session's active duration.</summary>
internal sealed class ExperiencePresentation(decimal? gained, TimeSpan? observed, TimeSpan duration,
    int? startLevel = null, int? endLevel = null, int? currentLevel = null, decimal? currentPercent = null)
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
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
            var description = HasObservation
                ? "Nettozuwachs in Prozentpunkten der Levelanzeige. Stundenwert bezogen auf die aktive Sessionzeit."
                : observed is null ? "Erfahrung nicht erfasst." : "Noch kein Erfahrungszuwachs erkannt.";
            if (IsPartial) description += $" Teilweise erfasst: {ShortTime(observed!.Value)} von {ShortTime(duration)}";
            if (startLevel is { } from && endLevel is { } to && from != to)
                description += $" Lvl. {from} → {to}; Levelwechsel in Prozentpunkten summiert.";
            if (currentLevel is > 0 && currentPercent is >= 0 and < 100)
                description += $" Aktuell: Lvl. {currentLevel} · {currentPercent.Value.ToString("0.000", German)} %.";
            return description;
        }
    }

    private static string Percentage(decimal value) => value.ToString("+0.000;-0.000;+0.000", German) + " %";
    private static string ShortTime(TimeSpan value) => value > TimeSpan.Zero && value < TimeSpan.FromMinutes(1)
        ? "unter 1 Min." : Presentation.ShortDuration(value);
}
