using BdoGrindTracker.App.Persistence;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Fact]
    public async Task RestoresUiLanguageWithoutChangingGameLanguage()
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { UiLanguage = "en", GameLanguage = "de" });

        Assert.Equal("en", fixture.Service.Preferences.UiLanguage);
        Assert.Equal("de", fixture.Service.Preferences.GameLanguage);
    }

    [Fact]
    public async Task UiLanguageCanChangeAndPersistDuringActiveTracking()
    {
        await using var fixture = new Fixture(autoUpload: false);
        fixture.Begin();
        var sessionId = fixture.Service.State.SessionId;
        var loot = fixture.Service.State.Loot.Totals;
        var gameLanguage = fixture.Service.Preferences.GameLanguage;

        foreach (var language in new[] { "en", "de" })
        {
            var result = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { UiLanguage = language });

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(language, fixture.Service.Preferences.UiLanguage);
            Assert.Equal(language, fixture.Settings.Load().UiLanguage);
            Assert.Equal(gameLanguage, fixture.Service.Preferences.GameLanguage);
            Assert.True(fixture.Service.State.IsRunning);
            Assert.Equal(sessionId, fixture.Service.State.SessionId);
            Assert.Equal(loot, fixture.Service.State.Loot.Totals);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    public async Task UnsupportedUiLanguageRejectsTheWholePreferenceSave(string? language)
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { UiLanguage = "en" });
        var previous = fixture.Service.Preferences;

        var result = await fixture.Service.SavePreferencesAsync(previous with { UiLanguage = language!, FamilyFame = 7200 });

        Assert.False(result.Succeeded);
        Assert.Same(previous, fixture.Service.Preferences);
        Assert.Equal("en", fixture.Settings.Load().UiLanguage);
        Assert.Equal(previous.FamilyFame, fixture.Settings.Load().SilverFamilyFame);
    }
}
