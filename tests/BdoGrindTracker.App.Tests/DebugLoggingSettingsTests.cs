using System.Text.Json;
using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed class DebugLoggingSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"SettingsVersion\": 7, \"AutoPauseMinutes\": 12 }")]
    public void ExistingSettingsGainThreeHourRetentionWithLoggingDisabled(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;

        settings.UpgradeDefaults();

        Assert.False(settings.AutomaticDebugLogging);
        Assert.Equal(3, settings.DebugLogRetentionHours);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    [InlineData(true, 24)]
    [InlineData(true, 168)]
    public void ValidDebugLoggingPreferencesSurviveUpgradeAndJsonRoundTrip(bool enabled, int hours)
    {
        var settings = new AppSettings { AutomaticDebugLogging = enabled, DebugLogRetentionHours = hours };

        settings.UpgradeDefaults();
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        restored.UpgradeDefaults();

        Assert.Equal(enabled, restored.AutomaticDebugLogging);
        Assert.Equal(hours, restored.DebugLogRetentionHours);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(169)]
    [InlineData(int.MaxValue)]
    public void InvalidStoredDebugRetentionFallsBackWithoutChangingOtherPreferences(int hours)
    {
        var settings = new AppSettings
        {
            SettingsVersion = 7,
            AutomaticDebugLogging = true,
            DebugLogRetentionHours = hours,
            MonitorDeviceName = "DISPLAY2",
            AutoPauseMinutes = 12,
        };

        settings.UpgradeDefaults();

        Assert.Equal(3, settings.DebugLogRetentionHours);
        Assert.True(settings.AutomaticDebugLogging);
        Assert.Equal("DISPLAY2", settings.MonitorDeviceName);
        Assert.Equal(12, settings.AutoPauseMinutes);
    }
}
