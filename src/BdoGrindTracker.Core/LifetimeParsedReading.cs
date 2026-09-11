namespace BdoGrindTracker.Core;

/// <summary>
/// A current interpretation of an original OCR reading. A null quantity can
/// associate a row but cannot create a counted drop. Excluded suppresses an
/// original accepted fallback when the current catalog or spot disallows it.
/// </summary>
public sealed record LifetimeParsedReading(string Name, int? Quantity, double Confidence)
{
    public bool IsExcluded { get; init; }

    public static LifetimeParsedReading Excluded { get; } = new("", null, 0) { IsExcluded = true };
}
