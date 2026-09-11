using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.Tests;

public sealed class ExperienceHudConfigurationReaderTests
{
    [Fact]
    public void ActiveProfileDisplaySettingsTakePrecedenceOverOptions()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteOptions("width=1920\nheight=1080\nuiScale=1.00\n");
        var older = fixture.WriteProfile("41", Global(2560, 1440, "1.2"));
        var selected = fixture.WriteProfile("+42", Global(3840, 2160, "1.49"));
        fixture.WriteProfile("0", Global(800, 600, "0.5"));
        fixture.WriteProfile("4294967296", Global(800, 600, "0.5"));
        fixture.WriteProfile("character", Global(800, 600, "0.5"));
        Directory.SetLastWriteTimeUtc(older, DateTime.UnixEpoch);
        Directory.SetLastWriteTimeUtc(selected, DateTime.UnixEpoch.AddSeconds(1));

        Assert.Equal(new ExperienceHudConfiguration(3840, 2160, 1.49), fixture.Read());
    }

    [Fact]
    public void OptionsWorkWithoutUserCacheAndAllowWhitespaceAndInvariantDecimals()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteOptions("\twidth\t=\t3440\r\n height = 1440 \r\n uiScale\t=  1.25 \r\n");

        Assert.Equal(new ExperienceHudConfiguration(3440, 1440, 1.25), fixture.Read());
    }

    [Fact]
    public void CompleteGlobalSettingsDoNotRequireGameOptionFileOrLootPanels()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteProfile("42", "<UIData><UIData Index='159' IsShow='false'/></UIData>" + Global(3840, 2160, "1.49"));

        Assert.Equal(new ExperienceHudConfiguration(3840, 2160, 1.49), fixture.Read());
    }

    [Fact]
    public void OnlyMissingFieldsFallBackToOptions()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteProfile("42", "<GameOptionGlobal><Resolution Width='3840'/></GameOptionGlobal>");
        fixture.WriteOptions("width=1920\nheight=2160\nuiScale=1.49\n");

        Assert.Equal(new ExperienceHudConfiguration(3840, 2160, 1.49), fixture.Read());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("32769")]
    [InlineData("2147483648")]
    [InlineData("NaN")]
    [InlineData("")]
    public void ExplicitInvalidDimensionsCannotBeOverriddenByValidOptions(string width)
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteProfile("42", $"<GameOptionGlobal><Resolution Width='{width}' Height='1080'/><UiScale Value='1'/></GameOptionGlobal>");
        fixture.WriteOptions("width=1920\nheight=1080\nuiScale=1\n");

        Assert.Null(fixture.Read());
    }

    [Theory]
    [InlineData("0.49")]
    [InlineData("3.01")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,49")]
    [InlineData("")]
    public void ExplicitInvalidScaleCannotBeOverriddenByValidOptions(string scale)
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteProfile("42", Global(1920, 1080, scale));
        fixture.WriteOptions("width=1920\nheight=1080\nuiScale=1\n");

        Assert.Null(fixture.Read());
    }

    [Theory]
    [InlineData("width=0\nheight=1080\nuiScale=1")]
    [InlineData("width=1920\nheight=32769\nuiScale=1")]
    [InlineData("width=1920\nheight=1080\nuiScale=Infinity")]
    [InlineData("width=1920\nwidth=1920\nheight=1080\nuiScale=1")]
    [InlineData("width=1920\nheight=1080\nuiScale=1\nuiScale=1")]
    [InlineData("width=1920\nheight=1080")]
    public void IncompleteInvalidOrAmbiguousOptionsAreUnavailable(string options)
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteOptions(options);

        Assert.Null(fixture.Read());
    }

    [Theory]
    [InlineData("<GameOptionGlobal>")]
    [InlineData("<GameOptionGlobal><Resolution Width='1920' Height='1080'/><Resolution Width='1920' Height='1080'/><UiScale Value='1'/></GameOptionGlobal>")]
    [InlineData("<GameOptionGlobal><Resolution Width='1920' Height='1080'/><UiScale Value='1'/><UiScale Value='1'/></GameOptionGlobal>")]
    [InlineData("<GameOptionGlobal/><GameOptionGlobal/>")]
    [InlineData("<!DOCTYPE GameOptionGlobal [<!ENTITY scale '1'>]><GameOptionGlobal><UiScale Value='&scale;'/></GameOptionGlobal>")]
    public void MalformedAmbiguousOrDtdXmlDoesNotFallBackToOptions(string variables)
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteProfile("42", variables);
        fixture.WriteOptions("width=1920\nheight=1080\nuiScale=1\n");

        Assert.Null(fixture.Read());
    }

    [Fact]
    public void SavedPresetsAndCharacterFilesCannotReplaceActiveGlobalSettings()
    {
        using var fixture = new ConfigurationFixture();
        var profile = fixture.WriteProfile("42",
            "<Preset>" + Global(800, 600, "0.5") + "</Preset>" +
            "<GameOptionGlobal><Preset><Resolution Width='800' Height='600'/><UiScale Value='0.5'/></Preset>" +
            "<Resolution Width='3840' Height='2160'/><UiScale Value='1.49'/></GameOptionGlobal>");
        var characterDirectory = Directory.CreateDirectory(Path.Combine(profile, "100", "200")).FullName;
        File.WriteAllText(Path.Combine(characterDirectory, "gameVariable.xml"), Global(800, 600, "0.5"));

        Assert.Equal(new ExperienceHudConfiguration(3840, 2160, 1.49), fixture.Read());
    }

    [Fact]
    public void NestedPresetFieldsCannotReplaceMissingActiveFields()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteProfile("42", "<Preset>" + Global(800, 600, "0.5") + "</Preset>");
        fixture.WriteOptions("width=2560\nheight=1440\nuiScale=1.2");

        Assert.Equal(new ExperienceHudConfiguration(2560, 1440, 1.2), fixture.Read());
    }

    [Fact]
    public void ChangesAreVisibleOnTheNextReadWithoutRestart()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteProfile("42", Global(1920, 1080, "1"));
        var reader = new ExperienceHudConfigurationReader();
        Assert.Equal(new ExperienceHudConfiguration(1920, 1080, 1), reader.Read(fixture.Root));

        fixture.WriteProfile("42", Global(3840, 2160, "1.49"));

        Assert.Equal(new ExperienceHudConfiguration(3840, 2160, 1.49), reader.Read(fixture.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedConfigurationIsUnavailable(bool xml)
    {
        using var fixture = new ConfigurationFixture();
        var content = new string(' ', 4 * 1024 * 1024 + 1);
        if (xml) fixture.WriteProfile("42", content);
        else fixture.WriteOptions(content);

        Assert.Null(fixture.Read());
    }

    [Fact]
    public void ConfigurationCanBeReadWhileTheGameKeepsAWritableHandleOpen()
    {
        using var fixture = new ConfigurationFixture();
        var options = fixture.WriteOptions("width=1920\nheight=1080\nuiScale=1");
        using var gameHandle = new FileStream(options, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal(new ExperienceHudConfiguration(1920, 1080, 1), fixture.Read());
    }

    [Fact]
    public void MissingConfigurationIsUnavailable()
    {
        using var fixture = new ConfigurationFixture();
        Assert.Null(fixture.Read());
        Assert.Null(new ExperienceHudConfigurationReader().Read(""));
        Assert.Null(new ExperienceHudConfigurationReader().Read("\0"));
    }

    private static string Global(int width, int height, string scale) =>
        $"<GameOptionGlobal><Resolution Width='{width}' Height='{height}'/><UiScale Value='{scale}'/></GameOptionGlobal>";

    private sealed class ConfigurationFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "grindcrest-xp-config-" + Guid.NewGuid().ToString("N"));

        public string WriteOptions(string text)
        {
            Directory.CreateDirectory(Root);
            var path = Path.Combine(Root, "GameOption.txt");
            File.WriteAllText(path, text);
            return path;
        }

        public string WriteProfile(string name, string text)
        {
            var path = Directory.CreateDirectory(Path.Combine(Root, "UserCache", name)).FullName;
            File.WriteAllText(Path.Combine(path, "gameVariable.xml"), text);
            return path;
        }

        public ExperienceHudConfiguration? Read() => new ExperienceHudConfigurationReader().Read(Root);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
