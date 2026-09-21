using System.Text;
using BdoGrindTracker.App.Character;

namespace BdoGrindTracker.App.Tests;

public sealed partial class CompanionCharacterClassDetectorTests
{
    private const string EncodedCharacterXml = """
        <Memo><MemoInfo Content='Grün, Rätsel, „Notiz“ &amp; 5 €'/></Memo>
        <QuickSlotSkillData><QuickSlotSkillData SkillNo='1768'/></QuickSlotSkillData>
        <Memo Content='Weitere Grüße'/>
        """;

    [Theory]
    [InlineData("windows-1252")]
    [InlineData("utf-8")]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16")]
    [InlineData("utf-16be")]
    [InlineData("utf-16-no-bom")]
    [InlineData("utf-16be-no-bom")]
    [InlineData("utf-32")]
    [InlineData("utf-32be")]
    [InlineData("utf-32-no-bom")]
    [InlineData("utf-32be-no-bom")]
    public void CharacterDetectionAcceptsNonAsciiNotesInSupportedEncodings(string encodingName)
    {
        using var fixture = new CharacterConfigurationFixture();
        var path = fixture.Create("111", "preset", 1768, accessDays: -1);
        File.WriteAllText(path, EncodedCharacterXml, CharacterEncodingFor(encodingName));
        var original = File.ReadAllBytes(path);
        var lastWrite = File.GetLastWriteTimeUtc(path);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal(CharacterClassDetectionStatus.Detected, result.Status);
        Assert.Equal("warrior-awakening", result.Class?.Id);
        Assert.Equal(1, result.MatchedSkillCount);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CharacterDetectionDoesNotOverrideExplicitUtf8WithLegacyEncoding(bool withBom)
    {
        using var fixture = new CharacterConfigurationFixture();
        var path = fixture.Create("111", "preset", 1768, accessDays: -1);
        var text = withBom ? EncodedCharacterXml :
            "<?xml version='1.0' encoding='utf-8'?>" + EncodedCharacterXml;
        var bytes = CharacterEncodingFor("windows-1252").GetBytes(text);
        File.WriteAllBytes(path, withBom ? [0xef, 0xbb, 0xbf, .. bytes] : bytes);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal(CharacterClassDetectionStatus.Unavailable, result.Status);
        Assert.Null(result.Class);
    }

    [Theory]
    [InlineData("<Memo Content='Grün & invalid'/>")]
    [InlineData("<Memo Content='Grün' Content='duplicate'/>")]
    [InlineData("<Memo Content='Grün'>")]
    [InlineData("<Memo Content='Grün\u0001'/>")]
    [InlineData("<!DOCTYPE Memo [<!ENTITY note SYSTEM 'file:///C:/not-to-be-opened'>]><Memo>&note;</Memo>")]
    public void LegacyCharacterXmlStillRequiresValidStructureAndRejectsDtd(string invalidXml)
    {
        using var fixture = new CharacterConfigurationFixture();
        var path = fixture.Create("111", "preset", 1768, accessDays: -1);
        File.WriteAllText(path, EncodedCharacterXml + invalidXml, CharacterEncodingFor("windows-1252"));

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal(CharacterClassDetectionStatus.Unavailable, result.Status);
        Assert.Null(result.Class);
    }

    [Fact]
    public void LegacyCharacterXmlRemainsBoundedAfterDecoding()
    {
        using var fixture = new CharacterConfigurationFixture();
        var path = fixture.Create("111", "preset", 1768, accessDays: -1);
        File.WriteAllText(path, EncodedCharacterXml + "<!--" + new string('ü', 4 * 1024 * 1024) + "-->",
            CharacterEncodingFor("windows-1252"));
        Assert.True(new FileInfo(path).Length < 8 * 1024 * 1024);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal(CharacterClassDetectionStatus.Unavailable, result.Status);
        Assert.Null(result.Class);
    }

    [Fact]
    public void CharacterXmlByteLimitRemainsEnforcedBeforeDecoding()
    {
        using var fixture = new CharacterConfigurationFixture();
        var path = fixture.Create("111", "preset", 1768, accessDays: -1);
        File.WriteAllText(path, EncodedCharacterXml + "<!--" + new string('x', 4 * 1024 * 1024) + "-->",
            Encoding.Unicode);
        Assert.True(new FileInfo(path).Length > 8 * 1024 * 1024);

        var result = new CompanionCharacterClassDetector().Detect(fixture.Root);

        Assert.Equal(CharacterClassDetectionStatus.Unavailable, result.Status);
        Assert.Null(result.Class);
    }

    [Fact]
    public void CharacterXmlStylesheetInstructionDoesNotPreventLegacyDecoding()
    {
        using var fixture = new CharacterConfigurationFixture();
        var path = fixture.Create("111", "preset", 1768, accessDays: -1);
        File.WriteAllText(path, "<?xml-stylesheet type='text/xsl' href='unused.xsl'?>" + EncodedCharacterXml,
            CharacterEncodingFor("windows-1252"));

        Assert.Equal("warrior-awakening", new CompanionCharacterClassDetector().Detect(fixture.Root).Class?.Id);
    }

    private static Encoding CharacterEncodingFor(string name) => name switch
    {
        "windows-1252" => CodePagesEncodingProvider.Instance.GetEncoding(1252)!,
        "utf-8" => new UTF8Encoding(false),
        "utf-8-bom" => new UTF8Encoding(true),
        "utf-16" => Encoding.Unicode,
        "utf-16be" => Encoding.BigEndianUnicode,
        "utf-16-no-bom" => new UnicodeEncoding(false, false),
        "utf-16be-no-bom" => new UnicodeEncoding(true, false),
        "utf-32" => Encoding.UTF32,
        "utf-32be" => new UTF32Encoding(true, true),
        "utf-32-no-bom" => new UTF32Encoding(false, false),
        "utf-32be-no-bom" => new UTF32Encoding(true, false),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
