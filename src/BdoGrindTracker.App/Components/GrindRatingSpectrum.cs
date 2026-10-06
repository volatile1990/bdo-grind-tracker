using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Components;

public sealed record GrindRatingStop(string Label, decimal TrashPerHour, double Position, string Value,
    OverlayMetricTone Tone);

/// <summary>Interpolates within benchmark intervals and zooms the range above a lone Average reference.</summary>
public sealed record GrindRatingSpectrum(double Position, IReadOnlyList<GrindRatingStop> Stops,
    string ProgressLabel, string GapLabel, string Description, string TrashHourly)
{
    public string? UpperEndLabel { get; init; }

    public bool Equals(GrindRatingSpectrum? other) => other is not null && Position == other.Position &&
        ProgressLabel == other.ProgressLabel && GapLabel == other.GapLabel && Description == other.Description &&
        TrashHourly == other.TrashHourly && UpperEndLabel == other.UpperEndLabel && Stops.SequenceEqual(other.Stops);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Position);
        hash.Add(ProgressLabel);
        hash.Add(GapLabel);
        hash.Add(Description);
        hash.Add(TrashHourly);
        hash.Add(UpperEndLabel);
        foreach (var stop in Stops) hash.Add(stop);
        return hash.ToHashCode();
    }

    internal static GrindRatingSpectrum? Create(GrindRatingResult result, string? language)
    {
        if (result.Tier == GrindRatingTier.Unavailable || result.Benchmark is not { } benchmark ||
            result.TrashPerHour is not { } rate) return null;

        string F(string text, params object[] args) => AppText.Format(text, language, args);
        string HourlyNumber(decimal value) => Presentation.HourlyNumber(value, language);
        var references = new List<(string Label, decimal Rate, OverlayMetricTone Tone)>
        {
            ("Average", benchmark.AverageTrashPerHour, OverlayMetricTone.Default),
        };
        if (benchmark.HighTrashPerHour is { } high) references.Add(("High", high, OverlayMetricTone.Positive));
        if (benchmark.TopTrashPerHour is { } top) references.Add(("Top", top, OverlayMetricTone.Accent));
        var averageOnly = references.Count == 1;

        // Shared thresholds occupy one position. Never invent absent High/Top references.
        var groups = references.GroupBy(reference => reference.Rate).ToArray();
        var stops = groups.Select((group, index) => new GrindRatingStop(
            string.Join(" / ", group.Select(reference => reference.Label)), group.Key,
            (index + 1d) / (groups.Length + 1) * 100, HourlyNumber(group.Key), group.Last().Tone)).ToArray();
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
            var percentage = Presentation.Number(decimal.Floor(fraction * 100), language);
            progress = previous is null ? F("{0} % von {1}", percentage, next.Label) :
                F("{0} → {1} · {2} %", groups[nextIndex - 1].Last().Label, next.Label, percentage);
            gap = F("Noch {0} Trash / h bis {1}", HourlyNumber(next.TrashPerHour - rate), next.Label);
        }
        else
        {
            var last = stops[^1];
            // With Average as the only reference, reserve the right half for +0 to +50%.
            // The previous +100% range visually understated ordinary gains above Average.
            var interval = averageOnly ? last.TrashPerHour / 2 :
                last.TrashPerHour - (stops.Length > 1 ? stops[^2].TrashPerHour : 0);
            var fraction = Math.Min(1, (double)(rate - last.TrashPerHour) / (double)interval);
            position = last.Position + fraction * (100 - last.Position);
            var label = references[^1].Label;
            var percentage = ((double)(rate - last.TrashPerHour) / (double)last.TrashPerHour * 100)
                .ToString("N1", AppText.Culture(language));
            progress = rate == last.TrashPerHour ? F("{0} erreicht", label) : F("{0} % über {1}", percentage, label);
            var difference = rate - last.TrashPerHour;
            gap = difference == 0 ? F("Referenz: {0} Trash / h", last.Value) :
                F("+{0} Trash / h über {1}", HourlyNumber(difference), label);
        }

        var hourly = HourlyNumber(rate) + " Trash / h";
        var description = hourly + ". " + progress + ". " + gap + ". " +
            string.Join(" · ", stops.Select(stop => stop.Label + ": " + stop.Value)) + ". " +
            AppText.Translate("Die Position zeigt den Abstand auf der Skala, keinen Spieler-Perzentilrang.", language) +
            (averageOnly ? " " + AppText.Translate("Bei nur einem Average-Referenzwert reicht die rechte Skalenhälfte bis 50 % darüber.", language) : "");
        return new(Math.Clamp(position, 0, 100), Array.AsReadOnly(stops), progress, gap, description, hourly)
        {
            UpperEndLabel = averageOnly ? AppText.Translate("+50 %", language) : null,
        };
    }
}
