using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace BdoGrindTracker.Core;

/// <summary>Text evidence for row association, independent of quantity parsing.</summary>
public static partial class LifetimeTextSimilarity
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKC))
            if (char.IsLetterOrDigit(character) || char.GetUnicodeCategory(character) is
                UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                result.Append(char.ToLowerInvariant(character));
        return result.ToString();
    }

    public static string NormalizeNameFragment(string text) => Normalize(MultiplierSuffix().Replace(text, ""));

    // A suffix is removed only to compare names. It is never parsed as a count.
    [GeneratedRegex(@"\s*[xX×хХмメ]\s*[0-9IlOoZzSsbB.,|]*\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex MultiplierSuffix();

    public static double JaroWinkler(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left == right) return 1;
        if (left.Length == 0 || right.Length == 0) return 0;
        var distance = Math.Max(0, Math.Max(left.Length, right.Length) / 2 - 1);
        var leftMatched = new bool[left.Length];
        var rightMatched = new bool[right.Length];
        var matches = 0;
        for (var index = 0; index < left.Length; index++)
            for (var other = Math.Max(0, index - distance); other < Math.Min(right.Length, index + distance + 1); other++)
                if (!rightMatched[other] && left[index] == right[other])
                {
                    leftMatched[index] = rightMatched[other] = true;
                    matches++;
                    break;
                }
        if (matches == 0) return 0;
        var transpositions = 0;
        var cursor = 0;
        for (var index = 0; index < left.Length; index++)
        {
            if (!leftMatched[index]) continue;
            while (!rightMatched[cursor]) cursor++;
            if (left[index] != right[cursor++]) transpositions++;
        }
        var jaro = (matches / (double)left.Length + matches / (double)right.Length +
            (matches - transpositions / 2d) / matches) / 3d;
        var prefix = 0;
        while (prefix < Math.Min(4, Math.Min(left.Length, right.Length)) && left[prefix] == right[prefix]) prefix++;
        return jaro + prefix * .1 * (1 - jaro);
    }
}
