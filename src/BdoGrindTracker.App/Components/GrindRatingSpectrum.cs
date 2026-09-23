using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Components;

public sealed record GrindRatingStop(string Label, decimal TrashPerHour, double Position, string Value,
    OverlayMetricTone Tone);

/// <summary>Interpolates within the available benchmark intervals, not population percentiles.</summary>
public sealed record GrindRatingSpectrum(double Position, IReadOnlyList<GrindRatingStop> Stops,
    string ProgressLabel, string GapLabel, string Description, string TrashHourly)
{
    public bool Equals(GrindRatingSpectrum? other) => other is not null && Position == other.Position &&
        ProgressLabel == other.ProgressLabel && GapLabel == other.GapLabel && Description == other.Description &&
        TrashHourly == other.TrashHourly && Stops.SequenceEqual(other.Stops);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Position);
        hash.Add(ProgressLabel);
        hash.Add(GapLabel);
        hash.Add(Description);
        hash.Add(TrashHourly);
        foreach (var stop in Stops) hash.Add(stop);
        return hash.ToHashCode();
    }

    internal static GrindRatingSpectrum? Create(GrindRatingResult result, string? language)
    {
        if (result.Tier == GrindRatingTier.Unavailable || result.Benchmark is not { } benchmark ||
            result.TrashPerHour is not { } rate) return null;

        string F(string text, params object[] args) => AppText.Format(text, language, args);
        string Number(decimal value) => Presentation.Number(value, language);
        var references = new List<(string Label, decimal Rate, OverlayMetricTone Tone)>
        {
            ("Average", benchmark.AverageTrashPerHour, OverlayMetricTone.Default),
        };
        if (benchmark.HighTrashPerHour is { } high) references.Add(("High", high, OverlayMetricTone.Positive));
        if (benchmark.TopTrashPerHour is { } top) references.Add(("Top", top, OverlayMetricTone.Accent));

        // Shared thresholds occupy one position. Never invent absent High/Top references.
        var groups = references.GroupBy(reference => reference.Rate).ToArray();
        var stops = groups.Select((group, index) => new GrindRatingStop(
            string.Join(" / ", group.Select(reference => reference.Label)), group.Key,
            (index + 1d) / (groups.Length + 1) * 100, Number(group.Key), group.Last().Tone)).ToArray();
        var nextIndex = Array.FindIndex(stops, stop => rate < stop.TrashPerHour);
        double position;
        string progress;
        string gap;
        if (nextIndex >= 0)
        {
            var next = stops[nextIndex];
            var previous = nextIndex > 0 ? stops[nextIndex - 1] : null;
            var fraction = (rate - (previous?.TrashPerHour ?? 0)) /
                (next.TrashPerHour - (previous?.TrashPerHour ?? 0));
            position = (previous?.Position ?? 0) + (double)fraction * (next.Position - (previous?.Position ?? 0));
            // A rounded 100% would falsely suggest the next threshold has been reached.
            var percentage = Number(decimal.Floor(fraction * 100));
            progress = previous is null ? F("{0} % von {1}", percentage, next.Label) :
                F("{0} → {1} · {2} %", groups[nextIndex - 1].Last().Label, next.Label, percentage);
            gap = F("Noch {0} Trash / h bis {1}", Number(decimal.Ceiling(next.TrashPerHour - rate)), next.Label);
        }
        else
        {
            var last = stops[^1];
            var interval = last.TrashPerHour - (stops.Length > 1 ? stops[^2].TrashPerHour : 0);
            var fraction = Math.Min(1, (double)(rate - last.TrashPerHour) / (double)interval);
            position = last.Position + fraction * (100 - last.Position);
            var label = references[^1].Label;
            var percentage = ((double)(rate - last.TrashPerHour) / (double)last.TrashPerHour * 100)
                .ToString("N1", AppText.Culture(language));
            progress = rate == last.TrashPerHour ? F("{0} erreicht", label) : F("{0} % über {1}", percentage, label);
            var difference = rate - last.TrashPerHour;
            gap = difference == 0 ? F("Referenz: {0} Trash / h", last.Value) :
                F("+{0} Trash / h über {1}", difference < 1 ? "<1" : Number(difference), label);
        }

        var hourly = Number(rate) + " Trash / h";
        var description = hourly + ". " + progress + ". " + gap + ". " +
            string.Join(" · ", stops.Select(stop => stop.Label + ": " + stop.Value)) + ". " +
            AppText.Translate("Die Position zeigt den Fortschritt zwischen den Referenzwerten, keinen Spieler-Perzentilrang.", language);
        return new(Math.Clamp(position, 0, 100), Array.AsReadOnly(stops), progress, gap, description, hourly);
    }
}
