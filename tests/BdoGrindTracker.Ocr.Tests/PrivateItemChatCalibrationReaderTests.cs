using System.Drawing;
using System.Xml.Linq;

namespace BdoGrindTracker.Ocr.Tests;

public sealed class PrivateItemChatCalibrationReaderTests
{
    [Fact]
    public void ReadsSavedDetachedPrivateItemPanelAndConvertsCenterCoordinates()
    {
        using var fixture = new Fixture();
        var window = CreateWindow(3);
        window.SetAttributeValue("PosX", "12");
        window.SetAttributeValue("PosY", "830");
        window.SetAttributeValue("RelativePosX", "0.1216530874");
        window.SetAttributeValue("RelativePosY", "0.6713793278");
        window.SetAttributeValue("SizeX", "603");
        window.SetAttributeValue("SizeY", "287");
        fixture.Write(window);

        var result = fixture.Read(fixture.Calibration with
        {
            ScreenWidth = 3840,
            ScreenHeight = 2160,
            UiScale = 1.49f,
        });

        Assert.Equal(new PrivateItemChatCalibration(3, new Rectangle(18, 1236, 898, 428), false), result);
    }

    [Fact]
    public void ActualInternalFlagsAreMarkedAsMixedAndExactSystemOnlyPanelIsPreferred()
    {
        using var fixture = new Fixture();
        var mixed = CreateWindow(3);
        foreach (var key in new[]
                 {
                     "Friend", "LocalWar", "NoticeOnlyTop", "AppGuild", "DeadMessage",
                     "GM", "Messenger", "Sign", "SolareCustom",
                 })
        {
            mixed.Add(Filter("ChatType", key, "true"));
        }

        fixture.Write(mixed);
        Assert.True(Assert.IsType<PrivateItemChatCalibration>(fixture.Read()).HasOtherChatTypes);

        fixture.Write(mixed, CreateWindow(5), CreateWindow(4));
        Assert.Equal(4, Assert.IsType<PrivateItemChatCalibration>(fixture.Read()).WindowIndex);
    }

    [Theory]
    [InlineData("World")]
    [InlineData("Public")]
    [InlineData("Private")]
    [InlineData("Party")]
    [InlineData("Guild")]
    [InlineData("Alliance")]
    [InlineData("Channel")]
    [InlineData("NewUnknownChatType")]
    public void RejectsOrdinaryOrUnknownEnabledChatChannels(string key)
    {
        using var fixture = new Fixture();
        var window = CreateWindow(3);
        window.Add(Filter("ChatType", key, "true"));
        fixture.Write(window);

        Assert.Null(fixture.Read());
    }

    [Fact]
    public void RejectsEveryOtherEnabledSystemFilterIncludingUnknownFutureCategories()
    {
        using var fixture = new Fixture();
        var keys = CreateWindow(3).Elements("ChatSystemType")
            .Select(element => element.Attribute("key")!.Value)
            .Where(key => key != "ChatSystemType_PrivateItem")
            .Append("ChatSystemType_FutureCategory");
        foreach (var key in keys)
        {
            var window = CreateWindow(3);
            var filter = window.Elements("ChatSystemType")
                .SingleOrDefault(element => element.Attribute("key")!.Value == key);
            if (filter is null)
            {
                window.Add(Filter("ChatSystemType", key, "true"));
            }
            else
            {
                filter.SetAttributeValue("value", "true");
            }

            fixture.Write(window);
            Assert.Null(fixture.Read());
        }
    }

    [Fact]
    public void RejectsMissingDisabledMalformedOrDuplicatedRequiredFilters()
    {
        using var fixture = new Fixture();
        foreach (var kind in new[] { "ChatType", "ChatSystemType" })
        {
            var key = kind == "ChatType" ? "System" : "ChatSystemType_PrivateItem";
            foreach (var failure in new[] { "missing", "false", "invalid", "duplicate" })
            {
                var window = CreateWindow(3);
                var filter = window.Elements(kind).Single(element => element.Attribute("key")!.Value == key);
                switch (failure)
                {
                    case "missing": filter.Remove(); break;
                    case "duplicate": window.Add(new XElement(filter)); break;
                    default: filter.SetAttributeValue("value", failure); break;
                }

                fixture.Write(window);
                Assert.Null(fixture.Read());
            }
        }

        var incomplete = CreateWindow(3);
        incomplete.Elements("ChatSystemType")
            .Single(element => element.Attribute("key")!.Value == "ChatSystemType_Undefined").Remove();
        fixture.Write(incomplete);
        Assert.Null(fixture.Read());
    }

    [Fact]
    public void RequiresExplicitVisibleUsedAndDetachedWindow()
    {
        using var fixture = new Fixture();
        foreach (var (attribute, invalidValue) in new[]
                 {
                     ("IsShow", "false"), ("IsUsing", "false"),
                     ("IsCombinedToMainPanel", "true"), ("WindowIndex", "-1"),
                 })
        {
            var window = CreateWindow(3);
            window.SetAttributeValue(attribute, invalidValue);
            fixture.Write(window);
            Assert.Null(fixture.Read());
            window.Attribute(attribute)!.Remove();
            fixture.Write(window);
            Assert.Null(fixture.Read());
        }
    }

