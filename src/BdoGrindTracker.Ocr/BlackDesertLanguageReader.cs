using System.Text.RegularExpressions;

namespace BdoGrindTracker.Ocr;

public sealed record GameLanguageDetection(string? Language, string Message);

/// <summary>Reads text-resource configuration. Launcher, voice and chat-channel languages are unrelated.</summary>
public static partial class BlackDesertLanguageReader
{
    public static GameLanguageDetection Read(string gameOptionPath, IEnumerable<string> installationDirectories)
    {
        // Some clients persist Language in GameOption.txt; the current NAEU client
        // uses [SERVICE] RES in Resource.ini. Never infer text language from Lan.txt,
        // service.ini (distribution region), AudioResourceType or a chat's LangType.
        var option = ReadSetting(gameOptionPath, "Language", null);
        if (option is not null) return Describe([option]);
        var values = installationDirectories.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => ReadSetting(Path.Combine(path, "Resource.ini"), "RES", "SERVICE"))
            .Where(value => value is not null).Select(value => value!).ToArray();
        return Describe(values);
    }

    private static GameLanguageDetection Describe(string[] values)
    {
        var languages = values.Select(value => value.Trim().Trim('"', '\'').Trim('_')
            .Replace('_', '-').ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        if (languages.Length != 1)
            return new(null, "BDO-Textsprache nicht eindeutig erkannt. Wähle Deutsch oder Englisch manuell.");
        var language = languages[0] switch
        {
            "de" or "de-de" => "de",
            "en" or "en-us" or "en-gb" => "en",
            _ => null,
        };
        return language is null
            ? new(null, "Die gespeicherte BDO-Textsprache wird nicht unterstützt. Unterstützt werden Deutsch und Englisch.")
            : new(language, $"Erkannt: {(language == "de" ? "Deutsch" : "Englisch")}");
    }

    private static string? ReadSetting(string path, string key, string? requiredSection)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 64 * 1024) return null;
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            string? section = null;
            var values = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                var text = line.Trim();
                if (text.StartsWith(';') || text.StartsWith('#')) continue;
                if (text.StartsWith('[') && text.EndsWith(']')) { section = text[1..^1].Trim(); continue; }
                if (requiredSection is not null && !string.Equals(section, requiredSection, StringComparison.OrdinalIgnoreCase)) continue;
                var match = SettingPattern().Match(text);
                if (match.Success && string.Equals(match.Groups[1].Value, key, StringComparison.OrdinalIgnoreCase))
                    values.Add(match.Groups[2].Value.Trim());
            }
            // A duplicate setting is ambiguous, even if its last value looks valid.
            return values.Count switch { 0 => null, 1 => values[0], _ => "ambiguous" };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9_]*)\s*=\s*([^;#]*)", RegexOptions.CultureInvariant)]
    private static partial Regex SettingPattern();
}
