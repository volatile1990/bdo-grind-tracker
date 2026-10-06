using System.Globalization;
using System.Text.Json;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed class DropRatePreferenceTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SettingsVersion\":7,\"AutoPauseMinutes\":12}")]
    public void ExistingSettingsReceiveDefaultDropRateWithoutChangingOtherPreferences(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        var autoPause = settings.AutoPauseMinutes;

        settings.UpgradeDefaults();

        Assert.Equal(320m, settings.DropRatePercent);
        Assert.Equal(autoPause, settings.AutoPauseMinutes);
        Assert.Equal(320m, new TrackerPreferences().DropRatePercent);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("320")]
    [InlineData("375.50")]
    [InlineData("1000")]
    public void DropRatePersistsThroughReloadAndUnrelatedSettingsSave(string configured)
    {
        using var fixture = new DropRateStore();
        var settings = fixture.Store.Load();
        settings.DropRatePercent = decimal.Parse(configured, CultureInfo.InvariantCulture);

        fixture.Store.Save(settings);
        var restarted = new SettingsStore(fixture.DirectoryPath);
        var reloaded = restarted.Load();
        Assert.Equal(settings.DropRatePercent, reloaded.DropRatePercent);
        reloaded.AutoPauseMinutes = 12;
        restarted.Save(reloaded);

        Assert.Equal(settings.DropRatePercent, new SettingsStore(fixture.DirectoryPath).Load().DropRatePercent);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1000.01")]
    public void InvalidStoredDropRateUsesDefaultWithoutDiscardingOtherSettings(string configured)
    {
        using var fixture = new DropRateStore();
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "settings.json"),
            "{\"DropRatePercent\":" + configured + ",\"AutoPauseMinutes\":12}");

        var settings = fixture.Store.Load();

        Assert.Null(fixture.Store.LoadError);
        Assert.Equal(320m, settings.DropRatePercent);
        Assert.Equal(12, settings.AutoPauseMinutes);
    }

    private sealed class DropRateStore : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "BdoGrindTracker.Tests", Guid.NewGuid().ToString("N"));
        public SettingsStore Store { get; }

        public DropRateStore()
        {
            Directory.CreateDirectory(DirectoryPath);
            Store = new SettingsStore(DirectoryPath);
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("375.50")]
    [InlineData("1000")]
    public async Task DropRateCanChangeDuringTrackingAndSurvivesOrdinaryPreferenceSaves(string configured)
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings { DropRatePercent = 300m });
        Assert.Equal(300m, fixture.Service.Preferences.DropRatePercent);
        fixture.Begin();
        var sessionId = fixture.Service.State.SessionId;
        var dropRatePercent = decimal.Parse(configured, CultureInfo.InvariantCulture);

        var result = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { DropRatePercent = dropRatePercent });

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(dropRatePercent, fixture.Service.Preferences.DropRatePercent);
        Assert.Equal(dropRatePercent, fixture.Settings.Load().DropRatePercent);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(sessionId, fixture.Service.State.SessionId);
        var unrelated = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { AutoPauseMinutes = 12 });
        Assert.True(unrelated.Succeeded, unrelated.Error);
        Assert.Equal(dropRatePercent, fixture.Settings.Load().DropRatePercent);

        await using var restarted = new Fixture(autoUpload: false, initialSettings: fixture.Settings.Load());
        Assert.Equal(dropRatePercent, restarted.Service.Preferences.DropRatePercent);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1000.01")]
    public async Task InvalidDropRatePreferenceDoesNotChangeCurrentOrPersistedValue(string configured)
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings { DropRatePercent = 375.5m });

        var result = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with
        {
            DropRatePercent = decimal.Parse(configured, CultureInfo.InvariantCulture),
        });

        Assert.False(result.Succeeded);
        Assert.Equal(375.5m, fixture.Service.Preferences.DropRatePercent);
        Assert.Equal(375.5m, fixture.Settings.Load().DropRatePercent);
    }

    [Fact]
    public async Task FailedDropRateSaveKeepsPreviousValueUntilExplicitRetry()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings { DropRatePercent = 300m });
        var requested = fixture.Service.Preferences with { DropRatePercent = 375.5m };
        using (var locked = new FileStream(fixture.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await fixture.Service.SavePreferencesAsync(requested);

            Assert.False(result.Succeeded);
            Assert.Equal(300m, fixture.Service.Preferences.DropRatePercent);
            Assert.Equal(300m, fixture.Settings.Load().DropRatePercent);
        }

        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        Assert.Equal(300m, fixture.Settings.Load().DropRatePercent);
        Assert.True((await fixture.Service.SavePreferencesAsync(requested)).Succeeded);
        Assert.Equal(375.5m, fixture.Settings.Load().DropRatePercent);
    }

    [Fact]
    public async Task RecoveringUnreadSettingsRestoresConfiguredDropRate()
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettingsJson: "{broken");
        Assert.Equal(320m, fixture.Service.Preferences.DropRatePercent);
        File.WriteAllText(fixture.SettingsPath, JsonSerializer.Serialize(new AppSettings { DropRatePercent = 375.5m }));

        var result = await fixture.Service.SaveSessionAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(375.5m, fixture.Service.Preferences.DropRatePercent);
    }

    [Fact]
    public async Task PreviewSupportsTheSameDefaultAndDropRateValidation()
    {
        await using var preview = new PreviewTrackerSession(empty: true);
        Assert.Equal(320m, preview.Preferences.DropRatePercent);
        Assert.True((await preview.SavePreferencesAsync(preview.Preferences with { DropRatePercent = 375.5m })).Succeeded);
        Assert.Equal(375.5m, preview.Preferences.DropRatePercent);

        Assert.False((await preview.SavePreferencesAsync(preview.Preferences with { DropRatePercent = -0.01m })).Succeeded);
        Assert.False((await preview.SavePreferencesAsync(preview.Preferences with { DropRatePercent = 1000.01m })).Succeeded);
        Assert.Equal(375.5m, preview.Preferences.DropRatePercent);
    }
}
