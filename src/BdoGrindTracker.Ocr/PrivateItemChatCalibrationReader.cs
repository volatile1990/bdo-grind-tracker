using System.Drawing;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace BdoGrindTracker.Ocr;

/// <summary>A visible detached chat panel, in the calibrated game's pixel space.</summary>
public sealed record PrivateItemChatCalibration(
    int WindowIndex,
    Rectangle Bounds,
    bool HasOtherChatTypes);

/// <summary>
/// Reads the active profile's saved private-item chat panel without inspecting presets
/// or changing the game's settings. The caller must still validate the captured messages.
/// </summary>
public sealed class PrivateItemChatCalibrationReader
{
    private const string PrivateItemFilter = "ChatSystemType_PrivateItem";

    private static readonly string[] RequiredSystemFilters =
    [
        "ChatSystemType_Undefined",
        PrivateItemFilter,
        "ChatSystemType_PartyItem",
        "ChatSystemType_Market",
        "ChatSystemType_Worker",
        "ChatSystemType_Harvest",
        "ChatSystemType_Enchant",
        "ChatSystemType_Siege",
        "ChatSystemType_FamilyWorkItem",
    ];

    // These flags remain enabled in BDO's saved private-item-only panel. They do
    // not prove the absence of other messages, so the result explicitly marks them.
    private static readonly HashSet<string> ToleratedOtherChatTypes = new(StringComparer.Ordinal)
    {
        "Friend", "LocalWar", "NoticeOnlyTop", "AppGuild", "DeadMessage", "GM",
        "Messenger", "Sign", "SolareCustom",
    };

    public PrivateItemChatCalibration? TryRead(CompanionCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        if (string.IsNullOrWhiteSpace(calibration.GameVariablePath) ||
            calibration.ScreenWidth <= 0 || calibration.ScreenHeight <= 0 ||
            !float.IsFinite(calibration.UiScale) || calibration.UiScale <= 0)
        {
            return null;
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                MaxCharactersInDocument = 16 * 1024 * 1024,
            };
            using var reader = XmlReader.Create(calibration.GameVariablePath, settings);
            XElement? activeUi = null;
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                if (reader.Depth == 0 && reader.LocalName == "UIData" &&
                    reader.NamespaceURI.Length == 0)
                {
                    // Two active top-level UI sections are ambiguous. Nested preset
                    // sections, however, are skipped with their enclosing root.
                    if (activeUi is not null)
                    {
                        return null;
                    }

                    activeUi = (XElement)XNode.ReadFrom(reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            if (activeUi is null)
            {
                return null;
            }

            return activeUi.Elements("UIData")
                .Where(static element => element.Attribute("Index")?.Value == "32")
                .Select(element => ReadCandidate(element, calibration))
                .OfType<PrivateItemChatCalibration>()
                .GroupBy(static candidate => candidate.WindowIndex)
                .Where(static group => group.Count() == 1)
                .Select(static group => group.Single())
                .OrderBy(static candidate => candidate.HasOtherChatTypes)
                .ThenBy(static candidate => candidate.WindowIndex)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or XmlException or ArgumentException or NotSupportedException)
        {
            // BDO can replace or temporarily truncate this file while saving its UI.
            // An unavailable optional fallback must not interrupt normal tracking.
            return null;
        }
    }

    private static PrivateItemChatCalibration? ReadCandidate(
        XElement element,
        CompanionCalibration calibration)
    {
        if (!IsBoolean(element.Attribute("IsShow"), true) ||
            !IsBoolean(element.Attribute("IsUsing"), true) ||
            !IsBoolean(element.Attribute("IsCombinedToMainPanel"), false) ||
            !int.TryParse(element.Attribute("WindowIndex")?.Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var windowIndex) || windowIndex < 0 ||
            !TryReadFilters(element, "ChatType", out var chatTypes) ||
            !chatTypes.TryGetValue("System", out var systemEnabled) || !systemEnabled ||
            !TryReadFilters(element, "ChatSystemType", out var systemTypes) ||
            RequiredSystemFilters.Any(key => !systemTypes.ContainsKey(key)) ||
            !systemTypes[PrivateItemFilter] ||
            systemTypes.Any(static filter => filter.Key != PrivateItemFilter && filter.Value))
        {
            return null;
        }

        var otherTypes = chatTypes.Where(static filter => filter.Key != "System" && filter.Value)
            .Select(static filter => filter.Key).ToArray();
        if (otherTypes.Any(static key => !ToleratedOtherChatTypes.Contains(key)) ||
            !TryReadBounds(element, calibration, out var bounds))
        {
            return null;
        }

        return new PrivateItemChatCalibration(windowIndex, bounds, otherTypes.Length > 0);
    }

    private static bool TryReadFilters(
        XElement element,
        string elementName,
        out Dictionary<string, bool> filters)
    {
        filters = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var filter in element.Elements(elementName))
        {
            var key = filter.Attribute("key")?.Value;
            if (string.IsNullOrWhiteSpace(key) ||
                !bool.TryParse(filter.Attribute("value")?.Value, out var enabled) ||
                !filters.TryAdd(key, enabled))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadBounds(
        XElement element,
        CompanionCalibration calibration,
        out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!TryReadFinite(element, "RelativePosX", out var relativeX) ||
            !TryReadFinite(element, "RelativePosY", out var relativeY) ||
            relativeX < 0 || relativeX > 1 || relativeY < 0 || relativeY > 1 ||
            !TryReadFinite(element, "SizeX", out var logicalWidth) ||
            !TryReadFinite(element, "SizeY", out var logicalHeight) ||
            logicalWidth <= 0 || logicalHeight <= 0)
        {
            return false;
        }

        var scaledWidth = logicalWidth * calibration.UiScale;
        var scaledHeight = logicalHeight * calibration.UiScale;
        if (!double.IsFinite(scaledWidth) || !double.IsFinite(scaledHeight) ||
            scaledWidth > calibration.ScreenWidth || scaledHeight > calibration.ScreenHeight)
        {
            return false;
        }

        // Chat RelativePos is the panel center; PosX/PosY are logical coordinates
        // and may refer to an older resolution. Convert before rounding to pixels.
        var width = (int)Math.Round(scaledWidth, MidpointRounding.AwayFromZero);
        var height = (int)Math.Round(scaledHeight, MidpointRounding.AwayFromZero);
        var left = (int)Math.Round(relativeX * calibration.ScreenWidth - scaledWidth / 2,
            MidpointRounding.AwayFromZero);
        var top = (int)Math.Round(relativeY * calibration.ScreenHeight - scaledHeight / 2,
            MidpointRounding.AwayFromZero);
        if (width <= 0 || height <= 0 || left < 0 || top < 0 ||
            (long)left + width > calibration.ScreenWidth ||
            (long)top + height > calibration.ScreenHeight)
        {
            return false;
        }

        bounds = new Rectangle(left, top, width, height);
        return true;
    }

    private static bool TryReadFinite(XElement element, string name, out double value) =>
        double.TryParse(element.Attribute(name)?.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static bool IsBoolean(XAttribute? attribute, bool expected) =>
        bool.TryParse(attribute?.Value, out var value) && value == expected;
}
