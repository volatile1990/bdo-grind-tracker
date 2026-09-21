using System.Text;
using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed class AutomaticBuffHudConfigurationReaderTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ActualSavedCenterIncludesAllThreeRowsAndTheTwentiethIcon()
    {
        using var files = new Files();
        files.Write(Panel("0.7167248726", "0.9344827533"));
        var calibration = files.Calibration with { ScreenWidth = 3840, ScreenHeight = 2160, UiScale = 1.49f };

        var layout = Assert.IsType<BuffHudLayout>(BuffHudConfigurationReader.ResolveAutomatic(calibration,
            new(3840, 2160), CapturedAt));

        Assert.True(layout.Region.Contains(new Point(2450, 1980)));
        Assert.True(layout.Region.Contains(new Point(3428, 2133)));
        Assert.InRange(layout.Region.Left, 2441, 2443);
        Assert.InRange(layout.Region.Top, 1892, 1894);
        Assert.True(layout.Region.Right >= 3437);
        Assert.True(layout.Region.Bottom >= 2140);
        Assert.Equal((double)1.49f, layout.Scale);
    }

    [Fact]
    public void AnotherPositionAndScaleUseTheSelectedCalibrationsGeometry()
    {
        using var files = new Files();
        // Display values here are deliberately different; the supplied loot calibration is authoritative.
        files.Write(Panel("0.25", "0.3") + "<GameOptionGlobal><Resolution Width='800' Height='600'/><UiScale Value='2'/></GameOptionGlobal>");

        var layout = BuffHudConfigurationReader.ResolveAutomatic(files.Calibration with { UiScale = .75f },
            new(1920, 1080), CapturedAt);

        Assert.Equal(new Rectangle(322, 259, 507, 130), layout?.Region);
        Assert.Equal(.75, layout?.Scale);
    }

    [Fact]
    public void VerticalRoundingOccursInLayoutCoordinatesBeforePixelScaling()
    {
        using var files = new Files();
        files.Write(Panel("0.5", "0.51"));

        Assert.Equal(new Rectangle(648, 426, 1005, 252), BuffHudConfigurationReader.ResolveAutomatic(
            files.Calibration with { UiScale = 1.5f }, new(1920, 1080), CapturedAt)?.Region);
    }

    [Fact]
    public void NegativeEdgeUsesOneScaledLayoutPixel()
    {
        using var files = new Files();
        files.Write(Panel("0", "0.5"));

        Assert.Equal(new Rectangle(0, 414, 1002, 252), BuffHudConfigurationReader.ResolveAutomatic(
            files.Calibration with { UiScale = 1.5f }, new(1920, 1080), CapturedAt)?.Region);
    }

    [Theory]
    [InlineData("0", "0.5", 0, 455, 669, 170)]
    [InlineData("1", "1", 1506, 914, 414, 166)]
    public void FrameEdgesUseNominalPanelCorrectionBeforeClippingTheExpandedSearchArea(string x, string y, int left, int top, int width, int height)
    {
        using var files = new Files();
        files.Write(Panel(x, y));

        Assert.Equal(new Rectangle(left, top, width, height), files.Read()?.Region);
    }

    [Fact]
    public void ZeroPositionUsesTheClientsDefaultTopLeftWithoutCenterAdjustment()
    {
        using var files = new Files();
        files.Write(Panel("0", "0"));

        Assert.Equal(new Rectangle(668, 806, 672, 170), files.Read()?.Region);
    }

    [Fact]
    public void UnresolvedServerInitializationPositionDoesNotMasqueradeAsAPanelCenter()
    {
        using var files = new Files();
        files.Write(Panel("-1", "-1"));

        Assert.Null(files.Read());
    }

    [Fact]
    public void OversizedNominalPanelsStillClipToTheVisibleFrame()
    {
        using var files = new Files();

        Assert.Equal(new Rectangle(0, 0, 100, 100), BuffHudConfigurationReader.ResolveAutomatic(
            files.Calibration with { ScreenWidth = 100, ScreenHeight = 100 }, new(100, 100), CapturedAt)?.Region);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<UIData/>")]
    [InlineData("<UIData><UIData Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='0.5'/></UIData>")]
    [InlineData("<Preset><UIData><UIData Index='119' IsShow='true' RelativePosX='0.5' RelativePosY='0.5'/></UIData></Preset>")]
    [InlineData("<UIData><Preset><UIData Index='119' IsShow='true' RelativePosX='0.5' RelativePosY='0.5'/></Preset></UIData>")]
    [InlineData("<UIData><UIData Index='119' IsShow='false'/><Preset><UIData Index='119' IsShow='true' RelativePosX='0.5' RelativePosY='0.5'/></Preset></UIData>")]
    [InlineData("<UIData><UIData Index='119' IsShow='true' RelativePosX='0.5' RelativePosY='0.5'/></UIData><UIData/>")]
    [InlineData("<UIData><UIData Index='119' IsShow='true' RelativePosX='0.5' RelativePosY='0.5'/><UIData Index='119' IsShow='false'/></UIData>")]
    [InlineData("<UIData><UIData Index='119' IsShow='TRUE' RelativePosX='0.5' RelativePosY='0.5'/></UIData>")]
    [InlineData("<UIData><UIData Index='119' IsShow='true' RelativePosX='NaN' RelativePosY='0.5'/></UIData>")]
    [InlineData("<UIData><UIData Index='119' IsShow='true' RelativePosX='0.5' RelativePosY='Infinity'/></UIData>")]
    [InlineData("<UIData><UIData Index='119' IsShow='true' RelativePosX='1.01' RelativePosY='0.5'/></UIData>")]
    [InlineData("<UIData><UIData Index='119' IsShow='true' RelativePosX='0.5' RelativePosY='-0.01'/></UIData>")]
    [InlineData("<UIData><UIData Index='119' IsShow='true' RelativePosX='0,5' RelativePosY='0.5'/></UIData>")]
    [InlineData("<UIData><UIData Index='119' IsShow='true' RelativePosX='0.5'/></UIData>")]
    [InlineData("<UIData><UIData Index='119'")]
    [InlineData("<!DOCTYPE UIData [<!ENTITY x '0.5'>]><UIData><UIData Index='119' IsShow='true' RelativePosX='&x;' RelativePosY='0.5'/></UIData>")]
    public void MissingHiddenAmbiguousOrMalformedActivePanelNeverUsesPresets(string xml)
    {
        using var files = new Files();
        files.Write(xml);

        Assert.Null(files.Read());
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("windows-1252")]
    public void SavedEncodingsAreReadWithoutChangingTheFile(string name)
    {
        using var files = new Files();
        var encoding = name == "windows-1252" ? CodePagesEncodingProvider.Instance.GetEncoding(name)! : Encoding.GetEncoding(name);
        files.Write(Panel("0.5", "0.5") + "<Notes Value='Größe und Würfel'/>", encoding);
        var original = File.ReadAllBytes(files.Calibration.GameVariablePath);

        Assert.NotNull(files.Read());
        Assert.Equal(original, File.ReadAllBytes(files.Calibration.GameVariablePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigurationSavedAfterTheFrameIsRejected(bool options)
    {
        using var files = new Files();
        var path = options ? files.Calibration.GameOptionPath : files.Calibration.GameVariablePath;
        File.SetLastWriteTimeUtc(path, CapturedAt.UtcDateTime.AddSeconds(1));

        Assert.Null(files.Read());
        Assert.NotNull(BuffHudConfigurationReader.ResolveAutomatic(files.Calibration, new(1920, 1080), CapturedAt.AddSeconds(2)));
    }

    [Fact]
    public void MissingOptionalOptionsFileDoesNotCauseProfileDiscovery()
    {
        using var files = new Files();
        File.Delete(files.Calibration.GameOptionPath);

        Assert.NotNull(files.Read());
    }

    [Fact]
    public void TheNextCallReadsTheNewPositionWithoutCachingIt()
    {
        using var files = new Files();
        var initial = files.Read();
        files.Write(Panel("0.25", "0.25"));

        var moved = files.Read();

        Assert.NotNull(initial);
        Assert.NotNull(moved);
        Assert.NotEqual(initial.Region, moved.Region);
    }

    [Fact]
    public void MissingSelectedXmlNeverUsesAnotherAccountOrCharacterFile()
    {
        using var files = new Files();
        var other = Path.Combine(Directory.CreateDirectory(Path.Combine(files.Root, "UserCache", "99")).FullName, "gameVariable.xml");
        File.WriteAllText(other, Panel("0.5", "0.5"));
        File.Delete(files.Calibration.GameVariablePath);

        Assert.Null(BuffHudConfigurationReader.ResolveAutomatic(files.Calibration with { ActiveCharacterGameVariablePath = other },
            new(1920, 1080), CapturedAt));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0f)]
    [InlineData(.49f)]
    [InlineData(3.01f)]
    public void InvalidScaleCannotProduceAnAutomaticRegion(float scale)
    {
        using var files = new Files();

        Assert.Null(BuffHudConfigurationReader.ResolveAutomatic(files.Calibration with { UiScale = scale }, new(1920, 1080)));
    }

    [Fact]
    public void CalibrationAndCapturedFrameMustMatch()
    {
        using var files = new Files();

        Assert.Null(BuffHudConfigurationReader.ResolveAutomatic(null, new(1920, 1080)));
        Assert.Null(BuffHudConfigurationReader.ResolveAutomatic(files.Calibration, new(3840, 2160)));
        Assert.Null(BuffHudConfigurationReader.ResolveAutomatic(files.Calibration with { ScreenWidth = 0 }, new(0, 1080)));
        Assert.Null(BuffHudConfigurationReader.ResolveAutomatic(files.Calibration with { ScreenWidth = 32769 }, new(32769, 1080)));
    }

    [Fact]
    public void OversizedXmlIsRejected()
    {
        using var files = new Files();
        files.Write(Panel("0.5", "0.5") + new string(' ', 4 * 1024 * 1024));

        Assert.Null(files.Read());
    }

    [Fact]
    public void GameMayKeepTheConfigurationOpenForWriting()
    {
        using var files = new Files();
        using var handle = new FileStream(files.Calibration.GameVariablePath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);

        Assert.NotNull(files.Read());
    }

    private static string Panel(string x, string y) =>
        $"<UIData><UIData Index='159' IsShow='true' RelativePosX='0.9' RelativePosY='0.1'/>" +
        $"<UIData Index='119' IsShow='true' RelativePosX='{x}' RelativePosY='{y}'/></UIData>";

    private sealed class Files : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Grindcrest.AutoBuffHud.Tests", Guid.NewGuid().ToString("N"));
        internal CompanionCalibration Calibration { get; }

        internal Files()
        {
            var account = Directory.CreateDirectory(Path.Combine(Root, "UserCache", "42")).FullName;
            Calibration = new(account, Path.Combine(account, "gameVariable.xml"), Path.Combine(Root, "GameOption.txt"),
                100, 200, 1920, 1080, 1, CompanionFontType.StrongSword, 0, false);
            File.WriteAllText(Calibration.GameOptionPath, "width = 1920\nheight = 1080\nuiScale = 1.00");
            File.SetLastWriteTimeUtc(Calibration.GameOptionPath, CapturedAt.UtcDateTime.AddSeconds(-5));
            Write(Panel("0.5", "0.5"));
        }

        internal void Write(string xml, Encoding? encoding = null)
        {
            File.WriteAllText(Calibration.GameVariablePath, xml, encoding ?? new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(Calibration.GameVariablePath, CapturedAt.UtcDateTime.AddSeconds(-5));
        }

        internal BuffHudLayout? Read() => BuffHudConfigurationReader.ResolveAutomatic(Calibration,
            new(Calibration.ScreenWidth, Calibration.ScreenHeight), CapturedAt);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
