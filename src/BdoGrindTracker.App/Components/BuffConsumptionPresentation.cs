using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Components;

/// <summary>Groups recorded start allowances and refreshes without repricing or inventing bookings.</summary>
internal sealed record BuffConsumptionPresentation(
    string BuffId, string Name, int Count, int UnpricedCount, int SessionStartCount, decimal KnownCost,
    decimal? MinimumUnitPrice, decimal? MaximumUnitPrice)
{
    private static readonly IReadOnlyDictionary<string, string> DisplayIds = CreateDisplayIds();

    internal bool HasStalePrice { get; init; }

    internal static IReadOnlyList<BuffConsumptionPresentation> Group(IReadOnlyList<BuffConsumption> consumptions) =>
        consumptions.GroupBy(item => DisplayId(item.BuffId), StringComparer.Ordinal)
            .Select(group => new BuffConsumptionPresentation(
                group.Key, group.Last().Name, group.Count(), group.Count(item => item.Price is null),
                group.Count(item => item.IsSessionStart),
                group.Sum(item => item.Cost ?? 0), group.Min(item => item.Cost), group.Max(item => item.Cost))
                { HasStalePrice = group.Any(item => item.Price?.IsStale == true) })
            .OrderByDescending(item => item.Count).ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

    internal static string DisplayId(string buffId) => DisplayIds.GetValueOrDefault(buffId, buffId);

    private static IReadOnlyDictionary<string, string> CreateDisplayIds()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var family in new[] { "tent-adventures-boon", "tent-body-enhancement", "tent-turning-gates" })
        {
            var maximum = BuffPriceCatalog.HistoryDefinitions.Single(item => item.Id == family + "-300");
            // Old purchases keep their IDs and recorded prices in the ledger.
            // Only these known duration families share a presentation stack.
            foreach (var definition in BuffPriceCatalog.HistoryDefinitions.Where(item =>
                item.RecognitionGroup == maximum.RecognitionGroup))
                result.Add(definition.Id, maximum.Id);
            result.Add("automatic-" + family, maximum.Id);
        }
        return result;
    }
}
