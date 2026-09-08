using System.Text;

namespace BdoGrindTracker.Core;

/// <summary>German OCR aliases only; every result retains an existing canonical item key.</summary>
internal sealed class GermanItemMatcher(IEnumerable<string> canonicalNames)
{
    private sealed record Entry(string CanonicalName, byte[] Bytes);
    private readonly Entry[] _entries = canonicalNames.Distinct(StringComparer.Ordinal)
        .Where(ItemLocalizationCatalog.GermanNames.ContainsKey)
        .Select(name => new Entry(name, Encoding.UTF8.GetBytes(
            ItemLocalizationCatalog.NormalizeGermanName(ItemLocalizationCatalog.GermanNames[name])))).ToArray();

    public CompanionItemMatch? Match(string observedText)
    {
        if (_entries.Length == 0 || observedText.Length > 512) return null;
        var observed = Encoding.UTF8.GetBytes(ItemLocalizationCatalog.NormalizeGermanName(observedText));
        if (observed.Length == 0) return null;
        foreach (var entry in _entries)
            if (observed.AsSpan().SequenceEqual(entry.Bytes))
                return new(observedText, entry.CanonicalName, 0, IsExact: true);
        if (_entries.Count(entry => entry.Bytes.AsSpan().StartsWith(observed)) > 1) return null;
        var candidates = _entries.Select(entry =>
        {
            var edits = CompanionItemMatcher.LevenshteinDistance(observed, entry.Bytes);
            return (Entry: entry, Edits: edits, Score: edits / (double)Math.Max(observed.Length, entry.Bytes.Length));
        }).OrderBy(candidate => candidate.Score).ToArray();
        var best = candidates[0];
        // Long German names differ in just a few meaningful letters (BON/WON,
        // pigment/luster, armor slot). Never guess between neighboring variants.
        if (best.Score > 0.20 || (best.Edits != 0 && candidates.Skip(1).Any(candidate =>
                candidate.Edits <= best.Edits + 1))) return null;
        return new(observedText, best.Entry.CanonicalName, best.Score, IsExact: best.Edits == 0);
    }
}
