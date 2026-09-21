using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(AppThemes.Grindcrest)]
    [InlineData(AppThemes.BlackDesert)]
    [InlineData(AppThemes.Light)]
    [InlineData(AppThemes.Cats)]
    [InlineData(AppThemes.Obsidian)]
    [InlineData(AppThemes.Kamasylvia)]
    [InlineData(AppThemes.Valencia)]
    public async Task RestoresThemeSelectionFromPersistedPreferences(string themeId)
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { ThemeId = themeId });

        Assert.Equal(themeId, fixture.Service.Preferences.ThemeId);
        Assert.False(fixture.Service.State.IsRunning);
    }

    [Fact]
    public async Task ThemeSelectionCanChangeAndPersistDuringAnActiveSession()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var sessionId = fixture.Service.State.SessionId;
        var loot = fixture.Service.State.Loot.Totals;

        foreach (var themeId in new[] { AppThemes.BlackDesert, AppThemes.Light, AppThemes.Cats, AppThemes.Obsidian,
            AppThemes.Kamasylvia, AppThemes.Valencia, AppThemes.Grindcrest })
        {
            var result = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { ThemeId = themeId });

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(themeId, fixture.Service.Preferences.ThemeId);
            Assert.Equal(themeId, fixture.Settings.Load().ThemeId);
            Assert.True(fixture.Service.State.IsRunning);
            Assert.Equal(sessionId, fixture.Service.State.SessionId);
            Assert.Equal(loot, fixture.Service.State.Loot.Totals);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-theme")]
    public async Task UnknownThemeRejectsTheWholePreferenceSave(string? themeId)
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { ThemeId = AppThemes.BlackDesert });
        var previous = fixture.Service.Preferences;

        var result = await fixture.Service.SavePreferencesAsync(previous with { ThemeId = themeId!, FamilyFame = 7200 });

        Assert.False(result.Succeeded);
        Assert.Contains("Theme", result.Error);
        Assert.Same(previous, fixture.Service.Preferences);
        Assert.Equal(AppThemes.BlackDesert, fixture.Settings.Load().ThemeId);
        Assert.Equal(previous.FamilyFame, fixture.Settings.Load().SilverFamilyFame);
    }
}
