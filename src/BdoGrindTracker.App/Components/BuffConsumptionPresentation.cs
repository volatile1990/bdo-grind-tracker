using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Components;

/// <summary>Groups recorded uses by their purchased variant without repricing or inventing bookings.</summary>
internal sealed record BuffConsumptionPresentation(
    string BuffId, string Name, int Count, int UnpricedCount, int SessionStartCount, decimal KnownCost,
    decimal? MinimumUnitPrice, decimal? MaximumUnitPrice)
{
    internal bool HasStalePrice { get; init; }

    internal static IReadOnlyList<BuffConsumptionPresentation> Group(IReadOnlyList<BuffConsumption> consumptions) =>
        consumptions.GroupBy(item => item.BuffId, StringComparer.Ordinal)
            .Select(group => new BuffConsumptionPresentation(
                group.Key, group.Last().Name, group.Count(), group.Count(item => item.Price is null),
                group.Count(item => item.IsSessionStart),
                group.Sum(item => item.Cost ?? 0), group.Min(item => item.Cost), group.Max(item => item.Cost))
                { HasStalePrice = group.Any(item => item.Price?.IsStale == true) })
            .OrderByDescending(item => item.Count).ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
}
