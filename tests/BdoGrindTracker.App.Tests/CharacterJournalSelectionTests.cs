using BdoGrindTracker.App.Character;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionCharacterClassDetectorTests
{
    [Fact]
    public void LoginJournalSelectsCurrentCharacterOverRecentlySavedPreviousCharacter()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 7366, accessDays: -3);
        var previous = fixture.Create("111", "10/1002", 1768, accessDays: -2);
        File.SetLastWriteTimeUtc(previous, fixture.Timestamp.AddSeconds(-17));
        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "10", "1001_20269.bcf", fixture.Timestamp);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        Assert.Equal(CharacterClassDetectionStatus.Detected, result.Status);
        Assert.Equal("maegu-awakening", result.Class?.Id);
    }

    [Fact]
    public void OlderJournalDoesNotOverrideMoreRecentlySavedCharacter()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 7366, accessDays: -3);
        fixture.Create("111", "10/1002", 1768, accessDays: -1);
        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "10", "1001_20269.bcf", fixture.Timestamp.AddDays(-2));

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        Assert.Equal("warrior-awakening", result.Class?.Id);
    }

    [Fact]
    public void CurrentJournalWithUnknownSkillsDoesNotBorrowPreviousCharactersClass()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", uint.MaxValue, accessDays: -3);
        fixture.Create("111", "10/1002", 1768, accessDays: -1);
        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "10", "1001_20269.bcf", fixture.Timestamp);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        Assert.Equal(CharacterClassDetectionStatus.Unknown, result.Status);
        Assert.Null(result.Class);
    }

    [Fact]
    public void CurrentJournalWithoutCharacterXmlDoesNotBorrowPreviousCharactersClass()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1002", 1768, accessDays: -1);
        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "10", "1001_20269.bcf", fixture.Timestamp);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        Assert.Equal(CharacterClassDetectionStatus.Unavailable, result.Status);
        Assert.Null(result.Class);
    }

    [Theory]
    [InlineData(1768u, (int)CharacterClassDetectionStatus.Ambiguous)]
    [InlineData(7366u, (int)CharacterClassDetectionStatus.Detected)]
    [InlineData(uint.MaxValue, (int)CharacterClassDetectionStatus.Unknown)]
    public void TiedJournalAndXmlActivityRequireMatchingClassEvidence(uint otherSkill, int expectedStatus)
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 7366, accessDays: -3);
        fixture.Create("111", "10/1002", otherSkill, accessDays: -1);
        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "10", "1001_20269.bcf", fixture.Timestamp.AddDays(-1));

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        Assert.Equal((CharacterClassDetectionStatus)expectedStatus, result.Status);
        Assert.Equal(expectedStatus == (int)CharacterClassDetectionStatus.Detected ? "maegu-awakening" : null,
            result.Class?.Id);
    }

    [Fact]
    public void EquallyRecentJournalsForDifferentCharactersRemainAmbiguous()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 7366, accessDays: -3);
        fixture.Create("111", "10/1002", 1768, accessDays: -2);
        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "10", "1001_20269.bcf", fixture.Timestamp);
        CreateCharacterJournal(fixture, installation, "10", "1002_20269.bcf", fixture.Timestamp);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        Assert.Equal(CharacterClassDetectionStatus.Ambiguous, result.Status);
        Assert.Null(result.Class);
    }

    [Fact]
    public void MultipleMonthsForOneCharacterCountAsOneConfiguration()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 7366, accessDays: -3);
        fixture.Create("111", "10/1002", 1768, accessDays: -1);
        var installation = JournalInstallation(fixture);
        for (var year = 2025; year <= 2026; year++)
        for (var month = 1; month <= 12; month++)
            CreateCharacterJournal(fixture, installation, "10", $"1001_{year}{month}.bcf", fixture.Timestamp);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation, installation]);

        Assert.Equal(CharacterClassDetectionStatus.Detected, result.Status);
        Assert.Equal("maegu-awakening", result.Class?.Id);
    }

    [Fact]
    public void JournalLookupStaysInsideSelectedAccountAndExactWorld()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1002", 1768, accessDays: -1);
        fixture.Create("111", "20", 0, accessDays: -2);
        fixture.Create("111", "20/1001", 7366, accessDays: -2);
        fixture.Create("222", "", 0, accessDays: -4);
        fixture.Create("222", "10", 0, accessDays: -4);
        fixture.Create("222", "10/1001", 7366, accessDays: -4);
        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "10", "1001_20269.bcf", fixture.Timestamp);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        // Neither the other world nor the older account proves the active character.
        Assert.Equal(CharacterClassDetectionStatus.Unavailable, result.Status);
        Assert.Null(result.Class);
    }

    [Fact]
    public void JournalForWorldOutsideSelectedProfileDoesNotOverrideItsSavedCharacter()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 1768, accessDays: -1);
        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "99", "1001_20269.bcf", fixture.Timestamp);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        Assert.Equal("warrior-awakening", result.Class?.Id);
    }

    [Theory]
    [InlineData("1001.bcf")]
    [InlineData("1001_2026.bcf")]
    [InlineData("1001_20260.bcf")]
    [InlineData("1001_202613.bcf")]
    [InlineData("1001_notes.bcf")]
    [InlineData("1001_20269.bcf.bak")]
    [InlineData("1001_20269.txt")]
    [InlineData("character_20269.bcf")]
    public void InvalidJournalNamesCannotPromoteAnOlderCharacter(string filename)
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 7366, accessDays: -3);
        fixture.Create("111", "10/1002", 1768, accessDays: -1);
        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "10", filename, fixture.Timestamp);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        Assert.Equal("warrior-awakening", result.Class?.Id);
    }

    [Fact]
    public void MissingInstallationFallsBackToSavedCharacterAndDoesNotHideAnotherInstallation()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 7366, accessDays: -3);
        fixture.Create("111", "10/1002", 1768, accessDays: -1);
        var missing = Path.Combine(fixture.Root, "missing-installation");
        var detector = new CompanionCharacterClassDetector();

        Assert.Equal("warrior-awakening", detector.Detect(fixture.Root, [missing]).Class?.Id);

        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "10", "1001_20269.bcf", fixture.Timestamp);
        Assert.Equal("maegu-awakening", detector.Detect(fixture.Root, [missing, installation]).Class?.Id);
    }

    [Fact]
    public void JournalSupportsCharacterIdentifiersLargerThanUint32()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        const string character = "90071992547409931";
        fixture.Create("111", $"10/{character}", 7366, accessDays: -3);
        fixture.Create("111", "10/1002", 1768, accessDays: -1);
        var installation = JournalInstallation(fixture);
        CreateCharacterJournal(fixture, installation, "10", $"{character}_20269.bcf", fixture.Timestamp);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        Assert.Equal("maegu-awakening", result.Class?.Id);
    }

    [Fact]
    public void EquallyRecentConflictingInstallationsRemainAmbiguous()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 7366, accessDays: -3);
        fixture.Create("111", "10/1002", 1768, accessDays: -1);
        var first = Path.Combine(fixture.Root, "first-installation");
        var second = Path.Combine(fixture.Root, "second-installation");
        CreateCharacterJournal(fixture, first, "10", "1001_20269.bcf", fixture.Timestamp);
        CreateCharacterJournal(fixture, second, "10", "1002_20269.bcf", fixture.Timestamp);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [first, second]);

        Assert.Equal(CharacterClassDetectionStatus.Ambiguous, result.Status);
        Assert.Null(result.Class);
    }

    [Fact]
    public void MostRecentInstallationActivityWinsRegardlessOfEnumerationOrder()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 7366, accessDays: -3);
        fixture.Create("111", "10/1002", 1768, accessDays: -2);
        var first = Path.Combine(fixture.Root, "first-installation");
        var second = Path.Combine(fixture.Root, "second-installation");
        CreateCharacterJournal(fixture, first, "10", "1001_20269.bcf", fixture.Timestamp);
        CreateCharacterJournal(fixture, second, "10", "1002_20269.bcf", fixture.Timestamp.AddDays(-1));
        var detector = new CompanionCharacterClassDetector();

        Assert.Equal("maegu-awakening", detector.Detect(fixture.Root, [first, second]).Class?.Id);
        Assert.Equal("maegu-awakening", detector.Detect(fixture.Root, [second, first]).Class?.Id);
    }

    [Fact]
    public void JournalContentsAreNotRequiredForCharacterSelection()
    {
        using var fixture = new CharacterConfigurationFixture();
        CreateJournalProfile(fixture);
        fixture.Create("111", "10/1001", 7366, accessDays: -3);
        fixture.Create("111", "10/1002", 1768, accessDays: -1);
        var installation = JournalInstallation(fixture);
        var journal = CreateCharacterJournal(fixture, installation, "10", "1001_20269.bcf", fixture.Timestamp);
        using var exclusive = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root, [installation]);

        Assert.Equal("maegu-awakening", result.Class?.Id);
        Assert.DoesNotContain(fixture.Root, result.ToString(), StringComparison.Ordinal);
    }

    private static void CreateJournalProfile(CharacterConfigurationFixture fixture)
    {
        fixture.Create("111", "", 0, accessDays: -1);
        fixture.Create("111", "10", 0, accessDays: -1);
    }

    private static string JournalInstallation(CharacterConfigurationFixture fixture) =>
        Path.Combine(fixture.Root, "installation");

    private static string CreateCharacterJournal(CharacterConfigurationFixture fixture, string installation,
        string world, string filename, DateTime written)
    {
        var directory = Path.Combine(installation, "Cache", world, "MyJournal");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, filename);
        File.WriteAllBytes(path, [0xff, 0x00, 0x80, 0x42]);
        File.SetLastWriteTimeUtc(path, written);
        File.SetLastAccessTimeUtc(path, fixture.Timestamp.AddDays(-10));
        return path;
    }
}
