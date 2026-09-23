using System.Text.Json;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Versioned, bundled HUD evidence, independent of the user's resolution and active buffs.</summary>
internal sealed class AutomaticBuffCatalog
{
    internal sealed record Template(string Id, string GroupId, string[] CandidateBuffIds,
        string IconPath, Rectangle TimerRegion, double MinimumSimilarity = .9);

    private sealed record Manifest(int SchemaVersion, Template[] Templates);
    private const string ResourcePrefix = "BdoGrindTracker.App.Buffs.";
    internal static AutomaticBuffCatalog Default { get; } = Load();
    internal IReadOnlyList<Template> Templates { get; }
    internal IReadOnlyList<BuffDefinition> GroupDefinitions { get; }
    /// <summary>Retains persisted family identities after newer templates distinguish their variants.</summary>
    internal IReadOnlyList<BuffDefinition> HistoricalGroupDefinitions { get; }
    private readonly Func<string, Stream?> _openIcon;

    internal AutomaticBuffCatalog(IEnumerable<Template> templates, Func<string, Stream?> openIcon)
    {
        Templates = templates.ToArray();
        _openIcon = openIcon;
        foreach (var template in Templates)
            if (template.CandidateBuffIds.Length == 0 || template.CandidateBuffIds.Any(id => BuffPriceCatalog.ResolveRecognitionDefinition(id) is null)
                || template.TimerRegion.Width <= 0 || template.TimerRegion.Height <= 0 || template.MinimumSimilarity is < .7 or > 1)
                throw new InvalidDataException($"Ungültiger eingebauter Buff-Eintrag: {template.Id}");
        GroupDefinitions = Templates.GroupBy(template => template.GroupId).Where(group =>
                group.SelectMany(template => template.CandidateBuffIds).Distinct().Count() > 1)
            .Select(group => CreateGroup(group.Key, group.SelectMany(template => template.CandidateBuffIds).Distinct())).ToArray();
        HistoricalGroupDefinitions = GroupDefinitions.Concat([
            new BuffDefinition("automatic-harmony-draught", "Harmony Draught (variant unknown)", null, TimeSpan.FromMinutes(20))
            {
                LocalizedName = "Arznei der Harmonie (Variante unbekannt)", Category = "Harmony Draughts",
                RecognitionGroup = "harmony-draught", Variant = "Unbekannt",
            },
            new BuffDefinition("automatic-cron-meal", "Cron-Mahlzeiten (variant unknown)", null, TimeSpan.FromMinutes(120))
            {
                LocalizedName = "Cron-Mahlzeiten (Variante unbekannt)", Category = "Cron-Mahlzeiten",
                RecognitionGroup = "cron-meal", Variant = "Unbekannt",
            },
        ]).DistinctBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
    }

    internal Stream? OpenIcon(Template template) => _openIcon(template.IconPath);

    internal BuffDefinition DefinitionFor(Template template)
    {
        var ids = template.CandidateBuffIds.Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length == 1 ? BuffPriceCatalog.ResolveRecognitionDefinition(ids[0])!
            : GroupDefinitions.Single(definition => definition.Id == GroupId(template.GroupId));
    }

    private static string GroupId(string group) => "automatic-" + group;

    private static BuffDefinition CreateGroup(string group, IEnumerable<string> ids)
    {
        var definitions = ids.Select(id => BuffPriceCatalog.ResolveRecognitionDefinition(id)!).ToArray();
        var first = definitions.FirstOrDefault(definition => !definition.Id.StartsWith("immortal-", StringComparison.Ordinal)) ?? definitions[0];
        var name = first.Name.Split(" (")[0];
        var german = first.DisplayName.Split(" (")[0];
        if (group == "tent-adventurers-luck")
        {
            name = "Adventurer's Luck";
            german = name;
        }
        else if (definitions.Select(item => item.RecognitionGroup).Distinct().Count() > 1)
        {
            name = first.Category == "Harmony Draughts" ? "Harmony Draught" : first.Category;
            german = first.Category == "Harmony Draughts" ? "Arznei der Harmonie" : first.Category;
        }
        return new(GroupId(group), name + " (variant unknown)", null, definitions.Max(item => item.Duration))
        {
            LocalizedName = german + " (Variante unbekannt)", Category = first.Category,
            RecognitionGroup = group, Variant = "Unbekannt",
            PreferMaximumDurationVariant = group is "tent-adventures-boon" or "tent-body-enhancement",
            // Only duration distinguishes these purchases. Equal-duration price
            // tiers or ordinary/Immortal variants cannot use a countdown estimate.
            DurationVariantIds = definitions.Select(item => item.RecognitionGroup).Distinct().Count() == 1 &&
                definitions.Select(item => item.Duration).Distinct().Count() == definitions.Length
                    ? definitions.OrderBy(item => item.Duration).Select(item => item.Id).ToArray()
                    : [],
        };
    }

    private static AutomaticBuffCatalog Load()
    {
        var assembly = typeof(AutomaticBuffCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + "catalog.json");
        if (stream is null) return new([], _ => null);
        var manifest = JsonSerializer.Deserialize<Manifest>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Buff-Symbolkatalog ist leer.");
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unbekannte Buff-Symbolkatalogversion.");
        return new(manifest.Templates, file => assembly.GetManifestResourceStream(ResourcePrefix + file));
    }
}
