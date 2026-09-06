using System.Collections.ObjectModel;

namespace BdoGrindTracker.App.Pricing;

internal enum LootPriceOrigin { FixedCatalog, LiveMarket, CachedMarket, DerivedMarket }
internal enum LootPriceKind { Fixed, Market, AncientSpiritDust, Unknown }

internal sealed record LootPriceDefinition(string ItemName, LootPriceKind Kind,
    int? MarketItemId = null, long? FixedUnitPrice = null);

/// <summary>Integer unit appraisal split into taxable and untaxed components.</summary>
internal sealed record LootPriceQuote(string ItemName, decimal TaxableUnitPrice,
    decimal UntaxedUnitPrice, LootPriceOrigin Origin, DateTimeOffset? FetchedAt, bool IsStale = false)
{
    public decimal UnitPrice => TaxableUnitPrice + UntaxedUnitPrice;
    public bool IsTaxable => TaxableUnitPrice != 0;
}

internal sealed class LootPriceSnapshot
{
    public LootPriceSnapshot(string region, IEnumerable<LootPriceQuote> quotes,
        DateTimeOffset? retrievedAt = null, string statusMessage = "")
    {
        Region = LootPriceCatalog.NormalizeRegion(region);
        ArgumentNullException.ThrowIfNull(quotes);
        Quotes = new ReadOnlyDictionary<string, LootPriceQuote>(quotes.ToDictionary(
            static quote => quote.ItemName, StringComparer.Ordinal));
        RetrievedAt = retrievedAt;
        StatusMessage = statusMessage;
    }

    public string Region { get; }
    public IReadOnlyDictionary<string, LootPriceQuote> Quotes { get; }
    public DateTimeOffset? RetrievedAt { get; }
    public bool IsStale => Quotes.Values.Any(static quote => quote.IsStale);
    public string StatusMessage { get; }
    public bool TryGetQuote(string itemName, out LootPriceQuote quote) => Quotes.TryGetValue(itemName, out quote!);
}

internal interface ILootPriceProvider : IDisposable
{
    Task<LootPriceSnapshot> GetSnapshotAsync(string region, CancellationToken cancellationToken = default);
    LootPriceSnapshot GetCachedSnapshot(string region);
}
