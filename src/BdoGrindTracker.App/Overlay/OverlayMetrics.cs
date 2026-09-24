using System.Collections.ObjectModel;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Components;
using BdoGrindTracker.App.Integrations.Garmoth;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Overlay;

/// <summary>Projects existing session totals. This class never creates or changes loot events.</summary>
internal sealed partial class OverlayMetrics
{
    private string? _catalogLanguage;
    private IReadOnlyList<OverlayLootItem> _itemCatalog = [];
    private static readonly HashSet<string> TrashItems = TrashLootMinimumCatalog.Entries
        .Select(entry => entry.ItemName).ToHashSet(StringComparer.Ordinal);

    // A deliberately explicit presentation filter. The aggregate does not retain whether
    // an item was read in the rare banner, and common market materials are not rare drops.
    private static readonly HashSet<string> RareItems = new(StringComparer.Ordinal)
    {
        "Apeiron Belt", "Apeiron Earring", "Apeiron Necklace", "Apeiron Ring",
        "Deboreka Belt", "Deboreka Earring", "Deboreka Necklace", "Deboreka Ring",
        "WON Crystal of Dusky Ruin", "BON Crystal of Dusky Ruin",
        "JIN Crystal of Dusky Ruin", "HAN Crystal of Dusky Ruin",
        "Distorted Crystal of Origin", "Herald's Crystal", "Flawless Herald's Crystal",
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

    internal OverlaySnapshot Update(TrackerState state, TrackerPreferences preferences, LootPriceSnapshot? prices = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(preferences);
        var session = new LiveSessionPresentation(state, preferences.UiLanguage);
        var consumables = ProjectConsumables(state.Buffs, preferences.UiLanguage);
        string T(string value) => AppText.Translate(value, preferences.UiLanguage);
        string Number(long value) => value.ToString("N0", AppText.Culture(preferences.UiLanguage));

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
        var incomplete = !state.Silver.IsComplete;
        var valuationDetail = session.SilverDetail;
        ProjectLoot(state.Loot.Totals, profile?.TrashItemName, language, preferences.UiLanguage);
        ProjectDropHistory(state, preferences, prices, language, profile?.TrashItemName);
        var drops = _drops;

        var rateText = session.SilverPerHour + (session.PartialSilverHourly ? " *" : "");
        var grindRating = session.GrindRating;
        var rotation = BdoGrindTracker.App.Analysis.RotationProfiles.Present(state.SpotId, state.Rotation);
        var metrics = new Dictionary<string, OverlayMetric>(StringComparer.Ordinal)
        {
            ["duration"] = new(T("Aktive Zeit"), session.Duration, session.DurationNote, Tooltip: session.DurationDescription),
            ["experience"] = new(T("Erfahrung"), session.Experience.Gain, session.Experience.Hourly + " / h",
                Tooltip: session.Experience.Description),
            ["spot"] = new("Grindspot", Presentation.SpotName(state.SpotId, preferences.UiLanguage), state.CharacterLabel),
            ["silver"] = new(T("Silber netto"), session.Silver + (session.PartialSilver ? " *" : ""), valuationDetail),
            ["silver-hour"] = new(T("Silber / Stunde"), rateText, incomplete ? valuationDetail : T("Ø aktive Grindzeit")),
            ["trash"] = new(T("Trashloot"), session.Trash, profile is null ? T("Spot wird erkannt") :
                ItemLocalizationCatalog.DisplayName(profile.TrashItemName, language)),
            ["trash-hour"] = new(T("Trash / Stunde"), session.TrashHourly, T("Ø aktive Grindzeit")),
            ["drops"] = new("Drops", Number(state.Loot.ItemTypeCount), T("Verschiedene Items")),
            ["consumables"] = new(T("Verbrauchte Items"), consumables.Cost, consumables.CostDescription,
                IsWarning: consumables.HasMissingPrices),
            ["rare-drops"] = new(T("Seltene Drops"), Number(_rareDrops.Count), T("Auswahl seltener Items")),
            ["total-drops"] = new(T("Bestätigte Drops"), Number(state.Loot.ConfirmedEventCount)),
            // Missing prices flatten the silver layer; the timeline says so below its plot.
            ["chart"] = new(T("Session-Timeline"), session.Duration, incomplete ? valuationDetail : null),
            ["controls"] = new("Tracking", session.Status),
            ["status"] = new("Session", session.Status, state.Status),
            ["loot-scroll"] = new(T("Loot-Scroll"), session.LootScroll, IsWarning: session.LootScrollWarning),
            ["grind-rating"] = new(T("Grind-Bewertung"), grindRating.Label, grindRating.Detail,
                Tone: grindRating.Tone, Tooltip: grindRating.Description) { Spectrum = grindRating.Spectrum },
            ["rotations-hour"] = RotationsPerHour(rotation, preferences.UiLanguage, preferences.IncludeSpecialEventRotations),
            ["rotation-count"] = RotationCount(rotation, preferences.UiLanguage),
            ["special-events"] = SpecialEvents(rotation, preferences.UiLanguage),
            ["special-events-hour"] = SpecialEventsPerHour(rotation, session.Elapsed, preferences.UiLanguage),
        };

        return new()
        {
            UiLanguage = AppText.NormalizeLanguage(preferences.UiLanguage),
            ThemeId = preferences.EffectiveOverlayThemeId,
            Metrics = new ReadOnlyDictionary<string, OverlayMetric>(metrics),
            Drops = drops,
            Consumables = consumables,
            RareDrops = _rareDrops,
            ItemCatalog = _itemCatalog,
            SessionElapsed = session.Elapsed,
            ObservedAt = state.ObservedAt,
            SilverDrops = _silverDrops,
            TrashDrops = _trashDrops,
            Rotation = rotation,
            DropMarkers = _dropMarkers,
            LootScroll = state.LootScroll,
            Status = state.Status,
            IsRunning = state.IsRunning,
            CanNewSession = !state.IsRunning && !state.IsBusy,
            CanToggleTracking = state.IsRunning ? state.CanPause :
                !state.IsBusy && !state.IsSubmitted && !state.IsInstallingOcrLanguage &&
                state.AnalyzerAvailable && state.TrackingBlockedReason is null &&
                state.MissingOcrLanguageTag is null && !state.OcrRestartRequired,
            TrackingButtonLabel = state.IsRunning ? T("Pausieren") : state.HasSession ? T("Fortsetzen") : T("Tracking starten"),
        };
    }

    private static OverlayMetric RotationsPerHour(RotationMonitorSnapshot rotation, string language, bool includeSpecialEvents = true)
    {
        string T(string value) => AppText.Translate(value, language);
        const string label = "Rotations / h";
        const string tooltip = "Volle Rotationen pro Stunde beim aktuellen Tempo: 60 Minuten geteilt durch die " +
            "durchschnittliche Zeit der letzten bis zu drei in dieser Session vollständig abgeschlossenen Rotationen, " +
            "jeweils einschließlich Rückweg bis zum Start der nächsten Rotation. Bis die nächste Rotation beginnt, gilt " +
            "der durchschnittliche Rückweg dieser Session. Pausen über zwei Minuten, Aufbau und abgebrochene Versuche zählen nicht.";
        if (!rotation.HasProfile) return new(label, "—", T("Kein Rotationsprofil für diesen Spot"), Tooltip: T(tooltip));
        // Without special events the tempo follows regular rotations only; the walk back stays a session average.
        if (UI.SessionRotationStats.Tempo(rotation, includeSpecialEvents) is not { } average)
            return new(label, "—", T(includeSpecialEvents || UI.SessionRotationStats.Count(rotation) == 0
                ? "Nach der ersten vollständigen Rotation" : "Nach der ersten Rotation ohne Special Event"), Tooltip: T(tooltip));
        var recent = UI.SessionRotationStats.RecentCount(rotation, includeSpecialEvents);
        var knownWalk = UI.SessionRotationStats.HasKnownWalkBack(rotation);
        var rate = 3600 / average;
        // One decimal: between two and six rotations per hour a whole number hides most of the tempo.
        return new(label, rate.ToString("0.0", AppText.Culture(language)),
            $"Ø {RotationPhases.Duration(average)} · " +
            (!knownWalk ? T("ohne Rückweg") : recent == 1 ? T("1 Rotation") :
                AppText.Format("letzte {0}", language, recent)), Tooltip: T(tooltip));
    }

    private static OverlayMetric RotationCount(RotationMonitorSnapshot rotation, string language)
    {
        string T(string value) => AppText.Translate(value, language);
        const string label = "Rotation Counter";
        const string tooltip = "Vollständig abgeschlossene Rotationen am aktuellen Spot in dieser Session. " +
            "Abgebrochene oder unvollständig erkannte Rotationen zählen nicht.";
        if (!rotation.HasProfile) return new(label, "—", T("Kein Rotationsprofil für diesen Spot"), Tooltip: T(tooltip));
        var rotations = BdoGrindTracker.App.UI.SessionRotationStats.Completed(rotation);
        return new(label, rotations.Count.ToString("N0", AppText.Culture(language)),
            rotations.Count == 0 ? T("In dieser Session") :
                AppText.Format("Zuletzt {0}", language, RotationPhases.Duration(rotations[^1].Duration)), Tooltip: T(tooltip));
    }

    private const string SpecialEventsTooltip = "Special Events sind zufällige Mechaniken, die eine volle Rotation nicht braucht " +
        "und die zusätzlich oder ersetzend auftreten: das Agris-Event in Aphrodon, das Mini-AFK in Event Horizon und die " +
        "Fragmente of Divinity in Magaia. Gezählt wird jedes erkannte Special Event dieser Session, auch in abgebrochenen " +
        "oder laufenden Rotationen.";

    private static OverlayMetric SpecialEvents(RotationMonitorSnapshot rotation, string language)
    {
        string T(string value) => AppText.Translate(value, language);
        const string label = "Special Events";
        var count = rotation.SessionSpecialEvents.ToString("N0", AppText.Culture(language));
        return new(label, count, rotation.SpecialEventActive ? T("Special Event läuft") :
            rotation.HasProfile && !rotation.SupportsSpecialEvents ? T("Keine Special Events an diesem Spot") : T("In dieser Session"),
            Tooltip: T(SpecialEventsTooltip));
    }

    private static OverlayMetric SpecialEventsPerHour(RotationMonitorSnapshot rotation, TimeSpan elapsed, string language)
    {
        string T(string value) => AppText.Translate(value, language);
        const string label = "Special Events / h";
        if (elapsed <= TimeSpan.Zero) return new(label, "—", T("Ø aktive Grindzeit"), Tooltip: T(SpecialEventsTooltip));
        var rate = rotation.SessionSpecialEvents / elapsed.TotalHours;
        return new(label, rate.ToString("0.0", AppText.Culture(language)),
            AppText.Format("{0} in {1}", language, rotation.SessionSpecialEvents.ToString("N0", AppText.Culture(language)),
                Presentation.Duration(elapsed)), Tooltip: T(SpecialEventsTooltip));
    }

    /// <summary>Each recorded loot increase valued with the current prices, so a price update revalues the whole curve.</summary>
    private static IReadOnlyList<OverlaySilverDrop> SilverDrops(TrackerState state, TrackerPreferences preferences, LootPriceSnapshot? prices)
    {
        if (prices is null || state.DropHistory.Count == 0) return [];
        var tax = preferences.Tax;
        return Array.AsReadOnly(state.DropHistory.Select(drop =>
        {
            if (!prices.TryGetQuote(drop.ItemName, out var quote)) return new OverlaySilverDrop(drop.Elapsed, 0);
            try { return new OverlaySilverDrop(drop.Elapsed, checked(SilverValuation.UnitAfterTax(quote, tax) * drop.Quantity)); }
            catch (OverflowException) { return new OverlaySilverDrop(drop.Elapsed, 0); }
        }).ToArray());
    }

    /// <summary>Favorites and items worth more than 200 million appear as timeline markers.</summary>
    private static bool IsMarked(string itemName, TrackerPreferences preferences, LootPriceSnapshot? prices) =>
        UI.SessionLootMarkers.IsMarked(itemName, preferences, prices);

    internal static OverlaySnapshot Demo { get; } = CreateDemo();

    internal static OverlaySnapshot DemoFor(string language) => language == "en" ? EnglishDemo.Value : Demo;
    private static readonly Lazy<OverlaySnapshot> EnglishDemo = new(() => CreateDemo("en"));

    /// <summary>The example session of <see cref="DemoSession"/>; loot scroll and daily goal target are illustrative.</summary>
    private static OverlaySnapshot CreateDemo(string language = "de")
    {
        var metrics = new OverlayMetrics();
        var prices = DemoSession.Prices;
        var preferences = new TrackerPreferences
        {
            GameLanguage = "en", UiLanguage = language, ValuePack = DemoSession.Tax.ValuePack, MerchantRing = DemoSession.Tax.MerchantRing,
            FamilyFame = DemoSession.Tax.FamilyFame,
        };
        var state = new TrackerState
        {
            SessionId = new Guid("45a2fa66-4b17-406e-bf52-29700850e9fc"),
            HasSession = true, IsRunning = true, CanPause = true, IsDemo = true, AnalyzerAvailable = true,
            SpotId = LootSpotCatalog.HermesiaId, CharacterLabel = CompanionCharacterClassCatalog.FindById("shai")?.DisplayName ?? "Shai",
            DetectedGameLanguage = "en", Status = AppText.Format("Beispieldaten · Hermesia-Session vom {0}", language, DemoSession.Date),
            ExperienceGainedPercentagePoints = DemoSession.ExperienceGainedPercentagePoints,
            ExperienceObservedDuration = DemoSession.ExperienceObservedDuration,
            ExperienceStartLevel = DemoSession.ExperienceLevel, ExperienceEndLevel = DemoSession.ExperienceLevel,
            LootScroll = new(LootScrollStatus.Active, Level: 2),
            Buffs = DemoConsumptions(),
            GrindBenchmark = GarmothGrindBenchmarks.Find(LootSpotCatalog.HermesiaId),
        };
        // The silver of everything the drop timeline recorded up to the end of the session.
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        var drops = DemoSession.DropHistory;
        foreach (var drop in drops.Where(drop => drop.Elapsed <= DemoSession.Elapsed))
            totals[drop.ItemName] = totals.GetValueOrDefault(drop.ItemName) + drop.Quantity;
        state = state with
        {
            Elapsed = DemoSession.Elapsed, Silver = SilverValuation.Calculate(totals, prices, DemoSession.Tax),
            Loot = new LootSessionSnapshot(DemoSession.Totals, DemoSession.Totals.Values.Sum(), DemoSession.ConfirmedEventCount),
            DropHistory = drops, Rotation = HermesiaRotationDemo.At(350),
        };
        var snapshot = metrics.Update(state, preferences, prices);
        return snapshot with { DailyGoal = new(state.Silver.AfterTax, 10_000_000_000) { UiLanguage = language } };
    }

    private static BuffLedgerSnapshot DemoConsumptions()
    {
        var at = DateTimeOffset.UnixEpoch;
        var consumptions = new List<BuffConsumption>();
        Add("simple-cron-meal", 1, 1_450_000m);
        Add("harmony-draught-edania", 3, 9_200_000m);
        Add("perfume-of-courage", 3, 4_800_000m);
        Add("tent-body-enhancement-180", 1, 4_500_000m);
        return new(consumptions.AsReadOnly(), [], []);

        void Add(string id, int count, decimal price)
        {
            var definition = BuffPriceCatalog.ResolveRecognitionDefinition(id);
            if (definition is null) return;
            for (var index = 0; index < count; index++)
                consumptions.Add(new(id, definition.Name, definition.MarketItemId, at.AddMinutes(index * 20),
                    new(price, "EU", at, false)
                    { Source = definition.FixedUnitPrice is null ? BuffPriceSource.CentralMarket : BuffPriceSource.FixedNpc })
                { IsSessionStart = index == 0 });
        }
    }
}
