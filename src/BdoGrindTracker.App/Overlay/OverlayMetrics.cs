using System.Collections.ObjectModel;
using BdoGrindTracker.App.Character;
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
        var rotation = BdoGrindTracker.App.Analysis.RotationProfiles.Present(state.SpotId, state.Rotation);
        var metrics = new Dictionary<string, OverlayMetric>(StringComparer.Ordinal)
        {
            ["duration"] = new("Aktive Zeit", session.Duration, session.DurationNote, Tooltip: session.DurationDescription),
            ["experience"] = new("Erfahrung", session.Experience.Gain, session.Experience.Hourly + " / h",
                Tooltip: session.Experience.Description),
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
            ["rotations-hour"] = RotationsPerHour(rotation),
            ["rotation-count"] = RotationCount(rotation),
        };

        return new()
        {
            ThemeId = BdoGrindTracker.App.Theming.AppThemes.Normalize(preferences.ThemeId),
            Metrics = new ReadOnlyDictionary<string, OverlayMetric>(metrics),
            Drops = Array.AsReadOnly(drops),
            RareDrops = Array.AsReadOnly(drops.Where(item => item.IsRare).ToArray()),
            ItemCatalog = _itemCatalog,
            SilverHistory = state.SilverHistory,
            SessionElapsed = session.Elapsed,
            SilverDrops = SilverDrops(state, preferences, prices),
            Rotation = rotation,
            DropMarkers = Array.AsReadOnly(state.DropHistory
                .Where(drop => IsMarked(drop.ItemName, preferences, prices))
                .Select(drop => new OverlayDropMarker(drop.Elapsed, new OverlayLootItem(drop.ItemName,
                    ItemLocalizationCatalog.DisplayName(drop.ItemName, language), Presentation.Number(drop.Quantity),
                    Presentation.ItemIcon(drop.ItemName), true, drop.Quantity))).ToArray()),
            LootScroll = state.LootScroll,
            Status = state.Status,
            IsRunning = state.IsRunning,
            CanNewSession = !state.IsRunning && !state.IsBusy,
            CanToggleTracking = state.IsRunning ? state.CanPause :
                !state.IsBusy && !state.IsSubmitted && !state.IsInstallingOcrLanguage &&
                state.AnalyzerAvailable && state.TrackingBlockedReason is null &&
                state.MissingOcrLanguageTag is null && !state.OcrRestartRequired,
            TrackingButtonLabel = state.IsRunning ? "Pausieren" : state.HasSession ? "Fortsetzen" : "Tracking starten",
        };
    }

    // The recent tempo, not the whole session: earlier slow rotations stop affecting the estimate.
    internal const int RotationTempoSample = 3;

    private static OverlayMetric RotationsPerHour(RotationMonitorSnapshot rotation)
    {
        const string label = "Rotations / h";
        const string tooltip = "Volle Rotationen pro Stunde beim aktuellen Tempo: 60 Minuten geteilt durch die " +
            "durchschnittliche Zeit der letzten bis zu drei in dieser Session vollständig abgeschlossenen Rotationen, " +
            "jeweils einschließlich Rückweg bis zum Start der nächsten Rotation. Bis die nächste Rotation beginnt, gilt " +
            "der durchschnittliche Rückweg dieser Session. Pausen über zwei Minuten, Aufbau und abgebrochene Versuche zählen nicht.";
        if (!rotation.HasProfile) return new(label, "—", "Kein Rotationsprofil für diesen Spot", Tooltip: tooltip);
        var completed = rotation.SessionRotations.Where(timing => double.IsFinite(timing.Duration) && timing.Duration > 0).ToArray();
        var recent = completed.TakeLast(RotationTempoSample).ToArray();
        if (recent.Length == 0) return new(label, "—", "Nach der ersten vollständigen Rotation", Tooltip: tooltip);
        var walks = completed.Where(timing => timing.WalkBack is { } walk && double.IsFinite(walk))
            .Select(timing => timing.WalkBack!.Value).ToArray();
        double? averageWalk = walks.Length > 0 ? walks.Average() : null;
        var average = recent.Average(timing => timing.Duration + (timing.WalkBack ?? averageWalk ?? 0));
        var rate = 3600 / average;
        return new(label, Presentation.Number((decimal)Math.Floor(rate)),
            $"{rate.ToString("0.0", Presentation.German)} / h · Ø {RotationPhases.Duration(average)} · " +
            (averageWalk is null ? "ohne Rückweg" : recent.Length == 1 ? "1 Rotation" : $"letzte {recent.Length}"), Tooltip: tooltip);
    }

    private static OverlayMetric RotationCount(RotationMonitorSnapshot rotation)
    {
        const string label = "Rotation Counter";
        const string tooltip = "Vollständig abgeschlossene Rotationen am aktuellen Spot in dieser Session. " +
            "Abgebrochene oder unvollständig erkannte Rotationen zählen nicht.";
        if (!rotation.HasProfile) return new(label, "—", "Kein Rotationsprofil für diesen Spot", Tooltip: tooltip);
        var rotations = rotation.SessionRotations;
        return new(label, Presentation.Number(rotations.Count),
            rotations.Count == 0 ? "In dieser Session" : $"Zuletzt {RotationPhases.Duration(rotations[^1].Duration)}", Tooltip: tooltip);
    }

    /// <summary>Each recorded loot increase valued with the current prices, so a price update revalues the whole curve.</summary>
    private static IReadOnlyList<OverlaySilverDrop> SilverDrops(TrackerState state, TrackerPreferences preferences, LootPriceSnapshot? prices)
    {
        if (prices is null || state.DropHistory.Count == 0) return [];
        var tax = preferences.Tax;
        return Array.AsReadOnly(state.DropHistory.Select(drop =>
        {
            var marked = IsMarked(drop.ItemName, preferences, prices);
            if (!prices.TryGetQuote(drop.ItemName, out var quote)) return new OverlaySilverDrop(drop.Elapsed, 0, marked);
            try { return new OverlaySilverDrop(drop.Elapsed, checked(SilverValuation.UnitAfterTax(quote, tax) * drop.Quantity), marked); }
            catch (OverflowException) { return new OverlaySilverDrop(drop.Elapsed, 0, marked); }
        }).ToArray());
    }

    /// <summary>Favorites and items worth more than 200 million appear as chart markers.</summary>
    private static bool IsMarked(string itemName, TrackerPreferences preferences, LootPriceSnapshot? prices) =>
        preferences.FavoriteItems.Contains(itemName, StringComparer.Ordinal) ||
        prices is not null && prices.TryGetQuote(itemName, out var quote) && quote.UnitPrice > 200_000_000m;

    internal static OverlaySnapshot Demo { get; } = CreateDemo();

    /// <summary>The example session of <see cref="DemoSession"/>; loot scroll and daily goal target are illustrative.</summary>
    private static OverlaySnapshot CreateDemo()
    {
        var metrics = new OverlayMetrics();
        var history = new SessionSilverHistory();
        var prices = DemoSession.Prices;
        var preferences = new TrackerPreferences
        {
            GameLanguage = "en", ValuePack = DemoSession.Tax.ValuePack, MerchantRing = DemoSession.Tax.MerchantRing,
            FamilyFame = DemoSession.Tax.FamilyFame,
        };
        var state = new TrackerState
        {
            SessionId = new Guid("45a2fa66-4b17-406e-bf52-29700850e9fc"),
            HasSession = true, IsRunning = true, CanPause = true, IsDemo = true, AnalyzerAvailable = true,
            SpotId = LootSpotCatalog.HermesiaId, CharacterLabel = CompanionCharacterClassCatalog.FindById("shai")?.DisplayName ?? "Shai",
            DetectedGameLanguage = "en", Status = $"Beispieldaten · Hermesia-Session vom {DemoSession.Date}",
            ExperienceGainedPercentagePoints = DemoSession.ExperienceGainedPercentagePoints,
            ExperienceObservedDuration = DemoSession.ExperienceObservedDuration,
            ExperienceStartLevel = DemoSession.ExperienceLevel, ExperienceEndLevel = DemoSession.ExperienceLevel,
            LootScroll = new(LootScrollStatus.Active, Level: 2),
            GrindBenchmark = GarmothGrindBenchmarks.Find(LootSpotCatalog.HermesiaId),
        };

        // Replay the drop timeline in the tracker's ten-second history steps.
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        var drops = DemoSession.DropHistory;
        var next = 0;
        for (var elapsed = TimeSpan.FromSeconds(10); ; elapsed += TimeSpan.FromSeconds(10))
        {
            if (elapsed > DemoSession.Elapsed) elapsed = DemoSession.Elapsed;
            for (; next < drops.Count && drops[next].Elapsed <= elapsed; next++)
                totals[drops[next].ItemName] = totals.GetValueOrDefault(drops[next].ItemName) + drops[next].Quantity;
            state = state with { Elapsed = elapsed, Silver = SilverValuation.Calculate(totals, prices, DemoSession.Tax) };
            state = state with { SilverHistory = history.Update(state) };
            if (elapsed == DemoSession.Elapsed) break;
        }
        state = state with
        {
            Loot = new LootSessionSnapshot(DemoSession.Totals, DemoSession.Totals.Values.Sum(), DemoSession.ConfirmedEventCount),
            DropHistory = drops, Rotation = HermesiaRotationDemo.At(350),
        };
        var snapshot = metrics.Update(state, preferences, prices);
        return snapshot with { DailyGoal = new(state.Silver.AfterTax, 10_000_000_000) };
    }
}
