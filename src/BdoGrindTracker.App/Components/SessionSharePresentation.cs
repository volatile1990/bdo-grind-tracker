using System.Globalization;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Components;

public sealed record SessionShareMetric(string Label, string Value, string? Note = null);
public sealed record SessionShareRow(string Name, string Quantity, string? Hourly = null, string? IconUrl = null);
public sealed record SessionShareImageData(
    string FileName, string Title, string Subtitle, string? BackgroundUrl, string? IconUrl,
    IReadOnlyList<SessionShareMetric> Metrics, IReadOnlyList<SessionShareMetric> Details,
    IReadOnlyList<SessionShareRow> Loot, IReadOnlyList<SessionShareRow> Consumables,
    string LootHeading, string ConsumablesHeading, string QuantityLabel, string HourlyLabel, string Footer);

/// <summary>Freezes only shareable, already observed session data. Exporting never reprices or updates a session.</summary>
internal static class SessionSharePresentation
{
    internal static SessionShareImageData FromLive(TrackerState state, TrackerPreferences preferences,
        DateTimeOffset? startedAt = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(preferences);
        var view = new LiveSessionPresentation(state, preferences.UiLanguage);
        return Create(new ShareSource
        {
            SpotId = state.SpotId,
            CharacterClass = state.CharacterClassId,
            ItemLanguage = ItemLanguage(preferences, state.DetectedGameLanguage),
            Duration = view.Elapsed,
            Timestamp = state.ObservedAt,
            StartedAt = startedAt,
            Totals = state.Loot.Totals,
            Silver = view.CanShowSilver ? state.Silver.AfterTax : null,
            SilverIsComplete = state.Silver.IsComplete,
            CombatStats = CombatStatsSpotRules.ForSpot(state.CombatStats, state.SpotId)
                ?? CombatStatsSpotRules.ForSpot(state.SessionCombatStats, state.SpotId),
            Agris = view.AgrisTime,
            Experience = view.Experience,
            Buffs = state.Buffs,
            Rotations = SessionRotationStats.Completed(state.Rotation).Select(rotation => rotation.Duration).ToArray(),
        }, preferences.UiLanguage);
    }

    internal static SessionShareImageData FromHistory(LootHistoryEntry entry, TrackerPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(preferences);
        return Create(new ShareSource
        {
            SpotId = entry.SpotId,
            CharacterClass = entry.CharacterClass,
            ItemLanguage = ItemLanguage(preferences),
            Duration = entry.Duration < TimeSpan.Zero ? TimeSpan.Zero : entry.Duration,
            Timestamp = entry.UpdatedAt,
            StartedAt = entry.StartedAt,
            Totals = entry.Totals,
            // An incomplete historical zero cannot distinguish unpriced items from a known zero value.
            Silver = entry.SilverIsComplete || entry.SilverAfterTax != 0 || !entry.Totals.Any(pair => pair.Value > 0)
                ? entry.SilverAfterTax : null,
            SilverIsComplete = entry.SilverIsComplete,
            CombatStats = CombatStatsSpotRules.ForSpot(entry.CombatStats, entry.SpotId),
            Agris = HistoryPresentation.AgrisTime(entry, preferences.UiLanguage),
            Experience = HistoryPresentation.Experience(entry, preferences.UiLanguage),
            Buffs = entry.Buffs,
            Rotations = entry.Rotations.Where(rotation => rotation.SpotId == entry.SpotId
                    && rotation.Run.EligibleForStatistics && double.IsFinite(rotation.Run.Duration)
                    && rotation.Run.Duration > 0)
                .Select(rotation => rotation.Run.Duration).ToArray(),
        }, preferences.UiLanguage);
    }

