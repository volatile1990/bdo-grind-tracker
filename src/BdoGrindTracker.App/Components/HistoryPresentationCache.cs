using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;

namespace BdoGrindTracker.App.Components;

/// <summary>Retains only a bounded current history projection, never earlier revisions.</summary>
internal sealed class HistoryPresentationCache
{
    internal const int MaximumCachedEntries = 4_096;
    internal const long MaximumEstimatedRetainedBytes = 8 * 1_024 * 1_024;
    private Dictionary<Guid, ValuedEntry> _values = [];
    private readonly Dictionary<string, bool> _searchMatches = new(StringComparer.Ordinal);
    private (string Query, string Language)? _search;
    private LootPriceSnapshot? _prices;
    private SilverTaxOptions? _tax;
    private long _generation;
    private LootHistoryEntry[] _selected = [];
    private HistoryProjection _projection = new([]);

    internal int CachedEntryCount => _values.Count;
    internal long EstimatedRetainedBytes { get; private set; }

    internal HistoryProjection Update(IReadOnlyList<LootHistoryEntry> history, LootPriceSnapshot prices,
        SilverTaxOptions tax, string? spotId, int days, string characterClass, string query,
        string language, DateTimeOffset now)
    {
        if (!ReferenceEquals(_prices, prices) || _tax != tax)
        {
            _values.Clear();
            _prices = prices;
            _tax = tax;
        }
        var search = (query.Trim(), language);
        if (_search != search)
        {
            _searchMatches.Clear();
            _search = search;
        }
        var cutoff = days == 0 ? DateTimeOffset.MinValue : now.AddDays(-days);
        var generation = ++_generation;
        List<LootHistoryEntry>? changed = null;
        var count = 0;
        long estimatedRetainedBytes = 0;
        foreach (var entry in history)
        {
            // Filter before valuation, including the selected spot. The cutoff is
            // checked every time so a running date window also works after clock rewinds.
            if (entry.StartedAt < cutoff || characterClass.Length > 0 && entry.CharacterClass != characterClass ||
                (spotId is not null ? entry.SpotId != spotId : !MatchesSearch(entry.SpotId, search))) continue;
            var valued = Value(entry, prices, tax, generation, out var estimatedEntryBytes);
            estimatedRetainedBytes += estimatedEntryBytes;
            if (changed is not null) changed.Add(valued);
            else if (count >= _selected.Length || !ReferenceEquals(_selected[count], valued))
            {
                changed = new List<LootHistoryEntry>(_selected.Take(count)) { valued };
            }
            count++;
        }
        if (_values.Values.Any(value => value.Generation != generation))
            foreach (var id in _values.Where(pair => pair.Value.Generation != generation).Select(pair => pair.Key).ToArray())
                _values.Remove(id);
        if (changed is null && count != _selected.Length)
            changed = new List<LootHistoryEntry>(_selected.Take(count));
        if (changed is not null)
        {
            _selected = changed.ToArray();
            _projection = new HistoryProjection(_selected);
        }
        var projection = _projection;
        if (count > MaximumCachedEntries || estimatedRetainedBytes > MaximumEstimatedRetainedBytes)
        {
            // Return every session, but do not retain this large view or dictionary capacity.
            _values = [];
            _selected = [];
            _projection = new([]);
            _searchMatches.Clear();
            _search = null;
            _prices = null;
            _tax = null;
            EstimatedRetainedBytes = 0;
        }
        else EstimatedRetainedBytes = estimatedRetainedBytes;
        return projection;
    }

    private bool MatchesSearch(string spotId, (string Query, string Language) search)
    {
        if (!_searchMatches.TryGetValue(spotId, out var matches))
        {
            matches = Presentation.SpotName(spotId, search.Language).Contains(search.Query, StringComparison.OrdinalIgnoreCase);
            if (_searchMatches.Count < 128) _searchMatches[spotId] = matches;
        }
        return matches;
    }

    private LootHistoryEntry Value(LootHistoryEntry entry, LootPriceSnapshot prices, SilverTaxOptions tax,
        long generation, out long estimatedRetainedBytes)
    {
        _values.TryGetValue(entry.SessionId, out var cached);
        // IReadOnly interfaces do not confer ownership: even the same entry can
        // contain a dictionary edited in place by its caller.
        var sameTotals = cached is not null && SameTotals(entry.Totals, cached.Totals);
        if (sameTotals && cached!.Source == entry)
        {
            cached.Generation = generation;
            estimatedRetainedBytes = cached.EstimatedRetainedBytes;
            return cached.Result;
        }
        var valuation = sameTotals ? cached!.Valuation : SilverValuation.Calculate(entry.Totals, prices, tax);
        var result = entry with
        {
            Totals = new Dictionary<string, long>(entry.Totals, entry.Totals.Comparer),
            SilverBeforeTax = valuation.BeforeTax,
            SilverAfterTax = valuation.AfterTax,
            SilverIsComplete = valuation.IsComplete,
        };
        estimatedRetainedBytes = EstimateRetainedBytes(entry);
        _values[entry.SessionId] = new ValuedEntry(entry, entry.Totals.ToArray(), valuation, result,
            generation, estimatedRetainedBytes);
        return result;
    }

