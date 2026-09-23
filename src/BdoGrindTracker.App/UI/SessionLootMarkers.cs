using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.UI;

/// <summary>Which drops deserve their own mark: the user's favorites and anything worth more than 200 million.</summary>
internal static class SessionLootMarkers
{
    public const decimal ValuableUnitPrice = 200_000_000m;

    public static bool IsMarked(string itemName, TrackerPreferences preferences, LootPriceSnapshot? prices) =>
        preferences.FavoriteItems.Contains(itemName, StringComparer.Ordinal) ||
        prices is not null && prices.TryGetQuote(itemName, out var quote) && quote.UnitPrice > ValuableUnitPrice;
}
