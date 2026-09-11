using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BdoGrindTracker.Core;

namespace BdoGrindTracker.App.Analysis;

/// <summary>
/// Reinterprets saved text against the current catalog, including localized
/// aliases. Partial text may support tracking; only an actual numeric token can
/// supply a new quantity. Native/template/review readings remain separate input.
/// </summary>
internal sealed partial class LifetimeLootTextParser
{
    private sealed record Alias(string Name, string Text);
    private Alias[] _aliases = [];
    private HashSet<string> _names = new(StringComparer.Ordinal);
    private Dictionary<string, LifetimeParsingCatalogEntry> _catalog = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LifetimeParsedReading?> _cache = new(StringComparer.Ordinal);
    public LifetimeParsingContext Context { get; private set; }

    public LifetimeLootTextParser(LifetimeParsingContext context)
    {
        Context = context;
        UpdateContext(context);
    }

    public void UpdateContext(LifetimeParsingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _aliases = context.Catalog.SelectMany(item => item.Aliases.Prepend(item.Name)
            .Select(text => new Alias(item.Name, NormalizeName(text))))
            .Distinct().Where(alias => alias.Text.Length > 0).ToArray();
        _names = context.Catalog.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        _catalog = context.Catalog.ToDictionary(item => item.Name, StringComparer.Ordinal);
        Context = context;
        _cache.Clear();
    }

    public IReadOnlyList<string> GetAliases(string canonicalName) =>
        _catalog.TryGetValue(canonicalName, out var item) ? item.Aliases : [];

    public LifetimeParsedReading? Parse(LootObservation row)
    {
        if (row.Source != LootSource.Normal || row.IsAlignmentAnchor ||
            row.RejectionReason == AutomaticLootSpotLock.OutsideSpotPoolReason ||
            row.ItemName is { } originalName && !_names.Contains(originalName))
            return LifetimeParsedReading.Excluded;
        if (string.IsNullOrWhiteSpace(row.RawText) || row.RawText.Length > 4096) return null;
        if (_cache.TryGetValue(row.RawText, out var cached)) return cached;
        var parsed = ParseText(row.RawText);
        // Bound text-only memoization to the current context; frames and images
        // are never retained here. Replacing the catalog invalidates every entry.
        if (_cache.Count >= 2048) _cache.Clear();
        _cache[row.RawText] = parsed;
        return parsed;
    }

    private LifetimeParsedReading? ParseText(string rawText)
    {
        var text = rawText.Normalize(NormalizationForm.FormKC).Trim();
        if (text.Contains(':') || text.Contains('\n') || text.Contains('\r')) return null;
        var quantityMatch = Multiplier().Match(text);
        if (!quantityMatch.Success) quantityMatch = LeadingQuantity().Match(text);
        if (!quantityMatch.Success) quantityMatch = TrailingQuantity().Match(text);
        int? quantity = null;
        var name = text;
        if (quantityMatch.Success)
        {
            name = quantityMatch.Groups["name"].Value.Trim();
            var token = quantityMatch.Groups["amount"].Value;
            if (!QuantityToken().IsMatch(token) || !int.TryParse(token.Replace(",", "").Replace(".", ""),
                    NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount is <= 0 or >= 100_000_000)
                return null;
            quantity = amount;
            // Extra numeric fields belong to mixed UI text, not another fallback
            // amount (e.g. a popup's date/time after an actual x4).
            if (name.Any(char.IsDigit)) return null;
        }
        else name = IncompleteMultiplier().Replace(name, "").Trim();

        var normalized = NormalizeName(name);
        if (normalized.Length == 0) return null;
        var candidates = _aliases.Select(alias => (alias.Name,
                Score: LifetimeTextSimilarity.JaroWinkler(normalized, alias.Text), Exact: normalized == alias.Text))
            .GroupBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score).ToArray();
        if (candidates.Length == 0) return null;
        var best = candidates[0];
        if (normalized.Length <= 4 && !best.Exact || best.Score < .86 ||
            candidates.Length > 1 && best.Score - candidates[1].Score < .025) return null;
        return new(best.Name, quantity is not null && _catalog[best.Name].IsFixedUnit ? 1 : quantity, best.Score);
    }

    internal static string NormalizeName(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var rune in text.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (rune.Value is 0x00DF or 0x1E9E) result.Append("ss");
            else if (Rune.IsLetterOrDigit(rune)) result.Append(Rune.ToLowerInvariant(rune));
        }
        return result.ToString();
    }

    [GeneratedRegex(@"^(?<name>.+?)[xX×хХ]\s*(?<amount>[0-9][0-9.,]*)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Multiplier();
    [GeneratedRegex(@"^(?<amount>[0-9][0-9.,]*)\s+(?<name>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingQuantity();
    [GeneratedRegex(@"^(?<name>.+?)\s+(?<amount>[0-9][0-9.,]*)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingQuantity();
    [GeneratedRegex(@"^(?:[0-9]+|[0-9]{1,3}(?:[.,][0-9]{3})+)$", RegexOptions.CultureInvariant)]
    private static partial Regex QuantityToken();
    [GeneratedRegex(@"\s+[xX×хХ]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex IncompleteMultiplier();
}
