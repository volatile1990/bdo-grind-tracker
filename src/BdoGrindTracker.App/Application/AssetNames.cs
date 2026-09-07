using System.Globalization;
using System.Text;

namespace BdoGrindTracker.App.Services;

internal static class AssetNames
{
    public static string ItemSlug(string itemName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        var result = new StringBuilder();
        var separator = false;
        foreach (var character in itemName.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark ||
                character is '\'' or '\u2019') continue;
            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (separator && result.Length > 0) result.Append('-');
                result.Append(char.ToLowerInvariant(character));
                separator = false;
            }
            else separator = result.Length > 0;
        }
        return result.Length == 0 ? "unknown-item" : result.ToString();
    }
}