    private static long EstimateRetainedBytes(LootHistoryEntry entry)
    {
        // Approximate retention, not a heap guarantee. Include source/result records,
        // valuation snapshots, dictionaries, grouped arrays and possible metrics/charts.
        // Shared journals are counted once on a rebuild, never traversed each UI tick.
        var bytes = 1_536L + StringBytes(entry.SpotId) + StringBytes(entry.CharacterClass);
        foreach (var item in entry.Totals) bytes += 256L + StringBytes(item.Key);
        bytes += (long)(entry.DropHistory?.Count ?? 0) * 128;
        bytes += (long)(entry.Pauses?.Count ?? 0) * 128;
        bytes += (long)(entry.Rotations?.Count ?? 0) * 512;
        bytes += (long)(entry.RotationTimeline?.Count ?? 0) * 320;
        bytes += (long)(entry.ManualLootItems?.Length ?? 0) * 128;
        bytes += (long)(entry.GarmothPendingCorrectionIntervals?.Length ?? 0) * 32;
        if (entry.Buffs is { } buffs)
            bytes += 128L + ((long)(buffs.Consumptions?.Count ?? 0) + (buffs.Usage?.Count ?? 0) + (buffs.Active?.Count ?? 0)) * 256;
        return bytes;
    }

    private static long StringBytes(string? value) => value is null ? 0 : 32L + (long)value.Length * 2;

    private static bool SameTotals(Dictionary<string, long> totals, KeyValuePair<string, long>[] previous)
    {
        if (totals.Count != previous.Length) return false;
        var index = 0;
        foreach (var pair in totals)
            if (pair.Key != previous[index].Key || pair.Value != previous[index++].Value) return false;
        return true;
    }

    private sealed class ValuedEntry(LootHistoryEntry source, KeyValuePair<string, long>[] totals,
        SilverValuationResult valuation, LootHistoryEntry result, long generation, long estimatedRetainedBytes)
    {
        internal LootHistoryEntry Source { get; } = source;
        internal KeyValuePair<string, long>[] Totals { get; } = totals;
        internal SilverValuationResult Valuation { get; } = valuation;
        internal LootHistoryEntry Result { get; } = result;
        internal long Generation { get; set; } = generation;
        internal long EstimatedRetainedBytes { get; } = estimatedRetainedBytes;
    }
}

internal sealed class HistoryProjection
{
    private readonly Dictionary<string, LootHistoryEntry[]> _spots;
    private readonly Dictionary<(string Spot, bool Chronological), (string Trash, SpotHistoryMetrics Metrics)> _metrics = [];
    private readonly Dictionary<string, (string Trash, SpotHistoryChartData Chart)> _charts = new(StringComparer.Ordinal);

    internal HistoryProjection(IEnumerable<LootHistoryEntry> entries)
    {
        var input = entries.ToArray();
        Sessions = input.OrderByDescending(entry => entry.StartedAt).ToArray();
        _spots = input.GroupBy(entry => entry.SpotId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    }

    internal LootHistoryEntry[] Sessions { get; }
    internal LootHistoryEntry[] ForSpot(string spotId) => _spots.GetValueOrDefault(spotId) ?? [];

    internal SpotHistoryMetrics Metrics(LootSpotPresentation profile, bool chronological = false)
    {
        var key = (profile.SpotId, chronological);
        if (!_metrics.TryGetValue(key, out var cached) || cached.Trash != profile.TrashItemName)
            _metrics[key] = cached = (profile.TrashItemName,
                HistoryPresentation.CalculateMetrics(profile, chronological
                    ? Sessions.Where(entry => entry.SpotId == profile.SpotId) : ForSpot(profile.SpotId)));
        return cached.Metrics;
    }

    internal SpotHistoryChartData Chart(LootSpotPresentation profile)
    {
        if (!_charts.TryGetValue(profile.SpotId, out var cached) || cached.Trash != profile.TrashItemName)
            _charts[profile.SpotId] = cached = (profile.TrashItemName,
                HistoryPresentation.BuildChartData(profile, Sessions.Where(entry => entry.SpotId == profile.SpotId)));
        return cached.Chart;
    }
}
