using System.Globalization;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Components;

internal sealed record SpotHistoryMetrics(
    decimal TotalSilver,
    decimal AverageSilverPerHour,
    decimal TrashPerHour,
    decimal RecentFiveHourTrashPerHour,
    decimal BestFiveHourTrashPerHour,
    decimal BestFiveHourAverageSilverPerHour,
    decimal TotalHours);

internal sealed record SpotHistoryChartData(
    IReadOnlyList<decimal> CumulativeSilver,
    IReadOnlyList<decimal> SilverPerHour,
    IReadOnlyList<decimal> TrashPerHour,
    IReadOnlyList<decimal> RecentFiveTrashPerHour,
    IReadOnlyList<decimal> BestFiveTrashPerHour);

internal sealed record SpotLootColumn(string ItemName, long TotalQuantity, decimal SilverPerHour);

/// <summary>Pure history projections shared by the Blazor views and regression tests.</summary>
internal static class HistoryPresentation
{
    private const decimal ValuableDropThreshold = 200_000_000m;

    internal static AgrisPresentation AgrisTime(LootHistoryEntry session) =>
        new(session.AgrisActiveDuration, session.AgrisObservedDuration, session.Duration);
    internal static ExperiencePresentation Experience(LootHistoryEntry session) =>
        new(session.ExperienceGainedPercentagePoints, session.ExperienceObservedDuration, session.Duration,
            session.ExperienceStartLevel, session.ExperienceEndLevel);

