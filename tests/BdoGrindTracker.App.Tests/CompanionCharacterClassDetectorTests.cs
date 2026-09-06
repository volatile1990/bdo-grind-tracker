using System.Security.Cryptography;
using System.Text;
using BdoGrindTracker.App.Character;

namespace BdoGrindTracker.App.Tests;

public sealed class CompanionCharacterClassDetectorTests
{
    public static IEnumerable<object[]> Profiles =>
        CompanionCharacterClassCatalog.Profiles.Select(value => new object[] { value.Class.Id });

    [Theory]
    [MemberData(nameof(Profiles))]
    public void AllFiftySixNativeClassProfilesAreRecognizedFromBothKindsOfSlot(string id)
    {
        var profile = CompanionCharacterClassCatalog.Profiles.Single(value => value.Class.Id == id);
        var xml = string.Join('\n', profile.SkillIds.Select((skill, index) =>
            index % 2 == 0
                ? $"<QuickSlotSkillData SkillNo=\"{skill}\" />"
                : $"<SkillCoolTimeSlot SkillNo=\"{skill}\" />"));

        var result = CompanionCharacterClassDetector.DetectFromGameVariableText(xml);

        Assert.Equal(CharacterClassDetectionStatus.Detected, result.Status);
        Assert.Equal(profile.Class, result.Class);
        Assert.Equal(profile.SkillIds.Count, result.MatchedSkillCount);
    }

    [Fact]
    public void AllDisplayNamesUseTheRequestedEnglishSpecializationLabels()
    {
        foreach (var characterClass in CompanionCharacterClassCatalog.Classes)
        {
            var expected = characterClass.Specialization switch
            {
                CharacterSpecialization.Awakening => $"{characterClass.Name} · Awakening",
                CharacterSpecialization.Succession => $"{characterClass.Name} · Succession",
                _ => characterClass.Name,
            };

            Assert.Equal(expected, characterClass.DisplayName);
            Assert.Equal(expected, characterClass.ToString());
        }
    }

    [Theory]
    [InlineData("maegu-awakening", "Maegu · Awakening")]
    [InlineData("corsair-succession", "Corsair · Succession")]
    public void DetectionFromSyntheticCharacterFilesKeepsEnglishSpecializationLabels(string id, string expectedLabel)
    {
        using var fixture = new CharacterConfigurationFixture();
        var profile = CompanionCharacterClassCatalog.Profiles.Single(value => value.Class.Id == id);
        var exclusiveSkill = profile.SkillIds.First(skill =>
            CompanionCharacterClassCatalog.Profiles.Count(other => other.SkillIds.Contains(skill)) == 1);
        fixture.Create("111", "preset", exclusiveSkill, accessDays: -1);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal(id, result.Class?.Id);
        Assert.Equal(expectedLabel, result.Class?.DisplayName);
        Assert.Equal(expectedLabel, result.Class?.ToString());
    }

