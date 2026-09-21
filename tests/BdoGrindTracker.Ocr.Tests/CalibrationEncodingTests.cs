using System.Text;
using System.Xml;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class CalibrationEncodingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "grindcrest-encoding-" + Guid.NewGuid().ToString("N"));
    private readonly string variables;
    private const string CalibrationXml = """
        <UIData Version="2">
          <UIData Index="159" IsShow="true" RelativePosX="0.7258333564" RelativePosY="0.8867505789"/>
          <UIData Index="161" IsShow="true" RelativePosX="0.4916666746" RelativePosY="0.1732050329"/>
        </UIData>
        <GameOptionGlobal Version="2"><Resolution Width="1920" Height="1080"/><UiScale Value="0.8"/></GameOptionGlobal>
        """;
    private const string MemoXml = "<Memo><MemoInfo Content='Grün, Rätsel, „Notiz“ &amp; 5 €'/></Memo>";

    public CalibrationEncodingTests()
    {
        var profile = Directory.CreateDirectory(Path.Combine(root, "UserCache", "42")).FullName;
        variables = Path.Combine(profile, "gameVariable.xml");
        File.WriteAllText(Path.Combine(root, "GameOption.txt"),
            "width = 2560\nheight = 1440\nuiScale =  1.00\nUIFontType = 2\nwindowed = 1\n");
    }

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
    public void NonAsciiNotesDoNotPreventCalibrationOrCandidateDiscovery(string encodingName)
    {
        var encoding = EncodingFor(encodingName);
        File.WriteAllText(variables, CalibrationXml + MemoXml, encoding);
        var original = File.ReadAllBytes(variables);
        var written = File.GetLastWriteTimeUtc(variables);
        var reader = new CompanionCalibrationReader();

        AssertCalibration(reader.Read(root));
        AssertCalibration(reader.Read(root, variables));
        var candidate = Assert.Single(reader.ScanCandidates(root));
        Assert.True(candidate.IsValid, candidate.ValidationError);
        AssertCalibration(candidate.Calibration!);
        Assert.Equal(original, File.ReadAllBytes(variables));
        Assert.Equal(written, File.GetLastWriteTimeUtc(variables));
    }

    [Theory]
    [InlineData("<Memo Content='Grün & invalid'/>")]
    [InlineData("<Memo Content='Grün' Content='duplicate'/>")]
    [InlineData("<Memo Content='Grün'>")]
    [InlineData("<Memo Content='Grün\u0001'/>")]
    [InlineData("<!DOCTYPE Memo [<!ENTITY note 'Grün'>]><Memo>&note;</Memo>")]
    public void LegacyEncodingDoesNotRelaxXmlValidation(string invalidXml)
    {
        File.WriteAllText(variables, CalibrationXml + invalidXml, EncodingFor("windows-1252"));

        Assert.Throws<XmlException>(() => new CompanionCalibrationReader().Read(root));
        Assert.False(Assert.Single(new CompanionCalibrationReader().ScanCandidates(root)).IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitUtf8EncodingDoesNotFallBackToLegacy(bool withBom)
    {
        var text = withBom ? CalibrationXml + MemoXml :
            "<?xml version='1.0' encoding='utf-8'?>" + CalibrationXml + MemoXml;
        var bytes = EncodingFor("windows-1252").GetBytes(text);
        File.WriteAllBytes(variables, withBom ? [0xef, 0xbb, 0xbf, .. bytes] : bytes);

        Assert.Throws<XmlException>(() => new CompanionCalibrationReader().Read(root));
    }

    [Fact]
    public void LegacyEncodingStillRejectsConflictingActiveLootEntries()
    {
        File.WriteAllText(variables, CalibrationXml + CalibrationXml + MemoXml, EncodingFor("windows-1252"));

        Assert.Throws<InvalidDataException>(() => new CompanionCalibrationReader().Read(root));
    }

    [Fact]
    public void StylesheetProcessingInstructionDoesNotCountAsAnEncodingDeclaration()
    {
        File.WriteAllText(variables, "<?xml-stylesheet type='text/xsl' href='unused.xsl'?>" +
            CalibrationXml + MemoXml, EncodingFor("windows-1252"));

        AssertCalibration(new CompanionCalibrationReader().Read(root));
    }

    private static void AssertCalibration(CompanionCalibration actual)
    {
        Assert.Equal(1920, actual.ScreenWidth);
        Assert.Equal(1080, actual.ScreenHeight);
        Assert.Equal(0.8f, actual.UiScale);
        Assert.Equal(1393, actual.LootAnchorX);
        Assert.Equal(957, actual.LootAnchorY);
        Assert.Equal(944, actual.RareLootAnchorX);
        Assert.Equal(187, actual.RareLootAnchorY);
        Assert.Equal(RareLootAnchorStatus.Active, actual.RareLootResolution!.Status);
    }

    private static Encoding EncodingFor(string name) => name switch
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

    public void Dispose() => Directory.Delete(root, recursive: true);
}
