using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Applies each output ID to user-visible totals at most once, including the
/// signed rare-loot corrections emitted by Companion reconciliation.
/// </summary>
internal sealed class LootSessionAggregate
{
    private readonly HashSet<Guid> _appliedEvents = [];
    private readonly HashSet<string> _manuallyEditedItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _totals =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, long> Totals => _totals;

    public long TotalQuantity { get; private set; }

    public int ConfirmedEventCount { get; private set; }

    public int ItemTypeCount => _totals.Count(pair => pair.Value != 0);

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
        if (_manuallyEditedItems.Contains(lootEvent.ItemName)) updatedQuantity = Math.Max(0, updatedQuantity);
        var totalQuantity = checked(TotalQuantity + (updatedQuantity - currentQuantity));
        var eventCount = checked(ConfirmedEventCount + (lootEvent.Quantity > 0 ? 1 : 0));
        _appliedEvents.Add(lootEvent.EventId);
        if (updatedQuantity == 0 && !_manuallyEditedItems.Contains(lootEvent.ItemName))
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

    public void AdjustQuantity(string itemName, long quantity, long originalQuantity,
        Action<LootSessionSnapshot>? beforeCommit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        var currentQuantity = _totals.GetValueOrDefault(itemName);
        // The editor captures its starting value. Apply only its correction so
        // drops arriving while it is open remain counted. A concurrent negative
        // reconciliation may already have removed the quantity being corrected.
        var correction = checked(quantity - originalQuantity);
        var updatedQuantity = Math.Max(0, checked(currentQuantity + correction));
        var totalQuantity = checked(TotalQuantity + checked(updatedQuantity - currentQuantity));
        var totals = new Dictionary<string, long>(_totals, StringComparer.OrdinalIgnoreCase)
        {
            [itemName] = updatedQuantity,
        };
        // Persistence may reject the proposed change. Nothing has mutated yet.
        beforeCommit?.Invoke(new(totals, totalQuantity, ConfirmedEventCount));
        _totals[itemName] = updatedQuantity;
        _manuallyEditedItems.Add(itemName);
        TotalQuantity = totalQuantity;
    }

    public void Reset()
    {
        _totals.Clear();
        _appliedEvents.Clear();
        _manuallyEditedItems.Clear();
        TotalQuantity = 0;
        ConfirmedEventCount = 0;
    }
}
