using System.Globalization;
using System.Text.Json;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Components;

internal static class Presentation
{
    internal static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly IReadOnlyDictionary<string, string> ItemIcons = ReadItemIcons();

    internal static string Number(decimal value, string? language = "de") => value.ToString("N0", AppText.Culture(language));
    internal static string Silver(decimal value, string? language = "de") => Math.Abs(value) switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.00", AppText.Culture(language)) + " " + AppText.Translate("Mrd.", language),
        >= 1_000_000 => (value / 1_000_000).ToString("0.0", AppText.Culture(language)) + " " + AppText.Translate("Mio.", language),
        >= 10_000 => (value / 1_000).ToString("0.0", AppText.Culture(language)) + " " + AppText.Translate("Tsd.", language),
        _ => Number(value, language)
    };
    internal static string Duration(TimeSpan duration) => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    internal static string ShortDuration(TimeSpan duration, string? language = "de") => duration.TotalHours >= 1
        ? AppText.Format("{0} Std. {1:00} Min.", language, (int)duration.TotalHours, duration.Minutes)
        : AppText.Format("{0} Min.", language, Math.Max(0, (int)duration.TotalMinutes));
    internal static string CompactDuration(TimeSpan duration, string? language = "de") => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}:{duration.Minutes:00} h"
        : $"{Math.Max(0, (int)duration.TotalMinutes)} min";
    internal static decimal Hours(TimeSpan duration) => (decimal)duration.Ticks / TimeSpan.TicksPerHour;
    internal static decimal Hourly(decimal amount, TimeSpan duration) => duration > TimeSpan.Zero ? amount / Hours(duration) : 0;
    internal static string SpotName(string? id, string? language = "de") => LootSpotCatalog.Spots.FirstOrDefault(spot => spot.Id == id)?.DisplayName ?? AppText.Translate("Grindspot wird erkannt", language);
    internal static LootSpotPresentation? Profile(string? id) => LootSpotPresentationCatalog.Profiles.FirstOrDefault(profile => profile.SpotId == id);
    internal static string SpotBackgroundStyle(LootSpotPresentation? profile, bool shaded = false)
    {
        var backdrop = shaded ? "linear-gradient(90deg,#111820ef,#111820cc)" : "linear-gradient(135deg,#25323a,#111820)";
        return profile?.BackgroundFileName is { Length: > 0 } file
            ? "background-image:" + (shaded ? backdrop + "," : "") + "url('assets/spot-backgrounds/" + file + "')"
            : "background-image:" + backdrop;
    }
    internal static string SpotGuidanceSummary(LootSpotPresentation profile, string? language = "de") =>
        profile.RecommendedAp is { } ap && profile.RecommendedDp is { } dp
            ? $"{Number(ap, language)} AP · {Number(dp, language)} DP"
            : profile.MaxApLimit is { } limit ? AppText.Format("AP-Limit {0}", language, Number(limit, language)) : profile.RegionName;
    internal static string? ItemIcon(string name) => ItemIcons.TryGetValue(name, out var file) ? "assets/icons/" + file : null;
    internal static string? ClassIcon(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        var known = CompanionCharacterClassCatalog.FindById(idOrName);
        var name = known?.Name ?? idOrName.Replace("Â·", "·", StringComparison.Ordinal).Split('·')[0].Trim();
        var file = name.ToLowerInvariant().Replace(' ', '-').Replace("-succession", "", StringComparison.Ordinal)
            .Replace("-awakening", "", StringComparison.Ordinal);
        return "assets/class-icons/" + file + ".png";
    }
    internal static long Trash(IReadOnlyDictionary<string, long> totals, string? spotId) => Profile(spotId) is { } profile
        ? totals.GetValueOrDefault(profile.TrashItemName) : 0;
    internal static string Kind(string name, string? language = "de")
    {
        var definition = LootPriceCatalog.Definitions.FirstOrDefault(item => item.ItemName == name);
        return AppText.Translate(definition?.Kind switch { LootPriceKind.Fixed => "NPC / Festwert", LootPriceKind.AncientSpiritDust => "Verarbeitung", LootPriceKind.Market => "Zentralmarkt", _ => "Ohne Marktwert" }, language);
    }
    internal static string Trait(string trait, string? language = "de") => AppText.Translate(trait switch
    {
        "#CombatEXP" => "Kampf-EP", "#MarnisRealmPrivate" => "Marnis Reich", "#Knockdown/Bound" => "Niederschlag / Umwerfen",
        "#Knockback/Floating" => "Rückstoß / Hochschleudern", "#Stun/Stiffness/Freezing" => "Betäuben / Erstarren / Einfrieren",
        "#AllanSerbinsLandscape" => "Allan Serbins Landschaft", "#HighestTier" => "Höchste Stufe", "#DivineAuthority" => "Göttliche Autorität",
        "#PartyOf3" => "Gruppe · 3 Spieler", "#FeverPowerfulMobs" => "Verstärkte Monster",
        "#Dehkia" => "Dehkias Laterne", "#DehkiaII" => "Dehkias Laterne · Stufe II", "#Elvia" => "Elvia",
        _ => trait.TrimStart('#')
    }, language);
    private static IReadOnlyDictionary<string, string> ReadItemIcons()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "icons", "catalog.json")));
            return document.RootElement.GetProperty("items").EnumerateArray()
                .Where(item => item.TryGetProperty("name", out _) && item.TryGetProperty("file", out _))
                .GroupBy(item => item.GetProperty("name").GetString()!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => Path.GetFileName(group.First().GetProperty("file").GetString()!), StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return new Dictionary<string, string>(); }
    }
}
