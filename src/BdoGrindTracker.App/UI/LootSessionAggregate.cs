using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Applies each output ID to user-visible totals at most once, including the
/// signed rare-loot corrections emitted by Companion reconciliation.
/// </summary>
internal sealed class LootSessionAggregate
{
    private readonly HashSet<Guid> _appliedEvents = [];
    private readonly Dictionary<string, long> _totals =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, long> Totals => _totals;

    public long TotalQuantity { get; private set; }

    public int ConfirmedEventCount { get; private set; }

    public int ItemTypeCount => _totals.Count;

    public void Apply(LootEventView lootEvent)
    {
        ArgumentNullException.ThrowIfNull(lootEvent);
        if (lootEvent.Quantity == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lootEvent), "A loot delta cannot be zero.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(lootEvent.ItemName);
        if (lootEvent.EventId == Guid.Empty)
        {
            throw new ArgumentException("A loot delta needs an output ID.", nameof(lootEvent));
        }

        if (_appliedEvents.Contains(lootEvent.EventId))
        {
            return;
        }

        _totals.TryGetValue(lootEvent.ItemName, out var currentQuantity);
        var updatedQuantity = checked(currentQuantity + lootEvent.Quantity);
        var totalQuantity = checked(TotalQuantity + lootEvent.Quantity);
        var eventCount = checked(ConfirmedEventCount + (lootEvent.Quantity > 0 ? 1 : 0));
        _appliedEvents.Add(lootEvent.EventId);
        if (updatedQuantity == 0)
        {
            _totals.Remove(lootEvent.ItemName);
        }
        else
        {
            _totals[lootEvent.ItemName] = updatedQuantity;
        }
        TotalQuantity = totalQuantity;
        ConfirmedEventCount = eventCount;
    }

    public void Reset()
    {
        _totals.Clear();
        _appliedEvents.Clear();
        TotalQuantity = 0;
        ConfirmedEventCount = 0;
    }
}