    [Fact]
    public void EmbeddedSkillTableMatchesFactualBinaryExtraction()
    {
        Assert.Equal(56, CompanionCharacterClassCatalog.Profiles.Count);
        Assert.Equal(2743, CompanionCharacterClassCatalog.Profiles.Sum(value => value.SkillIds.Count));
        var data = string.Concat(CompanionCharacterClassCatalog.Profiles.Select(profile =>
        {
            var suffix = profile.Class.Specialization switch
            {
                CharacterSpecialization.Awakening => " (A)",
                CharacterSpecialization.Succession => " (S)",
                _ => "",
            };
            return profile.Class.Name + suffix + ":" +
                   string.Join(',', profile.SkillIds.Order()) + "\n";
        }));
        Assert.Equal("67B7D6C22AE3B41D74626E4CA76ABAA60E993C5D40E15E8A432EB4DB9EBFC903",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data))));
        Assert.Equal(57, CompanionCharacterClassCatalog.Classes.Select(value => value.Id).Distinct().Count());
    }

    [Fact]
    public void RepeatedSlotsDoNotOutvoteDistinctSkills()
    {
        var xml = string.Concat(Enumerable.Repeat("<QuickSlotSkillData SkillNo=\"1768\"/>", 20)) +
            "<SkillCoolTimeSlot SkillNo=\"7366\"/><SkillCoolTimeSlot SkillNo=\"7365\"/>";

        var result = CompanionCharacterClassDetector.DetectFromGameVariableText(xml);

        Assert.Equal("maegu-awakening", result.Class?.Id);
        Assert.Equal(2, result.MatchedSkillCount);
    }

    [Fact]
    public void SingleClassSkillIsEnoughJustAsInCompanion()
    {
        var result = CompanionCharacterClassDetector.DetectFromGameVariableText(
            "<SkillCoolTimeSlot SkillNo=\"1768\" />");

        Assert.Equal("warrior-awakening", result.Class?.Id);
        Assert.Equal(1, result.MatchedSkillCount);
    }

    [Fact]
    public void EqualClassVotesRemainAmbiguousRatherThanRandom()
    {
        var result = CompanionCharacterClassDetector.DetectFromGameVariableText(
            "<QuickSlotSkillData SkillNo=\"1768\"/><QuickSlotSkillData SkillNo=\"7366\"/>");

        Assert.Equal(CharacterClassDetectionStatus.Ambiguous, result.Status);
        Assert.Null(result.Class);
        Assert.Equal(1, result.MatchedSkillCount);
    }

    [Fact]
    public void SharedWizardWitchSkillsDoNotGuessAClass()
    {
        var wizard = CompanionCharacterClassCatalog.Profiles.Single(value => value.Class.Id == "wizard-succession");
        var witch = CompanionCharacterClassCatalog.Profiles.Single(value => value.Class.Id == "witch-succession");
        var sharedSkill = wizard.SkillIds.Intersect(witch.SkillIds).First();

        var result = CompanionCharacterClassDetector.DetectFromGameVariableText(
            $"<QuickSlotSkillData SkillNo=\"{sharedSkill}\"/>");

        Assert.Equal(CharacterClassDetectionStatus.Ambiguous, result.Status);
        Assert.Null(result.Class);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<Unrelated SkillNo=\"1768\"/>")]
    [InlineData("<QuickSlotSkillData SkillNo=\"0\"/>")]
    [InlineData("<QuickSlotSkillData SkillNo=\"4294967295\"/>")]
    [InlineData("<QuickSlotSkillData SkillNo=\"4294967296\"/>")]
    [InlineData("<QuickSlotSkillData SkillNo=\"-1768\"/>")]
    [InlineData("<QuickSlotSkillData SkillNo=\" 1768\"/>")]
    [InlineData("<QuickSlotSkillData SkillNo=\"1768 \"/>")]
    [InlineData("<QuickSlotSkillData skillno=\"1768\"/>")]
    [InlineData("<quickslotskilldata SkillNo=\"1768\"/>")]
    [InlineData("<QuickSlotSkillData Value=\"1768\"/>")]
    [InlineData("<QuickSlotSkillData SkillNo=\"+\"/>")]
    [InlineData("<!-- <QuickSlotSkillData SkillNo=\"1768\"/> -->")]
    public void InvalidOrIrrelevantSkillValuesAreIgnored(string xml)
    {
        var result = CompanionCharacterClassDetector.DetectFromGameVariableText(xml);
        Assert.Equal(CharacterClassDetectionStatus.Unknown, result.Status);
        Assert.Null(result.Class);
    }

    [Fact]
    public void PositiveSignAndNestedXmlAreAccepted()
    {
        var result = CompanionCharacterClassDetector.DetectFromGameVariableText(
            "<Settings><Skills><QuickSlotSkillData SkillNo=\"+1768\"/></Skills></Settings>");
        Assert.Equal("warrior-awakening", result.Class?.Id);
    }

    [Theory]
    [InlineData("<QuickSlotSkillData SkillNo=\"1768\">")]
    [InlineData("<!DOCTYPE x [<!ENTITY test SYSTEM 'file:///C:/should-not-be-opened'>]><x>&test;</x>")]
    public void InvalidOrExternalEntityXmlCannotProduceAClass(string xml)
    {
        var result = CompanionCharacterClassDetector.DetectFromGameVariableText(xml);
        Assert.Equal(CharacterClassDetectionStatus.Unavailable, result.Status);
        Assert.Null(result.Class);
    }

    [Fact]
    public void OversizedXmlIsBounded()
    {
        var result = CompanionCharacterClassDetector.DetectFromGameVariableText(
            "<!--" + new string('x', 4 * 1024 * 1024) + "-->");
        Assert.Equal(CharacterClassDetectionStatus.Unavailable, result.Status);
    }

    [Fact]
    public void MissingConfigurationRemainsUnavailable()
    {
        using var fixture = new CharacterConfigurationFixture();
        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);
        Assert.Equal(CharacterClassDetectionStatus.Unavailable, result.Status);
    }

    [Fact]
    public void SelectsNewestNonzeroNumericProfileAndLatestAccessCharacterFile()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "preset", 1768, accessDays: -3);
        fixture.Create("222", "preset", 1768, accessDays: -3);
        var expected = fixture.Create("222", "preset/character", 7366, accessDays: -1);
        fixture.Create("0", "preset", 1768, accessDays: 0);
        fixture.Create("nonnumeric", "preset", 1768, accessDays: 0);
        fixture.SetProfileWrite("111", -3);
        fixture.SetProfileWrite("222", -1);
        fixture.SetProfileWrite("0", 0);
        fixture.SetProfileWrite("nonnumeric", 0);
        var before = File.ReadAllBytes(expected);
        File.SetLastAccessTimeUtc(expected, fixture.Timestamp.AddDays(-1));

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal("maegu-awakening", result.Class?.Id);
        Assert.Equal(before, File.ReadAllBytes(expected));
        Assert.DoesNotContain(fixture.Root, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotFallBackToAnotherAccountOrRecursivelyScanOtherFiles()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "preset", 1768, accessDays: -3);
        // Newer profile has no directly available preset file, just unrelated
        // deeper content. It must not borrow the older profile's class.
        fixture.Create("222", "unrelated/deep/child", 7366, accessDays: -1);
        fixture.SetProfileWrite("111", -3);
        fixture.SetProfileWrite("222", -1);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal(CharacterClassDetectionStatus.Unavailable, result.Status);
    }

    [Fact]
    public void DoesNotRequireGameOptionOrLootPanelCalibration()
    {
        using var fixture = new CharacterConfigurationFixture();
        fixture.Create("111", "preset", 1768, accessDays: -1);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal("warrior-awakening", result.Class?.Id);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "GameOption.txt")));
    }

    [Fact]
    public void ManualChoiceLookupAcceptsOnlyCatalogIds()
    {
        Assert.Equal("Wukong", CompanionCharacterClassCatalog.FindById("wukong")?.DisplayName);
        Assert.Equal("Agent", CompanionCharacterClassCatalog.FindById("agent")?.DisplayName);
        Assert.Null(CompanionCharacterClassCatalog.FindById(null));
        Assert.Null(CompanionCharacterClassCatalog.FindById("unknown"));
    }

    private sealed class CharacterConfigurationFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests",
            "class-detection-" + Guid.NewGuid().ToString("N"));
        public DateTime Timestamp { get; } = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

        public CharacterConfigurationFixture() => Directory.CreateDirectory(Root);

        public string Create(string profile, string child, uint skillId, int accessDays)
        {
            var directory = Path.Combine(Root, "UserCache", profile, child.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "gameVariable.xml");
            File.WriteAllText(path, $"<QuickSlotSkillData SkillNo=\"{skillId}\"/>");
            File.SetLastAccessTimeUtc(path, Timestamp.AddDays(accessDays));
            return path;
        }

        public void SetProfileWrite(string profile, int days) =>
            Directory.SetLastWriteTimeUtc(Path.Combine(Root, "UserCache", profile), Timestamp.AddDays(days));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