    [Fact]
    public void IgnoresPresetCopiesAndCharacterFileWithoutOverridingActivePanel()
    {
        using var fixture = new Fixture();
        var active = new XElement("UIData", CreateWindow(3));
        var presets = new XElement("UISettingPreset", new XElement("UIData", CreateWindow(0)));
        active.Add(new XElement("Preset", CreateWindow(1)));
        File.WriteAllText(fixture.Path, $"<CheckQuestList />{active}{presets}<QuestOption />");

        var result = fixture.Read(fixture.Calibration with { ActiveCharacterGameVariablePath = "not-read.xml" });

        Assert.Equal(3, Assert.IsType<PrivateItemChatCalibration>(result).WindowIndex);
        File.WriteAllText(fixture.Path, presets.ToString());
        Assert.Null(fixture.Read());
    }

    [Fact]
    public void RejectsDuplicateActiveSectionsAndDuplicateWindowIndices()
    {
        using var fixture = new Fixture();
        fixture.Write(CreateWindow(3), CreateWindow(3));
        Assert.Null(fixture.Read());

        var section = new XElement("UIData", CreateWindow(3));
        File.WriteAllText(fixture.Path, section + section.ToString());
        Assert.Null(fixture.Read());
    }

    [Fact]
    public void RejectsInvalidOrOffScreenGeometry()
    {
        using var fixture = new Fixture();
        foreach (var (attribute, value) in new[]
                 {
                     ("RelativePosX", "NaN"), ("RelativePosY", "Infinity"),
                     ("RelativePosX", "-0.1"), ("RelativePosY", "1.1"),
                     ("RelativePosX", "0"), ("RelativePosY", "1"),
                     ("SizeX", "0"), ("SizeY", "-1"), ("SizeX", "1000000"),
                     ("SizeY", "1e100"), ("SizeX", "invalid"),
                 })
        {
            var window = CreateWindow(3);
            window.SetAttributeValue(attribute, value);
            fixture.Write(window);
            Assert.Null(fixture.Read());
        }
    }

    [Fact]
    public void InvalidScreenAndUiScaleDisableOptionalChat()
    {
        using var fixture = new Fixture();
        fixture.Write(CreateWindow(3));
        foreach (var scale in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.MaxValue })
        {
            Assert.Null(fixture.Read(fixture.Calibration with { UiScale = scale }));
        }

        Assert.Null(fixture.Read(fixture.Calibration with { ScreenWidth = 0 }));
        Assert.Null(fixture.Read(fixture.Calibration with { ScreenHeight = -1 }));
    }

    [Fact]
    public void MissingTruncatedAndDtdFilesReturnNoFallback()
    {
        using var fixture = new Fixture();
        Assert.Null(fixture.Read());
        File.WriteAllText(fixture.Path, "<UIData><UIData");
        Assert.Null(fixture.Read());
        File.WriteAllText(fixture.Path,
            "<!DOCTYPE UIData [<!ENTITY unsafe SYSTEM 'file:///not-read'>]><UIData>&unsafe;</UIData>");
        Assert.Null(fixture.Read());
    }

    private static XElement CreateWindow(int index) => new("UIData",
        new XAttribute("Index", "32"),
        new XAttribute("WindowIndex", index),
        new XAttribute("IsShow", "true"),
        new XAttribute("IsUsing", "true"),
        new XAttribute("IsCombinedToMainPanel", "false"),
        new XAttribute("RelativePosX", "0.5"),
        new XAttribute("RelativePosY", "0.5"),
        new XAttribute("SizeX", "600"),
        new XAttribute("SizeY", "300"),
        Filter("ChatType", "System", "true"),
        Filter("ChatSystemType", "ChatSystemType_Undefined", "false"),
        Filter("ChatSystemType", "ChatSystemType_PrivateItem", "true"),
        Filter("ChatSystemType", "ChatSystemType_PartyItem", "false"),
        Filter("ChatSystemType", "ChatSystemType_Market", "false"),
        Filter("ChatSystemType", "ChatSystemType_Worker", "false"),
        Filter("ChatSystemType", "ChatSystemType_Harvest", "false"),
        Filter("ChatSystemType", "ChatSystemType_Enchant", "false"),
        Filter("ChatSystemType", "ChatSystemType_Siege", "false"),
        Filter("ChatSystemType", "ChatSystemType_FamilyWorkItem", "false"));

    private static XElement Filter(string type, string key, string value) => new(type,
        new XAttribute("key", key), new XAttribute("value", value));

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"private-item-chat-{Guid.NewGuid():N}.xml");

        public CompanionCalibration Calibration => new(
            System.IO.Path.GetDirectoryName(Path)!, Path, "unused.txt", 1500, 500,
            1920, 1080, 1f, CompanionFontType.StrongSword, 1, false);

        public void Write(params XElement[] windows) =>
            File.WriteAllText(Path, new XElement("UIData", windows).ToString());

        public PrivateItemChatCalibration? Read(CompanionCalibration? calibration = null) =>
            new PrivateItemChatCalibrationReader().TryRead(calibration ?? Calibration);

        public void Dispose() => File.Delete(Path);
    }
}
