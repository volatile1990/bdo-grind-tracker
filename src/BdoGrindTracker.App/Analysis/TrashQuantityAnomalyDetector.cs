using System.Globalization;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

internal sealed record TrashQuantityAnomaly(int BaselineQuantity, int TypicalQuantity, string Basis, int SampleCount)
{
    public bool IsPlausibleCorrection(int quantity, DropQuantityBounds? bounds)
    {
        if (quantity <= 0) return false;
        // A check may confirm a real elite/bulk drop. Never force such a value
        // down to a learned regular drop or silently clamp it in this review.
        if (quantity == BaselineQuantity) return true;
        if (bounds is not null && (quantity < bounds.Minimum || quantity > bounds.Maximum)) return false;
        var factor = Basis switch { "history" => 5, "catalog" => 8, _ => 0 };
        return TypicalQuantity > 0 && factor > 0 && quantity < (long)TypicalQuantity * factor;
    }
}

/// <summary>
/// Flags unusually large primary trash amounts for a second OCR check. Learns
/// only newly counted, non-estimated events, never repeated sightings of a HUD row.
/// This class neither changes a quantity nor rejects a drop.
/// </summary>
internal sealed class TrashQuantityAnomalyDetector
{
    private const int HistoryCapacity = 64;
    private const int MinimumHistorySamples = 8;
    private readonly Queue<(Guid Id, int Quantity)> _history = new();
    private readonly HashSet<Guid> _recentIds = [];
    private string? _spotId;
    private string? _trashItem;

    public void ObserveCountedDrops(string? spotId, IReadOnlyList<CompanionRecognizedEntry> reconciled)
    {
        ArgumentNullException.ThrowIfNull(reconciled);
        if (!string.Equals(_spotId, spotId, StringComparison.Ordinal))
        {
            Reset();
            _spotId = spotId;
            _trashItem = TrashLootMinimumCatalog.Entries.FirstOrDefault(entry => entry.SpotId == spotId)?.ItemName;
        }
        if (_spotId is null || _trashItem is null) return;
        foreach (var entry in reconciled)
        {
            if (entry.Name != _trashItem || entry.IsMinimumQuantityEstimate || entry.IsPlaceholder || entry.IsAlignmentAnchor ||
                entry.Revision != 0 || entry.QuantityDelta is <= 0 || entry.Count is 0 or > int.MaxValue ||
                entry.EventId is not { } id || id == Guid.Empty || !_recentIds.Add(id)) continue;
            _history.Enqueue((id, (int)entry.Count));
            if (_history.Count > HistoryCapacity) _recentIds.Remove(_history.Dequeue().Id);
        }
    }

    public TrashQuantityAnomaly? Assess(string? spotId, LootObservation row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Source != LootSource.Normal || row.ItemName is null || row.RejectionReason is not null ||
            row.Quantity is not > 0 || row.IsAlignmentAnchor || row.UsesFixedUnitQuantity) return null;
        var association = TrashLootMinimumCatalog.Entries.FirstOrDefault(entry => entry.ItemName == row.ItemName &&
            (spotId is null || entry.SpotId == spotId));
        if (association is null) return null;
        var bounds = row.QuantityBounds ?? DropQuantityCatalog.GetBounds(association.SpotId, association.ItemName);
        if (bounds?.IsFixedUnit == true) return null;
        var typical = 0;
        var basis = "catalog";
        var sampleCount = 0;
        if (spotId == _spotId && row.ItemName == _trashItem && _history.Count >= MinimumHistorySamples)
        {
            var modes = _history.GroupBy(entry => entry.Quantity)
                .Select(group => (Quantity: group.Key, Count: group.Count()))
                .OrderByDescending(group => group.Count).Take(2).ToArray();
            // A split between two buff states is not a stable mode. The catalog
            // fallback still permits ordinary x2/x4/x8 transitions without review.
            if (modes[0].Count * 2 >= _history.Count && (modes.Length == 1 || modes[0].Count > modes[1].Count))
            {
                typical = modes[0].Quantity;
                basis = "history";
                sampleCount = _history.Count;
            }
        }
        if (typical == 0) typical = bounds is { Minimum: <= int.MaxValue } ? (int)bounds.Minimum : 0;
        if (typical <= 0) return null;
        var amount = row.Quantity.Value;
        var factor = basis == "history" ? 5 : 8;
        var largeSpike = amount >= (long)typical * factor;
        // OCR sometimes appends a scenery digit to an otherwise normal amount,
        // e.g. x4 -> x43/x45. Require a whole extra decimal digit, not x2 -> x4.
        var ordinaryText = typical.ToString(CultureInfo.InvariantCulture);
        var observedText = amount.ToString(CultureInfo.InvariantCulture);
        var extraDigit = observedText.Length > ordinaryText.Length &&
            observedText.StartsWith(ordinaryText, StringComparison.Ordinal);
        return largeSpike || extraDigit ? new(amount, typical, basis, sampleCount) : null;
    }

    public void Reset()
    {
        _history.Clear();
        _recentIds.Clear();
        _spotId = null;
        _trashItem = null;
    }
}
