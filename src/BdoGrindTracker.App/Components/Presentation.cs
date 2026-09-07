using System.Globalization;
using System.Text.Json;
using BdoGrindTracker.App.Character;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.App.UI;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Components;

internal static class Presentation
{
    internal static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly IReadOnlyDictionary<string, string> ItemIcons = ReadItemIcons();

    internal static string Number(decimal value) => value.ToString("N0", German);
    internal static string Silver(decimal value) => Math.Abs(value) switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000).ToString("0.00", German) + " Mrd.",
        >= 1_000_000 => (value / 1_000_000).ToString("0.0", German) + " Mio.",
        >= 10_000 => (value / 1_000).ToString("0.0", German) + " Tsd.",
        _ => Number(value)
    };
    internal static string Duration(TimeSpan duration) => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    internal static string ShortDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours} Std. {duration.Minutes:00} Min."
        : $"{Math.Max(0, (int)duration.TotalMinutes)} Min.";
    internal static decimal Hours(TimeSpan duration) => (decimal)duration.Ticks / TimeSpan.TicksPerHour;
    internal static decimal Hourly(decimal amount, TimeSpan duration) => duration > TimeSpan.Zero ? amount / Hours(duration) : 0;
    internal static string SpotName(string? id) => LootSpotCatalog.Spots.FirstOrDefault(spot => spot.Id == id)?.DisplayName ?? "Grindspot wird erkannt";
    internal static LootSpotPresentation? Profile(string? id) => LootSpotPresentationCatalog.Profiles.FirstOrDefault(profile => profile.SpotId == id);
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
    internal static string Kind(string name)
    {
        var definition = LootPriceCatalog.Definitions.FirstOrDefault(item => item.ItemName == name);
        return definition?.Kind switch { LootPriceKind.Fixed => "NPC / Festwert", LootPriceKind.AncientSpiritDust => "Verarbeitung", LootPriceKind.Market => "Zentralmarkt", _ => "Ohne Marktwert" };
    }
    internal static string Trait(string trait) => trait switch
    {
        "#CombatEXP" => "Kampf-EP", "#MarnisRealmPrivate" => "Marnis Reich", "#Knockdown/Bound" => "Niederschlag / Umwerfen",
        "#Knockback/Floating" => "Rückstoß / Hochschleudern", "#Stun/Stiffness/Freezing" => "Betäuben / Erstarren / Einfrieren",
        "#AllanSerbinsLandscape" => "Allan Serbins Landschaft", "#HighestTier" => "Höchste Stufe", "#DivineAuthority" => "Göttliche Autorität",
        "#PartyOf3" => "Gruppe · 3 Spieler", "#FeverPowerfulMobs" => "Verstärkte Monster", _ => trait.TrimStart('#')
    };
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
