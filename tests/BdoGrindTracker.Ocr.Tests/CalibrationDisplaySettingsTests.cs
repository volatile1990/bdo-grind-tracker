namespace BdoGrindTracker.Ocr.Tests;

public sealed class CalibrationDisplaySettingsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "grindcrest-display-" + Guid.NewGuid().ToString("N"));
    private readonly string variables;
    private const string ActiveLoot = "<UIData Version='2'><UIData Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='0.5'/></UIData>";
    private const string ActiveDisplay = "<Resolution Width='3840' Height='2160'/><UiScale Value='1.49'/>";
    private const string SavedDisplay = """
        <UISettingPreset>
          <UISettingPreset0><Resolution Width='1920' Height='1080'/><UiScale Value='1.00'/></UISettingPreset0>
          <UISettingPreset1><GameOptionGlobal><Resolution Width='1280' Height='720'/><UiScale Value='0.8'/></GameOptionGlobal></UISettingPreset1>
        </UISettingPreset>
        <UISettingRevert><Resolution Width='800' Height='600'/><UiScale Value='0.5'/></UISettingRevert>
        """;

    public CalibrationDisplaySettingsTests()
    {
        var profile = Directory.CreateDirectory(Path.Combine(root, "UserCache", "42")).FullName;
        variables = Path.Combine(profile, "gameVariable.xml");
        File.WriteAllText(Path.Combine(root, "GameOption.txt"),
            "width = 2560\nheight = 1440\nuiScale =  1.00\nUIFontType = 2\nwindowed = 1\n");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SavedDisplaySettingsCannotOverrideActiveGlobalOrLegacyFields(bool presetsFirst, bool useGlobalSection)
    {
        var active = useGlobalSection ? $"<GameOptionGlobal Version='2'>{ActiveDisplay}</GameOptionGlobal>" : ActiveDisplay;
        File.WriteAllText(variables, ActiveLoot + (presetsFirst ? SavedDisplay + active : active + SavedDisplay));
        var reader = new CompanionCalibrationReader();

        AssertActiveDisplay(reader.Read(root));
        AssertActiveDisplay(reader.Read(root, variables));
        var candidate = Assert.Single(reader.ScanCandidates(root));
        Assert.True(candidate.IsValid, candidate.ValidationError);
        AssertActiveDisplay(candidate.Calibration!);
    }

    [Fact]
    public void PresetsAreNotUsedWhenActiveDisplayFieldsAreAbsent()
    {
        File.WriteAllText(variables, ActiveLoot + SavedDisplay + "<GameOptionGlobal Version='2'/>");

        var actual = new CompanionCalibrationReader().Read(root);

        Assert.Equal(2560, actual.ScreenWidth);
        Assert.Equal(1440, actual.ScreenHeight);
        Assert.Equal(1f, actual.UiScale);
        Assert.Equal(1280, actual.LootAnchorX);
        Assert.Equal(720, actual.LootAnchorY);
    }

    [Theory]
    [InlineData("<GameOptionGlobal/><GameOptionGlobal/>")]
    [InlineData("<GameOptionGlobal><Resolution Width='3840'/><Resolution Width='1920'/></GameOptionGlobal>")]
    [InlineData("<GameOptionGlobal><UiScale Value='1.49'/><UiScale Value='1.00'/></GameOptionGlobal>")]
    [InlineData("<Resolution Width='3840'/><Resolution Width='1920'/>")]
    [InlineData("<UiScale Value='1.49'/><UiScale Value='1.00'/>")]
    [InlineData("<Resolution Width='3840'/><GameOptionGlobal><Resolution Width='1920'/></GameOptionGlobal>")]
    [InlineData("<UiScale Value='1.49'/><GameOptionGlobal><UiScale Value='1.00'/></GameOptionGlobal>")]
    [InlineData("<GameOptionGlobal><UiScale/><UiScale Value='1.00'/></GameOptionGlobal>")]
    public void AmbiguousActiveDisplayFieldsAreRejectedRatherThanSelectedByOrder(string active)
    {
        File.WriteAllText(variables, ActiveLoot + active + SavedDisplay);

        var reader = new CompanionCalibrationReader();
        Assert.Throws<InvalidDataException>(() => reader.Read(root));
        Assert.Throws<InvalidDataException>(() => reader.Read(root, variables));
        Assert.False(Assert.Single(reader.ScanCandidates(root)).IsValid);
    }

    [Theory]
    [InlineData("<Resolution Width='3840' Height='2160'/><GameOptionGlobal><UiScale Value='1.49'/></GameOptionGlobal>")]
    [InlineData("<UiScale Value='1.49'/><GameOptionGlobal><Resolution Width='3840' Height='2160'/></GameOptionGlobal>")]
    public void UnambiguousGlobalAndLegacyFieldsRemainCompatible(string active)
    {
        File.WriteAllText(variables, ActiveLoot + SavedDisplay + active);

        AssertActiveDisplay(new CompanionCalibrationReader().Read(root));
    }

    [Fact]
    public void MissingActiveAttributesStillUseGameOptionFallback()
    {
        File.WriteAllText(variables, ActiveLoot + SavedDisplay +
            "<GameOptionGlobal><Resolution Width='3840'/><UiScale/></GameOptionGlobal>");

        var actual = new CompanionCalibrationReader().Read(root);

        Assert.Equal(3840, actual.ScreenWidth);
        Assert.Equal(1440, actual.ScreenHeight);
        Assert.Equal(1f, actual.UiScale);
    }

    private static void AssertActiveDisplay(CompanionCalibration actual)
    {
        Assert.Equal(3840, actual.ScreenWidth);
        Assert.Equal(2160, actual.ScreenHeight);
        Assert.Equal(1.49f, actual.UiScale);
        Assert.Equal(1920, actual.LootAnchorX);
        Assert.Equal(1080, actual.LootAnchorY);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
