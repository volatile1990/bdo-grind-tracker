using System.Collections.ObjectModel;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Overlay;

/// <summary>Projects existing session totals. This class never creates or changes loot events.</summary>
internal sealed class OverlayMetrics
{
    private string? _catalogLanguage;
    private IReadOnlyList<OverlayLootItem> _itemCatalog = [];
    private static readonly HashSet<string> TrashItems = LootSpotPresentationCatalog.Profiles
        .Select(profile => profile.TrashItemName).ToHashSet(StringComparer.Ordinal);

    // A deliberately explicit presentation filter. The aggregate does not retain whether
    // an item was read in the rare banner, and common market materials are not rare drops.
    private static readonly HashSet<string> RareItems = new(StringComparer.Ordinal)
    {
        "Apeiron Belt", "Apeiron Earring", "Apeiron Necklace", "Apeiron Ring",
        "WON Wandering Origin Crystal", "BON Wandering Origin Crystal",
        "JIN Wandering Origin Crystal", "HAN Wandering Origin Crystal",
        "Twilight of the End - Belt", "Twilight of the End - Earring",
        "Twilight of the End - Necklace", "Twilight of the End - Ring",
        "Embers of Ynix - Armor", "Embers of Ynix - Helmet", "Embers of Ynix - Gloves", "Embers of Ynix - Shoes",
        "Broken Vestige of Goldroot", "Broken Vestige of Ebonmere", "Broken Vestige of Everlight",
        "Broken Vestige of Crimsonflare", "Broken Vestige of Voidreach",
        "Silent Crystal of Origin", "Pure Black Stone",
        "Refined Origin of Hunger", "Refined Essence of Devouring",
        "Corrupt Oil of Immortality",
        "Crimson Primordial Pigment - Sovereign", "Crimson Primordial Luster - Sovereign",
        "Violet Primordial Pigment - Sovereign", "Violet Primordial Luster - Sovereign",
        "Violet Primordial Pigment - Edana", "Violet Primordial Luster - Edana",
        "Sunset Primordial Pigment - Edana", "Sunset Primordial Luster - Edana",
        "White Primordial Pigment - Sovereign", "White Primordial Luster - Sovereign",
        "White Primordial Pigment - Edana", "White Primordial Luster - Edana",
    };

