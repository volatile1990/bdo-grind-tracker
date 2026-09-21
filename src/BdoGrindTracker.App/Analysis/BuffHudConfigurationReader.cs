using System.Globalization;
using System.Security;
using System.Xml;
using System.Xml.Linq;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Analysis;

internal sealed record BuffHudLayout(Rectangle Region, double Scale);

internal static class BuffHudConfigurationReader
{
    /// <summary>Uses only the active, visible saved panel; presets cannot resurrect a hidden panel.</summary>
    internal static BuffHudLayout? Resolve(BuffRecognitionProfile profile, Size frame)
    {
        if (!profile.IsValid) return null;
        if (profile.UiDataIndex is not { } index)
            return frame.Width == profile.ScreenWidth && frame.Height == profile.ScreenHeight && Contains(frame, profile.Region.Rectangle)
                ? new(profile.Region.Rectangle, 1) : null;
        try
        {
            var root = profile.BlackDesertDirectory ?? FindOptionsDirectory(profile.GameVariablePath) ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Black Desert");
            var variablePath = profile.GameVariablePath ?? Path.Combine(
                CompanionCalibrationReader.SelectActiveProfileDirectory(Path.Combine(root, "UserCache")), "gameVariable.xml");
            using var file = new FileStream(variablePath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 4 * 1024 * 1024) return null;
            using var reader = XmlReader.Create(file, new XmlReaderSettings
            {
                ConformanceLevel = ConformanceLevel.Fragment, DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024,
            });
            var elements = new List<XElement>();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element) elements.Add((XElement)XNode.ReadFrom(reader));
                else reader.Read();
            }
            var sections = elements.Where(element => element.Name == "UIData").ToArray();
            if (sections.Length != 1) return null;
            var panels = sections[0].Elements("UIData").Where(element =>
                uint.TryParse(element.Attribute("Index")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id == index).ToArray();
            if (panels.Length != 1 || panels[0].Attribute("IsShow")?.Value != "true") return null;
            var x = Number(panels[0].Attribute("RelativePosX")?.Value);
            var y = Number(panels[0].Attribute("RelativePosY")?.Value);
            if (x is null or < 0 or > 1 || y is null or < 0 or > 1) return null;
            var globals = elements.Where(element => element.Name == "GameOptionGlobal").ToArray();
            if (globals.Length > 1) return null;
            var global = globals.SingleOrDefault();
            var resolutions = global?.Elements("Resolution").ToArray() ?? [];
            var scales = global?.Elements("UiScale").ToArray() ?? [];
            if (resolutions.Length > 1 || scales.Length > 1) return null;
            var resolution = resolutions.SingleOrDefault();
            var options = ReadOptions(root);
            // Explicit malformed saved values must fail, never be replaced with an option/preset.
            var width = Number(resolution?.Attribute("Width")?.Value ?? options.GetValueOrDefault("width"));
            var height = Number(resolution?.Attribute("Height")?.Value ?? options.GetValueOrDefault("height"));
            var uiScale = Number(scales.SingleOrDefault()?.Attribute("Value")?.Value ?? options.GetValueOrDefault("uiScale"));
            if (width != frame.Width || height != frame.Height || uiScale is null or < .5 or > 3) return null;
            var scale = uiScale.Value / profile.UiScale;
            var region = new Rectangle((int)(x.Value * frame.Width) + (int)Math.Round(profile.Region.X * scale),
                (int)(y.Value * frame.Height) + (int)Math.Round(profile.Region.Y * scale),
                (int)Math.Round(profile.Region.Width * scale), (int)Math.Round(profile.Region.Height * scale));
            return Contains(frame, region) ? new(region, scale) : null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or SecurityException or
                   XmlException or ArgumentException or NotSupportedException or OverflowException)
        { return null; }
    }

    private static string? FindOptionsDirectory(string? variables)
    {
        if (string.IsNullOrWhiteSpace(variables)) return null;
        for (var directory = Path.GetDirectoryName(Path.GetFullPath(variables)); directory is not null;
             directory = Path.GetDirectoryName(directory))
            if (File.Exists(Path.Combine(directory, "GameOption.txt"))) return directory;
        return null;
    }

    private static Dictionary<string, string> ReadOptions(string root)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(root, "GameOption.txt");
        if (!File.Exists(path)) return options;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException("Display options are too large.");
        using var reader = new StreamReader(file);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0] is not ("width" or "height" or "uiScale")) continue;
            if (!options.TryAdd(parts[0], parts[1])) throw new InvalidDataException("Ambiguous display options.");
        }
        return options;
    }

    private static double? Number(string? text) => double.TryParse(text, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;

    internal static bool Contains(Size size, Rectangle region) => region.Width > 0 && region.Height > 0 &&
        region.X >= 0 && region.Y >= 0 && (long)region.X + region.Width <= size.Width && (long)region.Y + region.Height <= size.Height;
}
