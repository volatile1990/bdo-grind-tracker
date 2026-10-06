using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Components;

internal enum RareDropRelation
{
    BelowAverage,
    Average,
    AboveAverage,
}

/// <summary>Public 100% Garmoth rates scaled to the selected bonus and the session's trash performance.</summary>
internal sealed record RareDropPresentation(decimal ExpectedPerHour, decimal ExpectedCount,
    RareDropRelation Relation, string Label, string Description)
{
    internal string ToneClass => Relation switch
    {
        RareDropRelation.AboveAverage => "is-above",
        RareDropRelation.BelowAverage => "is-below",
        _ => "is-average",
    };

    internal static RareDropPresentation? Create(string itemName, long actualCount, TimeSpan activeElapsed,
        string? spotId, GrindBenchmark? benchmark, decimal dropRatePercent, long sessionTrashCount,
        string? language = "de")
    {
        var sourceUrl = benchmark?.RareDropSourceUrl ?? benchmark?.SourceUrl;
        if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(spotId) ||
            benchmark is null || benchmark.SpotId != spotId || actualCount < 0 || activeElapsed <= TimeSpan.Zero ||
            sessionTrashCount <= 0 || benchmark.RareDropReferenceTrashPerHour is not > 0 ||
            dropRatePercent is < 0 or > 1000 || !HasSource(sourceUrl, spotId) ||
            !LootSourceCatalog.Allows(itemName, LootSource.Rare) ||
            LootSpotCatalog.Spots.FirstOrDefault(spot => spot.Id == spotId) is not { } spot ||
            !spot.Allows(itemName) && !LootSpotCatalog.EventItems.Contains(itemName, StringComparer.Ordinal) ||
            benchmark.RareDropHourlyRates is not { } rates || !rates.TryGetValue(itemName, out var baseHourly) ||
            baseHourly <= 0)
            return null;

        decimal expectedPerHour;
        decimal expectedCount;
        decimal scaledHourly;
        decimal sessionTrashPerHour;
        decimal trashFactor;
        var referenceTrashPerHour = benchmark.RareDropReferenceTrashPerHour.Value;
        var scale = benchmark.RareDropRateScalingApplies ? (100m + dropRatePercent) / 200m : 1m;
        try
        {
            // Garmoth's percentage is a bonus: 100% is twice the 0% rate,
            // while 320% is 4.2 times that 0% rate (2.1 times visible 100%).
            scaledHourly = baseHourly * scale;
            var hours = Presentation.Hours(activeElapsed);
            sessionTrashPerHour = sessionTrashCount / hours;
            trashFactor = sessionTrashPerHour / referenceTrashPerHour;
            // hourly * hours * (own trash/hour / reference trash/hour) simplifies
            // to hourly * own total trash / reference trash/hour. Use that form
            // so waiting without new loot cannot change the expected drop count.
            expectedCount = scaledHourly * (sessionTrashCount / referenceTrashPerHour);
            expectedPerHour = expectedCount / hours;
        }
        catch (OverflowException)
        {
            return null;
        }
        if (expectedPerHour <= 0 || expectedCount <= 0) return null;

        // Keep the source precision through classification. Display rounding must
        // not turn 0.9999 or 1.0001 expected drops into an equality with 1 actual drop.
        var relation = actualCount > expectedCount ? RareDropRelation.AboveAverage :
            actualCount < expectedCount ? RareDropRelation.BelowAverage : RareDropRelation.Average;
        var labelTemplate = relation switch
        {
            RareDropRelation.AboveAverage => "Über Durchschnitt · Ø {0} erwartet",
            RareDropRelation.BelowAverage => "Unter Durchschnitt · Ø {0} erwartet",
            _ => "Im Durchschnitt · Ø {0} erwartet",
        };
        var expectedLabel = Presentation.HourlyNumber(expectedCount, language);
        var label = AppText.Format(labelTemplate, language, expectedLabel);
        var percentLabel = dropRatePercent.ToString("0.##", AppText.Culture(language));
        var description = AppText.Format("Garmoth: {0} Drops / h bei 100 % Dropratenbonus.", language,
            Presentation.HourlyNumber(baseHourly, language)) + " " +
            (benchmark.RareDropRateScalingApplies
                ? AppText.Format("Dein Bonus: {0} %. Hochrechnung: (100 + {0}) / 200 = {1}; {2} Drops / h.", language,
                    percentLabel, scale.ToString("0.####", AppText.Culture(language)),
                    Presentation.HourlyNumber(scaledHourly, language))
                : AppText.Format("Dein Bonus: {0} %. An diesem Grindspot beeinflusst die Droprate die Referenz nicht: {1} Drops / h.",
                    language, percentLabel, Presentation.HourlyNumber(scaledHourly, language))) + " " +
            AppText.Format("Trash-Abgleich: {0} Trash / h in deiner Session / {1} Trash / h bei Garmoth ({2}) = Faktor {3}. Angepasst: {4} Drops / h.",
                language, Presentation.HourlyNumber(sessionTrashPerHour, language),
                Presentation.HourlyNumber(referenceTrashPerHour, language), AppText.Translate(benchmark.Conditions, language),
                trashFactor.ToString("0.####", AppText.Culture(language)),
                Presentation.HourlyNumber(expectedPerHour, language)) + " " +
            AppText.Format("Erwartet für {0} aktive Grindzeit (ohne Pausen): {1} Drops. Referenz: Stand {2}. Quelle: {3}",
                language, ActiveDuration(activeElapsed, language), expectedLabel,
                (benchmark.RareDropUpdatedAt ?? benchmark.UpdatedAt).ToString("d", AppText.Culture(language)), sourceUrl);
        return new(expectedPerHour, expectedCount, relation, label, description);
    }

    private static bool HasSource(string? sourceUrl, string spotId) =>
        GarmothCatalog.TryGetSpot(spotId, out var garmothSpot) &&
        Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("garmoth.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("www.garmoth.com", StringComparison.OrdinalIgnoreCase)) &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsolutePath.TrimEnd('/') == $"/grind-tracker/best-grind-spots/{garmothSpot}";

    private static string ActiveDuration(TimeSpan duration, string? language)
    {
        var seconds = duration.Seconds + (decimal)(duration.Ticks % TimeSpan.TicksPerSecond) / TimeSpan.TicksPerSecond;
        return $"{duration.Ticks / TimeSpan.TicksPerHour:00}:{duration.Minutes:00}:" +
            seconds.ToString("00.#######", AppText.Culture(language));
    }
}