    internal OverlaySnapshot Update(TrackerState state, TrackerPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(preferences);
        var session = new LiveSessionPresentation(state);

        var language = preferences.GameLanguage == "auto" ? state.DetectedGameLanguage ?? "en" : preferences.GameLanguage;
        if (_catalogLanguage != language)
        {
            _catalogLanguage = language;
            _itemCatalog = Array.AsReadOnly(LootSpotCatalog.Spots.SelectMany(spot => spot.AllowedItems)
                .Concat(LootSpotCatalog.EventItems).Distinct(StringComparer.Ordinal)
                .Select(name => new OverlayLootItem(name, ItemLocalizationCatalog.DisplayName(name, language),
                    "0", Presentation.ItemIcon(name), RareItems.Contains(name), 0, TrashItems.Contains(name)))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        }
        var profile = Presentation.Profile(state.SpotId);
        var positiveItems = state.Loot.Totals.Where(item => item.Value > 0).ToArray();
        var incomplete = !state.Silver.IsComplete;
        var valuationDetail = session.SilverDetail;
        var drops = positiveItems
            .OrderByDescending(item => string.Equals(item.Key, profile?.TrashItemName, StringComparison.Ordinal))
            .ThenByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new OverlayLootItem(item.Key, ItemLocalizationCatalog.DisplayName(item.Key, language),
                Presentation.Number(item.Value), Presentation.ItemIcon(item.Key), RareItems.Contains(item.Key),
                item.Value, TrashItems.Contains(item.Key)))
            .ToArray();

        var rateText = session.SilverPerHour + (session.PartialSilverHourly ? " *" : "");
        var grindRating = session.GrindRating;
        var metrics = new Dictionary<string, OverlayMetric>(StringComparer.Ordinal)
        {
            ["duration"] = new("Aktive Zeit", session.Duration, "Ohne Pausenzeiten"),
            ["spot"] = new("Grindspot", Presentation.SpotName(state.SpotId), state.CharacterLabel),
            ["silver"] = new("Silber netto", session.Silver + (session.PartialSilver ? " *" : ""), valuationDetail),
            ["silver-hour"] = new("Silber / Stunde", rateText, incomplete ? valuationDetail : "Ø aktive Grindzeit"),
            ["trash"] = new("Trashloot", session.Trash, profile is null ? "Spot wird erkannt" :
                ItemLocalizationCatalog.DisplayName(profile.TrashItemName, language)),
            ["trash-hour"] = new("Trash / Stunde", session.TrashHourly, "Ø aktive Grindzeit"),
            ["drops"] = new("Drops", Presentation.Number(state.Loot.ItemTypeCount), "Verschiedene Items"),
            ["rare-drops"] = new("Seltene Drops", Presentation.Number(drops.Count(item => item.IsRare)), "Auswahl seltener Items"),
            ["total-drops"] = new("Bestätigte Drops", Presentation.Number(state.Loot.ConfirmedEventCount)),
            ["chart"] = new("Silber / h · Verlauf", rateText, incomplete ? valuationDetail : "Session-Durchschnitt"),
            ["controls"] = new("Tracking", session.Status),
            ["status"] = new("Session", session.Status, state.Status),
            ["loot-scroll"] = new("Loot-Scroll", session.LootScroll, IsWarning: session.LootScrollWarning),
            ["grind-rating"] = new("Grind-Bewertung", grindRating.Label, grindRating.Detail,
                Tone: grindRating.Tone, Tooltip: grindRating.Description),
        };

        return new()
        {
            Metrics = new ReadOnlyDictionary<string, OverlayMetric>(metrics),
            Drops = Array.AsReadOnly(drops),
            RareDrops = Array.AsReadOnly(drops.Where(item => item.IsRare).ToArray()),
            ItemCatalog = _itemCatalog,
            SilverHistory = state.SilverHistory,
            LootScroll = state.LootScroll,
            Status = state.Status,
            IsRunning = state.IsRunning,
            CanToggleTracking = state.IsRunning ? state.CanPause :
                !state.IsBusy && !state.IsSubmitted && !state.IsInstallingOcrLanguage &&
                state.AnalyzerAvailable && state.TrackingBlockedReason is null &&
                state.MissingOcrLanguageTag is null && !state.OcrRestartRequired,
            TrackingButtonLabel = state.IsRunning ? "Pausieren" : state.HasSession ? "Fortsetzen" : "Tracking starten",
        };
    }

    internal static OverlaySnapshot Demo { get; } = CreateDemo();

    private static OverlaySnapshot CreateDemo()
    {
        var metrics = new OverlayMetrics();
        var history = new SessionSilverHistory();
        var state = new TrackerState
        {
            SessionId = new Guid("15a78f74-8514-4a50-b510-8eefb1c95c8e"),
            HasSession = true, IsRunning = true, CanPause = true, IsDemo = true, AnalyzerAvailable = true,
            SpotId = LootSpotCatalog.HermesiaId, CharacterLabel = "Agent", DetectedGameLanguage = "de",
            Status = "Beispieldaten · keine echte Session",
            LootScroll = new(LootScrollStatus.Active, Level: 2),
            GrindBenchmark = GarmothGrindBenchmarks.Find(LootSpotCatalog.HermesiaId),
            Loot = new LootSessionSnapshot(new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["Black Crystal Fragment"] = 2387,
                ["Black Stone"] = 112,
                ["Caphras Stone"] = 41,
                ["BON Wandering Origin Crystal"] = 1,
                ["Ancient Spirit Dust"] = 73,
            }, 2614, 536),
        };
        var preferences = new TrackerPreferences { GameLanguage = "de" };
        OverlaySnapshot snapshot = new();
        var rates = new decimal[] { 1660, 1540, 1810, 1730, 1910, 1850, 1990, 1940, 2070, 2040, 2110, 2090 };
        for (var index = 0; index < rates.Length; index++)
        {
            var elapsed = TimeSpan.FromSeconds(830 + index * 10);
            var value = rates[index] * 1_000_000m * (decimal)elapsed.TotalHours;
            state = state with { Elapsed = elapsed, Silver = new(value, value, 5, [], [], false) };
            snapshot = metrics.Update(state with { SilverHistory = history.Update(state) }, preferences);
        }
        return snapshot;
    }
}
