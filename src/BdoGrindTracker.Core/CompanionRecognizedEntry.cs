// Restored from the verified 0.5.1 assembly; recognition behavior is intentionally unchanged.
using System;

namespace BdoGrindTracker.Core;

public sealed record CompanionRecognizedEntry
{
    public string Name { get; }

    public uint Count { get; }

    public int Y { get; }

    public CompanionRecognizedEntry(string name, uint count, int y = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, "name");
        Name = name;
        Count = count;
        Y = y;
    }
}

