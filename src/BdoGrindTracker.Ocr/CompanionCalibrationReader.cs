using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BdoGrindTracker.Ocr;

/// <summary>The three font profiles stored by BDO Companion 0.7.4.</summary>
public enum CompanionFontType
{
    StrongSword = 0,
    DejaVu = 1,
    CabinDroid = 2,
}

/// <summary>
/// Calibration values consumed by Companion's normal-loot capture path.
/// Coordinates use the full captured game's pixel space.
/// </summary>
public sealed record CompanionCalibration(
    string ProfileDirectoryPath,
    string GameVariablePath,
    string GameOptionPath,
    int LootAnchorX,
    int LootAnchorY,
    int ScreenWidth,
    int ScreenHeight,
    float UiScale,
    CompanionFontType FontType,
    int WindowedMode,
    bool CustomHp,
    string? ActiveCharacterGameVariablePath = null,
    bool HasRareLootAnchor = false,
    int RareLootAnchorX = 0,
    int RareLootAnchorY = 0)
{
    public RareLootAnchorResolution? RareLootResolution { get; init; }
}

/// <summary>
/// Reads Companion-compatible normal-loot calibration, selecting the most recently
/// saved profile configuration and resolving optional rare-loot presets. The caller supplies the Black Desert documents
/// directory so the reader has no dependency on a particular Windows user profile.
/// </summary>
public sealed partial class CompanionCalibrationReader
{
    private const uint LootUiDataIndex = 159;

    /// <summary>
    /// Reads <c>GameOption.txt</c> and the active numeric UserCache profile's
    /// <c>gamevariable.xml</c> below <paramref name="blackDesertDirectoryPath"/>.
    /// </summary>
    public CompanionCalibration Read(string blackDesertDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blackDesertDirectoryPath);

        var root = Path.GetFullPath(blackDesertDirectoryPath);
        var userCachePath = Path.Combine(root, "UserCache");
        var profilePath = SelectActiveProfileDirectory(userCachePath);
        // Companion joins this exact mixed-case filename. Windows resolves the
        // lower-case filename written by BDO without changing the stored path.
        var gameVariablePath = Path.Combine(profilePath, "gameVariable.xml");
        var gameOptionPath = Path.Combine(root, "GameOption.txt");

        if (!File.Exists(gameVariablePath))
        {
            throw new FileNotFoundException(
                "The selected UserCache profile has no gamevariable.xml.",
                gameVariablePath);
        }

        if (!File.Exists(gameOptionPath))
        {
            throw new FileNotFoundException("GameOption.txt was not found.", gameOptionPath);
        }

        var optionText = File.ReadAllText(gameOptionPath);
        var variableElements = ReadXmlFragment(gameVariablePath);