    internal static string CreateClassIconFileName(string className)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        return className.Trim().ToLowerInvariant().Replace(' ', '-') + ".png";
    }

    internal static string ExtractBaseClassName(string className)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        var normalized = className.Replace("Â·", "·", StringComparison.Ordinal);
        var separator = normalized.IndexOf('·');
        var baseName = (separator >= 0 ? normalized[..separator] : normalized).Trim();
        return baseName.TrimEnd('Â').TrimEnd();
    }


    internal static IReadOnlyList<KeyValuePair<string, long>> BuildLootItems(
        LootSpotPresentation profile,
        IEnumerable<LootHistoryEntry> sessions,
        LootPriceSnapshot prices,
        SilverTaxOptions tax) => BuildLootColumns(profile, sessions, prices, tax)
        .Select(static column => new KeyValuePair<string, long>(column.ItemName, column.TotalQuantity))
        .ToArray();

    internal static IReadOnlyList<SpotLootColumn> BuildLootColumns(
        LootSpotPresentation profile,
        IEnumerable<LootHistoryEntry> sessions,
        LootPriceSnapshot prices,
        SilverTaxOptions tax)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(tax);
        var sessionArray = sessions.ToArray();
        var trackedTotals = sessionArray
            .SelectMany(static session => session.Totals)
            .GroupBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Sum(static pair => pair.Value),
                StringComparer.Ordinal);
        var totalHours = sessionArray
            .Where(static session => session.Duration > TimeSpan.Zero)
            .Sum(static session => (decimal)session.Duration.Ticks / TimeSpan.TicksPerHour);
        var spotItems = LootSpotCatalog.GetRequired(profile.SpotId).AllowedItems;
        return new[] { profile.TrashItemName }
            .Concat(spotItems.Where(item => !string.Equals(item, profile.TrashItemName, StringComparison.Ordinal)))
            .Concat(trackedTotals.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select((name, stableIndex) =>
            {
                var quantity = trackedTotals.GetValueOrDefault(name);
                var hourlySilver = totalHours <= 0 || quantity <= 0
                    ? 0m
                    : SilverValuation.Calculate(
                        new Dictionary<string, long>(StringComparer.Ordinal) { [name] = quantity },
                        prices, tax).AfterTax / totalHours;
                return new
                {
                    Column = new SpotLootColumn(name, quantity, hourlySilver),
                    HourlySilver = hourlySilver,
                    StableIndex = stableIndex
                };
            })
            .OrderByDescending(static item => item.HourlySilver)
            .ThenBy(static item => item.StableIndex)
            .Select(static item => item.Column)
            .ToArray();
    }

    internal static SpotHistoryMetrics CalculateMetrics(
        LootSpotPresentation profile,
        IEnumerable<LootHistoryEntry> sessions)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sessions);
        var valid = sessions.Where(static session => session.Duration > TimeSpan.Zero).ToArray();
        var totalHours = valid.Sum(static session => (decimal)session.Duration.Ticks / TimeSpan.TicksPerHour);
        var totalSilver = valid.Sum(static session => session.SilverAfterTax);
        var totalTrash = valid.Sum(session => (decimal)session.Totals.GetValueOrDefault(profile.TrashItemName));
        var recent = CalculateTrashWindow(
            valid.OrderByDescending(static session => session.UpdatedAt), profile.TrashItemName, 5m);
        var best = CalculateTrashWindow(
            valid.OrderByDescending(session => TrashPerHour(session, profile.TrashItemName)),
            profile.TrashItemName, 5m);
        var bestFive = valid
            .OrderByDescending(session => TrashPerHour(session, profile.TrashItemName))
            .Take(5)
            .ToArray();
        var bestFiveHours = bestFive.Sum(static session =>
            (decimal)session.Duration.Ticks / TimeSpan.TicksPerHour);
        var bestFiveSilver = bestFive.Sum(static session => session.SilverAfterTax);
        return new SpotHistoryMetrics(
            totalSilver,
            totalHours > 0 ? totalSilver / totalHours : 0m,
            totalHours > 0 ? totalTrash / totalHours : 0m,
            recent,
            best,
            bestFiveHours > 0 ? bestFiveSilver / bestFiveHours : 0m,
            totalHours);
    }

    internal static SpotHistoryChartData BuildChartData(
        LootSpotPresentation profile,
        IEnumerable<LootHistoryEntry> sessions)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sessions);
        var chronological = sessions
            .Where(static session => session.Duration > TimeSpan.Zero)
            .OrderBy(static session => session.UpdatedAt)
            .ToArray();
        var cumulativeSilver = new decimal[chronological.Length];
        var silverPerHour = new decimal[chronological.Length];
        var trashPerHour = new decimal[chronological.Length];
        var runningSilver = 0m;
        for (var index = 0; index < chronological.Length; index++)
        {
            var session = chronological[index];
            var hours = (decimal)session.Duration.Ticks / TimeSpan.TicksPerHour;
            runningSilver += session.SilverAfterTax;
            cumulativeSilver[index] = runningSilver;
            silverPerHour[index] = hours > 0 ? session.SilverAfterTax / hours : 0m;
            trashPerHour[index] = TrashPerHour(session, profile.TrashItemName);
        }
        return new SpotHistoryChartData(
            cumulativeSilver,
            silverPerHour,
            trashPerHour,
            trashPerHour.TakeLast(5).ToArray(),
            chronological
                .OrderByDescending(session => TrashPerHour(session, profile.TrashItemName))
                .Take(5)
                .Select(session => TrashPerHour(session, profile.TrashItemName))
                .ToArray());
    }

    internal static string FormatTimeAgo(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var elapsed = now - timestamp;
        if (elapsed <= TimeSpan.FromMinutes(1))
            return "gerade eben";
        if (elapsed < TimeSpan.FromHours(1))
            return $"vor {(int)elapsed.TotalMinutes:N0} Min.";
        if (elapsed < TimeSpan.FromDays(1))
            return $"vor {(int)elapsed.TotalHours:N0} Std.";
        if (elapsed < TimeSpan.FromDays(2))
            return "gestern";
        if (elapsed < TimeSpan.FromDays(60))
            return $"vor {(int)elapsed.TotalDays:N0} Tagen";
        if (elapsed < TimeSpan.FromDays(730))
            return $"vor {(int)(elapsed.TotalDays / 30):N0} Mon.";
        return $"vor {(int)(elapsed.TotalDays / 365):N0} J.";
    }

    private static decimal CalculateTrashWindow(
        IEnumerable<LootHistoryEntry> orderedSessions,
        string trashItemName,
        decimal targetHours)
    {
        var remaining = targetHours;
        var usedHours = 0m;
        var trash = 0m;
        foreach (var session in orderedSessions)
        {
            if (remaining <= 0)
                break;
            var hours = (decimal)session.Duration.Ticks / TimeSpan.TicksPerHour;
            if (hours <= 0)
                continue;
            var used = Math.Min(hours, remaining);
            trash += TrashPerHour(session, trashItemName) * used;
            usedHours += used;
            remaining -= used;
        }
        return usedHours > 0 ? trash / usedHours : 0m;
    }

    private static decimal TrashPerHour(LootHistoryEntry session, string trashItemName)
    {
        var hours = (decimal)session.Duration.Ticks / TimeSpan.TicksPerHour;
        return hours > 0 ? session.Totals.GetValueOrDefault(trashItemName) / hours : 0m;
    }


    internal static IReadOnlyList<KeyValuePair<string, long>> BuildCollapsedLootItems(
        LootHistoryEntry entry,
        LootSpotPresentation profile,
        LootPriceSnapshot prices,
        SilverTaxOptions tax)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(tax);
        var trash = entry.Totals
            .Where(pair => string.Equals(pair.Key, profile.TrashItemName, StringComparison.Ordinal) && pair.Value > 0);
        var valuable = entry.Totals
            .Where(pair => pair.Value > 0 &&
                !string.Equals(pair.Key, profile.TrashItemName, StringComparison.Ordinal))
            .Select(pair => new { Item = pair, UnitValue = CalculateUnitMarketValue(pair.Key, prices, tax) })
            .Where(static pair => pair.UnitValue > ValuableDropThreshold)
            .OrderByDescending(static pair => pair.UnitValue)
            .ThenBy(static pair => pair.Item.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(static pair => pair.Item);
        return trash.Concat(valuable).ToArray();
    }

    private static decimal CalculateUnitMarketValue(string itemName,
        LootPriceSnapshot prices, SilverTaxOptions tax) =>
        SilverValuation.Calculate(
            new Dictionary<string, long>(StringComparer.Ordinal) { [itemName] = 1 },
            prices, tax).BeforeTax;

}
