using BdoGrindTracker.App.Localization;

namespace BdoGrindTracker.App.Components;

/// <summary>Age of the latest observed drop in active session time, excluding pauses.</summary>
internal sealed record LastDropPresentation(string Label, string Description)
{
    internal static LastDropPresentation Create(TimeSpan? lastDrop, TimeSpan elapsed, string? language)
    {
        if (lastDrop is not { } observed || observed < TimeSpan.Zero || observed > elapsed)
            return new("—", AppText.Translate("Für dieses Item wurde noch kein Zeitpunkt des letzten Drops erfasst.", language));

        var age = elapsed - observed;
        var label = age.TotalSeconds < 5
            ? AppText.Translate("Gerade eben", language)
            : age.TotalMinutes < 1
                ? AppText.Format("vor {0} s", language, (int)age.TotalSeconds)
                : age.TotalHours < 1
                    ? AppText.Format("vor {0} Min.", language, (int)age.TotalMinutes)
                    : AppText.Format("vor {0} Std. {1} Min.", language, (int)age.TotalHours, age.Minutes);
        return new(label, AppText.Format("Letzter Drop: {0} · aktive Grindzeit, ohne Pausen.", language, label));
    }
}