        var variableResolution = ReadVariableResolution(variableElements);
        var optionResolution = ReadOptionResolution(optionText);
        var screenWidth = variableResolution.Width ?? optionResolution.Width ??
            throw new InvalidDataException("No screen width was found.");
        var screenHeight = variableResolution.Height ?? optionResolution.Height ??
            throw new InvalidDataException("No screen height was found.");
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            throw new InvalidDataException("The calibrated screen resolution must be positive.");
        }

        var variableScale = ReadVariableScale(variableElements);
        var optionScale = ReadOptionScale(optionText);
        var uiScale = variableScale ?? optionScale ??
            throw new InvalidDataException("No UI scale was found.");
        if (!float.IsFinite(uiScale) || uiScale <= 0f)
        {
            throw new InvalidDataException("The calibrated UI scale must be finite and positive.");
        }

        var (relativeX, relativeY) = ReadRequiredVisibleUiPosition(
            variableElements,
            LootUiDataIndex);
        var lootAnchorX = ScaleRelativeCoordinate(relativeX, screenWidth);
        var lootAnchorY = ScaleRelativeCoordinate(relativeY, screenHeight);
        var calibration = new CompanionCalibration(
            profilePath,
            gameVariablePath,
            gameOptionPath,
            lootAnchorX,
            lootAnchorY,
            screenWidth,
            screenHeight,
            uiScale,
            ReadFontType(optionText),
            ReadWindowedMode(optionText),
            ReadCustomHp(variableElements),
            SelectActiveCharacterGameVariablePath(profilePath));
        return ResolveRareCalibration(variableElements, calibration);
    }

    internal static string SelectActiveProfileDirectory(string userCachePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userCachePath);
        if (!Directory.Exists(userCachePath))
        {
            throw new DirectoryNotFoundException(
                $"The UserCache directory was not found: {userCachePath}");
        }

        string? selected = null;
        long selectedLastWrite = long.MinValue;
        foreach (var candidate in Directory.EnumerateDirectories(userCachePath))
        {
            var name = Path.GetFileName(candidate);
            if (!TryParseNonZeroProfileId(name, out _))
            {
                continue;
            }

            var variables = Path.Combine(candidate, "gameVariable.xml");
            if (!File.Exists(variables)) continue;

            // Saving an existing XML does not update its parent directory's timestamp.
            // Rank the actual configuration, and never select a cache without one.
            var lastWrite = File.GetLastWriteTimeUtc(variables).ToFileTimeUtc();
            if (selected is null || lastWrite > selectedLastWrite ||
                lastWrite == selectedLastWrite && StringComparer.OrdinalIgnoreCase.Compare(candidate, selected) > 0)
            {
                selected = candidate;
                selectedLastWrite = lastWrite;
            }
        }

        return selected ?? throw new InvalidDataException(
            "UserCache contains no non-zero, unsigned 32-bit numeric profile directory with gamevariable.xml.");
    }

    internal static bool TryParseNonZeroProfileId(string value, out uint profileId)
    {
        var parsed = uint.TryParse(
            value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out profileId);
        return parsed && profileId != 0;
    }

    /// <summary>
    /// Companion first finds the first immediate profile child containing a
    /// <c>gameVariable.xml</c>. That file and the same-named files in its immediate
    /// child directories are compared by last-access FILETIME; ties replace the
    /// current candidate.
    /// </summary>
    internal static string? SelectActiveCharacterGameVariablePath(string profilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);

        string? presetDirectory = null;
        foreach (var directory in Directory.EnumerateDirectories(profilePath))
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

        var selectedPath = Path.Combine(presetDirectory, "gameVariable.xml");
        var selectedLastAccess = File.GetLastAccessTimeUtc(selectedPath).ToFileTimeUtc();
        foreach (var directory in Directory.EnumerateDirectories(presetDirectory))
        {
            var candidatePath = Path.Combine(directory, "gameVariable.xml");
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            var candidateLastAccess = File.GetLastAccessTimeUtc(candidatePath).ToFileTimeUtc();
            if (candidateLastAccess >= selectedLastAccess)
            {
                selectedPath = candidatePath;
                selectedLastAccess = candidateLastAccess;
            }
        }

        return selectedPath;
    }

    private static IReadOnlyList<XElement> ReadXmlFragment(string path)
    {
        var settings = new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        using var reader = XmlReader.Create(path, settings);
        var elements = new List<XElement>();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                elements.Add((XElement)XNode.ReadFrom(reader));
            }
            else
            {
                reader.Read();
            }
        }

        return elements;
    }

    private static IEnumerable<XElement> DescendantsAndSelf(
        IReadOnlyList<XElement> elements) =>
        elements.SelectMany(static element => element.DescendantsAndSelf());

    private static (int? Width, int? Height) ReadVariableResolution(
        IReadOnlyList<XElement> elements)
    {
        var element = DescendantsAndSelf(elements)
            .FirstOrDefault(static element => element.Name == "Resolution");
        return element is null
            ? (null, null)
            : (ParseOptionalPositiveInt(element.Attribute("Width")),
                ParseOptionalPositiveInt(element.Attribute("Height")));
    }

    private static (int? Width, int? Height) ReadOptionResolution(string optionText)
    {
        var match = OptionResolutionRegex().Match(optionText);
        if (!match.Success)
        {
            return (null, null);
        }

        return (
            ParsePositiveInt(match.Groups[1].Value, "GameOption width"),
            ParsePositiveInt(match.Groups[2].Value, "GameOption height"));
    }

    private static float? ReadVariableScale(IReadOnlyList<XElement> elements)
    {
        var value = DescendantsAndSelf(elements)
            .Where(static element => element.Name == "UiScale")
            .Select(element => element.Attribute("Value"))
            .FirstOrDefault(attribute => attribute is not null);
        return value is null ? null : ParseFloat(value.Value, "UiScale Value");
    }

    private static float? ReadOptionScale(string optionText)
    {
        var match = OptionScaleRegex().Match(optionText);
        return match.Success ? ParseFloat(match.Groups[1].Value, "GameOption uiScale") : null;
    }

    private static (float X, float Y) ReadRequiredVisibleUiPosition(
        IReadOnlyList<XElement> elements,
        uint index)
    {
        // A saved preset must not make a hidden/missing active main log usable.
        var activeSections = elements.Where(element => element.Name == "UIData").ToArray();
        if (activeSections.Length != 1)
            throw new InvalidDataException("The active UI configuration is missing or ambiguous.");
        var activePanels = activeSections[0].Elements("UIData")
            .Where(element => TryParseUnsigned(element.Attribute("Index")?.Value, out var id) && id == index)
            .ToArray();
        if (activePanels.Length != 1)
            throw new InvalidDataException("The active main loot panel is missing or ambiguous.");
        var position = ReadOptionalVisibleUiPosition(activePanels, index);
        if (position is null)
        {
            throw new InvalidDataException(
                $"UIData Index {index} with visible position was not found.");
        }

        if (!float.IsFinite(position.Value.X) || !float.IsFinite(position.Value.Y) ||
            position.Value.X is < 0f or > 1f || position.Value.Y is < 0f or > 1f)
            throw new InvalidDataException("The required loot-panel position is outside the screen.");

        return position.Value;
    }

    private static (float X, float Y)? ReadOptionalVisibleUiPosition(
        IReadOnlyList<XElement> elements,
        uint index)
    {
        (float X, float Y)? position = null;
        foreach (var uiData in DescendantsAndSelf(elements)
                     .Where(static element => element.Name == "UIData"))
        {
            if (!TryParseUnsigned(uiData.Attribute("Index")?.Value, out var candidateIndex) ||
                candidateIndex != index ||
                !string.Equals(
                    uiData.Attribute("IsShow")?.Value,
                    "true",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var xAttribute = uiData.Attribute("RelativePosX");
            var yAttribute = uiData.Attribute("RelativePosY");
            if (xAttribute is null || yAttribute is null)
            {
                continue;
            }

            position = (
                ParseFloat(xAttribute.Value, "RelativePosX"),
                ParseFloat(yAttribute.Value, "RelativePosY"));
        }

        return position;
    }

    private static CompanionFontType ReadFontType(string optionText)
    {
        var match = FontTypeRegex().Match(optionText);
        if (!match.Success)
        {
            return CompanionFontType.CabinDroid;
        }

        return match.Groups[1].Value[0] switch
        {
            '0' => CompanionFontType.DejaVu,
            '2' => CompanionFontType.StrongSword,
            _ => CompanionFontType.StrongSword,
        };
    }

    private static int ReadWindowedMode(string optionText)
    {
        var match = WindowedRegex().Match(optionText);
        return match.Success ? match.Groups[1].Value[0] - '0' : -1;
    }

    private static bool ReadCustomHp(IReadOnlyList<XElement> elements) =>
        DescendantsAndSelf(elements)
        .Where(static element => element.Name == "UIData")
        .Any(element =>
            TryParseUnsigned(element.Attribute("Index")?.Value, out var index) &&
            index == 2 &&
            element.Attribute("RelativePosX") is null &&
            element.Attribute("RelativePosY") is null &&
            string.Equals(
                element.Attribute("IsShow")?.Value,
                "true",
                StringComparison.Ordinal));

    private static int ScaleRelativeCoordinate(float relative, int dimension)
    {
        // Companion performs the multiplication in f32 and then truncates toward zero.
        var scaled = relative * dimension;
        return checked((int)scaled);
    }

    private static int? ParseOptionalPositiveInt(XAttribute? attribute) =>
        attribute is null ? null : ParsePositiveInt(attribute.Value, attribute.Name.LocalName);

    private static int ParsePositiveInt(string value, string field)
    {
        if (!uint.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed == 0 ||
            parsed > int.MaxValue)
        {
            throw new InvalidDataException($"{field} is not a positive 32-bit screen dimension.");
        }

        return (int)parsed;
    }

    private static bool TryParseUnsigned(string? value, out uint parsed) =>
        uint.TryParse(
            value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out parsed);

    private static float ParseFloat(string value, string field)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"{field} is not a valid single-precision number.");
        }

        return parsed;
    }

    [GeneratedRegex(@"UIFontType = (\d)", RegexOptions.CultureInvariant)]
    private static partial Regex FontTypeRegex();

    [GeneratedRegex(@"uiScale =  (\d\.\d\d)", RegexOptions.CultureInvariant)]
    private static partial Regex OptionScaleRegex();

    [GeneratedRegex(
        @"width = (\d+).*height = (\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex OptionResolutionRegex();

    [GeneratedRegex(@"windowed = (\d)", RegexOptions.CultureInvariant)]
    private static partial Regex WindowedRegex();
}
