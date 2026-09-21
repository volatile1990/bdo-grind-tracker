using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

/// <summary>
/// Normal and special logs contain independent physical drops and their complete
/// lifetime estimates are added. The rare-delta overload preserves historical replay rules.
/// Summation differences are audit records, not newly arrived physical drops.
/// </summary>
internal sealed partial class LifetimeLootProjectionComposer
{
    private Dictionary<string, long> _rare = new(StringComparer.Ordinal);
    private Dictionary<string, List<int>> _rareLots = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, long> _previous = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>());
    private int _dropCount;
    private DateTimeOffset? _latestArrival;
    private long _revision;

    public (LootTotalsProjection Projection, IReadOnlyList<TrackedLootEvent> Events) Combine(
        LifetimeSnapshot normal, LifetimeSnapshot special, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(normal);
        ArgumentNullException.ThrowIfNull(special);
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        AddSource(normal);
        AddSource(special);
        var count = checked(normal.SupportedDropCount + special.SupportedDropCount);
        var normalEvidence = ReadSourceEvidence(normal);
        var specialEvidence = ReadSourceEvidence(special);
        var normalCorrection = CompareSourceEvidence(_normalEvidence, normalEvidence);
        var specialCorrection = CompareSourceEvidence(_specialEvidence, specialEvidence);
        var trusted = _correctionEvidenceTrusted && normalCorrection is not null && specialCorrection is not null;
        var correctionVersion = normalCorrection == true || specialCorrection == true
            ? checked(_correctionVersion + 1) : _correctionVersion;
        var result = CreateProjection(totals, count, Max(normal.LatestArrivalAt, special.LatestArrivalAt), timestamp,
            trusted ? correctionVersion : null);
        _normalEvidence = normalEvidence;
        _specialEvidence = specialEvidence;
        _correctionEvidenceTrusted = trusted;
        _correctionVersion = correctionVersion;
        Commit(result.Projection);
        return result;

        void AddSource(LifetimeSnapshot source)
        {
            ArgumentNullException.ThrowIfNull(source.Totals);
            if (source.SupportedDropCount < 0)
                throw new ArgumentException("Source drop counts cannot be negative.");
            foreach (var (name, amount) in source.Totals)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 4096 || amount < 0)
                    throw new ArgumentException("Invalid source loot totals.");
                if (amount > 0) totals[name] = checked(totals.GetValueOrDefault(name) + amount);
            }
        }
    }

    // Compatibility for recordings made before independent special-log lifetimes:
    // a positive normal total took priority over the same rare item.
    public (LootTotalsProjection Projection, IReadOnlyList<TrackedLootEvent> Events) Combine(
        LifetimeSnapshot normal, IReadOnlyList<CompanionRareCountDelta> rareChanges, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(normal);
        ArgumentNullException.ThrowIfNull(rareChanges);
        var rare = new Dictionary<string, long>(_rare, StringComparer.Ordinal);
        var rareLots = new Dictionary<string, List<int>>(_rareLots, StringComparer.Ordinal);
        var latest = Max(_latestArrival, normal.LatestArrivalAt);
        foreach (var change in rareChanges)
        {
            ArgumentNullException.ThrowIfNull(change);
            ArgumentException.ThrowIfNullOrWhiteSpace(change.Name);
            var amount = checked(rare.GetValueOrDefault(change.Name) + change.Count);
            if (amount < 0) throw new InvalidOperationException("Rare correction exceeds its independent balance.");
            if (change.Count == 0) continue;
            var lots = rareLots.TryGetValue(change.Name, out var previousLots) ? new List<int>(previousLots) : [];
            if (change.Count > 0) lots.Add(change.Count);
            else
            {
                long remaining = -(long)change.Count;
                while (remaining > 0)
                {
                    var taken = (int)Math.Min(remaining, lots[^1]);
                    lots[^1] -= taken;
                    remaining -= taken;
                    if (lots[^1] == 0) lots.RemoveAt(lots.Count - 1);
                }
            }
            if (lots.Count == 0) rareLots.Remove(change.Name); else rareLots[change.Name] = lots;
            if (amount == 0) rare.Remove(change.Name); else rare[change.Name] = amount;
            if (change.Count > 0 && normal.Totals.GetValueOrDefault(change.Name) <= 0)
                latest = Max(latest, timestamp);
        }

        var totals = new Dictionary<string, long>(rare, StringComparer.Ordinal);
        foreach (var (name, amount) in normal.Totals)
        {
            if (amount < 0) throw new InvalidOperationException("Normal totals cannot be negative.");
            if (amount > 0) totals[name] = amount;
        }
        var count = checked(normal.SupportedDropCount + rareLots
            .Where(pair => normal.Totals.GetValueOrDefault(pair.Key) <= 0).Sum(pair => pair.Value.Count));
        var result = CreateProjection(totals, count, latest, timestamp);
        // Commit only after the complete replacement and all audit deltas validate.
        _rare = rare;
        _rareLots = rareLots;
        Commit(result.Projection);
        return result;
    }

    private (LootTotalsProjection Projection, IReadOnlyList<TrackedLootEvent> Events) CreateProjection(
        Dictionary<string, long> totals, int count, DateTimeOffset? latest, DateTimeOffset timestamp,
        long? correctionVersion = null)
    {
        var differences = totals.Keys.Concat(_previous.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(name => (Name: name, Amount: checked(totals.GetValueOrDefault(name) - _previous.GetValueOrDefault(name))))
            .Where(pair => pair.Amount != 0).ToArray();
        var revision = differences.Length > 0 || count != _dropCount || latest != _latestArrival ||
            _hasProjection && correctionVersion != _lastCorrectionVersion
            ? checked(_revision + 1) : _revision;
        var projection = new LootTotalsProjection(revision, totals, count, latest)
        {
            QuantityCorrectionRevision = correctionVersion,
        };
        projection.Validate();
        var events = new List<TrackedLootEvent>();
        foreach (var (name, amount) in differences)
        {
            var remaining = amount;
            var chunk = 0;
            while (remaining != 0)
            {
                var delta = (int)Math.Clamp(remaining, -int.MaxValue, int.MaxValue);
                var identity = Encoding.UTF8.GetBytes($"lifetime-v1|{timestamp.UtcTicks}|{revision}|{name}|{chunk++}");
                events.Add(new(new Guid(SHA256.HashData(identity).AsSpan(0, 16)), timestamp, name, delta));
                remaining -= delta;
            }
        }
        return (projection, events.AsReadOnly());
    }

    private void Commit(LootTotalsProjection projection)
    {
        _previous = projection.Totals;
        _dropCount = projection.ConfirmedDropCount;
        _latestArrival = projection.LatestArrivalAt;
        _revision = projection.Revision;
        _lastCorrectionVersion = projection.QuantityCorrectionRevision;
        _hasProjection = true;
    }

    public void Reset()
    {
        _rare.Clear();
        _rareLots.Clear();
        _previous = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>());
        _dropCount = 0;
        _latestArrival = null;
        _revision = 0;
        _normalEvidence = _specialEvidence = null;
        _correctionEvidenceTrusted = true;
        _correctionVersion = 0;
        _lastCorrectionVersion = null;
        _hasProjection = false;
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null || left >= right ? left : right;
}
