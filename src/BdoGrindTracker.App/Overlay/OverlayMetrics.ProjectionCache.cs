using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Overlay;

internal sealed partial class OverlayMetrics
{
    // Keep only the latest projection. Inputs are copied on a change because some
    // publishers create equivalent arrays on every tick; a reference key misses those.
    private Dictionary<string, long>? _lootTotals;
    private (string? Trash, string GameLanguage, string UiLanguage) _lootKey;
    private IReadOnlyList<OverlayLootItem> _drops = [], _rareDrops = [];
    private IReadOnlyList<BuffConsumption>? _consumptions;
    private IReadOnlyList<BuffConsumption>? _consumptionsSource;
    private bool _hasBuffObservation;
    private string? _consumablesLanguage;
    private ConsumablesPresentation? _consumables;
    private IReadOnlyList<SessionDropSample>? _dropHistory;
    private IReadOnlyList<SessionDropSample>? _dropHistorySource;
    private LootPriceSnapshot? _dropPrices;
    private SilverTaxOptions? _dropTax;
    private IReadOnlyList<string> _dropFavorites = [];
    private (string GameLanguage, string UiLanguage) _dropLanguage;
    private string? _dropTrash;
    private IReadOnlyList<OverlaySilverDrop> _silverDrops = [];
    private IReadOnlyList<SessionDropSample> _trashDrops = [];
    private IReadOnlyList<OverlayDropMarker> _dropMarkers = [];

    private void ProjectLoot(IReadOnlyDictionary<string, long> totals, string? trash,
        string gameLanguage, string uiLanguage)
    {
        var key = (trash, gameLanguage, uiLanguage);
        if (_lootKey == key && _lootTotals is not null && _lootTotals.Count == totals.Count &&
            totals.All(item => _lootTotals.TryGetValue(item.Key, out var previous) && previous == item.Value)) return;
        var drops = totals.Where(item => item.Value > 0)
            .OrderByDescending(item => string.Equals(item.Key, trash, StringComparison.Ordinal))
            .ThenByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new OverlayLootItem(item.Key, ItemLocalizationCatalog.DisplayName(item.Key, gameLanguage),
                item.Value.ToString("N0", AppText.Culture(uiLanguage)), Presentation.ItemIcon(item.Key),
                RareItems.Contains(item.Key), item.Value, TrashItems.Contains(item.Key))).ToArray();
        _drops = Array.AsReadOnly(drops);
        _rareDrops = Array.AsReadOnly(drops.Where(item => item.IsRare).ToArray());
        _lootTotals = new(totals, StringComparer.Ordinal);
        _lootKey = key;
    }

    private ConsumablesPresentation ProjectConsumables(BuffLedgerSnapshot? buffs, string language)
    {
        var consumptions = buffs?.Consumptions ?? [];
        if (_consumables is null || _consumablesLanguage != language || _hasBuffObservation != (buffs is not null) ||
            _consumptions is null || !ReferenceEquals(_consumptionsSource, consumptions) && !_consumptions.SequenceEqual(consumptions))
        {
            _consumables = ConsumablesPresentation.Create(buffs, language);
            _consumptions = consumptions.ToArray();
            _consumablesLanguage = language;
            _hasBuffObservation = buffs is not null;
        }
        _consumptionsSource = consumptions;
        return _consumables;
    }

    private void ProjectDropHistory(TrackerState state, TrackerPreferences preferences,
        LootPriceSnapshot? prices, string gameLanguage, string? trash)
    {
        var tax = preferences.Tax;
        var language = (gameLanguage, preferences.UiLanguage);
        if (_dropHistory is not null && ReferenceEquals(_dropPrices, prices) && _dropTax == tax &&
            _dropLanguage == language && _dropTrash == trash && _dropFavorites.SequenceEqual(preferences.FavoriteItems) &&
            (ReferenceEquals(_dropHistorySource, state.DropHistory) || _dropHistory.SequenceEqual(state.DropHistory)))
        {
            _dropHistorySource = state.DropHistory;
            return;
        }

        _silverDrops = SilverDrops(state, preferences, prices);
        _dropMarkers = Array.AsReadOnly(state.DropHistory
            .Where(drop => IsMarked(drop.ItemName, preferences, prices))
            .Select(drop => new OverlayDropMarker(drop.Elapsed, new OverlayLootItem(drop.ItemName,
                ItemLocalizationCatalog.DisplayName(drop.ItemName, gameLanguage),
                drop.Quantity.ToString("N0", AppText.Culture(preferences.UiLanguage)),
                Presentation.ItemIcon(drop.ItemName), true, drop.Quantity))).ToArray());
        _trashDrops = trash is null ? [] : Array.AsReadOnly(state.DropHistory
            .Where(drop => string.Equals(drop.ItemName, trash, StringComparison.Ordinal)).ToArray());
        _dropHistory = state.DropHistory.ToArray();
        _dropHistorySource = state.DropHistory;
        _dropPrices = prices;
        _dropTax = tax;
        _dropFavorites = preferences.FavoriteItems.ToArray();
        _dropLanguage = language;
        _dropTrash = trash;
    }
}
