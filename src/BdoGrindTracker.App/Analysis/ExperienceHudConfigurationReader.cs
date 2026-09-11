using System.Globalization;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BdoGrindTracker.App.Analysis;

internal sealed record ExperienceHudConfiguration(int ScreenWidth, int ScreenHeight, double UiScale);

/// <summary>
/// Reads saved display settings as a hint for locating the visible XP HUD.
/// Game settings can lag behind the live frame, so they are not evidence of XP.
/// </summary>
internal sealed class ExperienceHudConfigurationReader
{
    private const int MaximumFileBytes = 4 * 1024 * 1024;

    public ExperienceHudConfiguration? ReadDefault()
    {
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(documents) ? null : Read(Path.Combine(documents, "Black Desert"));
        }
        catch (Exception error) when (IsConfigurationError(error))
        {
            return null;
        }
    }

    public ExperienceHudConfiguration? Read(string blackDesertDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(blackDesertDirectory)) return null;
            var root = Path.GetFullPath(blackDesertDirectory);
            var profile = SelectProfile(Path.Combine(root, "UserCache"));
            var variablesPath = profile is null ? null : Path.Combine(profile, "gameVariable.xml");
            var variables = variablesPath is not null && File.Exists(variablesPath)
                ? ReadGlobalSettings(ReadText(variablesPath)) : null;

            var resolution = SingleChild(variables, "Resolution");
            var scaleElement = SingleChild(variables, "UiScale");
            var width = OptionalDimension(resolution?.Attribute("Width")?.Value);
            var height = OptionalDimension(resolution?.Attribute("Height")?.Value);
            var scale = OptionalScale(scaleElement?.Attribute("Value")?.Value);
            if (width is null || height is null || scale is null)
            {
                var optionsPath = Path.Combine(root, "GameOption.txt");
                if (!File.Exists(optionsPath)) return null;
                var options = ReadText(optionsPath);
                width ??= OptionalDimension(ReadOption(options, "width"));
                height ??= OptionalDimension(ReadOption(options, "height"));
                scale ??= OptionalScale(ReadOption(options, "uiScale"));
            }

            return width is not null && height is not null && scale is not null
                ? new ExperienceHudConfiguration(width.Value, height.Value, scale.Value) : null;
        }
        catch (Exception error) when (IsConfigurationError(error))
        {
            return null;
        }
    }

    private static string? SelectProfile(string userCachePath)
    {
        if (!Directory.Exists(userCachePath)) return null;
        string? selected = null;
        var selectedWriteTime = DateTime.MinValue;
        foreach (var candidate in Directory.EnumerateDirectories(userCachePath))
        {
            if (!uint.TryParse(Path.GetFileName(candidate), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var id) || id == 0) continue;
            var written = Directory.GetLastWriteTimeUtc(candidate);
            if (selected is not null && written < selectedWriteTime) continue;
            selected = candidate;
            selectedWriteTime = written;
        }
        return selected;
    }

    private static XElement? ReadGlobalSettings(string text)
    {
        using var input = new StringReader(text);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumFileBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        });
        XElement? global = null;
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 0)
            {
                reader.Read();
                continue;
            }
            if (reader.LocalName != "GameOptionGlobal" || reader.NamespaceURI.Length != 0)
            {
                reader.Skip();
                continue;
            }
            if (global is not null) throw new InvalidDataException("Ambiguous active display settings.");
            global = (XElement)XNode.ReadFrom(reader);
        }
        return global;
    }

    private static XElement? SingleChild(XElement? parent, string name)
    {
        var children = parent?.Elements(name).Take(2).ToArray();
        if (children is { Length: > 1 }) throw new InvalidDataException("Ambiguous active display field.");
        return children?.SingleOrDefault();
    }

    private static string? ReadOption(string text, string name)
    {
        string? value = null;
        using var lines = new StringReader(text);
        while (lines.ReadLine() is { } line)
        {
            var separator = line.IndexOf('=');
            if (separator < 0 || !line.AsSpan(0, separator).Trim().SequenceEqual(name)) continue;
            if (value is not null) throw new InvalidDataException("Ambiguous display option.");
            value = line[(separator + 1)..].Trim();
        }
        return value;
    }

    private static int? OptionalDimension(string? value)
    {
        if (value is null) return null;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ||
            result is < 1 or > 32768) throw new InvalidDataException("Invalid display dimension.");
        return result;
    }

    private static double? OptionalScale(string? value)
    {
        if (value is null) return null;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
            !double.IsFinite(result) || result is < 0.5 or > 3) throw new InvalidDataException("Invalid UI scale.");
        return result;
    }

    private static string ReadText(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > MaximumFileBytes) throw new InvalidDataException("Display configuration is too large.");
        using var content = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = file.Read(buffer)) > 0)
        {
            if (content.Length + count > MaximumFileBytes)
                throw new InvalidDataException("Display configuration is too large.");
            content.Write(buffer, 0, count);
        }
        content.Position = 0;
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static bool IsConfigurationError(Exception error) => error is
        IOException or InvalidDataException or UnauthorizedAccessException or SecurityException or XmlException or
        ArgumentException or NotSupportedException;
}
