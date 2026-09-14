namespace BdoGrindTracker.App.UI;

internal sealed record LootSessionSnapshot(
    IReadOnlyDictionary<string, long> Totals,
    long TotalQuantity,
    int ConfirmedEventCount)
{
    public static LootSessionSnapshot Empty { get; } = new(
        new Dictionary<string, long>(), 0, 0);

    public int ItemTypeCount => Totals.Count(pair => pair.Value != 0);
}
