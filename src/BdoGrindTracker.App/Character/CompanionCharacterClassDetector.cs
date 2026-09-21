using System.Globalization;
using System.Xml;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Character;

internal enum CharacterClassDetectionStatus
{
    Detected,
    Unknown,
    Ambiguous,
    Unavailable,
}

internal sealed record CharacterClassDetection(
    CharacterClass? Class,
    CharacterClassDetectionStatus Status,
    int MatchedSkillCount = 0)
{
    public static CharacterClassDetection Unknown { get; } =
        new(null, CharacterClassDetectionStatus.Unknown);

    public static CharacterClassDetection Unavailable { get; } =
        new(null, CharacterClassDetectionStatus.Unavailable);
}

/// <summary>
/// Passive class/spec inference from the locally saved skill slots, following
/// Companion 0.7.4 calibration. It never accesses a game process or screenshot,
/// never changes a game file, and does not expose account/character paths.
/// This identifies the most recently saved character configuration;
/// unsaved character or specialization changes cannot be detected from it.
/// </summary>
internal sealed class CompanionCharacterClassDetector
{
    private const long MaxXmlCharacters = 4 * 1024 * 1024;
    private const int MaxTiedConfigurations = 16;

    public CharacterClassDetection DetectDefault() => Detect(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Black Desert"));

    public CharacterClassDetection Detect(string blackDesertDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blackDesertDirectoryPath);
        try
        {
            var selectedPaths = SelectCharacterConfigurations(blackDesertDirectoryPath);
            if (selectedPaths.Count == 0)
            {
                return CharacterClassDetection.Unavailable;
            }
            CharacterClassDetection? accepted = null;
            foreach (var selectedPath in selectedPaths)
            {
                using var reader = GameVariableXmlReader.Open(selectedPath,
                    CreateReaderSettings(), maxBytes: MaxXmlCharacters * 2);
                var detected = ReadSkills(reader);
                // A tied save time does not prove which character is active.
                // Only identical class/spec evidence may resolve such a tie.
                if (detected.Status != CharacterClassDetectionStatus.Detected) return detected;
                if (accepted is not null && accepted.Class != detected.Class)
                    return new(null, CharacterClassDetectionStatus.Ambiguous);
                accepted = detected;
            }
            return accepted ?? CharacterClassDetection.Unknown;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or XmlException or ArgumentException or
            System.Security.SecurityException)
        {
            // File paths may contain account/character identifiers: do not
            // forward exception messages to the dashboard or an upload.
            return CharacterClassDetection.Unavailable;
        }
    }

