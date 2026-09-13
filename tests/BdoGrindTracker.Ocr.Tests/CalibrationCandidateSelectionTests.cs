using System.Xml;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CalibrationCandidateSelectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "grindcrest-calibration-candidates-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Written = new(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
    private readonly CompanionCalibrationReader reader = new();

    public CalibrationCandidateSelectionTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "GameOption.txt"),
            "width = 1920\nheight = 1080\nuiScale =  1.00\nUIFontType = 2\nwindowed = 1\n");
    }

    [Fact]
    public void ScanIncludesAllProfilesAndArbitrarilyNestedCharacterConfigurations()
    {
        var direct = Configuration("gameVariable.xml");
        var first = Configuration(Path.Combine("41", "gameVariable.xml"));
        var second = Configuration(Path.Combine("42", "GAMEVARIABLE.XML"));
        var nested = Configuration(Path.Combine("42", "400", "character", "deeper", "gamevariable.xml"));
        var named = Configuration(Path.Combine("named-profile", "gameVariable.xml"));
        File.WriteAllText(Path.Combine(root, "UserCache", "unrelated.xml"), "<unrelated/>");
        var candidates = reader.ScanCandidates(root);
        Assert.Equal(new[] { direct, first, second, nested, named }.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            candidates.Select(candidate => candidate.GameVariablePath));
        Assert.All(candidates, candidate =>
        {
            Assert.True(candidate.IsValid);
            Assert.Equal(Written, candidate.LastWriteUtc);
            Assert.Equal(candidate.GameVariablePath, candidate.Calibration!.GameVariablePath);
            Assert.Equal(RareLootAnchorStatus.Active, candidate.Calibration.RareLootResolution!.Status);
        });
    }

    [Fact]
    public void InvalidAndLockedConfigurationsDoNotHideUsableCandidates()
    {
        var valid = Configuration(Path.Combine("41", "gameVariable.xml"));
        var malformed = Configuration(Path.Combine("42", "gameVariable.xml"), "<incomplete");
        var hidden = Configuration(Path.Combine("43", "gameVariable.xml"),
            "<UIData><UIData Index='159' IsShow='false'/></UIData>");
        var locked = Configuration(Path.Combine("44", "gameVariable.xml"));
        using var stream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var candidates = reader.ScanCandidates(root);
        Assert.Equal(4, candidates.Count);
        Assert.True(Assert.Single(candidates, candidate => candidate.GameVariablePath == valid).IsValid);
        foreach (var path in new[] { malformed, hidden, locked })
        {
            var rejected = Assert.Single(candidates, candidate => candidate.GameVariablePath == path);
            Assert.False(rejected.IsValid);
            Assert.Null(rejected.Calibration);
            Assert.False(string.IsNullOrWhiteSpace(rejected.ValidationError));
        }
    }

    [Fact]
    public void ExplicitSelectionUsesExactlyTheChosenNestedFileForBothLootPanels()
    {
        var automatic = Configuration(Path.Combine("42", "gameVariable.xml"));
        File.SetLastWriteTimeUtc(automatic, Written.AddDays(1));
        var selected = Configuration(Path.Combine("41", "400", "character", "gameVariable.xml"),
            "<Resolution Width='2560' Height='1440'/><UiScale Value='0.9'/>" +
            "<UIData><UIData Index='159' IsShow='true' RelativePosX='0.25' RelativePosY='0.5'/>" +
            "<UIData Index='161' IsShow='true' RelativePosX='0.6' RelativePosY='0.3'/></UIData>");
        Configuration(Path.Combine("41", "400", "character", "other", "gameVariable.xml"));
        var actual = reader.Read(root, selected);
        Assert.Equal(selected, actual.GameVariablePath);
        Assert.Equal(Path.GetDirectoryName(selected), actual.ProfileDirectoryPath);
        Assert.Null(actual.ActiveCharacterGameVariablePath);
        Assert.Equal(Path.Combine(root, "GameOption.txt"), actual.GameOptionPath);
        Assert.Equal(640, actual.LootAnchorX);
        Assert.Equal(720, actual.LootAnchorY);
        Assert.Equal(1536, actual.RareLootAnchorX);
        Assert.Equal(432, actual.RareLootAnchorY);
        Assert.Equal(0.9f, actual.UiScale);
        Assert.Equal(automatic, reader.Read(root).GameVariablePath);
        Assert.Equal(automatic, reader.Read(root, null).GameVariablePath);
    }

    [Fact]
    public void ExplicitMissingOrInvalidSelectionNeverFallsBackToAnotherValidProfile()
    {
        Configuration(Path.Combine("41", "gameVariable.xml"));
        var invalid = Configuration(Path.Combine("42", "gameVariable.xml"), "<incomplete");
        Assert.Throws<XmlException>(() => reader.Read(root, invalid));
        Assert.Throws<FileNotFoundException>(() => reader.Read(root,
            Path.Combine(root, "UserCache", "missing", "gameVariable.xml")));
    }

    [Fact]
    public void AccessTimesCannotChangeExplicitSelectionOrScanOrderAndFilesAreNotRewritten()
    {
        var first = Configuration(Path.Combine("41", "gameVariable.xml"));
        var second = Configuration(Path.Combine("42", "gameVariable.xml"));
        var content = File.ReadAllText(first);
        File.SetLastAccessTimeUtc(first, Written.AddDays(-10));
        File.SetLastAccessTimeUtc(second, Written.AddDays(10));
        Assert.Equal(first, reader.Read(root, first).GameVariablePath);
        Assert.Equal(new[] { first, second }, reader.ScanCandidates(root).Select(candidate => candidate.GameVariablePath));
        File.SetLastAccessTimeUtc(first, Written.AddDays(20));
        Assert.Equal(new[] { first, second }, reader.ScanCandidates(root).Select(candidate => candidate.GameVariablePath));
        Assert.Equal(content, File.ReadAllText(first));
        Assert.Equal(Written, File.GetLastWriteTimeUtc(first));
        Assert.Equal(Written, File.GetLastWriteTimeUtc(second));
    }

    [Fact]
    public void RarePresetFallbackIsReportedAndComesOnlyFromTheSelectedXml()
    {
        var selected = Configuration(Path.Combine("41", "gameVariable.xml"),
            "<UIData><UIData Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='0.8'/>" +
            "<UIData Index='161' IsShow='true' PendingType='RightBottom' PosX='100' PosY='100' RelativePosX='0' RelativePosY='0'/></UIData>" +
            "<UISettingPreset><UISettingPreset0 Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='0.8'/>" +
            "<UISettingPreset0 Index='161' IsShow='true' RelativePosX='0.4' RelativePosY='0.3'/></UISettingPreset>");
        Configuration(Path.Combine("41", "nested", "gameVariable.xml"));
        var actual = reader.Read(root, selected);
        Assert.Equal(selected, actual.GameVariablePath);
        Assert.Null(actual.ActiveCharacterGameVariablePath);
        Assert.Equal(RareLootAnchorStatus.PresetFallback, actual.RareLootResolution!.Status);
        Assert.Equal("UISettingPreset0", actual.RareLootResolution.Source);
        Assert.Equal(768, actual.RareLootAnchorX);
        Assert.Equal(324, actual.RareLootAnchorY);
    }

    [Fact]
    public void MissingUserCacheIsAnEmptyScanAndMissingOptionsArePerCandidateErrors()
    {
        Assert.Empty(reader.ScanCandidates(root));
        Configuration(Path.Combine("41", "gameVariable.xml"));
        File.Delete(Path.Combine(root, "GameOption.txt"));
        var candidate = Assert.Single(reader.ScanCandidates(root));
        Assert.False(candidate.IsValid);
        Assert.Contains("GameOption.txt", candidate.ValidationError);
    }

    private string Configuration(string relativePath, string? xml = null)
    {
        var path = Path.Combine(root, "UserCache", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, xml ??
            "<UIData><UIData Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='0.8'/>" +
            "<UIData Index='161' IsShow='true' RelativePosX='0.6' RelativePosY='0.3'/></UIData>");
        File.SetLastWriteTimeUtc(path, Written);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
