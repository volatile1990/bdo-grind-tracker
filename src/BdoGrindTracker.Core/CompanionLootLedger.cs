// Restored from the verified 0.5.1 assembly; recognition behavior is intentionally unchanged.
using System;
using System.Collections.Generic;

namespace BdoGrindTracker.Core;

public sealed class CompanionLootLedger
{
    private readonly Dictionary<string, long> totals = new Dictionary<string, long>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, long> Totals => totals;

    public void Add(string name, long count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, "name");
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException("count");
        }
        totals.TryGetValue(name, out var value);
        totals[name] = checked(value + count);
    }

    public bool TryRemoveOne(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, "name");
        if (!totals.TryGetValue(name, out var value) || value <= 0)
        {
            return false;
        }
        if (value == 1)
        {
            totals.Remove(name);
        }
        else
        {
            totals[name] = value - 1;
        }
        return true;
    }

    public void ApplyDelta(string name, long delta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var quantity = checked(totals.GetValueOrDefault(name) + delta);
        if (quantity < 0) throw new InvalidOperationException("A loot correction exceeds the booked quantity.");
        if (quantity == 0) totals.Remove(name);
        else totals[name] = quantity;
    }

    public void Reset()
    {
        totals.Clear();
    }
}
