namespace BdoGrindTracker.Ocr.Tests;

public sealed class BlackDesertLanguageReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
    public BlackDesertLanguageReaderTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("_DE_", "de")]
    [InlineData("_EN_", "en")]
    [InlineData("de-DE", "de")]
    [InlineData("en-US", "en")]
    public void ReadsTextResourcesWithoutConfusingLauncherVoiceOrChatLanguages(string resource, string expected)
    {
        File.WriteAllText(Path.Combine(_root, "Resource.ini"), $"[SERVICE]\nRES = {resource}\n");
        File.WriteAllText(Path.Combine(_root, "Lan.txt"), "DE");
        File.WriteAllText(Path.Combine(_root, "service.ini"), "[SERVICE]\nRES=_EN_");
        File.WriteAllText(Path.Combine(_root, "GameOption.txt"), "AudioResourceType=2\nwebvLanguage=0\nLangType=1");
        Assert.Equal(expected, Read().Language);
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData("en", "en")]
    public void ExplicitSavedTextLanguageTakesPrecedence(string value, string expected)
    {
        File.WriteAllText(Path.Combine(_root, "GameOption.txt"), $"Language = {value}\nAudioResourceType=0");
        File.WriteAllText(Path.Combine(_root, "Resource.ini"), "[SERVICE]\nRES = _FR_");
        Assert.Equal(expected, Read().Language);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[SERVICE]\nRES=_FR_")]
    [InlineData("[SERVICE]\nRES=_EN_\nRES=_DE_")]
    [InlineData("[LAUNCHER]\nRES=_DE_")]
    [InlineData(";[SERVICE]\n;RES=_DE_\n")]
    public void MissingUnsupportedAndAmbiguousValuesDoNotSilentlyChooseEnglish(string contents)
    {
        File.WriteAllText(Path.Combine(_root, "Resource.ini"), contents);
        Assert.Null(Read().Language);
        Assert.NotEmpty(Read().Message);
    }

    [Fact]
    public void ConflictingInstallationsRequireManualChoice()
    {
        var other = Path.Combine(_root, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(_root, "Resource.ini"), "[SERVICE]\nRES=_EN_");
        File.WriteAllText(Path.Combine(other, "Resource.ini"), "[SERVICE]\nRES=_DE_");
        Assert.Null(BlackDesertLanguageReader.Read(Path.Combine(_root, "GameOption.txt"), [_root, other]).Language);
    }

    [Fact]
    public void RereadingObservesGameLanguageChangesWithoutWritingGameFiles()
    {
        var path = Path.Combine(_root, "Resource.ini");
        File.WriteAllText(path, "[SERVICE]\nRES=_EN_");
        Assert.Equal("en", Read().Language);
        File.WriteAllText(path, "[SERVICE]\nRES=_DE_");
        Assert.Equal("de", Read().Language);
        Assert.Equal("[SERVICE]\nRES=_DE_", File.ReadAllText(path));
    }

    private GameLanguageDetection Read() => BlackDesertLanguageReader.Read(Path.Combine(_root, "GameOption.txt"), [_root]);
    public void Dispose() => Directory.Delete(_root, true);
}
