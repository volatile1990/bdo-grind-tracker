using System.Globalization;
using System.Xml;

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
/// This identifies the most recently accessed saved character configuration;
/// unsaved character or specialization changes cannot be detected from it.
/// </summary>
internal sealed class CompanionCharacterClassDetector
{
    private const long MaxXmlCharacters = 4 * 1024 * 1024;

    public CharacterClassDetection DetectDefault() => Detect(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Black Desert"));

    public CharacterClassDetection Detect(string blackDesertDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blackDesertDirectoryPath);
        try
        {
            var selectedPath = SelectCharacterConfiguration(blackDesertDirectoryPath);
            if (selectedPath is null)
            {
                return CharacterClassDetection.Unavailable;
            }

            using var stream = new FileStream(selectedPath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxXmlCharacters * 2)
            {
                return CharacterClassDetection.Unavailable;
            }

            using var reader = XmlReader.Create(stream, CreateReaderSettings());
            return ReadSkills(reader);
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

    private static string? SelectCharacterConfiguration(string blackDesertDirectoryPath)
    {
        // Same selection rules as CompanionCalibrationReader, but independent
        // of screen resolution/loot-panel visibility needed only for OCR.
        var userCachePath = Path.Combine(Path.GetFullPath(blackDesertDirectoryPath), "UserCache");
        if (!Directory.Exists(userCachePath))
        {
            return null;
        }

        string? selectedProfile = null;
        var latestWrite = DateTime.MinValue;
        foreach (var directory in Directory.EnumerateDirectories(userCachePath))
        {
            if (!uint.TryParse(Path.GetFileName(directory),
                    NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                    out var profileId) || profileId == 0)
            {
                continue;
            }

            var lastWrite = Directory.GetLastWriteTimeUtc(directory);
            if (selectedProfile is null || lastWrite >= latestWrite)
            {
                selectedProfile = directory;
                latestWrite = lastWrite;
            }
        }

        if (selectedProfile is null)
        {
            return null;
        }

        string? presetDirectory = null;
        foreach (var directory in Directory.EnumerateDirectories(selectedProfile))
        {
            if (File.Exists(Path.Combine(directory, "gameVariable.xml")))
            {
                presetDirectory = directory;
                break;
            }
        }

        if (presetDirectory is null)
        {
            return null;
        }

        var selectedFile = Path.Combine(presetDirectory, "gameVariable.xml");
        var latestAccess = File.GetLastAccessTimeUtc(selectedFile);
        foreach (var directory in Directory.EnumerateDirectories(presetDirectory))
        {
            var candidate = Path.Combine(directory, "gameVariable.xml");
            if (!File.Exists(candidate))
            {
                continue;
            }

            var lastAccess = File.GetLastAccessTimeUtc(candidate);
            if (lastAccess >= latestAccess)
            {
                selectedFile = candidate;
                latestAccess = lastAccess;
            }
        }

        return selectedFile;
    }
}
