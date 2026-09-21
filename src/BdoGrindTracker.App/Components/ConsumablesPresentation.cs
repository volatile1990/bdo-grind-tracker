using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.App.Localization;
using BdoGrindTracker.App.Pricing;
using BdoGrindTracker.Core.Buffs;

namespace BdoGrindTracker.App.Components;

public sealed record ConsumableItem(
    string Id, string Name, string? IconPath, int Count, decimal KnownCost, int UnpricedCount, string Tooltip);

/// <summary>Projects recorded purchases for the live session and overlay without repricing them.</summary>
public sealed record ConsumablesPresentation(
    IReadOnlyList<ConsumableItem> Items, string Cost, string CostDescription, bool HasMissingPrices)
{
    private static readonly IReadOnlyDictionary<string, string> Icons = ReadIcons();

    public static ConsumablesPresentation Create(BuffLedgerSnapshot? value, string language)
    {
        language = AppText.NormalizeLanguage(language);
        if (value is null)
            return new([], "—", AppText.Translate("Noch keine Buff-Beobachtungen gespeichert.", language), false);

        var groups = BuffConsumptionPresentation.Group(value.Consumptions);
        var items = groups.Select(group =>
        {
            var name = BuffName(group.BuffId, group.Name, language);
            var lines = new List<string>
            {
                name,
                AppText.Translate("Anzahl", language) + ": " + Presentation.Number(group.Count, language),
                AppText.Translate("Kosten", language) + ": " + CostText(group.KnownCost, group.Count, group.UnpricedCount, language, exact: true),
            };
            if (group.MinimumUnitPrice is { } minimum)
            {
                var unitPrice = minimum == group.MaximumUnitPrice ? ExactMoney(minimum, language)
                    : Presentation.Number(minimum, language) + "–" + ExactMoney(group.MaximumUnitPrice!.Value, language);
                lines.Add(AppText.Translate("Preis je Buff", language) + ": " + unitPrice);
            }
            if (group.SessionStartCount > 0)
                lines.Add(AppText.Format("Bei erster Erkennung aktiv: {0}", language, Presentation.Number(group.SessionStartCount, language)));
            if (group.UnpricedCount > 0)
                lines.Add(AppText.Format("{0} ohne Preis", language, Presentation.Number(group.UnpricedCount, language)));
            if (group.HasStalePrice)
                lines.Add(AppText.Translate("Einige Kosten basieren auf gespeicherten Marktpreisen.", language));
            return new ConsumableItem(group.BuffId, name, Icons.GetValueOrDefault(group.BuffId),
                group.Count, group.KnownCost, group.UnpricedCount, string.Join(Environment.NewLine, lines));
        }).ToArray();
        var missing = items.Sum(item => item.UnpricedCount);
        var cost = items.Sum(item => item.KnownCost);
        var count = items.Sum(item => item.Count);
        var description = count == 0
            ? AppText.Translate("Noch keine verbrauchten Items", language)
            : AppText.Translate("Kosten", language) + ": " + CostText(cost, count, missing, language, exact: true);
        if (missing > 0)
            description += Environment.NewLine + AppText.Format("Teilbetrag: {0} von {1} gezählten Buffs ohne Preis.", language,
                Presentation.Number(missing, language), Presentation.Number(count, language));
        return new(Array.AsReadOnly(items), CostText(cost, count, missing, language), description, missing > 0);
    }

    private static string CostText(decimal known, int count, int missing, string language, bool exact = false) =>
        count > 0 && missing == count ? AppText.Translate("Preis fehlt", language)
            : (exact ? ExactMoney(known, language) : Money(known, language)) + (missing > 0 ? " *" : "");

    private static string ExactMoney(decimal value, string language) =>
        AppText.Format("{0} Silber", language, Presentation.Number(value, language));

    private static string Money(decimal value, string language)
    {
        var (divisor, unit) = Math.Abs(value) switch
        {
            >= 1_000_000_000m => (1_000_000_000m, "Mrd."),
            >= 1_000_000m => (1_000_000m, "Mio."),
            >= 10_000m => (1_000m, "Tsd."),
            _ => (1m, ""),
        };
        var amount = divisor == 1 ? Presentation.Number(value, language)
            : (value / divisor).ToString("0.00", AppText.Culture(language)) + " " + AppText.Translate(unit, language);
        return AppText.Format("{0} Silber", language, amount);
    }

    private static string BuffName(string id, string savedName, string language)
    {
        var definition = BuffPriceCatalog.HistoryDefinitions.FirstOrDefault(item => item.Id == id);
        if (definition is not null) return language == "de" ? definition.DisplayName : definition.Name;
        var group = AutomaticBuffCatalog.Default.HistoricalGroupDefinitions.FirstOrDefault(item => item.Id == id);
        if (group is not null)
            return language == "de" ? group.DisplayName
                : AppText.Translate(group.Name.Replace(" (variant unknown)", "", StringComparison.Ordinal), language)
                    + " (" + AppText.Translate("Variante unbekannt", language) + ")";
        if (!id.StartsWith("automatic-", StringComparison.Ordinal)) return savedName;
        var baseName = savedName.Replace(" (variant unknown)", "", StringComparison.Ordinal)
            .Replace(" (Variante unbekannt)", "", StringComparison.Ordinal);
        if (baseName.StartsWith("Immortal: ", StringComparison.Ordinal)) baseName = baseName[10..];
        if (baseName.StartsWith("Unsterblich: ", StringComparison.Ordinal)) baseName = baseName[13..];
        var known = BuffPriceCatalog.HistoryDefinitions.FirstOrDefault(item =>
            item.Name.Split(" (")[0] == baseName || item.DisplayName.Split(" (")[0] == baseName);
        var label = known is null ? AppText.Translate(baseName, language) : language == "de"
            ? known.DisplayName.Split(" (")[0] : known.Name.Split(" (")[0];
        return label + " (" + AppText.Translate("Variante unbekannt", language) + ")";
    }

    private static IReadOnlyDictionary<string, string> ReadIcons()
    {
        var catalog = AutomaticBuffCatalog.Default;
        var templates = catalog.Templates.Where(template =>
            template.IconPath == Path.GetFileName(template.IconPath) &&
            template.IconPath.StartsWith("client-", StringComparison.Ordinal) &&
            template.IconPath.EndsWith(".png", StringComparison.Ordinal)).ToArray();
        var candidates = templates.SelectMany(template => template.CandidateBuffIds.Select(id =>
            (Id: id, Path: template.IconPath))).ToList();
        // Only current, proven families may share an icon. Historical families
        // such as automatic-harmony-draught no longer identify a unique image.
        foreach (var definition in catalog.GroupDefinitions)
            candidates.AddRange(templates.Where(template => template.GroupId == definition.RecognitionGroup)
                .Select(template => (definition.Id, template.IconPath)));
        return candidates.GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(group => group.Key, group => "assets/buffs/" + group.First().Path, StringComparer.Ordinal);
    }
}
