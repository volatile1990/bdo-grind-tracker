using BdoGrindTracker.App.Character;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionCharacterClassDetectorTests
{
    [Fact]
    public void AccountSelectionUsesSavedConfigurationInsteadOfDirectoryTimestamp()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "", 0, accessDays: -1);
        fixture.Create("111", "preset", 0, accessDays: -1);
        fixture.Create("111", "preset/current", 7366, accessDays: -1);
        fixture.SetProfileWrite("111", -10);
        fixture.Create("222", "", 0, accessDays: -5);
        fixture.Create("222", "preset", 0, accessDays: -5);
        fixture.Create("222", "preset/old", 1768, accessDays: -5);
        fixture.SetProfileWrite("222", 0);

        Assert.Equal("maegu-awakening", new CompanionCharacterClassDetector().Detect(fixture.Root).Class?.Id);
    }

    [Fact]
    public void EmptyNewerCacheDoesNotHideAnAccountWithASavedConfiguration()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "", 0, accessDays: -1);
        fixture.Create("111", "preset", 7366, accessDays: -1);
        fixture.SetProfileWrite("111", -10);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "UserCache", "222"));
        fixture.SetProfileWrite("222", 0);

        Assert.Equal("maegu-awakening", new CompanionCharacterClassDetector().Detect(fixture.Root).Class?.Id);
    }

    [Fact]
    public void SelectedAccountWithoutCharacterEvidenceDoesNotBorrowAnOlderAccountClass()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "", 0, accessDays: -1);
        fixture.SetProfileWrite("111", -10);
        fixture.Create("222", "", 0, accessDays: -5);
        fixture.Create("222", "preset", 1768, accessDays: -5);
        fixture.SetProfileWrite("222", 0);

        Assert.Equal(CharacterClassDetectionStatus.Unavailable,
            new CompanionCharacterClassDetector().Detect(fixture.Root).Status);
    }

    [Theory]
    [InlineData(1768u, (int)CharacterClassDetectionStatus.Ambiguous)]
    [InlineData(7366u, (int)CharacterClassDetectionStatus.Detected)]
    [InlineData(uint.MaxValue, (int)CharacterClassDetectionStatus.Unknown)]
    public void EquallyRecentAccountConfigurationsRequireMatchingClassEvidence(uint secondSkill, int expectedStatus)
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "", 0, accessDays: -1);
        fixture.Create("111", "preset", 7366, accessDays: -2);
        fixture.SetProfileWrite("111", -10);
        fixture.Create("222", "", 0, accessDays: -1);
        fixture.Create("222", "preset", secondSkill, accessDays: -3);
        fixture.SetProfileWrite("222", 0);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal((CharacterClassDetectionStatus)expectedStatus, result.Status);
        Assert.Equal(expectedStatus == (int)CharacterClassDetectionStatus.Detected ? "maegu-awakening" : null,
            result.Class?.Id);
    }

    [Theory]
    [InlineData(7366u, (int)CharacterClassDetectionStatus.Detected)]
    [InlineData(uint.MaxValue, (int)CharacterClassDetectionStatus.Unknown)]
    public void LatestCharacterIsSelectedAcrossAllPresetDirectories(uint currentSkill, int expectedStatus)
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "a-old", 0, accessDays: 0);
        fixture.Create("111", "a-old/character", 1768, accessDays: -3);
        fixture.Create("111", "z-current", 0, accessDays: -1);
        fixture.Create("111", "z-current/character", currentSkill, accessDays: -1);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal((CharacterClassDetectionStatus)expectedStatus, result.Status);
        Assert.Equal(expectedStatus == (int)CharacterClassDetectionStatus.Detected ? "maegu-awakening" : null,
            result.Class?.Id);
    }

    [Fact]
    public void EmptySharedPresetCannotHideACharacterInAnotherPresetDirectory()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "a-shared", 0, accessDays: 0);
        fixture.Create("111", "z-current", 0, accessDays: -2);
        fixture.Create("111", "z-current/character", 7366, accessDays: -2);

        Assert.Equal("maegu-awakening", new CompanionCharacterClassDetector().Detect(fixture.Root).Class?.Id);
    }

    [Fact]
    public void EquallyRecentCharactersInDifferentPresetsRemainAmbiguous()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "a-first", 0, accessDays: 0);
        fixture.Create("111", "a-first/character", 1768, accessDays: -1);
        fixture.Create("111", "z-second", 0, accessDays: -2);
        fixture.Create("111", "z-second/character", 7366, accessDays: -1);

        Assert.Equal(CharacterClassDetectionStatus.Ambiguous,
            new CompanionCharacterClassDetector().Detect(fixture.Root).Status);
    }

    [Fact]
    public void EquallyRecentAccountWithoutCharacterEvidencePreventsDetection()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "", 0, accessDays: -1);
        fixture.Create("111", "preset", 7366, accessDays: -2);
        fixture.Create("222", "", 0, accessDays: -1);

        Assert.Equal(CharacterClassDetectionStatus.Unavailable,
            new CompanionCharacterClassDetector().Detect(fixture.Root).Status);
    }

    [Fact]
    public void LatestSharedPresetIsUsedWhenNoCharacterFilesExist()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "a-old", 1768, accessDays: -3);
        fixture.Create("111", "z-current", 7366, accessDays: -1);

        Assert.Equal("maegu-awakening", new CompanionCharacterClassDetector().Detect(fixture.Root).Class?.Id);
    }

    [Theory]
    [InlineData(16, (int)CharacterClassDetectionStatus.Detected)]
    [InlineData(17, (int)CharacterClassDetectionStatus.Unavailable)]
    public void TiedCharacterLimitAppliesAcrossAccounts(int characterCount, int expectedStatus)
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "", 0, accessDays: -1);
        fixture.Create("111", "preset", 0, accessDays: -1);
        fixture.Create("222", "", 0, accessDays: -1);
        fixture.Create("222", "preset", 0, accessDays: -1);
        for (var index = 0; index < characterCount; index++)
            fixture.Create(index % 2 == 0 ? "111" : "222", $"preset/character-{index}", 7366, accessDays: -2);

        Assert.Equal((CharacterClassDetectionStatus)expectedStatus,
            new CompanionCharacterClassDetector().Detect(fixture.Root).Status);
    }
}
