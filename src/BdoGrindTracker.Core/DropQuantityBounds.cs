namespace BdoGrindTracker.Core;

/// <summary>
/// Quantity policy for one item at one grind spot. The minimum is used only
/// when OCR quantity repair fails; the maximum limits each individual drop.
/// A null maximum means no verified upper bound is available.
/// </summary>
public sealed record DropQuantityBounds
{
    public uint Minimum { get; }

    public uint? Maximum { get; }

    public bool IsFixedUnit => Minimum == 1 && Maximum == 1;

    public DropQuantityBounds(uint minimum, uint? maximum = null)
    {
        if (minimum is 0 or > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum must be between 1 and Int32.MaxValue.");
        if (maximum is 0 or > int.MaxValue || maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Maximum must be at least the minimum and at most Int32.MaxValue.");

        Minimum = minimum;
        Maximum = maximum;
    }
}
