using System.Text.Json;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Tests;

public sealed class AppSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"SettingsVersion\": 6, \"AutoPauseMinutes\": 12 }")]
    public void AutomaticGarmothUploadsRequireExplicitOptIn(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;

        settings.UpgradeDefaults();

        Assert.False(settings.GarmothAutoUploadEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticGarmothUploadPreferenceSurvivesUpgradeAndJsonRoundTrip(bool enabled)
    {
        var settings = new AppSettings { GarmothAutoUploadEnabled = enabled };

        settings.UpgradeDefaults();
        var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        reloaded.UpgradeDefaults();

        Assert.Equal(enabled, reloaded.GarmothAutoUploadEnabled);
    }

    [Fact]
    public void ExistingSettingsGainThreeMinuteAutoPauseWithoutChangingPreferences()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """
            { "SettingsVersion": 4, "MonitorDeviceName": "DISPLAY2", "SpotId": "hermesia" }
            """)!;

        settings.UpgradeDefaults();

        Assert.Equal(3, settings.AutoPauseMinutes);
        Assert.Equal(6, settings.SettingsVersion);
        Assert.Equal("DISPLAY2", settings.MonitorDeviceName);
        Assert.Equal("hermesia", settings.SpotId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(15)]
    [InlineData(60)]
    public void ValidAutoPausePreferencesSurviveUpgradeAndJsonRoundTrip(int minutes)
    {
        var settings = new AppSettings { SettingsVersion = 4, AutoPauseMinutes = minutes };

        settings.UpgradeDefaults();
        var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;

        Assert.Equal(minutes, reloaded.AutoPauseMinutes);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(int.MaxValue)]
    public void InvalidAutoPausePreferencesReturnToDefaultEvenWithCurrentSchema(int minutes)
    {
        var settings = new AppSettings { SettingsVersion = 6, AutoPauseMinutes = minutes };

        settings.UpgradeDefaults();

        Assert.Equal(AppSettings.DefaultAutoPauseMinutes, settings.AutoPauseMinutes);
    }

    [Fact]
    public void PreviousSettingsReceiveExplicitSilverDefaultsWithoutChangingExistingPreferences()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """
            { "SettingsVersion": 5, "MonitorDeviceName": "DISPLAY2", "SpotId": "hermesia", "AutoPauseMinutes": 12 }
            """)!;

        settings.UpgradeDefaults();

        Assert.Equal(6, settings.SettingsVersion);
        Assert.Equal("eu", settings.MarketRegion);
        Assert.Equal(new SilverTaxOptions(), settings.GetSilverTaxOptions());
        Assert.Equal(12, settings.AutoPauseMinutes);
        Assert.Equal("DISPLAY2", settings.MonitorDeviceName);
        Assert.Equal("hermesia", settings.SpotId);
    }

    [Fact]
    public void ExplicitSilverPreferencesRoundTripWithoutSerializingComputedRates()
    {
        var settings = new AppSettings();
        settings.UpdateSilverPreferences("NA", new SilverTaxOptions(true, true, 7200));
        settings.UpgradeDefaults();

        var json = JsonSerializer.Serialize(settings);
        var reloaded = JsonSerializer.Deserialize<AppSettings>(json)!;
        reloaded.UpgradeDefaults();

        Assert.Equal("na", reloaded.MarketRegion);
        Assert.Equal(new SilverTaxOptions(true, true, 7200), reloaded.GetSilverTaxOptions());
        Assert.DoesNotContain("MarketReturnRate", json);
        Assert.DoesNotContain("FamilyFameBonus", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-region")]
    public void InvalidSilverFieldsReceiveSafeDefaultsWithoutResettingSelectedBenefits(string? region)
    {
        var settings = new AppSettings
        {
            SettingsVersion = 6,
            MarketRegion = region!,
            SilverValuePack = true,
            SilverMerchantRing = true,
            SilverFamilyFame = -1,
            AutoPauseMinutes = 10
        };

        settings.UpgradeDefaults();

        Assert.Equal("eu", settings.MarketRegion);
        Assert.Equal(new SilverTaxOptions(true, true, 0), settings.GetSilverTaxOptions());
        Assert.Equal(10, settings.AutoPauseMinutes);
    }

    [Fact]
    public void InvalidPreferenceUpdateDoesNotPartiallyMutateSettings()
    {
        var settings = new AppSettings();
        settings.UpdateSilverPreferences("na", new SilverTaxOptions(false, true, 4000));

        Assert.Throws<ArgumentException>(() =>
            settings.UpdateSilverPreferences("invalid", new SilverTaxOptions(true, false, 7000)));

        Assert.Equal("na", settings.MarketRegion);
        Assert.Equal(new SilverTaxOptions(false, true, 4000), settings.GetSilverTaxOptions());
    }

    [Fact]
    public void FreshSettingsDoNotGuessAnInitialSpot()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;

        settings.UpgradeDefaults();

        Assert.Null(settings.SpotId);
        Assert.True(settings.SettingsVersion > 0);
    }

    [Fact]
    public void ExplicitSpotSelectionRoundTripsThroughSettingsJson()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """
            {
              "SettingsVersion": 4,
              "SpotId": "hermesia"
            }
            """)!;

        settings.UpgradeDefaults();
        var savedJson = JsonSerializer.Serialize(settings);
        var reloaded = JsonSerializer.Deserialize<AppSettings>(savedJson)!;

        Assert.Equal("hermesia", reloaded.SpotId);
    }

    [Fact]
    public void LegacySamplingRateIsDiscardedWhenSettingsAreSavedAgain()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """
            {
              "SettingsVersion": 0,
              "FramesPerSecond": 6
            }
            """)!;

        settings.UpgradeDefaults();
        var savedJson = JsonSerializer.Serialize(settings);

        Assert.DoesNotContain("FramesPerSecond", savedJson, StringComparison.Ordinal);
        Assert.True(settings.SettingsVersion > 0);
    }

    [Fact]
    public void LegacyCaptureRegionIsDiscardedWhenSettingsAreSavedAgain()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """
            {
              "SettingsVersion": 2,
              "MonitorDeviceName": "DISPLAY1",
              "RegionMonitorDeviceName": "DISPLAY1",
              "RegionOnMonitor": { "X": 20, "Y": 30, "Width": 640, "Height": 480 },
              "FramesPerSecond": 6
            }
            """)!;

        settings.UpdateCapturePreferences("DISPLAY2");
        settings.UpgradeDefaults();

        var savedJson = JsonSerializer.Serialize(settings);

        Assert.Equal("DISPLAY2", settings.MonitorDeviceName);
        Assert.DoesNotContain("RegionMonitorDeviceName", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("RegionOnMonitor", savedJson, StringComparison.Ordinal);
    }
}
