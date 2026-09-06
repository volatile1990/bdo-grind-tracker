using System.Reflection;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void MissingSettingsUseThreeMinuteDefault()
    {
        using var fixture = new IsolatedStore();

        var settings = fixture.Store.Load();

        Assert.Equal(3, settings.AutoPauseMinutes);
        Assert.Equal(6, settings.SettingsVersion);
    }

    [Fact]
    public void LegacyFileRetainsMonitorAndSpotWhileReceivingAutoPauseDefault()
    {
        using var fixture = new IsolatedStore();
        File.WriteAllText(fixture.Path,
            """
            { "SettingsVersion": 4, "MonitorDeviceName": "DISPLAY2", "SpotId": "hermesia" }
            """);

        var settings = fixture.Store.Load();

        Assert.Equal(3, settings.AutoPauseMinutes);
        Assert.Equal("DISPLAY2", settings.MonitorDeviceName);
        Assert.Equal("hermesia", settings.SpotId);
        Assert.Equal(6, settings.SettingsVersion);
    }

    [Fact]
    public void ConfiguredTimeoutPersistsAlongWithCapturePreferences()
    {
        using var fixture = new IsolatedStore();
        var settings = fixture.Store.Load();
        settings.AutoPauseMinutes = 12;
        settings.UpdateCapturePreferences("DISPLAY3");

        fixture.Store.Save(settings);
        var reloaded = fixture.Store.Load();

        Assert.Equal(12, reloaded.AutoPauseMinutes);
        Assert.Equal("DISPLAY3", reloaded.MonitorDeviceName);
    }

    [Fact]
    public void SilverPreferencesPersistOnlyWhenTheCallerSaves()
    {
        using var fixture = new IsolatedStore();
        var settings = fixture.Store.Load();
        settings.UpdateSilverPreferences("na", new SilverTaxOptions(true, true, 7000));
        Assert.False(File.Exists(fixture.Path));

        fixture.Store.Save(settings);
        var reloaded = fixture.Store.Load();

        Assert.Equal("na", reloaded.MarketRegion);
        Assert.Equal(new SilverTaxOptions(true, true, 7000), reloaded.GetSilverTaxOptions());
    }

    [Fact]
    public void LegacyStoredFileAddsSilverDefaultsAndKeepsAutoPause()
    {
        using var fixture = new IsolatedStore();
        File.WriteAllText(fixture.Path,
            """
            { "SettingsVersion": 5, "MonitorDeviceName": "DISPLAY3", "AutoPauseMinutes": 9 }
            """);

        var settings = fixture.Store.Load();

        Assert.Equal(6, settings.SettingsVersion);
        Assert.Equal("eu", settings.MarketRegion);
        Assert.Equal(new SilverTaxOptions(), settings.GetSilverTaxOptions());
        Assert.Equal(9, settings.AutoPauseMinutes);
        Assert.Equal("DISPLAY3", settings.MonitorDeviceName);
    }

    [Fact]
    public void InvalidStoredTimeoutFallsBackWithoutDiscardingOtherPreferences()
    {
        using var fixture = new IsolatedStore();
        File.WriteAllText(fixture.Path,
            """
            { "SettingsVersion": 5, "MonitorDeviceName": "DISPLAY2", "AutoPauseMinutes": 0 }
            """);

        var settings = fixture.Store.Load();

        Assert.Equal(3, settings.AutoPauseMinutes);
        Assert.Equal("DISPLAY2", settings.MonitorDeviceName);
    }

    private sealed class IsolatedStore : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));

        public IsolatedStore()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "settings.json");
            Store = new SettingsStore();
            var field = typeof(SettingsStore).GetField("_settingsPath", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(Store, Path);
        }

        public SettingsStore Store { get; }
        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
            Directory.Delete(_directory);
        }
    }
}
