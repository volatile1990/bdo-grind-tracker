using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.UI;

/// <summary>
/// Applies authoritative loot projections with independent manual corrections,
/// or individual outputs for historical reconciliation modes.
/// </summary>
internal sealed class LootSessionAggregate
{
    private sealed record AppliedDrop(string ItemName, int Revision, int Quantity);
    private readonly Dictionary<Guid, AppliedDrop> _appliedEvents = [];
    private readonly HashSet<string> _manuallyEditedItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _totals =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _manualOffsets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _restoredTotals =
        new(StringComparer.OrdinalIgnoreCase);
    private int _restoredEventCount;
    private Dictionary<string, long>? _projectionTotals;
    private long? _projectionRevision;

    public IReadOnlyDictionary<string, long> Totals => _totals;

    public long TotalQuantity { get; private set; }

    public int ConfirmedEventCount { get; private set; }

    public int ItemTypeCount => _totals.Count(pair => pair.Value != 0);

    public DateTimeOffset? LatestArrivalAt { get; private set; }

    public (bool TotalsChanged, bool HasNewArrival) ApplyProjection(LootTotalsProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfNegative(projection.Revision);
        if (_projectionRevision is { } revision && projection.Revision <= revision)
            return (false, false);

        ArgumentNullException.ThrowIfNull(projection.Totals);
        ArgumentOutOfRangeException.ThrowIfNegative(projection.ConfirmedDropCount);
        var automaticTotals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (itemName, quantity) in projection.Totals)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
            ArgumentOutOfRangeException.ThrowIfNegative(quantity);
            if (!automaticTotals.TryAdd(itemName, quantity))
                throw new ArgumentException("A projection contains duplicate item names.", nameof(projection));
        }
        foreach (var (itemName, quantity) in _restoredTotals)
            automaticTotals[itemName] = checked(automaticTotals.GetValueOrDefault(itemName) + quantity);
        var eventCount = checked(_restoredEventCount + projection.ConfirmedDropCount);

        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long totalQuantity = 0;
        foreach (var itemName in automaticTotals.Keys.Concat(_manuallyEditedItems).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var quantity = Math.Max(0, checked(automaticTotals.GetValueOrDefault(itemName) +
                _manualOffsets.GetValueOrDefault(itemName)));
            totalQuantity = checked(totalQuantity + quantity);
            if (quantity != 0 || _manuallyEditedItems.Contains(itemName))
                totals.Add(itemName, quantity);
        }

        var totalsChanged = ConfirmedEventCount != eventCount ||
            totals.Count != _totals.Count || totals.Any(pair =>
                !_totals.TryGetValue(pair.Key, out var quantity) || quantity != pair.Value);
        var hasNewArrival = projection.LatestArrivalAt is { } arrival &&
            (LatestArrivalAt is null || arrival > LatestArrivalAt.Value);

        // Validate and calculate every item first: a malformed snapshot must not
        // partly replace the session or consume its revision.
        _totals.Clear();
        foreach (var pair in totals)
            _totals.Add(pair.Key, pair.Value);
        _projectionTotals = automaticTotals;
        _projectionRevision = projection.Revision;
        TotalQuantity = totalQuantity;
        ConfirmedEventCount = eventCount;
        if (hasNewArrival)
            LatestArrivalAt = projection.LatestArrivalAt;
        return (totalsChanged, hasNewArrival);
    }

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

        if (lootEvent.Revision < 0 || lootEvent.TotalDropQuantity is <= 0 ||
            lootEvent.Revision > 0 && lootEvent.TotalDropQuantity is null)
            throw new ArgumentException("Invalid loot quantity revision.", nameof(lootEvent));
        _appliedEvents.TryGetValue(lootEvent.EventId, out var applied);
        if (applied is not null && applied.ItemName != lootEvent.ItemName)
            throw new ArgumentException("A drop ID cannot change its item.", nameof(lootEvent));
        if (applied is not null && applied.Revision >= lootEvent.Revision)
        {
            return;
        }

        _totals.TryGetValue(lootEvent.ItemName, out var currentQuantity);
        var delta = lootEvent.TotalDropQuantity is { } total
            ? checked(total - (applied?.Quantity ?? 0)) : lootEvent.Quantity;
        var updatedQuantity = checked(currentQuantity + delta);
        if (_manuallyEditedItems.Contains(lootEvent.ItemName)) updatedQuantity = Math.Max(0, updatedQuantity);
        var totalQuantity = checked(TotalQuantity + (updatedQuantity - currentQuantity));
        var eventCount = checked(ConfirmedEventCount + (applied is null && delta > 0 ? 1 : 0));
        _appliedEvents[lootEvent.EventId] = new(lootEvent.ItemName, lootEvent.Revision,
            lootEvent.TotalDropQuantity ?? lootEvent.Quantity);
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
        var manualOffset = _projectionTotals is not null
            ? checked(updatedQuantity - _projectionTotals.GetValueOrDefault(itemName))
            : checked(_manualOffsets.GetValueOrDefault(itemName) + correction);
        var totals = new Dictionary<string, long>(_totals, StringComparer.OrdinalIgnoreCase)
        {
            [itemName] = updatedQuantity,
        };
        // Persistence may reject the proposed change. Nothing has mutated yet.
        beforeCommit?.Invoke(new(totals, totalQuantity, ConfirmedEventCount));
        _totals[itemName] = updatedQuantity;
        _manuallyEditedItems.Add(itemName);
        _manualOffsets[itemName] = manualOffset;
        TotalQuantity = totalQuantity;
    }

    public void Reset()
    {
        _totals.Clear();
        _appliedEvents.Clear();
        _manuallyEditedItems.Clear();
        _manualOffsets.Clear();
        _restoredTotals.Clear();
        _restoredEventCount = 0;
        _projectionTotals = null;
        _projectionRevision = null;
        LatestArrivalAt = null;
        TotalQuantity = 0;
        ConfirmedEventCount = 0;
    }

    public void Restore(LootSessionSnapshot snapshot, IEnumerable<string> manualItems)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Totals);
        ArgumentNullException.ThrowIfNull(manualItems);
        ArgumentOutOfRangeException.ThrowIfNegative(snapshot.ConfirmedEventCount);
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long totalQuantity = 0;
        foreach (var (itemName, quantity) in snapshot.Totals)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
            ArgumentOutOfRangeException.ThrowIfNegative(quantity);
            if (!totals.TryAdd(itemName, quantity))
                throw new ArgumentException("A restored snapshot contains duplicate item names.", nameof(snapshot));
            totalQuantity = checked(totalQuantity + quantity);
        }
        if (snapshot.TotalQuantity != totalQuantity)
            throw new ArgumentException("A restored snapshot has an inconsistent total quantity.", nameof(snapshot));
        var manual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var itemName in manualItems)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
            if (!totals.ContainsKey(itemName))
                throw new ArgumentException("A restored manual item is missing from the totals.", nameof(manualItems));
            manual.Add(itemName);
        }

        // Old projections and event IDs belong to the previous analyzer. Only
        // its earned totals survive as the baseline for the new analyzer run.
        Reset();
        foreach (var pair in totals)
        {
            _totals.Add(pair.Key, pair.Value);
            _restoredTotals.Add(pair.Key, pair.Value);
        }
        _manuallyEditedItems.UnionWith(manual);
        TotalQuantity = totalQuantity;
        ConfirmedEventCount = _restoredEventCount = snapshot.ConfirmedEventCount;
    }
}
