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
    private readonly LootSource _source;
    public LifetimeParsingContext Context { get; private set; }

    public LifetimeLootTextParser(LifetimeParsingContext context, LootSource source = LootSource.Normal)
    {
        if (source is not (LootSource.Normal or LootSource.Rare)) throw new ArgumentOutOfRangeException(nameof(source));
        _source = source;
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
        if (row.Source != _source || row.IsAlignmentAnchor ||
            _source == LootSource.Rare && row.RejectionReason == "ocr-geometry" ||
            row.RejectionReason == AutomaticLootSpotLock.OutsideSpotPoolReason ||
            row.RejectionReason == LootSourceCatalog.WrongSourceReason ||
            row.ItemName is { } originalName && (!_names.Contains(originalName) || !AllowsSource(originalName)))
            return LifetimeParsedReading.Excluded;
        if (string.IsNullOrWhiteSpace(row.RawText) || row.RawText.Length > 4096) return null;
        if (_source == LootSource.Rare && row.ItemName is { } acceptedName &&
            HasEnhancementPrefix(NormalizeName(row.RawText), acceptedName))
            return LifetimeParsedReading.Excluded;
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
            if (name.Any(char.IsDigit) && (_source != LootSource.Rare ||
                !TryCorrectAccessoryGlyphs(NormalizeName(name), out _))) return null;
        }
        else name = IncompleteMultiplier().Replace(name, "").Trim();

        var normalized = NormalizeName(name);
        if (normalized.Length == 0) return null;
        if (_source == LootSource.Rare && TryCorrectAccessoryGlyphs(normalized, out var corrected))
            normalized = corrected;
        var candidates = _aliases.Select(alias => (alias.Name,
                Score: LifetimeTextSimilarity.JaroWinkler(normalized, alias.Text), Exact: normalized == alias.Text))
            .GroupBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score).ToArray();
        if (candidates.Length == 0) return null;
        var best = candidates[0];
        if (_source == LootSource.Rare && !best.Exact && HasEnhancementPrefix(normalized, best.Name)) return null;
        if (normalized.Length <= 4 && !best.Exact || best.Score < .86 ||
            candidates.Length > 1 && best.Score - candidates[1].Score < .025) return null;
        // Rank the complete catalog first: an item from the other UI channel must
        // be excluded, never replaced with a weaker same-channel candidate.
        if (!AllowsSource(best.Name)) return LifetimeParsedReading.Excluded;
        return new(best.Name, quantity is not null && _catalog[best.Name].IsFixedUnit ? 1 : quantity, best.Score);
    }

    private bool AllowsSource(string itemName) =>
        _catalog[itemName].AllowedSource is not { } allowedSource || allowedSource == _source;

    private bool HasEnhancementPrefix(string observed, string candidate)
    {
        string[] tiers = ["pri", "duo", "tri", "tet", "pen", "hex", "sep", "oct", "nov", "dec",
            "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "x"];
        return _aliases.Where(alias => alias.Name == candidate).Any(alias => tiers.Any(tier =>
            observed.StartsWith(tier, StringComparison.Ordinal) && !alias.Text.StartsWith(tier, StringComparison.Ordinal) &&
            LifetimeTextSimilarity.JaroWinkler(observed[tier.Length..], alias.Text) >= .86));
    }

    private bool TryCorrectAccessoryGlyphs(string observed, out string corrected)
    {
        corrected = observed;
        if (!observed.EndsWith("rlng", StringComparison.Ordinal) &&
            !observed.EndsWith("r1ng", StringComparison.Ordinal)) return false;
        var repaired = observed[..^4] + "ring";
        // The entire remaining name must match one catalog identity. This fixes
        // the common i/l/1 glyph confusion without weakening the fuzzy margin
        // between Ring and Earring or accepting an incomplete accessory family.
        var matches = _aliases.Where(alias => alias.Text == repaired &&
                (alias.Name.EndsWith(" Ring", StringComparison.Ordinal) ||
                 alias.Name.EndsWith(" Earring", StringComparison.Ordinal)))
            .Select(alias => alias.Name).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        if (matches.Length != 1) return false;
        corrected = repaired;
        return true;
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
