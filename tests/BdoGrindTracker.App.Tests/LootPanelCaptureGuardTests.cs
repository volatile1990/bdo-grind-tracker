using BdoGrindTracker.App.Analysis;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Tests;

public sealed class LootPanelCaptureGuardTests
{
    private static CompanionCalibration Calibration() => new("profile", "gamevariable.xml", "GameOption.txt",
        960, 540, 1920, 1080, 1f, CompanionFontType.StrongSword, 0, false);

    [Theory]
    [InlineData("missing")]
    [InlineData("hidden")]
    [InlineData("malformed")]
    public void UnreadableMainPanelHasAnActionableError(string kind)
    {
        Exception cause = kind switch
        {
            "missing" => new FileNotFoundException("private path"),
            "hidden" => new InvalidDataException("hidden UIData 159"),
            _ => new System.Xml.XmlException("invalid fragment"),
        };
        var error = Assert.Throws<LootPanelUnavailableException>(() =>
            LootPanelCaptureGuard.ReadCalibration(() => throw cause));
        Assert.Equal(LootPanelCaptureGuard.MissingPanelMessage, error.Message);
        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public void ValidPositionRemainsAvailableWithoutAnyLootAndChecksAreThrottled()
    {
        var reads = 0;
        var calibration = Calibration();
        var guard = new LootPanelCaptureGuard(calibration, () => { reads++; return calibration; });
        for (var i = 0; i < 5; i++)
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddMilliseconds(i * 450));
        Assert.Equal(1, reads);
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2));
        Assert.Equal(2, reads);
        Assert.Null(guard.Error);
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2.1), force: true);
        Assert.Equal(3, reads);
    }

    [Theory]
    [InlineData("scale")]
    [InlineData("resolution")]
    [InlineData("font")]
    [InlineData("profile")]
    public void ChangedCalibrationStopsUseOfTheOldCoordinates(string field)
    {
        var calibration = Calibration();
        var current = calibration;
        var guard = new LootPanelCaptureGuard(calibration, () => current);
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch);
        current = field switch
        {
            "scale" => calibration with { UiScale = 1.2f },
            "resolution" => calibration with { ScreenWidth = 2560 },
            "font" => calibration with { FontType = CompanionFontType.DejaVu },
            _ => calibration with { GameVariablePath = "another-profile/gameVariable.xml" },
        };
        var error = Assert.Throws<LootPanelUnavailableException>(() =>
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2)));
        Assert.Contains("geändert", error.Message);
        Assert.Equal(error.Message, guard.Error);
        Assert.Throws<LootPanelUnavailableException>(() =>
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(3)));
    }

    [Fact]
    public void FreshConfigCorrectsBothAnchorsAndBecomesTheNewBaseline()
    {
        var calibration = Calibration() with { HasRareLootAnchor = true, RareLootAnchorX = 1200, RareLootAnchorY = 500 };
        var current = calibration;
        var reads = 0;
        var guard = new LootPanelCaptureGuard(calibration, () => { reads++; return current; });
        Assert.Equal(calibration, guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch));
        current = calibration with { LootAnchorX = 800, LootAnchorY = 600, RareLootAnchorX = 1300, RareLootAnchorY = 450 };
        var moved = guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2));
        Assert.Equal(current, moved);
        Assert.Null(guard.Error);
        Assert.Equal(current, guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2.1)));
        Assert.Equal(2, reads);
        Assert.Equal(current, guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(4)));
        Assert.Null(guard.Error);
    }

    [Fact]
    public void InvalidOptionalRareGeometryDoesNotStopNormalTracking()
    {
        var initial = Calibration() with { HasRareLootAnchor = true, RareLootAnchorX = int.MaxValue };
        var validated = LootPanelCaptureGuard.ReadCalibration(() => initial);
        Assert.False(validated.HasRareLootAnchor);
        Assert.Equal(RareLootAnchorStatus.Invalid, validated.RareLootResolution!.Status);
        Assert.Equal(initial.LootAnchorX, validated.LootAnchorX);
        var guard = new LootPanelCaptureGuard(validated);
        Assert.Equal(validated, guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch));
        Assert.Null(guard.Error);
    }

    [Fact]
    public void RareClippingChangeDisablesOnlyRareUntilRestartAndPreservesNormalPosition()
    {
        var initial = Calibration() with { HasRareLootAnchor = true, RareLootAnchorX = 500, RareLootAnchorY = 400 };
        var current = initial;
        var guard = new LootPanelCaptureGuard(initial, () => current);
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch);
        current = current with { RareLootAnchorY = 0 };
        for (var i = 1; i <= 3; i++)
        {
            var result = guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(i * 2));
            Assert.False(result.HasRareLootAnchor);
            Assert.Equal(initial.LootAnchorX, result.LootAnchorX);
            Assert.Equal(initial.LootAnchorY, result.LootAnchorY);
            Assert.Equal("rare-band-shape-changed-restart-required", result.RareLootResolution!.Reason);
            Assert.Null(guard.Error);
        }
    }

    [Fact]
    public void RareResolutionProvenanceFollowsReloadWithoutChangingNormalGeometry()
    {
        var initial = Calibration() with
        {
            RareLootResolution = new(RareLootAnchorStatus.Ambiguous, "presets", "conflicting positions"),
        };
        var current = initial;
        var guard = new LootPanelCaptureGuard(initial, () => current);
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch);
        current = initial with
        {
            HasRareLootAnchor = true, RareLootAnchorX = 1300, RareLootAnchorY = 700,
            RareLootResolution = new(RareLootAnchorStatus.PresetFallback, "UISettingPreset0", "unique matching position"),
        };
        Assert.Equal(current, guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2)));
        Assert.Null(guard.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedFileChangeReloadsBeforeTheNextFrameDespiteThePollingInterval(bool options)
    {
        var folder = Path.Combine(Path.GetTempPath(), "grindcrest-panel-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var calibration = Calibration() with
            {
                GameVariablePath = Path.Combine(folder, "gameVariable.xml"),
                GameOptionPath = Path.Combine(folder, "GameOption.txt"),
            };
            File.WriteAllText(calibration.GameVariablePath, "original");
            File.WriteAllText(calibration.GameOptionPath, "original");
            File.SetLastWriteTimeUtc(calibration.GameVariablePath, DateTime.UnixEpoch.AddSeconds(-5));
            File.SetLastWriteTimeUtc(calibration.GameOptionPath, DateTime.UnixEpoch.AddSeconds(-5));
            var current = calibration;
            var reads = 0;
            var guard = new LootPanelCaptureGuard(calibration, () => { reads++; return current; });
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch);
            var path = options ? calibration.GameOptionPath : calibration.GameVariablePath;
            current = calibration with { LootAnchorX = 800, LootAnchorY = 600 };
            File.WriteAllText(path, "saved move");
            File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddMilliseconds(100));

            // A queued image from before the move keeps its original coordinates.
            Assert.Equal(calibration, guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddMilliseconds(50)));
            Assert.Equal(1, reads);
            Assert.Equal(current, guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddMilliseconds(450)));
            Assert.Equal(2, reads);
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddMilliseconds(900));
            Assert.Equal(2, reads);
        }
        finally
        {
            File.Delete(Path.Combine(folder, "gameVariable.xml"));
            File.Delete(Path.Combine(folder, "GameOption.txt"));
            Directory.Delete(folder);
        }
    }

    [Theory]
    [InlineData(540, 980)] // bottom clipping changes the physical row assigned to slot zero
    [InlineData(100, 540)] // revealing older rows cannot be treated as newly dropped loot
    [InlineData(540, 100)]
    public void MovingAcrossScreenEdgesCannotSilentlyChangeRowIdentities(int previousY, int currentY)
    {
        var calibration = Calibration() with { LootAnchorY = previousY };
        var current = calibration;
        var guard = new LootPanelCaptureGuard(calibration, () => current);
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch);
        current = calibration with { LootAnchorY = currentY };
        var error = Assert.Throws<LootPanelUnavailableException>(() =>
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2)));
        Assert.Contains("Bildschirmrand", error.Message);
        Assert.Equal(error.Message, guard.Error);
    }

    [Fact]
    public void SavedBdoXmlIsReadAgainAndSuppliesTheCorrectedPosition()
    {
        var folder = Path.Combine(Path.GetTempPath(), "grindcrest-bdo-config-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(folder, "UserCache");
        var profile = Path.Combine(cache, "42");
        var variables = Path.Combine(profile, "gameVariable.xml");
        var options = Path.Combine(folder, "GameOption.txt");
        Directory.CreateDirectory(profile);
        try
        {
            File.WriteAllText(options, "width = 1920\nheight = 1080\nuiScale =  1.00\nUIFontType = 2\nwindowed = 1\n");
            void SavePosition(string x, string y) => File.WriteAllText(variables,
                $"<Resolution Width='1920' Height='1080'/><UiScale Value='1.0'/><UIData>" +
                $"<UIData Index='159' IsShow='true' RelativePosX='{x}' RelativePosY='{y}'/></UIData>");
            SavePosition("0.5", "0.5");
            File.SetLastWriteTimeUtc(variables, DateTime.UnixEpoch.AddSeconds(-5));
            var reader = new CompanionCalibrationReader();
            var initial = reader.Read(folder);
            var reads = 0;
            var guard = new LootPanelCaptureGuard(initial, () => { reads++; return reader.Read(folder); });
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch);
            SavePosition("0.4", "0.6");
            File.SetLastWriteTimeUtc(variables, DateTime.UnixEpoch.AddMilliseconds(100));

            var corrected = guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddMilliseconds(450));

            Assert.Equal(768, corrected.LootAnchorX);
            Assert.Equal(648, corrected.LootAnchorY);
            Assert.Equal(2, reads);
            Assert.Null(guard.Error);
        }
        finally
        {
            File.Delete(variables);
            File.Delete(options);
            Directory.Delete(profile);
            Directory.Delete(cache);
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void PanelDisappearingDuringCaptureIsFatal()
    {
        var readable = true;
        var guard = new LootPanelCaptureGuard(Calibration(), () =>
            readable ? Calibration() : throw new InvalidDataException("No visible UIData 159"));
        guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch);
        readable = false;
        Assert.Throws<LootPanelUnavailableException>(() =>
            guard.Validate(new Size(1920, 1080), DateTimeOffset.UnixEpoch.AddSeconds(2)));
        Assert.Equal(LootPanelCaptureGuard.MissingPanelMessage, guard.Error);
    }

    [Fact]
    public void WrongCaptureSizeCannotUseCoordinatesFromAnotherResolution()
    {
        var guard = new LootPanelCaptureGuard(Calibration());
        var error = Assert.Throws<LootPanelUnavailableException>(() =>
            guard.Validate(new Size(3840, 2160), DateTimeOffset.UnixEpoch));
        Assert.Contains("Spielbilds", error.Message);
        Assert.NotNull(guard.Error);
    }
}
