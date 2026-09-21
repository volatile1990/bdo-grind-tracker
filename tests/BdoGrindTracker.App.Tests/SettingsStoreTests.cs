using System.Reflection;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Pricing;

namespace BdoGrindTracker.App.Tests;

public sealed class SettingsStoreTests
{
    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public void CorruptSettingsCannotBeOverwrittenUntilExplicitSuccessfulReload(string content)
    {
        using var fixture = new IsolatedStore();
        File.WriteAllText(fixture.Path, content);
        var fallback = fixture.Store.Load();
        Assert.NotNull(fixture.Store.LoadError);
        Assert.Throws<IOException>(() => fixture.Store.Save(fallback));
        Assert.Equal(content, File.ReadAllText(fixture.Path));
        File.WriteAllText(fixture.Path, "{\"AutoPauseMinutes\":12,\"MarketRegion\":\"na\"}");
        Assert.Throws<IOException>(() => fixture.Store.Save(fallback));
        var recovered = fixture.Store.Load();
        Assert.Null(fixture.Store.LoadError);
        Assert.Equal(12, recovered.AutoPauseMinutes);
        Assert.Equal("na", recovered.MarketRegion);
        fixture.Store.Save(recovered);
    }

    [Fact]
    public void TransientReadLockDoesNotTurnLaterSaveIntoAReset()
    {
        using var fixture = new IsolatedStore();
        File.WriteAllText(fixture.Path, "{\"AutoPauseMinutes\":12}");
        AppSettings fallback;
        using (var locked = new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.None))
            fallback = fixture.Store.Load();
        Assert.Throws<IOException>(() => fixture.Store.Save(fallback));
        Assert.Contains("12", File.ReadAllText(fixture.Path));
        Assert.Equal(12, fixture.Store.Load().AutoPauseMinutes);
        Assert.Null(fixture.Store.LoadError);
    }

    [Fact]
    public void SaveWithoutPriorLoadStillProtectsAnUnreadExistingFile()
    {
        using var fixture = new IsolatedStore();
        File.WriteAllText(fixture.Path, "broken settings");
        Assert.Throws<IOException>(() => fixture.Store.Save(new AppSettings()));
        Assert.Equal("broken settings", File.ReadAllText(fixture.Path));
    }

    [Fact]
    public void MissingSettingsUseThreeMinuteDefault()
    {
        using var fixture = new IsolatedStore();

        var settings = fixture.Store.Load();

        Assert.Equal(3, settings.AutoPauseMinutes);
        Assert.Equal(7, settings.SettingsVersion);
        Assert.False(settings.GarmothAutoUploadEnabled);
        Assert.False(settings.AutoStartGrinding);
        Assert.False(settings.SetupCompleted);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SettingsVersion\":6,\"AutoPauseMinutes\":12}")]
    public void ExistingSettingsWithoutSetupFlagKeepTheirNormalStartup(string json)
    {
        using var fixture = new IsolatedStore();
        File.WriteAllText(fixture.Path, json);

        var settings = fixture.Store.Load();
        fixture.Store.Save(settings);

        Assert.True(settings.SetupCompleted);
        Assert.True(fixture.Store.Load().SetupCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitSetupStatusSurvivesSaveAndReload(bool completed)
    {
        using var fixture = new IsolatedStore();
        var settings = fixture.Store.Load();
        settings.SetupCompleted = completed;

        fixture.Store.Save(settings);

        Assert.Equal(completed, fixture.Store.Load().SetupCompleted);
    }

    [Fact]
    public void SetupMigrationRespectsCaseInsensitivePropertyNames()
    {
        using var fixture = new IsolatedStore();
        File.WriteAllText(fixture.Path, "{\"setupCompleted\":false}");

        Assert.False(fixture.Store.Load().SetupCompleted);
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
        Assert.Equal(7, settings.SettingsVersion);
    }

    [Fact]
    public void LegacyDisabledEventLootIsIgnoredAndRemovedOnNextSave()
    {
        using var fixture = new IsolatedStore();
        File.WriteAllText(fixture.Path,
            """
            { "SettingsVersion": 6, "IncludeEventLoot": false, "AutoPauseMinutes": 9, "MonitorDeviceName": "DISPLAY2" }
            """);

        var settings = fixture.Store.Load();
        fixture.Store.Save(settings);

        Assert.Equal(9, settings.AutoPauseMinutes);
        Assert.Equal("DISPLAY2", settings.MonitorDeviceName);
        Assert.DoesNotContain("IncludeEventLoot", File.ReadAllText(fixture.Path));
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
    public void AutomaticGarmothUploadsCanBeEnabledAndDisabledPersistently()
    {
        using var fixture = new IsolatedStore();
        var settings = fixture.Store.Load();
        settings.GarmothAutoUploadEnabled = true;
        Assert.False(File.Exists(fixture.Path));

        fixture.Store.Save(settings);
        var reloaded = fixture.Store.Load();
        Assert.True(reloaded.GarmothAutoUploadEnabled);

        reloaded.GarmothAutoUploadEnabled = false;
        fixture.Store.Save(reloaded);
        Assert.False(fixture.Store.Load().GarmothAutoUploadEnabled);
    }

    [Fact]
    public void AutomaticGrindDetectionCanBeEnabledAndDisabledPersistently()
    {
        using var fixture = new IsolatedStore();
        var settings = fixture.Store.Load();
        settings.AutoStartGrinding = true;

        fixture.Store.Save(settings);
        var reloaded = fixture.Store.Load();
        Assert.True(reloaded.AutoStartGrinding);

        reloaded.AutoStartGrinding = false;
        fixture.Store.Save(reloaded);
        Assert.False(fixture.Store.Load().AutoStartGrinding);
    }

    [Fact]
    public void LegacySuspensionIsIgnoredAndRemovedWhenSettingsAreSaved()
    {
        using var fixture = new IsolatedStore();
        File.WriteAllText(fixture.Path,
            """{"SettingsVersion":7,"AutoStartGrinding":true,"AutoStartSuspended":true}""");

        var reloaded = fixture.Store.Load();
        Assert.Null(fixture.Store.LoadError);
        Assert.True(reloaded.AutoStartGrinding);
        fixture.Store.Save(reloaded);

        Assert.True(fixture.Store.Load().AutoStartGrinding);
        Assert.DoesNotContain("AutoStartSuspended", File.ReadAllText(fixture.Path));
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

        Assert.Equal(7, settings.SettingsVersion);
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
            if (File.Exists(Path + ".bak")) File.Delete(Path + ".bak");
            Directory.Delete(_directory);
        }
    }
}
