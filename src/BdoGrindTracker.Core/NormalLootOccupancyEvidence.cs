namespace BdoGrindTracker.Core;

/// <summary>
/// A previously accepted glyph mask also matches this physical row. This confirms
/// visible text, not its item, quantity, or identity among repeated equal items.
/// </summary>
public sealed record NormalLootOccupancyEvidence
{
    public NormalLootOccupancyEvidence(IReadOnlyList<NormalLootOccupancyMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        Matches = Array.AsReadOnly(matches.ToArray());
    }

    public IReadOnlyList<NormalLootOccupancyMatch> Matches { get; }

    public void Validate()
    {
        if (Matches.Count > 6 || Matches.Any(match => match is null ||
                match.PreviousSlot is < 0 or > 5 || !double.IsFinite(match.Correlation) ||
                match.Correlation is < -1 or > 1) ||
            Matches.Select(match => match.PreviousSlot).Distinct().Count() != Matches.Count)
            throw new ArgumentException("Invalid normal-loot occupancy evidence.");
    }
}

public sealed record NormalLootOccupancyMatch(int PreviousSlot, double Correlation);
