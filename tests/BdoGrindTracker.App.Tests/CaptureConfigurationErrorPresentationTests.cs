using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;
using System.Security;
using System.Xml;

namespace BdoGrindTracker.App.Tests;

public sealed class CaptureConfigurationErrorPresentationTests
{
    private const string NormalPanel =
        "<UIData Index='159' IsShow='true' RelativePosX='0.5' RelativePosY='0.7'/>";
    private const string ValidXml = "<UIData>" + NormalPanel +
        "<UIData Index='161' IsShow='true' RelativePosX='0.6' RelativePosY='0.3'/></UIData>";
    private const string ValidOptions = "width = 1920\nheight = 1080\nuiScale =  1.00\n";
    private const string MissingNormal =
        "Die Position des normalen Droplogs fehlt oder das Droplog ist ausgeblendet. " +
        "Blende es in BDO unter „UI bearbeiten“ ein und speichere die Oberfläche.";
    private const string AmbiguousNormal =
        "Die Position des normalen Droplogs fehlt oder ist nicht eindeutig gespeichert. " +
        "Blende es in BDO unter „UI bearbeiten“ ein und speichere die Oberfläche erneut.";
    private const string InvalidNormal =
        "Die gespeicherte Position des normalen Droplogs ist ungültig oder liegt außerhalb des Spielbilds. " +
        "Platziere es in BDO unter „UI bearbeiten“ vollständig im Spielbild und speichere die Oberfläche.";
    private const string InvalidResolution =
        "Die gespeicherte Bildschirmauflösung fehlt oder ist ungültig. " +
        "Prüfe die Auflösung in den BDO-Einstellungen und speichere sie erneut.";
    private const string InvalidScale =
        "Die gespeicherte Größe der Benutzeroberfläche fehlt oder ist ungültig. " +
        "Prüfe die UI-Skalierung in den BDO-Einstellungen und speichere sie erneut.";
    private const string DamagedFile =
        "Die Konfigurationsdatei ist beschädigt oder unvollständig. " +
        "Speichere die Oberfläche in BDO erneut oder wähle eine andere Konfigurationsdatei.";
    private const string MissingFile =
        "Die Konfigurationsdatei wurde nicht gefunden. Lade die Liste erneut oder wähle eine vorhandene Datei.";
    private const string MissingOptions =
        "Die Datei GameOption.txt mit den BDO-Anzeigeeinstellungen fehlt. " +
        "Starte BDO einmal und speichere die Einstellungen. Bei einer Sicherung wird auch diese Datei benötigt.";
    private const string LockedFile =
        "Die Konfigurationsdatei wird gerade von einem anderen Programm verwendet. Warte kurz und lade die Liste erneut.";
    private const string UnknownError =
        "Die BDO-Konfiguration konnte nicht geprüft werden. " +
        "Speichere die Oberfläche im Spiel erneut oder wähle eine andere Konfigurationsdatei.";

