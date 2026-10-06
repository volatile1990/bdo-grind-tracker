using System.Globalization;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Components;

internal sealed record LootTableRow(string Name, long Quantity, decimal? Value, bool Stale, TimeSpan? LastDropElapsed);

/// <summary>Owns keys for the latest inputs; mutable caller lists and dictionaries remain supported.</summary>
internal sealed class LootTableProjectionCache
{
    private KeyValuePair<string, long>[]? _totals;
    private LootPriceSnapshot? _prices;
    private SilverTaxOptions? _tax;
    private LootTableRow[] _valued = [];
    private SessionDropSample[] _drops = [];
    private Guid? _session;
    private TimeSpan _lastElapsed;
    private TimeSpan? _nextDropAt;
    private IReadOnlyDictionary<string, TimeSpan> _lastDrops = new Dictionary<string, TimeSpan>();
    private IReadOnlyDictionary<string, TimeSpan>? _rowLastDrops;
    private (string Query, string Sort, string GameLanguage, string Culture)? _rowKey;
    private IReadOnlyList<LootTableRow> _rows = [];

    internal IReadOnlyList<LootTableRow> Update(IReadOnlyDictionary<string, long> totals, LootPriceSnapshot prices,
        SilverTaxOptions tax, string query, string sort, string gameLanguage, IReadOnlyList<SessionDropSample> drops,
        TimeSpan elapsed, Guid? session)
    {
        var revalued = _totals is null || !ReferenceEquals(_prices, prices) || _tax != tax ||
            totals.Count != _totals.Length || !totals.SequenceEqual(_totals);
        if (revalued)
        {
            _totals = totals.ToArray();
            _prices = prices;
            _tax = tax;
            _valued = _totals.Select(pair =>
            {
                var valuation = SilverValuation.Calculate(new Dictionary<string, long> { [pair.Key] = pair.Value }, prices, tax);
                return new LootTableRow(pair.Key, pair.Value, valuation.IsComplete ? valuation.AfterTax : null, valuation.IsStale, null);
            }).ToArray();
        }
        UpdateLastDrops(drops, elapsed, session);
        var key = (query, sort, gameLanguage, CultureInfo.CurrentCulture.Name);
        if (!revalued && _rowKey == key && ReferenceEquals(_rowLastDrops, _lastDrops)) return _rows;
        var items = _valued.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                ItemLocalizationCatalog.GermanNames.GetValueOrDefault(item.Name, item.Name).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(item => item with { LastDropElapsed = _lastDrops.TryGetValue(item.Name, out var at) ? at : null });
        _rows = Array.AsReadOnly((sort switch
        {
            "quantity" => items.OrderByDescending(item => item.Quantity).ThenBy(item => item.Name),
            "name" => items.OrderBy(item => ItemLocalizationCatalog.DisplayName(item.Name, gameLanguage)),
            _ => items.OrderByDescending(item => item.Value).ThenBy(item => item.Name),
        }).ToArray());
        _rowKey = key;
        _rowLastDrops = _lastDrops;
        return _rows;
    }

    private void UpdateLastDrops(IReadOnlyList<SessionDropSample> drops, TimeSpan elapsed, Guid? session)
    {
        if (session is null) drops = [];
        var changed = _session != session || _drops.Length != drops.Count || !_drops.SequenceEqual(drops);
        if (!changed && elapsed >= _lastElapsed && (_nextDropAt is null || elapsed < _nextDropAt))
        {
            _lastElapsed = elapsed;
            return;
        }
        if (changed) _drops = drops.ToArray();
        var latest = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        TimeSpan? next = null;
        foreach (var drop in _drops)
        {
            if (drop.Quantity <= 0 || drop.Elapsed < TimeSpan.Zero) continue;
            if (drop.Elapsed > elapsed)
            {
                if (next is null || drop.Elapsed < next) next = drop.Elapsed;
                continue;
            }
            if (!latest.TryGetValue(drop.ItemName, out var previous) || drop.Elapsed > previous)
                latest[drop.ItemName] = drop.Elapsed;
        }
        _lastDrops = latest;
        _session = session;
        _lastElapsed = elapsed;
        _nextDropAt = next;
    }
}
