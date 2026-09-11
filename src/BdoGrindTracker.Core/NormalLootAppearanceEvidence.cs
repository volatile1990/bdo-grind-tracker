namespace BdoGrindTracker.Core;

/// <summary>
/// A new bright rendering cannot continue a verified faded rendering of the
/// same glyphs in these previous physical slots. This supplies scroll evidence,
/// not an item name or a quantity and never creates an unobserved drop.
/// </summary>
public sealed record NormalLootAppearanceEvidence(
    int FadedPreviousSlots, IReadOnlyList<NormalLootAppearanceMatch> Matches)
{
    public void Validate()
    {
        if (FadedPreviousSlots is < 0 or > 63 || Matches is null || Matches.Count > 6 ||
            Matches.Any(match => match is null || match.PreviousSlot is < 0 or > 5 ||
                !double.IsFinite(match.Correlation) || match.Correlation is < -1 or > 1 ||
                !double.IsFinite(match.PreviousContrastRatio) || match.PreviousContrastRatio < 0))
            throw new ArgumentException("Invalid normal-loot appearance evidence.");
        var supported = Matches.Where(match => match.Correlation >= .8 &&
                match.PreviousContrastRatio is >= .15 and <= .85)
            .Aggregate(0, (mask, match) => mask | (1 << match.PreviousSlot));
        if ((FadedPreviousSlots & ~supported) != 0 ||
            Matches.Select(match => match.PreviousSlot).Distinct().Count() != Matches.Count)
            throw new ArgumentException("A faded previous slot requires a matching glyph comparison.");
    }
}

public sealed record NormalLootAppearanceMatch(int PreviousSlot, double Correlation, double PreviousContrastRatio);
