using System.Text.Json;
using BdoGrindTracker.App.Persistence;
using BdoGrindTracker.App.Theming;

namespace BdoGrindTracker.App.Tests;

public sealed partial class TrackerSessionServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(AppThemes.Grindcrest)]
    [InlineData(AppThemes.BlackDesert)]
    [InlineData(AppThemes.Light)]
    [InlineData(AppThemes.Cats)]
    [InlineData(AppThemes.Obsidian)]
    [InlineData(AppThemes.Kamasylvia)]
    [InlineData(AppThemes.Valencia)]
    public async Task RestoresIndependentOverlayThemeFromSettings(string? overlayTheme)
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { ThemeId = AppThemes.Valencia, OverlayThemeId = overlayTheme });

        Assert.Equal(AppThemes.Valencia, fixture.Service.Preferences.ThemeId);
        Assert.Equal(overlayTheme, fixture.Service.Preferences.OverlayThemeId);
        Assert.Equal(overlayTheme ?? AppThemes.Valencia, fixture.Service.Preferences.EffectiveOverlayThemeId);
    }

    [Fact]
    public async Task OverlayThemeCanChangeAndFollowAgainWithoutChangingMainThemeOrSession()
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { ThemeId = AppThemes.Light });
        fixture.Begin();
        var sessionId = fixture.Service.State.SessionId;
        var loot = fixture.Service.State.Loot.Totals;

        foreach (var theme in new string?[] { AppThemes.Obsidian, AppThemes.Kamasylvia, AppThemes.Valencia, null })
        {
            var result = await fixture.Service.SavePreferencesAsync(fixture.Service.Preferences with { OverlayThemeId = theme });

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(AppThemes.Light, fixture.Service.Preferences.ThemeId);
            Assert.Equal(theme, fixture.Service.Preferences.OverlayThemeId);
            Assert.Equal(theme ?? AppThemes.Light, fixture.Service.Preferences.EffectiveOverlayThemeId);
            var saved = fixture.Settings.Load();
            Assert.Equal(AppThemes.Light, saved.ThemeId);
            Assert.Equal(theme, saved.OverlayThemeId);
            Assert.True(fixture.Service.State.IsRunning);
            Assert.Equal(sessionId, fixture.Service.State.SessionId);
            Assert.Equal(loot, fixture.Service.State.Loot.Totals);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-theme")]
    public async Task UnknownOverlayThemeRejectsTheWholePreferenceSave(string theme)
    {
        await using var fixture = new Fixture(autoUpload: false,
            initialSettings: new AppSettings { ThemeId = AppThemes.Light, OverlayThemeId = AppThemes.Obsidian });
        var previous = fixture.Service.Preferences;

        var result = await fixture.Service.SavePreferencesAsync(previous with { OverlayThemeId = theme, FamilyFame = 7200 });

        Assert.False(result.Succeeded);
        Assert.Contains("Overlay-Theme", result.Error);
        Assert.Same(previous, fixture.Service.Preferences);
        var saved = fixture.Settings.Load();
        Assert.Equal(AppThemes.Light, saved.ThemeId);
        Assert.Equal(AppThemes.Obsidian, saved.OverlayThemeId);
        Assert.Equal(previous.FamilyFame, saved.SilverFamilyFame);
    }

    [Fact]
    public async Task RecoveringSettingsRestoresBothThemesInsteadOfFallbackSelections()
    {
        await using var fixture = new Fixture(autoUpload: false);
        File.WriteAllText(fixture.SettingsPath, "{");
        fixture.Settings.Load();
        Assert.NotNull(fixture.Settings.LoadError);
        File.WriteAllText(fixture.SettingsPath, JsonSerializer.Serialize(new AppSettings
        {
            ThemeId = AppThemes.Kamasylvia, OverlayThemeId = AppThemes.Valencia,
        }));

        typeof(BdoGrindTracker.App.Services.TrackerSessionService)
            .GetMethod("RecoverSettingsIfNeeded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(fixture.Service, null);

        Assert.Null(fixture.Settings.LoadError);
        Assert.Equal(AppThemes.Kamasylvia, fixture.Service.Preferences.ThemeId);
        Assert.Equal(AppThemes.Valencia, fixture.Service.Preferences.OverlayThemeId);
        Assert.Equal(AppThemes.Valencia, fixture.Service.Preferences.EffectiveOverlayThemeId);
    }
}
