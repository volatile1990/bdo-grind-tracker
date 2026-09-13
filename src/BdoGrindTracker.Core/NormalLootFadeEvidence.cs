namespace BdoGrindTracker.Core;

/// <summary>
/// Contrast of a recognized row against a stable, fully rendered glyph template
/// of the same item. Fading constrains age, never item identity or quantity.
/// </summary>
public sealed record NormalLootFadeEvidence(double ContrastRatio, double Correlation)
{
    public void Validate()
    {
        if (!double.IsFinite(ContrastRatio) || ContrastRatio is < 0 or > 4 ||
            !double.IsFinite(Correlation) || Correlation is < -1 or > 1)
            throw new ArgumentException("Invalid normal-loot fade evidence.");
    }
}