    internal static CharacterClassDetection DetectFromGameVariableText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            using var textReader = new StringReader(text);
            using var reader = XmlReader.Create(textReader, CreateReaderSettings());
            return ReadSkills(reader);
        }
        catch (XmlException)
        {
            return CharacterClassDetection.Unavailable;
        }
    }

    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        ConformanceLevel = ConformanceLevel.Fragment,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        MaxCharactersInDocument = MaxXmlCharacters,
    };

    private static CharacterClassDetection ReadSkills(XmlReader reader)
    {
        // Native 0x1406EDD43..0x1406EE13F: exact element names, SkillNo
        // attribute, non-zero uint32, then HashSet insertion. Duplicate
        // quickslots/cooldown slots must not produce extra votes.
        var skillIds = new HashSet<uint>();
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                reader.Name is not ("QuickSlotSkillData" or "SkillCoolTimeSlot"))
            {
                continue;
            }

            var value = reader.GetAttribute("SkillNo");
            if (value is not null && uint.TryParse(value,
                    NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out var skillId) && skillId != 0)
            {
                skillIds.Add(skillId);
            }
        }

        // Native 0x1406EE5DD..0x1406EE683 compares each distinct skill with
        // all 56 class/spec lists. 0x1406EE5BE increments frequency, and
        // 0x1406EEB19..0x1406EEC44 selects its maximum. There is no minimum
        // confidence/extra confirmation gate in the original algorithm.
        var bestScore = 0;
        CharacterClass? bestClass = null;
        var tied = false;
        foreach (var profile in CompanionCharacterClassCatalog.Profiles)
        {
            var score = skillIds.Count(profile.SkillIds.Contains);
            if (score > bestScore)
            {
                bestScore = score;
                bestClass = profile.Class;
                tied = false;
            }
            else if (score == bestScore && score > 0)
            {
                tied = true;
            }
        }

        if (bestClass is null)
        {
            return CharacterClassDetection.Unknown;
        }

        // Companion resolves ties using randomized HashMap iteration. Preserve
        // the evidence instead of pretending that nondeterministic choice is a
        // reliable class identification; the UI offers manual correction.
        return tied
            ? new(null, CharacterClassDetectionStatus.Ambiguous, bestScore)
            : new(bestClass, CharacterClassDetectionStatus.Detected, bestScore);
    }

    private static IReadOnlyList<string> SelectCharacterConfigurations(string blackDesertDirectoryPath)
    {
        // Profile directories are not touched when BDO saves existing files.
        // Use the profile XML's save time, as the capture calibration does.
        var userCachePath = Path.Combine(Path.GetFullPath(blackDesertDirectoryPath), "UserCache");
        if (!Directory.Exists(userCachePath))
        {
            return [];
        }

        var savedProfiles = new List<(string Path, DateTime Saved)>();
        var legacyProfiles = new List<(string Path, DateTime Saved)>();
        foreach (var directory in Directory.EnumerateDirectories(userCachePath))
        {
            if (!uint.TryParse(Path.GetFileName(directory),
                    NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out var profileId) || profileId == 0)
            {
                continue;
            }

            var variables = Path.Combine(directory, "gameVariable.xml");
            if (File.Exists(variables))
            {
                savedProfiles.Add((directory, File.GetLastWriteTimeUtc(variables)));
            }
            else
            {
                legacyProfiles.Add((directory, Directory.GetLastWriteTimeUtc(directory)));
            }
        }

        // Retain layouts without a profile-wide XML only when no saved profile
        // exists. Empty/stale cache directories must not hide a saved profile.
        var profiles = SelectLatest(savedProfiles.Count > 0 ? savedProfiles : legacyProfiles);
        var selectedPaths = new List<string>();
        foreach (var profile in profiles)
        {
            var paths = SelectCharacterConfigurationsInProfile(profile);
            // Equally recent accounts all need evidence; never borrow another
            // account's class when one of the tied profiles has no character.
            if (paths.Count == 0) return [];
            selectedPaths.AddRange(paths);
            if (selectedPaths.Count > MaxTiedConfigurations) return [];
        }

        return selectedPaths;
    }

    private static IReadOnlyList<string> SelectCharacterConfigurationsInProfile(string profile)
    {
        var presets = new List<(string Path, DateTime Saved)>();
        var characters = new List<(string Path, DateTime Saved)>();
        foreach (var presetDirectory in Directory.EnumerateDirectories(profile))
        {
            var preset = Path.Combine(presetDirectory, "gameVariable.xml");
            if (!File.Exists(preset)) continue;
            presets.Add((preset, File.GetLastWriteTimeUtc(preset)));

            foreach (var directory in Directory.EnumerateDirectories(presetDirectory))
            {
                var candidate = Path.Combine(directory, "gameVariable.xml");
                if (File.Exists(candidate))
                    characters.Add((candidate, File.GetLastWriteTimeUtc(candidate)));
            }
        }

        // Consider every preset, but shared/default files cannot hide genuine
        // characters in another preset. Access times never influence selection.
        return SelectLatest(characters.Count > 0 ? characters : presets);
    }

    private static IReadOnlyList<string> SelectLatest(List<(string Path, DateTime Saved)> candidates)
    {
        if (candidates.Count == 0) return [];
        var latestSave = candidates.Max(candidate => candidate.Saved);
        var latest = candidates.Where(candidate => candidate.Saved == latestSave)
            .Select(candidate => candidate.Path).Take(MaxTiedConfigurations + 1).ToArray();
        return latest.Length > MaxTiedConfigurations ? [] : latest;
    }
}
