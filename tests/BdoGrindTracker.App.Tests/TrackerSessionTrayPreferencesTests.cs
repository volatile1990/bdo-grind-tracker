using System.Text.Json;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TrayPreferencesRestoreAndCanChangeDuringTracking(bool minimizeToTray, bool closeToTray)
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            MinimizeToTray = minimizeToTray,
            CloseToTray = closeToTray,
            CloseBehaviorConfigured = true,
        });
        Assert.Equal(minimizeToTray, fixture.Service.Preferences.MinimizeToTray);
        Assert.Equal(closeToTray, fixture.Service.Preferences.CloseToTray);
        Assert.True(fixture.Service.Preferences.CloseBehaviorConfigured);
        fixture.Begin();
        var sessionId = fixture.Service.State.SessionId;

        var saved = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with
        {
            MinimizeToTray = !minimizeToTray,
            CloseToTray = !closeToTray,
        });

        Assert.True(saved.Succeeded, saved.Error);
        Assert.Equal(!minimizeToTray, fixture.Service.Preferences.MinimizeToTray);
        Assert.Equal(!closeToTray, fixture.Service.Preferences.CloseToTray);
        var settings = fixture.Settings.Load();
        Assert.Equal(!minimizeToTray, settings.MinimizeToTray);
        Assert.Equal(!closeToTray, settings.CloseToTray);
        Assert.True(settings.CloseBehaviorConfigured);
        Assert.True(fixture.Service.State.IsRunning);
        Assert.Equal(sessionId, fixture.Service.State.SessionId);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public async Task FailedTrayPreferenceSaveKeepsPreviousWindowBehaviorUntilExplicitRetry(
        bool minimizeToTray, bool closeToTray, bool closeBehaviorConfigured)
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettings: new AppSettings
        {
            MinimizeToTray = minimizeToTray,
            CloseToTray = closeToTray,
            CloseBehaviorConfigured = closeBehaviorConfigured,
        });
        var requested = fixture.Service.Preferences with
        {
            MinimizeToTray = !minimizeToTray,
            CloseToTray = !closeToTray,
            CloseBehaviorConfigured = true,
        };
        using (var locked = new FileStream(fixture.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = await fixture.Service.SavePreferencesAsync(requested);

            Assert.False(failed.Succeeded);
            Assert.Equal(minimizeToTray, fixture.Service.Preferences.MinimizeToTray);
            Assert.Equal(closeToTray, fixture.Service.Preferences.CloseToTray);
            Assert.Equal(closeBehaviorConfigured, fixture.Service.Preferences.CloseBehaviorConfigured);
            Assert.Equal(minimizeToTray, fixture.Settings.Load().MinimizeToTray);
            Assert.Equal(closeToTray, fixture.Settings.Load().CloseToTray);
            Assert.Equal(closeBehaviorConfigured, fixture.Settings.Load().CloseBehaviorConfigured);
        }

        // A later automatic persistence retry must also retain the original behavior.
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        Assert.Equal(minimizeToTray, fixture.Settings.Load().MinimizeToTray);
        Assert.Equal(closeToTray, fixture.Settings.Load().CloseToTray);
        Assert.Equal(closeBehaviorConfigured, fixture.Settings.Load().CloseBehaviorConfigured);

        Assert.True((await fixture.Service.SavePreferencesAsync(requested)).Succeeded);
        Assert.Equal(!minimizeToTray, fixture.Service.Preferences.MinimizeToTray);
        Assert.Equal(!closeToTray, fixture.Service.Preferences.CloseToTray);
        Assert.True(fixture.Service.Preferences.CloseBehaviorConfigured);
        Assert.Equal(!minimizeToTray, fixture.Settings.Load().MinimizeToTray);
        Assert.Equal(!closeToTray, fixture.Settings.Load().CloseToTray);
        Assert.True(fixture.Settings.Load().CloseBehaviorConfigured);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveringUnreadSettingsRestoresTrayPreferences(bool closeToTray)
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettingsJson: "{broken");
        Assert.False(fixture.Service.Preferences.MinimizeToTray);
        Assert.False(fixture.Service.Preferences.CloseToTray);
        Assert.False(fixture.Service.Preferences.CloseBehaviorConfigured);
        Assert.NotNull(fixture.Settings.LoadError);
        File.WriteAllText(fixture.SettingsPath, JsonSerializer.Serialize(new AppSettings
        {
            MinimizeToTray = true,
            CloseToTray = closeToTray,
            CloseBehaviorConfigured = true,
        }));

        var result = await fixture.Service.SaveSessionAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.Null(fixture.Settings.LoadError);
        Assert.True(fixture.Service.Preferences.MinimizeToTray);
        Assert.Equal(closeToTray, fixture.Service.Preferences.CloseToTray);
        Assert.True(fixture.Service.Preferences.CloseBehaviorConfigured);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"CloseToTray\": false }")]
    [InlineData("{ \"CloseToTray\": true }")]
    public async Task SavingOtherTrayPreferencesDoesNotConfigureAnUnchosenCloseBehavior(string json)
    {
        await using var fixture = new Fixture(autoUpload: false, initialSettingsJson: json);
        Assert.False(fixture.Service.Preferences.MinimizeToTray);
        Assert.False(fixture.Service.Preferences.CloseBehaviorConfigured);

        Assert.True((await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with
        {
            MinimizeToTray = true,
        })).Succeeded);

        Assert.False(fixture.Service.Preferences.CloseBehaviorConfigured);
        Assert.False(fixture.Settings.Load().CloseBehaviorConfigured);
    }

    [Fact]
    public async Task PreviewKeepsTrayPreferencesIndependentAndDefaultsToNormalWindowBehavior()
    {
        await using var preview = new PreviewTrackerSession(empty: true);
        Assert.False(preview.Preferences.MinimizeToTray);
        Assert.False(preview.Preferences.CloseToTray);
        Assert.False(preview.Preferences.CloseBehaviorConfigured);

        Assert.True((await preview.SavePreferencesAsync(preview.Preferences with { MinimizeToTray = true })).Succeeded);
        Assert.True(preview.Preferences.MinimizeToTray);
        Assert.False(preview.Preferences.CloseToTray);
        Assert.False(preview.Preferences.CloseBehaviorConfigured);

        Assert.True((await preview.SavePreferencesAsync(preview.Preferences with
        {
            MinimizeToTray = false,
            CloseToTray = true,
            CloseBehaviorConfigured = true,
        })).Succeeded);
        Assert.False(preview.Preferences.MinimizeToTray);
        Assert.True(preview.Preferences.CloseToTray);
        Assert.True(preview.Preferences.CloseBehaviorConfigured);
    }
}
