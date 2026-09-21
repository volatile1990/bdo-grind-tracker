using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Services;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task NewInstallationRequiresSetupUntilCompletionIsSaved()
    {
        await using var fixture = new Fixture(autoUpload: false);
        Assert.False(fixture.Service.Preferences.SetupCompleted);

        var intermediate = await fixture.Service.SavePreferencesAsync(
            fixture.Service.Preferences with { AutoPauseMinutes = 12 });

        Assert.True(intermediate.Succeeded);
        Assert.False(fixture.Settings.Load().SetupCompleted);

        var completed = await fixture.Service.SavePreferencesAsync(
            fixture.Service.Preferences with { SetupCompleted = true });

        Assert.True(completed.Succeeded);
        Assert.True(fixture.Service.Preferences.SetupCompleted);
        Assert.True(fixture.Settings.Load().SetupCompleted);
        Assert.Equal(12, fixture.Settings.Load().AutoPauseMinutes);
        Assert.False(fixture.Service.State.HasSession);
        Assert.Equal(0, fixture.Captures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupRestoresExplicitSetupStatus(bool completed)
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { SetupCompleted = completed });

        Assert.Equal(completed, fixture.Service.Preferences.SetupCompleted);
    }

    [Fact]
    public async Task FailedSetupSaveNeverPublishesCompletionAndOrdinaryRetryDoesNotCompleteIt()
    {
        await using var fixture = new Fixture(autoUpload: false);
        Assert.True((await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences)).Succeeded);
        var publishedCompletion = new List<bool>();
        fixture.Service.Changed += () => publishedCompletion.Add(fixture.Service.Preferences.SetupCompleted);

        using (var locked = new FileStream(fixture.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = await fixture.Service.SavePreferencesAsync(
                fixture.Service.Preferences with { SetupCompleted = true });

            Assert.False(failed.Succeeded);
            Assert.Contains("nicht gespeichert", failed.Error);
            Assert.False(fixture.Service.Preferences.SetupCompleted);
        }

        Assert.NotEmpty(publishedCompletion);
        Assert.All(publishedCompletion, completed => Assert.False(completed));
        Assert.False(fixture.Settings.Load().SetupCompleted);
        Assert.True((await fixture.Service.SaveSessionAsync()).Succeeded);
        Assert.False(fixture.Service.Preferences.SetupCompleted);
        Assert.False(fixture.Settings.Load().SetupCompleted);

        Assert.True((await fixture.Service.SavePreferencesAsync(
            fixture.Service.Preferences with { SetupCompleted = true })).Succeeded);
        Assert.True(fixture.Service.Preferences.SetupCompleted);
        Assert.True(fixture.Settings.Load().SetupCompleted);
    }

    [Fact]
    public async Task SettingsRecoveryRestoresSetupCompletion()
    {
        await using var fixture = new Fixture(autoUpload: false);
        File.WriteAllText(fixture.SettingsPath, "broken");
        fixture.Settings.Load();
        Assert.NotNull(fixture.Settings.LoadError);
        File.WriteAllText(fixture.SettingsPath, "{\"SetupCompleted\":true}");

        var recovered = await fixture.Service.SaveSessionAsync();

        Assert.True(recovered.Succeeded);
        Assert.True(fixture.Service.Preferences.SetupCompleted);
        Assert.True(fixture.Settings.Load().SetupCompleted);
    }

    [Fact]
    public void BrowserPreviewKeepsItsNormalStartup()
    {
        Assert.True(new PreviewTrackerSession().Preferences.SetupCompleted);
        Assert.True(new PreviewTrackerSession(empty: true).Preferences.SetupCompleted);
    }
}
