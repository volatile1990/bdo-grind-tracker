using System.Globalization;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CompanionCalibrationReaderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("<UIData Index='159' IsShow='false' RelativePosX='0.5' RelativePosY='0.5' />")]
    [InlineData("<UIData Index='159' IsShow='true' RelativePosX='0.5' />")]
    [InlineData("<UIData Index='159' IsShow='true' RelativePosX='NaN' RelativePosY='0.5' />")]
    [InlineData("<UIData Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='Infinity' />")]
    [InlineData("<UIData Index='159' IsShow='true' RelativePosX='-0.1' RelativePosY='0.5' />")]
    [InlineData("<UIData Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='1.1' />")]
    [InlineData("<Preset><UIData Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='0.5' /></Preset>")]
    [InlineData("<UIData Index='159' IsShow='false' /><UIData Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='0.5' />")]
    public void VisibleRareLogCannotReplaceAMissingOrInvalidMainLog(string mainLog)
    {
        using var fixture = new CalibrationFixture();
        var profile = fixture.AddProfile("42", DateTime.UtcNow);
        fixture.WriteOptions("width = 1920\nheight = 1080\nuiScale =  1.00\n");
        File.WriteAllText(Path.Combine(profile, "gameVariable.xml"),
            "<UIData>" + mainLog + "<UIData Index='161' IsShow='true' RelativePosX='0.5' RelativePosY='0.4' /></UIData>");
        Assert.Throws<InvalidDataException>(() => new CompanionCalibrationReader().Read(fixture.RootPath));
    }

    [Fact]
    public void InstalledBdoConfigurationStartsWhenPresent()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var root = Path.Combine(documents, "Black Desert");
        if (!File.Exists(Path.Combine(root, "GameOption.txt")) ||
            !Directory.Exists(Path.Combine(root, "UserCache")))
        {
            return;
        }

        var actual = new CompanionCalibrationReader().Read(root);
        var panel = CompanionNormalLootGeometry.CalculatePanelBounds(actual);

        Assert.True(File.Exists(actual.GameVariablePath));
        Assert.True(actual.ActiveCharacterGameVariablePath is null ||
                    File.Exists(actual.ActiveCharacterGameVariablePath));
        Assert.InRange(panel.Left, 0, actual.ScreenWidth);
        Assert.InRange(panel.Top, 0, actual.ScreenHeight);
        Assert.InRange(panel.Right, 0, actual.ScreenWidth);
        Assert.InRange(panel.Bottom, 0, actual.ScreenHeight);
    }

    [Fact]
    public void ReaderSelectsNewestValidUintProfileAndUsesVariableOverrides()
    {
        using var fixture = new CalibrationFixture();
        var olderTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var selectedTime = olderTime.AddMinutes(1);
        var older = fixture.AddProfile("41", olderTime);
        var selected = fixture.AddProfile("+42", selectedTime);
        fixture.AddProfile("0", DateTime.UtcNow);
        fixture.AddProfile("4294967296", DateTime.UtcNow);
        fixture.AddProfile("character-name", DateTime.UtcNow);

        fixture.WriteVariables(
            older,
            relativeX: "0.1",
            relativeY: "0.2",
            width: "1280",
            height: "720",
            uiScale: "0.90");
        fixture.WriteVariables(
            selected,
            relativeX: "0.8083042502",
            relativeY: "0.5641379356",
            width: "3840",
            height: "2160",
            uiScale: "1.49",
            customHp: true);
        Directory.SetLastWriteTimeUtc(older, olderTime);
        Directory.SetLastWriteTimeUtc(selected, selectedTime);
        fixture.WriteOptions(
            "width = 1920\nheight = 1080\nuiScale =  1.00\nUIFontType = 0\nwindowed = 1\n");

        var actual = new CompanionCalibrationReader().Read(fixture.RootPath);

        Assert.Equal(Path.GetFullPath(selected), actual.ProfileDirectoryPath);
        Assert.Equal(3840, actual.ScreenWidth);
        Assert.Equal(2160, actual.ScreenHeight);
        Assert.Equal(1.49f, actual.UiScale);
        Assert.Equal(3103, actual.LootAnchorX);
        Assert.Equal(1218, actual.LootAnchorY);
        Assert.Equal(CompanionFontType.DejaVu, actual.FontType);
        Assert.Equal(1, actual.WindowedMode);
        Assert.True(actual.CustomHp);
    }

    [Fact]
    public void ReaderParsesBdoMultiRootFragmentAndKeepsActiveCharacterSeparate()
    {
        using var fixture = new CalibrationFixture();
        var profile = fixture.AddProfile("11", DateTime.UtcNow);
        fixture.WriteVariables(profile, "0.5", "0.25", "2560", "1440", "1.20");
        fixture.WriteOptions(
            "width = 1920\nheight = 1080\nuiScale =  1.00\nUIFontType = 2\nwindowed = 1\n");

        var presetDirectory = Directory.CreateDirectory(Path.Combine(profile, "400")).FullName;
        var preset = Path.Combine(presetDirectory, "gameVariable.xml");
        File.WriteAllText(preset, "<Preset />");
        File.SetLastAccessTimeUtc(preset, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var olderDirectory = Directory.CreateDirectory(
            Path.Combine(presetDirectory, "40000000000000001")).FullName;
        var older = Path.Combine(olderDirectory, "gameVariable.xml");
        File.WriteAllText(older, "<Character />");
        File.SetLastAccessTimeUtc(older, new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc));

        var activeDirectory = Directory.CreateDirectory(
            Path.Combine(presetDirectory, "40000000000000002")).FullName;
        var active = Path.Combine(activeDirectory, "gameVariable.xml");
        File.WriteAllText(active, "<Character />");
        File.SetLastAccessTimeUtc(active, new DateTime(2026, 1, 1, 0, 2, 0, DateTimeKind.Utc));

        var actual = new CompanionCalibrationReader().Read(fixture.RootPath);

        Assert.Equal(1280, actual.LootAnchorX);
        Assert.Equal(360, actual.LootAnchorY);
        Assert.Equal(Path.GetFullPath(active), actual.ActiveCharacterGameVariablePath);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(profile), "gameVariable.xml"),
            actual.GameVariablePath);
    }

    [Fact]
    public void ReaderLoadsVisibleUiData161AsSeparateRareAnchor()
    {
        using var fixture = new CalibrationFixture();
        var profile = fixture.AddProfile("12", DateTime.UtcNow);
        fixture.WriteVariables(
            profile,
            "0.5",
            "0.25",
            "2560",
            "1440",
            "1.20",
            rareRelativeX: "0.75",
            rareRelativeY: "0.40");
        fixture.WriteOptions(
            "width = 1920\nheight = 1080\nuiScale =  1.00\nUIFontType = 2\nwindowed = 1\n");

        var actual = new CompanionCalibrationReader().Read(fixture.RootPath);

        Assert.True(actual.HasRareLootAnchor);
        Assert.Equal(1920, actual.RareLootAnchorX);
        Assert.Equal(576, actual.RareLootAnchorY);
    }

    [Fact]
    public void ReaderDoesNotUseHiddenUiData161()
    {
        using var fixture = new CalibrationFixture();
        var profile = fixture.AddProfile("13", DateTime.UtcNow);
        fixture.WriteVariables(
            profile,
            "0.5",
            "0.25",
            "2560",
            "1440",
            "1.20",
            rareRelativeX: "0.75",
            rareRelativeY: "0.40",
            rareIsShown: false);
        fixture.WriteOptions(
            "width = 1920\nheight = 1080\nuiScale =  1.00\nUIFontType = 2\nwindowed = 1\n");

        var actual = new CompanionCalibrationReader().Read(fixture.RootPath);

        Assert.False(actual.HasRareLootAnchor);
        Assert.Equal(0, actual.RareLootAnchorX);
        Assert.Equal(0, actual.RareLootAnchorY);
    }

    [Fact]
    public void ReaderRejectsHiddenUiData159()
    {
        using var fixture = new CalibrationFixture();
        var profile = fixture.AddProfile("14", DateTime.UtcNow);
        fixture.WriteVariables(
            profile,
            "0.5",
            "0.25",
            "2560",
            "1440",
            "1.20",
            normalIsShown: false);
        fixture.WriteOptions(
            "width = 1920\nheight = 1080\nuiScale =  1.00\nUIFontType = 2\nwindowed = 1\n");

        Assert.Throws<InvalidDataException>(
            () => new CompanionCalibrationReader().Read(fixture.RootPath));
    }

    [Fact]
    public void ReaderUsesGameOptionsOnlyWhenVariableValuesAreAbsent()
    {
        using var fixture = new CalibrationFixture();
        var profile = fixture.AddProfile("7", DateTime.UtcNow);
        fixture.WriteVariables(
            profile,
            relativeX: "0.5",
            relativeY: "0.25",
            width: null,
            height: null,
            uiScale: null);
        fixture.WriteOptions(
            "width = 1920\nheight = 1080\nuiScale =  1.00\nUIFontType = 2\nwindowed = 0\n");

        var actual = new CompanionCalibrationReader().Read(fixture.RootPath);

        Assert.Equal(1920, actual.ScreenWidth);
        Assert.Equal(1080, actual.ScreenHeight);
        Assert.Equal(1f, actual.UiScale);
        Assert.Equal(960, actual.LootAnchorX);
        Assert.Equal(270, actual.LootAnchorY);
        Assert.Equal(CompanionFontType.StrongSword, actual.FontType);
        Assert.Equal(0, actual.WindowedMode);
    }

    [Theory]
    [InlineData(null, CompanionFontType.CabinDroid)]
    [InlineData("0", CompanionFontType.DejaVu)]
    [InlineData("1", CompanionFontType.StrongSword)]
    [InlineData("2", CompanionFontType.StrongSword)]
    public void FontMappingMatchesCompanion(string? rawFont, CompanionFontType expected)
    {
        using var fixture = new CalibrationFixture();
        var profile = fixture.AddProfile("9", DateTime.UtcNow);
        fixture.WriteVariables(profile, "0.5", "0.5", "1920", "1080", "1.00");
        var fontLine = rawFont is null ? string.Empty : $"UIFontType = {rawFont}\n";
        fixture.WriteOptions(
            $"width = 1920\nheight = 1080\nuiScale =  1.00\n{fontLine}windowed = 0\n");

        var actual = new CompanionCalibrationReader().Read(fixture.RootPath);

        Assert.Equal(expected, actual.FontType);
    }

    private sealed class CalibrationFixture : IDisposable
    {
        public CalibrationFixture()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                $"bdo-companion-calibration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(RootPath, "UserCache"));
        }

        public string RootPath { get; }

        public string AddProfile(string name, DateTime lastWriteUtc)
        {
            var path = Directory.CreateDirectory(Path.Combine(RootPath, "UserCache", name)).FullName;
            Directory.SetLastWriteTimeUtc(path, lastWriteUtc);
            return path;
        }

        public void WriteVariables(
            string profilePath,
            string relativeX,
            string relativeY,
            string? width,
            string? height,
            string? uiScale,
            bool customHp = false,
            string? rareRelativeX = null,
            string? rareRelativeY = null,
            bool rareIsShown = true,
            bool normalIsShown = true)
        {
            var resolution = width is null && height is null
                ? string.Empty
                : $"<Resolution{Attribute("Width", width)}{Attribute("Height", height)} />";
            var scale = uiScale is null ? string.Empty : $"<UiScale Value=\"{uiScale}\" />";
            var customHpData = customHp
                ? "<UIData Index=\"2\" IsShow=\"true\" />"
                : string.Empty;
            var rareData = rareRelativeX is null || rareRelativeY is null
                ? string.Empty
                : $"<UIData Index=\"161\" RelativePosX=\"{rareRelativeX}\" " +
                  $"RelativePosY=\"{rareRelativeY}\" " +
                  $"IsShow=\"{rareIsShown.ToString().ToLowerInvariant()}\" />";
            // BDO writes this file as an XML fragment: UIData, Resolution and
            // UiScale are independent top-level elements, with other roots mixed in.
            var xml =
                "<CheckQuestList Version=\"1\"><CheckedQuest Group=\"1\" /></CheckQuestList>\n" +
                $"<UIData Version=\"2\"><UIData Index=\"159\" " +
                $"RelativePosX=\"{relativeX}\" RelativePosY=\"{relativeY}\" " +
                $"IsShow=\"{normalIsShown.ToString().ToLowerInvariant()}\" />" +
                $"{customHpData}{rareData}</UIData>\n" +
                $"{resolution}\n{scale}\n" +
                "<QuestSortType Value=\"0\" />";
            File.WriteAllText(Path.Combine(profilePath, "gamevariable.xml"), xml);
            Directory.SetLastWriteTimeUtc(profilePath, Directory.GetLastWriteTimeUtc(profilePath));
        }

        public void WriteOptions(string value) =>
            File.WriteAllText(Path.Combine(RootPath, "GameOption.txt"), value);

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static string Attribute(string name, string? value) =>
            value is null
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture, $" {name}=\"{value}\"");
    }
}
