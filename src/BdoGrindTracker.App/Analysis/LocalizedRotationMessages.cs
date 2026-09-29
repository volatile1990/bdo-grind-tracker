using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BdoGrindTracker.App.Analysis;

/// <summary>Translations of the registered rotation events.</summary>
internal static class LocalizedRotationMessages
{
    internal sealed record Pattern(string Spot, string Kind, string Language, string Text, string Phrase, string Mode);
    internal static IReadOnlyList<Pattern> Patterns { get; } = Load();

    private static Pattern[] Load()
    {
        using var stream = typeof(LocalizedRotationMessages).Assembly.GetManifestResourceStream(
            "BdoGrindTracker.App.RotationMessages.json")
            ?? throw new InvalidOperationException("Rotation message translations are missing.");
        return JsonSerializer.Deserialize<Pattern[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    // Preserve accents and expand ß consistently when comparing message text.
    // Do not strip accented letters to fragments or accept an unchecked ASCII fallback.
    internal static string Normalize(string text) => Regex.Replace(
        text.Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace("ß", "ss"),
        @"[^\p{L}\p{N}]+", " ").Trim();

    internal static IReadOnlyList<(string Kind, string Label)> Parse(string spot, string text,
        (string Kind, string Label, string Phrase)[] legacy, string? language = null)
    {
        var normalized = Normalize(text);
        var ascii = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        var lines = text.Split(['\n', '\f']).Select(Normalize).ToArray();
        var kinds = Patterns.Where(p => p.Spot == spot && p.Language != "en" &&
            (language is null || p.Language == language) && Matches(p, normalized, lines))
            .Select(p => p.Kind).ToHashSet(StringComparer.Ordinal);
        // Keep the short English anchors already tested against recordings, including
        // their deliberately partial words and observed OCR errors.
        if (language is null or "en")
            foreach (var definition in legacy)
                if (ascii.Contains(definition.Phrase, StringComparison.Ordinal)) kinds.Add(definition.Kind);
        return legacy.Where(d => kinds.Contains(d.Kind)).Select(d => (d.Kind, d.Label)).Distinct().ToArray();
    }

    private static bool Matches(Pattern pattern, string normalized, string[] lines) => pattern.Mode switch
    {
        "line" => lines.Contains(Normalize(pattern.Phrase), StringComparer.Ordinal),
        "glitched" => lines.Any(line => Regex.IsMatch(line, pattern.Phrase,
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))),
        _ => (" " + normalized + " ").Contains(" " + Normalize(pattern.Phrase) + " ", StringComparison.Ordinal)
    };

    internal static int CountLines(string spot, string text, string kind,
        (string Kind, string Label, string Phrase)[] legacy, string? language = null)
    {
        var phrases = Patterns.Where(p => p.Spot == spot && p.Kind == kind && p.Language != "en" &&
            (language is null || p.Language == language)).Select(p => Normalize(p.Phrase)).ToList();
        if (language is null or "en") phrases.AddRange(legacy.Where(d => d.Kind == kind).Select(d => d.Phrase));
        // Each OCR pass reads the same stack. Alternatives must not count the same occurrence twice.
        // New captures preserve physical lines and separate passes with form feed.
        // Old recordings separated the flattened passes with a newline.
        return text.Split(text.Contains('\f') ? '\f' : '\n').Select(line => phrases.Select(phrase =>
            Regex.Count(Normalize(line), Regex.Escape(phrase))).DefaultIfEmpty().Max()).DefaultIfEmpty().Max();
    }
}
