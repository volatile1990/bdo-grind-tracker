using System.Text.Json;
using BdoGrindTracker.App.Analysis;

namespace BdoGrindTracker.App.Tests;

public sealed class BuffRecognitionProfileTests
{
    [Fact]
    public void RelativeTemplatesResolveBesideProfileAndErrorsAreExplicit()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        var profile = store.Load();
        Assert.NotNull(profile);
        Assert.Null(store.LastError);
        Assert.Equal(Path.Combine(fixture.Root, "icon.png"), profile.Templates[0].IconPath);
        File.Delete(profile.Templates[0].IconPath);
        Assert.Null(store.Load());
        Assert.Contains("fehlt", store.LastError);
    }

    [Fact]
    public void InvalidCatalogIdsAndDuplicateDefinitionsAreRejected()
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Profile with { Templates = [new("invented-item", "icon.png", new(0, 32, 40, 16))] });
        Assert.Null(fixture.Store.Load());
        fixture.Write(fixture.Profile with { Templates = [fixture.Profile.Templates[0], fixture.Profile.Templates[0]] });
        Assert.Null(fixture.Store.Load());
    }

    [Theory]
    [InlineData("perfume-of-courage", "immortal-perfume-of-courage")]
    [InlineData("tent-body-enhancement-60", "tent-body-enhancement-120")]
    public void MultipleVariantsOfOneRecognitionGroupAreRejected(string first, string second)
    {
        using var fixture = new Fixture();
        fixture.Write(fixture.Profile with
        { Templates = [new(first, "icon.png", new(0, 32, 40, 16)), new(second, "icon.png", new(0, 32, 40, 16))] });
        var store = fixture.Store;
        Assert.Null(store.Load());
        Assert.Contains("Buff-Familie", store.LastError);
    }

    [Fact]
    public void ExplicitRegionRequiresMatchingFrameAndNeverClips()
    {
        using var fixture = new Fixture();
        Assert.Equal(new Rectangle(20, 30, 300, 64), BuffHudConfigurationReader.Resolve(fixture.Profile, new(1920, 1080))?.Region);
        Assert.Null(BuffHudConfigurationReader.Resolve(fixture.Profile, new(2560, 1440)));
        Assert.Null(BuffHudConfigurationReader.Resolve(fixture.Profile with { Region = new(1900, 30, 300, 64) }, new(1920, 1080)));
    }

    [Fact]
    public void UserSuppliedIndexTracksOnlyActiveVisiblePanelWithSavedUiScale()
    {
        using var fixture = new Fixture();
        fixture.WriteVariables("<UIData><UIData Index='81' IsShow='true' RelativePosX='0.25' RelativePosY='0.5'/></UIData>");
        var profile = fixture.Profile with { UiDataIndex = 81, BlackDesertDirectory = fixture.Root };
        var layout = BuffHudConfigurationReader.Resolve(profile, new(1920, 1080));
        Assert.NotNull(layout);
        Assert.Equal(1.5, layout.Scale);
        Assert.Equal(new Rectangle(510, 585, 450, 96), layout.Region);
    }

    [Theory]
    [InlineData("<UIData><UIData Index='81' IsShow='false' RelativePosX='0.25' RelativePosY='0.5'/></UIData>")]
    [InlineData("<Preset><UIData><UIData Index='81' IsShow='true' RelativePosX='0.25' RelativePosY='0.5'/></UIData></Preset>")]
    [InlineData("<UIData><UIData Index='81' IsShow='true' RelativePosX='NaN' RelativePosY='0.5'/></UIData>")]
    [InlineData("<UIData><UIData Index='81' IsShow='true' RelativePosX='0.25' RelativePosY='0.5'/><UIData Index='81' IsShow='true' RelativePosX='0.25' RelativePosY='0.5'/></UIData>")]
    [InlineData("<!DOCTYPE UIData [<!ENTITY x '0.25'>]><UIData><UIData Index='81' IsShow='true' RelativePosX='&x;' RelativePosY='0.5'/></UIData>")]
    public void HiddenAmbiguousInvalidOrPresetOnlyPositionsAreUnknown(string variables)
    {
        using var fixture = new Fixture();
        fixture.WriteVariables(variables);
        Assert.Null(BuffHudConfigurationReader.Resolve(fixture.Profile with { UiDataIndex = 81, BlackDesertDirectory = fixture.Root }, new(1920, 1080)));
    }

    [Fact]
    public void ProfileSelectionUsesConfigFileWriteTimeRatherThanDirectoryTime()
    {
        using var fixture = new Fixture();
        var selected = fixture.WriteVariables("<UIData><UIData Index='81' IsShow='false'/></UIData>");
        var older = Directory.CreateDirectory(Path.Combine(fixture.Root, "UserCache", "43")).FullName;
        var olderFile = Path.Combine(older, "gameVariable.xml");
        File.WriteAllText(olderFile, "<UIData><UIData Index='81' IsShow='true' RelativePosX='0.25' RelativePosY='0.5'/></UIData>");
        File.SetLastWriteTimeUtc(olderFile, DateTime.UnixEpoch);
        File.SetLastWriteTimeUtc(selected, DateTime.UnixEpoch.AddHours(1));
        Directory.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(1));
        Assert.Null(BuffHudConfigurationReader.Resolve(fixture.Profile with { UiDataIndex = 81, BlackDesertDirectory = fixture.Root }, new(1920, 1080)));
    }

    [Fact]
    public void ExplicitSelectedConfigurationCannotBeSupersededByAnotherAccount()
    {
        using var fixture = new Fixture();
        var selected = fixture.WriteVariables("<UIData><UIData Index='81' IsShow='true' RelativePosX='0.25' RelativePosY='0.5'/></UIData>");
        File.SetLastWriteTimeUtc(selected, DateTime.UnixEpoch);
        var other = Directory.CreateDirectory(Path.Combine(fixture.Root, "UserCache", "43")).FullName;
        File.WriteAllText(Path.Combine(other, "gameVariable.xml"), "<UIData><UIData Index='81' IsShow='false'/></UIData>");
        Assert.NotNull(BuffHudConfigurationReader.Resolve(fixture.Profile with
        { UiDataIndex = 81, BlackDesertDirectory = fixture.Root, GameVariablePath = selected }, new(1920, 1080)));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("windows-1252")]
    public void SavedBuffPanelAcceptsLegacyAndUnicodeGameConfiguration(string encoding)
    {
        using var fixture = new Fixture();
        var variables = fixture.WriteVariables("<UIData><UIData Index='81' IsShow='true' RelativePosX='0.25' RelativePosY='0.5'/></UIData>");
        var text = File.ReadAllText(variables) + "<Notes Value='Größe und Würfel'/>";
        var fileEncoding = encoding == "windows-1252"
            ? System.Text.CodePagesEncodingProvider.Instance.GetEncoding(encoding)!
            : System.Text.Encoding.GetEncoding(encoding);
        File.WriteAllText(variables, text, fileEncoding);
        var original = File.ReadAllBytes(variables);
        var layout = BuffHudConfigurationReader.Resolve(fixture.Profile with
        { UiDataIndex = 81, BlackDesertDirectory = fixture.Root, GameVariablePath = variables }, new(1920, 1080));
        Assert.Equal(new Rectangle(510, 585, 450, 96), layout?.Region);
        Assert.Equal(original, File.ReadAllBytes(variables));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "grindcrest-buff-profile-" + Guid.NewGuid().ToString("N"));
        internal BuffRecognitionProfile Profile { get; } = new()
        {
            ScreenWidth = 1920, ScreenHeight = 1080, Region = new(20, 30, 300, 64),
            Templates = [new("harmony-draught", "icon.png", new(0, 32, 40, 16))],
        };
        internal BuffRecognitionProfileStore Store => new(Path.Combine(Root, "profile.json"));
        internal Fixture() { Directory.CreateDirectory(Root); File.WriteAllBytes(Path.Combine(Root, "icon.png"), [1]); Write(Profile); }
        internal void Write(BuffRecognitionProfile profile) => File.WriteAllText(Path.Combine(Root, "profile.json"), JsonSerializer.Serialize(profile));
        internal string WriteVariables(string xml)
        {
            var directory = Directory.CreateDirectory(Path.Combine(Root, "UserCache", "42")).FullName;
            var path = Path.Combine(directory, "gameVariable.xml");
            File.WriteAllText(path, xml + "<GameOptionGlobal><Resolution Width='1920' Height='1080'/><UiScale Value='1.5'/></GameOptionGlobal>");
            return path;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
