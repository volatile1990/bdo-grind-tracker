// Restored from the verified 0.5.1 assembly; recognition behavior is intentionally unchanged.
using System;

namespace BdoGrindTracker.Core;

public sealed record CompanionRareRecognizedEntry
{
    public string Name { get; }

    public int Count { get; }

    public int Y { get; }

    public bool SuppressCounting { get; }

    public CompanionRareRecognizedEntry(string name, int count, int y = 0, bool suppressCounting = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, "name");
        Name = name;
        Count = count;
        Y = y;
        SuppressCounting = suppressCounting;
    }
}