    public static IEnumerable<object[]> InvalidConfigurations()
    {
        yield return ["<UIData><UIData Index='159' IsShow='false'/></UIData>", ValidOptions, MissingNormal];
        yield return ["<UIData><UIData Index='159' IsShow='true'/></UIData>", ValidOptions, MissingNormal];
        yield return ["<UIData/>", ValidOptions, AmbiguousNormal];
        yield return ["<Other/>", ValidOptions, AmbiguousNormal];
        yield return ["<UIData>" + NormalPanel + NormalPanel + "</UIData>", ValidOptions, AmbiguousNormal];
        yield return [ValidXml + ValidXml, ValidOptions, AmbiguousNormal];
        yield return [ValidXml.Replace("RelativePosX='0.5'", "RelativePosX='1.1'"), ValidOptions, InvalidNormal];
        yield return [ValidXml.Replace("RelativePosX='0.5'", "RelativePosX='NaN'"), ValidOptions, InvalidNormal];
        yield return [ValidXml.Replace("RelativePosY='0.7'", "RelativePosY='private-position-value'"), ValidOptions, InvalidNormal];
        yield return [ValidXml, "width = 1\nheight = 1\nuiScale =  1.00\n", InvalidNormal];
        yield return ["<Resolution Width='0' Height='1080'/>" + ValidXml, ValidOptions, InvalidResolution];
        yield return ["<Resolution Width='1920' Height='private-height-value'/>" + ValidXml, ValidOptions, InvalidResolution];
        yield return [ValidXml, "uiScale =  1.00\n", InvalidResolution];
        yield return ["<UiScale Value='NaN'/>" + ValidXml, ValidOptions, InvalidScale];
        yield return ["<UiScale Value='private-scale-value'/>" + ValidXml, ValidOptions, InvalidScale];
        yield return [ValidXml, "width = 1920\nheight = 1080\n", InvalidScale];
        yield return ["<private-customer-content></wrong-ending>", ValidOptions, DamagedFile];
    }

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void InspectScanAndReadDescribeInvalidRealConfigurationsInGerman(string xml, string options, string expected)
    {
        using var files = new ConfigurationFiles(options);
        var path = files.Write("41", xml);
        var catalog = new CaptureConfigurationCatalog(files.Root);

        AssertInvalid(catalog.Inspect(path), expected);
        var scan = catalog.Scan(path);
        Assert.Null(scan.ActivePath);
        AssertInvalid(Assert.Single(scan.Candidates), expected);
        Assert.Equal("Aktuelle Konfiguration: " + expected, scan.Error);
        var error = Assert.Throws<LootPanelUnavailableException>(() => catalog.Read(path));
        Assert.Equal(expected, error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void ScanDoesNotExposeTheRawXmlErrorOfAnUnselectedCandidate()
    {
        using var files = new ConfigurationFiles();
        var invalid = files.Write("41", "<private-customer-content></wrong-ending>");
        var selected = files.Write("42", ValidXml);
        var catalog = new CaptureConfigurationCatalog(files.Root);

        var scan = catalog.Scan(selected);

        Assert.Null(scan.Error);
        Assert.Equal(selected, scan.ActivePath);
        AssertInvalid(Assert.Single(scan.Candidates, candidate => candidate.Path == invalid), DamagedFile);
        Assert.True(Assert.Single(scan.Candidates, candidate => candidate.Path == selected).IsValid);
    }

    [Theory]
    [InlineData(false, MissingFile)]
    [InlineData(true, MissingOptions)]
    public void MissingSelectedFileOrGameOptionsHaveActionableGermanErrors(bool deleteOptions, string expected)
    {
        using var files = new ConfigurationFiles();
        var path = files.Write("41", ValidXml);
        File.Delete(deleteOptions ? Path.Combine(files.Root, "GameOption.txt") : path);
        var catalog = new CaptureConfigurationCatalog(files.Root);

        AssertInvalid(catalog.Inspect(path), expected);
        var scan = catalog.Scan(path);
        Assert.Equal("Aktuelle Konfiguration: " + expected, scan.Error);
        AssertInvalid(Assert.Single(scan.Candidates), expected);
    }

    [Fact]
    public void LockedConfigurationIsReportedWithoutExposingTheOperatingSystemMessage()
    {
        using var files = new ConfigurationFiles();
        var path = files.Write("41", ValidXml);
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var catalog = new CaptureConfigurationCatalog(files.Root);

        AssertInvalid(catalog.Inspect(path), LockedFile);
        var scan = catalog.Scan(path);
        Assert.Equal("Aktuelle Konfiguration: " + LockedFile, scan.Error);
        AssertInvalid(Assert.Single(scan.Candidates), LockedFile);
    }

    [Fact]
    public void ScanWithAMissingConfigurationDirectoryUsesOnlyGermanErrorText()
    {
        using var files = new ConfigurationFiles();
        var scan = new CaptureConfigurationCatalog(files.Root).Scan(null);

        Assert.Empty(scan.Candidates);
        Assert.Null(scan.ActivePath);
        Assert.Equal("Aktuelle Konfiguration: Der BDO-Konfigurationsordner wurde nicht gefunden. " +
            "Starte BDO einmal und speichere die Oberfläche oder wähle eine vorhandene Konfigurationsdatei.", scan.Error);
    }

    [Fact]
    public void ScanRootFailuresDoNotLeakInvalidPathsOrArgumentMessages()
    {
        var scan = new CaptureConfigurationCatalog("private-path\0").Scan(null);
        const string message = "Der Pfad zur BDO-Konfiguration ist ungültig. Wähle eine vorhandene Konfigurationsdatei.";

        Assert.Empty(scan.Candidates);
        Assert.Equal(message + " Aktuelle Konfiguration: " + message, scan.Error);
    }

    [Fact]
    public void PresentationUnwrapsMultipleGuardExceptionsWithoutLeakingXmlContents()
    {
        var original = new XmlException("private-file.xml contains <private-customer-content>");
        var error = new LootPanelUnavailableException("outer English failure",
            new LootPanelUnavailableException("inner English failure", original));

        Assert.Equal(DamagedFile, CaptureConfigurationErrorPresentation.Describe(error));
    }

    [Fact]
    public void PermissionsAndUnknownErrorsDoNotExposeUntrustedDiagnosticText()
    {
        const string denied = "Grindcrest darf die Konfigurationsdatei nicht lesen. " +
            "Prüfe die Zugriffsrechte des BDO-Ordners oder wähle eine lesbare Kopie.";
        Assert.Equal(denied, CaptureConfigurationErrorPresentation.Describe(new UnauthorizedAccessException("private path")));
        Assert.Equal(denied, CaptureConfigurationErrorPresentation.Describe(new SecurityException("private path")));
        Assert.Equal(UnknownError, CaptureConfigurationErrorPresentation.Describe(new Exception("unknown English <private>")));
        Assert.Equal(UnknownError, CaptureConfigurationErrorPresentation.Describe(new InvalidDataException("unknown English <private>")));
    }

    [Fact]
    public void OptionalSpecialStatusReportsSavedGeometryWithoutClaimingThePanelIsCurrentlyVisible()
    {
        using var files = new ConfigurationFiles();
        var path = files.Write("41", ValidXml);
        var catalog = new CaptureConfigurationCatalog(files.Root);
        var calibration = catalog.Read(path);

        Assert.Equal("Position gespeichert · aktive Oberfläche", catalog.Describe(calibration).RareStatus);
        Assert.Equal("Position gespeichert · aus einem UI-Preset", catalog.Describe(calibration with
        {
            RareLootResolution = new(RareLootAnchorStatus.PresetFallback, "preset", "internal-English-reason"),
        }).RareStatus);
        Assert.Equal("Position verändert · für eine neue Session erneut übernehmen", catalog.Describe(calibration with
        {
            HasRareLootAnchor = false,
            RareLootResolution = new(RareLootAnchorStatus.Invalid, "active", "rare-band-shape-changed-restart-required"),
        }).RareStatus);
    }

    private static void AssertInvalid(BdoGrindTracker.App.Services.CaptureConfigurationOption option, string expected)
    {
        Assert.False(option.IsValid);
        Assert.Equal(expected, option.Error);
        Assert.Equal("Nicht geprüft", option.RareStatus);
        Assert.Null(option.NormalBounds);
        Assert.Null(option.RareBounds);
    }

    private sealed class ConfigurationFiles : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "grindcrest-config-presentation-" + Guid.NewGuid().ToString("N"));

        internal ConfigurationFiles(string options = ValidOptions)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "GameOption.txt"), options);
        }

        internal string Write(string profile, string xml)
        {
            var path = Path.Combine(Root, "UserCache", profile, "gameVariable.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