    private static SessionShareImageData Create(ShareSource source, string language)
    {
        language = AppText.NormalizeLanguage(language);
        string T(string key) => AppText.Translate(key, language);
        string Number(decimal value) => Presentation.Number(value, language);
        string Money(decimal? value) => value is { } amount
            ? (source.SilverIsComplete ? "" : "≈ ") + Presentation.Silver(amount, language) : "—";
        string Hourly(decimal amount) => LiveSessionPresentation.Hourly(amount, source.Duration) is { } rate ? Number(rate) : "—";
        var profile = Presentation.Profile(source.SpotId);
        var character = CompanionCharacterClassCatalog.FindById(source.CharacterClass)
            ?? CompanionCharacterClassCatalog.Classes.FirstOrDefault(candidate =>
                candidate.DisplayName.Equals(source.CharacterClass, StringComparison.OrdinalIgnoreCase));
        var characterName = character?.DisplayName ?? (string.IsNullOrWhiteSpace(source.CharacterClass)
            ? T("Klasse") + " · —" : source.CharacterClass);
        var trash = profile is null ? (long?)null : source.Totals.GetValueOrDefault(profile.TrashItemName);
        var metrics = new SessionShareMetric[]
        {
            new(T("Dauer"), Presentation.Duration(source.Duration)),
            new(T("Trashloot"), trash is { } quantity ? Number(quantity) : "—",
                trash is { } hourlyTrash ? Hourly(hourlyTrash) + " / h" : null),
            new(T("Silber netto"), Money(source.Silver)),
            new(T("Silber / h"), Money(source.Silver is { } silver
                ? LiveSessionPresentation.Hourly(silver, source.Duration) : null)),
        };
        var details = new List<SessionShareMetric>();
        if (source.StartedAt is { } started)
            details.Add(new(T("Sessionstart"), Date(started, language)));
        var combat = source.CombatStats;
        details.Add(new("AP / DP", combat is null ? "—" : $"{Number(combat.Ap!.Value)} / {Number(combat.Dp!.Value)}",
            combat is null ? null : T(combat.Category switch
            {
                CombatStatsCategory.Edania => "Edania",
                CombatStatsCategory.Demihuman => "Halbmenschen",
                CombatStatsCategory.Kamasylvian => "Kamasilvia",
                _ => "Allgemein",
            })));
        details.Add(new(T("Agris aktiv"), source.Agris.Duration.TrimEnd(' ', '*')));
        details.Add(new(T("Erfahrung"), source.Experience.Gain,
            source.Experience.HasObservation ? source.Experience.Hourly + " / h" : null));
        if (source.Rotations.Count > 0)
        {
            details.Add(new(T("Rotationen"), Number(source.Rotations.Count)));
            details.Add(new(T("Ø Rotation"), RotationTime(source.Rotations.Average())));
            details.Add(new(T("Schnellste Rotation"), RotationTime(source.Rotations.Min())));
        }
        var consumables = ConsumablesPresentation.Create(source.Buffs, language);
        var buffCost = source.Buffs is null || consumables.HasMissingPrices
                && consumables.Items.All(item => item.UnpricedCount == item.Count)
            ? "—" : (consumables.HasMissingPrices ? "≈ " : "") + consumables.Cost.TrimEnd(' ', '*');
        details.Add(new(T("Buffkosten"), buffCost));
        var loot = source.Totals.Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Key == profile?.TrashItemName)
            .ThenByDescending(pair => OverlayMetrics.IsRareItem(pair.Key))
            .ThenByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SessionShareRow(ItemLocalizationCatalog.DisplayName(pair.Key, source.ItemLanguage),
                Number(pair.Value), Hourly(pair.Value), Presentation.ItemIcon(pair.Key)))
            .ToArray();
        return new SessionShareImageData(
            "grindcrest-session-" + (profile?.SpotId ?? "unknown") + "-"
                + source.Timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png",
            profile is null ? T("Grindspot") + " · —" : Presentation.SpotName(source.SpotId, language),
            characterName,
            profile?.BackgroundFileName is { Length: > 0 } background ? "assets/spot-backgrounds/" + Path.GetFileName(background) : null,
            character is null ? null : Presentation.ClassIcon(character.Id),
            Array.AsReadOnly(metrics), details.AsReadOnly(), Array.AsReadOnly(loot),
            Array.AsReadOnly(consumables.Items.Select(item =>
                new SessionShareRow(item.Name, Number(item.Count), IconUrl: item.IconPath)).ToArray()),
            T("Loot der Session"), T("Verbrauchte Items"), T("Anzahl"), T("Pro Stunde"), "GRINDCREST · BDO GRIND TRACKER");
    }

    private static string Date(DateTimeOffset value, string language) =>
        value.ToLocalTime().ToString("g", AppText.Culture(language)) + " " + value.ToLocalTime().ToString("zzz", CultureInfo.InvariantCulture);

    private static string RotationTime(double seconds) => seconds >= TimeSpan.MaxValue.TotalSeconds ? "—"
        : Presentation.Duration(TimeSpan.FromSeconds(seconds));

    private static string ItemLanguage(TrackerPreferences preferences, string? detected = null) =>
        preferences.GameLanguage is "de" or "en" ? preferences.GameLanguage
            : detected is "de" or "en" ? detected : AppText.NormalizeLanguage(preferences.UiLanguage);

    private sealed record ShareSource
    {
        public string? SpotId { get; init; }
        public string? CharacterClass { get; init; }
        public required string ItemLanguage { get; init; }
        public TimeSpan Duration { get; init; }
        public DateTimeOffset Timestamp { get; init; }
        public DateTimeOffset? StartedAt { get; init; }
        public required IReadOnlyDictionary<string, long> Totals { get; init; }
        public decimal? Silver { get; init; }
        public bool SilverIsComplete { get; init; }
        public CombatStatsState? CombatStats { get; init; }
        public required AgrisPresentation Agris { get; init; }
        public required ExperiencePresentation Experience { get; init; }
        public BuffLedgerSnapshot? Buffs { get; init; }
        public required IReadOnlyList<double> Rotations { get; init; }
    }
}
