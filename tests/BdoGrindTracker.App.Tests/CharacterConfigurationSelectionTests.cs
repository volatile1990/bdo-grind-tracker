using BdoGrindTracker.App.Character;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionCharacterClassDetectorTests
{
    [Fact]
    public void ReadingAnOldCharacterCannotMakeItTheActiveSavedCharacter()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "preset", 0, accessDays: 0);
        var old = fixture.Create("111", "preset/older", 1768, accessDays: -3);
        fixture.Create("111", "preset/current", 7366, accessDays: -1);
        File.SetLastAccessTimeUtc(old, DateTime.UtcNow);
        var detector = new CompanionCharacterClassDetector();

        Assert.Equal("maegu-awakening", detector.Detect(fixture.Root).Class?.Id);
        File.ReadAllText(old);
        File.SetLastAccessTimeUtc(old, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal("maegu-awakening", detector.Detect(fixture.Root).Class?.Id);
    }

    [Fact]
    public void SharedPresetWithoutSkillsCannotHideARealCharacter()
    {
        using var fixture = new CharacterConfigurationFixture();
        var shared = fixture.Create("111", "preset", 0, accessDays: 0);
        File.WriteAllText(shared, "<QuickSlotData Version='1'/><SkillCoolTimeSlot Version='1'/>");
        fixture.Create("111", "preset/current", 7366, accessDays: -2);

        Assert.Equal("maegu-awakening", new CompanionCharacterClassDetector().Detect(fixture.Root).Class?.Id);
    }

    [Fact]
    public void NewestUnknownCharacterDoesNotBorrowAnOlderRecognizedClass()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "preset", 0, accessDays: 0);
        fixture.Create("111", "preset/old", 1768, accessDays: -3);
        fixture.Create("111", "preset/new", uint.MaxValue, accessDays: -1);

        Assert.Equal(CharacterClassDetectionStatus.Unknown,
            new CompanionCharacterClassDetector().Detect(fixture.Root).Status);
    }

    [Fact]
    public void NewestIncompleteFileDoesNotBorrowAnOlderRecognizedClass()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "preset", 0, accessDays: 0);
        fixture.Create("111", "preset/old", 1768, accessDays: -3);
        var current = fixture.Create("111", "preset/new", 7366, accessDays: -1);
        File.WriteAllText(current, "<QuickSlotSkillData");

        Assert.Equal(CharacterClassDetectionStatus.Unavailable,
            new CompanionCharacterClassDetector().Detect(fixture.Root).Status);
    }

    [Theory]
    [InlineData(1768u, (int)CharacterClassDetectionStatus.Ambiguous)]
    [InlineData(7366u, (int)CharacterClassDetectionStatus.Detected)]
    [InlineData(uint.MaxValue, (int)CharacterClassDetectionStatus.Unknown)]
    public void TiedSaveTimesRequireMatchingClassEvidence(uint secondSkill, int expectedValue)
    {
        var expected = (CharacterClassDetectionStatus)expectedValue;
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "preset", 0, accessDays: 0);
        fixture.Create("111", "preset/one", 7366, accessDays: -1);
        fixture.Create("111", "preset/two", secondSkill, accessDays: -1);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);
        Assert.Equal(expected, result.Status);
        if (expected != CharacterClassDetectionStatus.Detected) Assert.Null(result.Class);
    }

    [Fact]
    public void SavingAnotherCharacterIsVisibleWithoutRecreatingTheDetector()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "preset", 0, accessDays: 0);
        fixture.Create("111", "preset/one", 1768, accessDays: -1);
        var second = fixture.Create("111", "preset/two", 7366, accessDays: -2);
        var detector = new CompanionCharacterClassDetector();
        Assert.Equal("warrior-awakening", detector.Detect(fixture.Root).Class?.Id);

        File.SetLastWriteTimeUtc(second, fixture.Timestamp);

        Assert.Equal("maegu-awakening", detector.Detect(fixture.Root).Class?.Id);
    }
}
