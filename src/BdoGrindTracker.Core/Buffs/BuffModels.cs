namespace BdoGrindTracker.Core.Buffs;

public sealed record BuffDefinition(string Id, string Name, int? MarketItemId, TimeSpan Duration)
{
    public string Category { get; init; } = string.Empty;
    public string Variant { get; init; } = string.Empty;
    public string LocalizedName { get; init; } = string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(LocalizedName) ? Name : LocalizedName;
    public string SourceUrl { get; init; } = string.Empty;

    /// <summary>Variants whose effect cannot be assumed to identify the purchased item.</summary>
    public string RecognitionGroup { get; init; } = string.Empty;

    /// <summary>
    /// Visually identical duration variants. By default a new observed cycle uses the shortest
    /// unique duration covering its remaining time; PreferMaximumDurationVariant selects the longest.
    /// </summary>
    public IReadOnlyList<string> DurationVariantIds { get; init; } = [];

    /// <summary>Account for each use with the longest duration variant, independent of its remaining time.</summary>
    public bool PreferMaximumDurationVariant { get; init; }

    /// <summary>Fixed purchase cost for a tent buff, independent of Central Market quotes.</summary>
    public decimal? FixedUnitPrice { get; init; }

    /// <summary>A received party effect alone does not establish that the player consumed an item.</summary>
    public bool RequiresConsumptionConfirmation { get; init; }
}

public enum BuffPriceSource
{
    CentralMarket,
    FixedNpc,
}

/// <param name="TimerPrecision">The displayed timer's resolution, for example one minute for “29m”.</param>
public sealed record BuffObservation(string BuffId, TimeSpan Remaining, TimeSpan TimerPrecision)
{
    /// <summary>The player explicitly assigned this party effect to their own consumable in the profile.</summary>
    public bool ConsumptionAttributionConfirmed { get; init; }
}

/// <summary>The purchase price captured when a buff was first observed; no marketplace sale tax applies.</summary>
public sealed record BuffPrice(decimal UnitPrice, string Region, DateTimeOffset? FetchedAt, bool IsStale)
{
    public BuffPriceSource Source { get; init; } = BuffPriceSource.CentralMarket;
}

public sealed record BuffConsumption(
    string BuffId,
    string Name,
    int? MarketItemId,
    DateTimeOffset ConsumedAt,
    BuffPrice? Price)
{
    public decimal? Cost => Price?.UnitPrice;

    /// <summary>Counts an already active buff on its first confirmed observation, rather than a timer refresh.</summary>
    public bool IsSessionStart { get; init; }
}

public sealed record BuffActive(
    string BuffId,
    string Name,
    int? MarketItemId,
    TimeSpan Remaining,
    DateTimeOffset ObservedAt,
    BuffPrice? Price,
    bool IsBaseline);

public sealed record BuffUsage(
    string BuffId,
    string Name,
    int? MarketItemId,
    TimeSpan ObservedDuration,
    decimal KnownProratedCost,
    TimeSpan UnpricedDuration,
    bool HasStalePrice = false)
{
    public decimal? ProratedCost => UnpricedDuration > TimeSpan.Zero ? null : KnownProratedCost;
}

public sealed record BuffLedgerSnapshot(
    IReadOnlyList<BuffConsumption> Consumptions,
    IReadOnlyList<BuffUsage> Usage,
    IReadOnlyList<BuffActive> Active)
{
    public static BuffLedgerSnapshot Empty => new([], [], []);

    public decimal KnownConsumedCost => Consumptions.Sum(item => item.Cost ?? 0);
    public decimal? ConsumedCost => Consumptions.Any(item => item.Price is null) ? null : KnownConsumedCost;
    public decimal KnownProratedCost => Usage.Sum(item => item.KnownProratedCost);
    public decimal? ProratedCost => Usage.Any(item => item.UnpricedDuration > TimeSpan.Zero) ? null : KnownProratedCost;
    public bool HasStalePrices => Consumptions.Any(item => item.Price?.IsStale == true) ||
        Usage.Any(item => item.HasStalePrice) || Active.Any(item => item.Price?.IsStale == true);
}
